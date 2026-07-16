# Security policy

Please report suspected vulnerabilities privately to the repository maintainers through GitHub’s private vulnerability reporting flow. Do not include credentials, customer source, or exploit details in public issues.

Archy treats remote exposure, authentication bypass, arbitrary path writes, secret disclosure, sidecar integrity failures, and database migration/data-loss risks as security issues. The local web UI and optional MCP HTTP server bind to loopback by default; HTTP MCP requires an explicit bearer token.
