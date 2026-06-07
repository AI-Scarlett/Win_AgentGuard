param(
  [string]$Agent = "all",
  [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$bridge = Join-Path $root "src\AgentGuard.HookBridge\bin\$Configuration\net8.0\agentguard-bridge.exe"

if (-not (Test-Path $bridge)) {
  dotnet build (Join-Path $root "src\AgentGuard.HookBridge\AgentGuard.HookBridge.csproj") -c $Configuration
}

dotnet run --project (Join-Path $root "tools\AgentGuard.SmokeTest\AgentGuard.SmokeTest.csproj") -- --install-hooks $Agent --bridge $bridge
