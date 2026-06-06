# AgentGuard for Windows

> **Repository archived — source code removed.**
>
> This repo is kept only as a release pointer. The Windows source tree
> (`src/`, `tools/`, `scripts/`, `.github/workflows/`, solution file and
> MSBuild props) has been intentionally removed; the build workflow no
> longer exists. The git history up to the removal commit is preserved
> for reference but cannot be used to rebuild the binaries.
>
> To rebuild from scratch, recover the source from your local working
> copy or from the last commit before the removal (`60bda58` and earlier).

## Where to get the binary

The Windows installer and portable build are published on the unified
AgentGuard release:

👉 **https://github.com/AI-Scarlett/AIMacCleaner/releases/tag/v2.1.9**

| Asset | Size | Use it for |
|---|---|---|
| `AgentGuardSetup.exe` | ~51 MB | Installing AgentGuard on a Windows machine (Inno Setup) |
| `AgentGuard-Windows-Release-win-x64.zip` | ~71 MB | Portable / extract-anywhere build |
| `AgentGuard-v2.1.9-arm64.dmg` | ~7 MB | macOS Apple Silicon build (sister project) |

## What AgentGuard for Windows is (was)

AgentGuard for Windows is a native WPF port of the macOS AgentGuard
feature set. It kept the same product shape:

- Agent Center with approvals first
- local hook server for `PermissionRequest`, `AskQuestion`, and
  `PlanApproval` events
- agent integration detection and hook installation
- session, token, tool, and audit tracking
- protected-folder monitoring
- command rules and sensitive-file alerts
- historical Agent activity scanner (Agent History tab)
- Windows Action Center toast for new pending approvals
- configurable bridge path and audit export
- process tree attribution (parent PID + parent name) on monitored
  launches

## Project docs (preserved here for reference)

- [`docs/AGENT_DATA_FORMATS.md`](docs/AGENT_DATA_FORMATS.md) — research
  notes for Claude / Codex / Cursor / OpenClaw / Hermes session formats,
  paths, and fields used by the Agent History scanner.
- [`docs/PROJECT_AUDIT.md`](docs/PROJECT_AUDIT.md) — original project
  audit log.
- [`docs/PROJECT_REQUIREMENTS.md`](docs/PROJECT_REQUIREMENTS.md) —
  original product requirements.

## License

No license file is shipped. Treat the source as All Rights Reserved by
the original authors.
