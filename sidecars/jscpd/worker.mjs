import { createInterface } from "node:readline";
import { createRequire } from "node:module";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import { constants, realpathSync } from "node:fs";
import { access } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join, relative, resolve, sep } from "node:path";
import { spawn } from "node:child_process";

const protocolVersion = 1;
const maximumLineBytes = 1_000_000;
const maximumTimeoutMilliseconds = 300_000;
const require = createRequire(import.meta.url);
const packageInfo = require("./package.json");
const jscpdBinary = resolve(new URL("./node_modules/.bin/jscpd", import.meta.url).pathname);

const lines = createInterface({ input: process.stdin, crlfDelay: Infinity });
for await (const line of lines) {
  const response = await handleLine(line);
  process.stdout.write(`${JSON.stringify(response)}\n`);
}

async function handleLine(line) {
  if (Buffer.byteLength(line, "utf8") === 0 || Buffer.byteLength(line, "utf8") > maximumLineBytes) {
    return failure("unknown", "invalid-request", "Sidecar messages must be one bounded JSON line.", false);
  }

  let request;
  try {
    request = JSON.parse(line);
  } catch {
    return failure("unknown", "invalid-request", "Sidecar messages must be valid JSON.", false);
  }

  const requestId = property(request, "requestId");
  const validation = validateEnvelope(request);
  if (validation) {
    return failure(requestId ?? "unknown", validation.code, validation.message, false);
  }

  try {
    if (property(request, "method") === "handshake") {
      return handshake(request);
    }
    if (property(request, "method") === "detect_clones") {
      return await detectClones(request);
    }
    return failure(requestId, "unknown-method", "The sidecar method is not supported.", false);
  } catch (error) {
    return failure(requestId, "sidecar-failure", error instanceof Error ? error.message : "The sidecar failed.", true);
  }
}

function handshake(request) {
  const payload = parsePayload(request);
  if (!payload || !Array.isArray(payload.requiredCapabilities)) {
    return failure(property(request, "requestId"), "invalid-handshake", "Handshake payload must contain requiredCapabilities.", false);
  }

  const capabilities = [{ name: "structural-clone-detection", version: 1 }];
  const unsupported = payload.requiredCapabilities.find((required) => !capabilities.some((capability) => capability.name === required.name && capability.version >= required.version));
  if (unsupported) {
    return failure(property(request, "requestId"), "missing-capability", `Capability '${unsupported.name}' is unavailable.`, false);
  }

  return success(property(request, "requestId"), {
    protocolVersion,
    sidecarName: "archy-jscpd",
    toolVersion: packageInfo.dependencies.jscpd,
    capabilities
  });
}

async function detectClones(request) {
  const payload = parsePayload(request);
  if (!payload || typeof payload.repositoryRoot !== "string" || !Array.isArray(payload.files) || payload.files.length === 0 || payload.files.length > 10_000) {
    return failure(property(request, "requestId"), "invalid-payload", "Clone detection requires a bounded repositoryRoot and file list.", false);
  }

  const allowedPaths = property(request, "allowedRepositoryRelativePaths");
  const root = realpathSync(payload.repositoryRoot);
  const files = await validateFiles(root, payload.files, allowedPaths);
  const outputDirectory = await mkdtemp(join(tmpdir(), "archy-jscpd-"));
  try {
    const minTokens = boundedInteger(payload.minTokens, 50, 10, 500);
    const minLines = boundedInteger(payload.minLines, 5, 2, 100);
    await runJscpd(root, files, outputDirectory, minTokens, minLines, property(request, "timeoutMilliseconds"));
    const report = JSON.parse(await readFile(join(outputDirectory, "jscpd-report.json"), "utf8"));
    return success(property(request, "requestId"), {
      toolVersion: packageInfo.dependencies.jscpd,
      clones: normalizeClones(report, root)
    });
  } finally {
    await rm(outputDirectory, { recursive: true, force: true });
  }
}

function validateEnvelope(request) {
  if (!request || property(request, "protocolVersion") !== protocolVersion || typeof property(request, "requestId") !== "string" || !property(request, "requestId").trim() || typeof property(request, "method") !== "string" || !property(request, "method").trim() || !Array.isArray(property(request, "allowedRepositoryRelativePaths")) || property(request, "allowedRepositoryRelativePaths").some((path) => !isRelativePath(path)) || !isJson(property(request, "payloadJson"))) {
    return { code: "invalid-request", message: "The request envelope is invalid." };
  }
  const timeout = property(request, "timeoutMilliseconds");
  return Number.isInteger(timeout) && timeout >= 1 && timeout <= maximumTimeoutMilliseconds ? null : { code: "invalid-request", message: "The request timeout is invalid." };
}

