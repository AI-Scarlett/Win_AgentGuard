param(
  [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
  dotnet restore .\AgentGuard.Windows.sln
  dotnet build .\AgentGuard.Windows.sln -c $Configuration --no-restore
  dotnet run --project .\tools\AgentGuard.SmokeTest\AgentGuard.SmokeTest.csproj -c $Configuration
}
finally {
  Pop-Location
}
