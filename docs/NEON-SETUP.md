# Neon setup

This file records the initial Neon setup. Subsequent application migration, current validation and remaining deployment work are documented in [RENDER-NEON.md](RENDER-NEON.md).

## Selected infrastructure

- Existing project: `green-tasty-marathon` (`cold-shadow-92100701`).
- Linked branch: `production`.
- PostgreSQL only; the frontend and .NET backend are intended for Render.
- No additional Auth, Functions, Object Storage, Realtime, or AI Gateway services were enabled.

## Changes made during setup

- Ran the Neon CLI through `npx neon@latest`; no global npm CLI installation was needed.
- The initial `neon init` installed the Neon plugin for Codex, Claude Desktop, and VS Code, and user-level Neon skills plus MCP configuration for GitHub Copilot CLI. These are user-level installations, not application dependencies.
- Verified the Codex plugin is enabled and contains the OAuth MCP endpoint `https://mcp.neon.tech/mcp`. No duplicate project-level MCP configuration was added.
- Verified the seven installed user-level skills report `up to date`: neon, neon-postgres, neon-auth, neon-functions, neon-object-storage, neon-ai-gateway, and neon-realtime.
- Added `neon.ts`, root `package.json`, and `package-lock.json` using the CLI. The root npm dependencies are infrastructure tooling (`@neon/config` and `@neon/env`), not a replacement for the .NET application.
- Added `.neon` to `.gitignore`. Existing environment-file exclusions were retained.
- The CLI wrote the local project link to `.neon` and pulled `DATABASE_URL`, `DATABASE_URL_UNPOOLED`, and `NEON_BRANCH` into `.env.local`. Both files are ignored by Git. Never copy their values into frontend code or documentation.
- Completed CLI browser authentication with the account owner.

## Verification

- Fetched and read `https://neon.com/.well-known/agent-skills/neon/SKILL.md`.
- `neon deploy`: `No changes — branch production already matches the policy.` Environment pull succeeded.
- `neon config plan`: `No changes — branch production already matches the policy.`
- Frontend `npm run build`, with `BACKEND_URL=http://localhost:5080`: passed, including `Bundle check passed: no configuration placeholders left.` This URL is a local build target, not a deployed backend.
- The frontend build reports existing bundle/style budget warnings and CommonJS dependency warnings.

## Application work remaining at the initial setup checkpoint

The existing backend is an AWS Lambda library and still depends on AWS services. A successful Neon setup and Angular build do not prove an application connection to PostgreSQL. SQL connectivity, PostgreSQL persistence, replacement authentication, an HTTP host, and end-to-end application checks remain to be implemented and verified before Render deployment.

For .NET application traffic, use a server-side PostgreSQL driver such as Npgsql with the pooled connection. Use the direct connection for schema migrations. Keep credentials on the backend.

The installed MCP definition is present on disk; a successful MCP tool call in this Codex session has not been verified. Newly installed tools may require reloading the session and OAuth authorization.