async function validateFiles(root, requestedFiles, allowedPaths) {
  const allowed = new Set(allowedPaths);
  if (new Set(allowedPaths).size !== allowed.size || new Set(requestedFiles).size !== requestedFiles.length || requestedFiles.some((path) => !isRelativePath(path) || !allowed.has(path) || !path.toLowerCase().endsWith(".cs"))) {
    throw new Error("Requested files must be unique allowed C# repository-relative paths.");
  }
  const resolved = [];
  for (const relativePath of requestedFiles.sort()) {
    const fullPath = resolve(root, relativePath);
    await access(fullPath, constants.R_OK);
    const realPath = realpathSync(fullPath);
    if (!isWithinRoot(root, realPath)) {
      throw new Error("A requested clone-detection path escapes the repository root.");
    }
    resolved.push(relativePath);
  }
  return resolved;
}

function runJscpd(root, files, outputDirectory, minTokens, minLines, timeoutMilliseconds) {
  return new Promise((resolveRun, rejectRun) => {
    const child = spawn(jscpdBinary, ["--format", "csharp", "--reporters", "json", "--output", outputDirectory, "--silent", "--min-tokens", String(minTokens), "--min-lines", String(minLines), ...files], {
      cwd: root,
      env: { PATH: process.env.PATH ?? "", HOME: process.env.HOME ?? "", PWD: root, TMPDIR: tmpdir() },
      shell: false,
      stdio: ["ignore", "ignore", "pipe"]
    });
    let stderr = "";
    child.stderr.on("data", (chunk) => { if (stderr.length < 16_384) stderr += chunk.toString("utf8"); });
    const timeout = setTimeout(() => child.kill("SIGKILL"), timeoutMilliseconds);
    child.on("error", (error) => { clearTimeout(timeout); rejectRun(error); });
    child.on("exit", (code) => {
      clearTimeout(timeout);
      if (code === 0 || code === 1) resolveRun();
      else rejectRun(new Error(`jscpd exited with code ${code ?? "unknown"}: ${stderr.trim().slice(0, 512)}`));
    });
  });
}

function normalizeClones(report, root) {
  const duplicates = Array.isArray(report.duplicates) ? report.duplicates : [];
  return duplicates.map((duplicate) => {
    const first = duplicate.firstFile ?? duplicate.first ?? {};
    const second = duplicate.secondFile ?? duplicate.second ?? {};
    return {
      first: occurrence(first, root),
      second: occurrence(second, root),
      tokens: Number.isInteger(duplicate.tokens) && duplicate.tokens > 0 ? duplicate.tokens : countFragmentTokens(duplicate.fragment),
      lines: Number.isInteger(duplicate.lines) ? duplicate.lines : 0,
      format: String(duplicate.format ?? "csharp")
    };
  }).filter((clone) => clone.first && clone.second && clone.tokens > 0 && clone.lines > 0);
}

function occurrence(value, root) {
  const name = value.name ?? value.path;
  const start = value.startLoc ?? value.start ?? {};
  const end = value.endLoc ?? value.end ?? {};
  if (typeof name !== "string" || !Number.isInteger(start.line) || !Number.isInteger(start.column) || !Number.isInteger(end.line) || !Number.isInteger(end.column)) return null;
  const fullPath = resolve(root, name);
  if (!isWithinRoot(root, fullPath)) return null;
  return { path: relative(root, fullPath).split(sep).join("/"), startLine: start.line, startColumn: start.column, endLine: end.line, endColumn: end.column };
}

function success(requestId, result) { return { ProtocolVersion: protocolVersion, RequestId: requestId, IsSuccess: true, ResultJson: JSON.stringify(result), Error: null }; }
function failure(requestId, code, message, isRetryable) { return { ProtocolVersion: protocolVersion, RequestId: requestId, IsSuccess: false, ResultJson: null, Error: { Code: code, Message: message, IsRetryable: isRetryable } }; }
function property(value, name) { return value?.[name] ?? value?.[name[0].toUpperCase() + name.slice(1)]; }
function parsePayload(request) { try { return JSON.parse(property(request, "payloadJson")); } catch { return null; } }
function isJson(value) { try { JSON.parse(value); return true; } catch { return false; } }
function isRelativePath(path) { return typeof path === "string" && path.length > 0 && !path.startsWith("/") && !path.includes("\\") && !path.split("/").some((part) => part === "." || part === ".."); }
function isWithinRoot(root, candidate) { return candidate === root || candidate.startsWith(`${root}${sep}`); }
function boundedInteger(value, defaultValue, min, max) { return Number.isInteger(value) && value >= min && value <= max ? value : defaultValue; }
function countFragmentTokens(fragment) { return typeof fragment === "string" ? (fragment.match(/[A-Za-z_][A-Za-z0-9_]*|\d+(?:\.\d+)?|\S/g) ?? []).length : 0; }
