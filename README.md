# AgentGuard for Windows

Native WPF port of the macOS [AgentGuard](https://github.com/AI-Scarlett/AIMacCleaner)
feature set. Provides local agent activity monitoring, permission
approvals, hook server, command rules, protected-folder monitoring, and
historical agent history scanning.

The Windows installer is published on the unified AgentGuard release:

👉 **https://github.com/AI-Scarlett/AIMacCleaner/releases/tag/v2.1.9**

| Asset | Size | Use it for |
|---|---|---|
| `AgentGuardSetup.exe` | ~51 MB | Installing AgentGuard on a Windows machine (Inno Setup) |
| `AgentGuard-v2.1.9-arm64.dmg` | ~7 MB | macOS Apple Silicon build (sister project) |

A portable/extract-anywhere build is **not** distributed to keep the
debug-symbol surface minimal — the Inno Setup installer above is the
only Windows binary shipped.

## Feature set

- Agent Center with approvals first
- Local hook server for `PermissionRequest`, `AskQuestion`, and
  `PlanApproval` events
- Agent integration detection and hook installation
- Session, token, tool, and audit tracking
- Protected-folder monitoring
- Command rules and sensitive-file alerts
- Historical Agent activity scanner (Agent History tab)
- Windows Action Center toast for new pending approvals
- Configurable bridge path and audit export
- Process tree attribution (parent PID + parent name) on monitored
  launches

## Building locally

```pwsh
dotnet build .\AgentGuard.Windows.sln -c Release
dotnet run --project .\tools\AgentGuard.SmokeTest\AgentGuard.SmokeTest.csproj -c Release
```

The Windows-only `AgentGuard.App` project requires the .NET 8 SDK on
Windows. The portable `AgentGuard.Core` and `AgentGuard.HookBridge`
projects build cross-platform; use the workflow
`.github/workflows/windows-package.yml` for a full installer build.

## Project docs

- [`docs/AGENT_DATA_FORMATS.md`](docs/AGENT_DATA_FORMATS.md) — research
  notes for Claude / Codex / Cursor / OpenClaw / Hermes session formats,
  paths, and fields used by the Agent History scanner.
- [`docs/PROJECT_AUDIT.md`](docs/PROJECT_AUDIT.md) — original project
  audit log.
- [`docs/PROJECT_REQUIREMENTS.md`](docs/PROJECT_REQUIREMENTS.md) —
  original product requirements.
