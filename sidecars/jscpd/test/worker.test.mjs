import assert from "node:assert/strict";
import test from "node:test";
import { spawn } from "node:child_process";

const worker = new URL("../worker.mjs", import.meta.url).pathname;

test("handshake and allowed-path enforcement are protocol-safe", async () => {
  const handshake = await invoke({ ProtocolVersion: 1, RequestId: "handshake", Method: "handshake", TimeoutMilliseconds: 1000, AllowedRepositoryRelativePaths: ["src/A.cs"], PayloadJson: JSON.stringify({ requiredCapabilities: [{ name: "structural-clone-detection", version: 1 }] }) });
  assert.equal(handshake.IsSuccess, true);
  const escaped = await invoke({ ProtocolVersion: 1, RequestId: "escape", Method: "detect_clones", TimeoutMilliseconds: 1000, AllowedRepositoryRelativePaths: ["src/A.cs"], PayloadJson: JSON.stringify({ repositoryRoot: "/tmp", files: ["../secret.cs"] }) });
  assert.equal(escaped.IsSuccess, false);
});

test("jscpd returns normalized C# clone occurrences for an allowed file set", async () => {
  const root = new URL("./fixtures/", import.meta.url).pathname;
  const response = await invoke({ ProtocolVersion: 1, RequestId: "clone", Method: "detect_clones", TimeoutMilliseconds: 15_000, AllowedRepositoryRelativePaths: ["A.cs", "B.cs"], PayloadJson: JSON.stringify({ repositoryRoot: root, files: ["A.cs", "B.cs"], minTokens: 10, minLines: 2 }) });
  assert.equal(response.IsSuccess, true);
  const result = JSON.parse(response.ResultJson);
  assert.ok(result.clones.length > 0);
  assert.equal(result.toolVersion, "4.0.5");
});

function invoke(message) {
  return new Promise((resolveResponse, reject) => {
    const child = spawn(process.execPath, [worker], { stdio: ["pipe", "pipe", "pipe"] });
    let output = "";
    let errors = "";
    child.stdout.on("data", (chunk) => { output += chunk; });
    child.stderr.on("data", (chunk) => { errors += chunk; });
    child.on("error", reject);
    child.stdin.end(`${JSON.stringify(message)}\n`);
    child.on("exit", (code) => {
      if (code !== 0) reject(new Error(errors));
      else resolveResponse(JSON.parse(output.trim()));
    });
  });
}
