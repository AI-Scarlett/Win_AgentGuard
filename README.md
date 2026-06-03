# AgentGuard for Windows

AgentGuard for Windows is a native WPF port of the current macOS AgentGuard feature set. It keeps the same product shape:

- Agent Center with approvals first
- local hook server for PermissionRequest, AskQuestion, and PlanApproval events
- agent integration detection and hook installation
- session, token, tool, and audit tracking
- protected-folder monitoring
- command rules and sensitive-file alerts

The macOS app uses FSEvents and a Unix domain socket. This Windows version uses `FileSystemWatcher` and a local named pipe:

```text
\\.\pipe\agentguard-hook
```

## Project Layout

```text
Win_AgentGuard/
  AgentGuard.Windows.sln
  docs/
    PROJECT_REQUIREMENTS.md
  src/
    AgentGuard.App/          WPF desktop application
    AgentGuard.Core/         models, hook server, stores, monitors
    AgentGuard.HookBridge/   stdin-to-named-pipe bridge for agent hooks
  tools/
    AgentGuard.SmokeTest/    small console smoke checks
  scripts/
    build.ps1
    install-hooks.ps1
```

## Build on Windows

Install .NET 8 SDK, then run:

```powershell
cd $HOME\Downloads\Win_AgentGuard
.\scripts\build.ps1
```

Or open `AgentGuard.Windows.sln` in Visual Studio 2022.

## Package on Windows

Create a framework-dependent win-x64 package:

```powershell
.\scripts\package.ps1 -Configuration Release -Runtime win-x64
```

The output zip is written to:

```text
artifacts\AgentGuard-Windows-Release-win-x64.zip
```

The package script publishes the WPF app and copies `agentguard-bridge.exe` into
the same folder so hook installation can point agents at the bridge executable.
The GitHub Actions workflow in `.github\workflows\windows-package.yml` runs the
same script on `windows-latest` and uploads the zip as a workflow artifact.

## Language

AgentGuard follows the current UI culture and supports English and Chinese UI
text. Set `AGENTGUARD_LANG` to force a language during testing:

```powershell
$env:AGENTGUARD_LANG = "zh-CN"
dotnet run --project .\src\AgentGuard.App\AgentGuard.App.csproj
```

Use `en-US` to force English.

## Requirements

The Windows maintenance handoff and feature requirements live in:

```text
docs\PROJECT_REQUIREMENTS.md
```

## Run

```powershell
dotnet run --project .\src\AgentGuard.App\AgentGuard.App.csproj
```

The app stores local state under:

```text
%APPDATA%\AgentGuard
```

## Hook Protocol

Agent hooks can send a single JSON object per line to the bridge. The bridge forwards it to AgentGuard and prints the response:

```powershell
'{"event":"PermissionRequest","session_id":"demo","agent":"Codex","cwd":"C:\\Work","tool":"Write","tool_input":"{\"file_path\":\"C:\\Work\\app.cs\"}","diff":"demo change","options":["allow","deny"]}' |
  .\src\AgentGuard.HookBridge\bin\Debug\net8.0\agentguard-bridge.exe
```

Permission responses include both the simple fields and the hook-specific output shape used by the macOS implementation.

## Current Scope

This version is intentionally native and dependency-light. It does not depend on Electron, Avalonia, or external NuGet UI packages.

Implemented now:

- WPF shell and Agent Center tabs
- named-pipe hook server
- pending approval/question/plan queue
- JSON/YAML hook installer with AgentGuard sentinels
- process polling for known AI agent names
- protected folder audit through `FileSystemWatcher`
- command rule matching, sensitive file alerts, hourly operation statistics
- JSON persistence under `%APPDATA%\AgentGuard`

Planned hardening:

- ETW provider for richer process-to-file attribution
- Windows notification toast integration
- installer/MSIX packaging
- code signing and auto-start registration
