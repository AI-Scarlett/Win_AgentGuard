param(
  [string]$Configuration = "Release",
  [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root "artifacts"
$publishRoot = Join-Path $artifacts "publish"
$appOut = Join-Path $publishRoot "AgentGuard"
$bridgeOut = Join-Path $publishRoot "HookBridge"
$packagePath = Join-Path $artifacts "AgentGuard-Windows-$Configuration-$Runtime.zip"

Push-Location $root
try {
  if (Test-Path $artifacts) {
    Remove-Item $artifacts -Recurse -Force
  }

  New-Item -ItemType Directory -Force -Path $appOut | Out-Null
  New-Item -ItemType Directory -Force -Path $bridgeOut | Out-Null

  dotnet restore .\AgentGuard.Windows.sln
  dotnet build .\AgentGuard.Windows.sln -c $Configuration --no-restore
  dotnet run --project .\tools\AgentGuard.SmokeTest\AgentGuard.SmokeTest.csproj -c $Configuration

  dotnet publish .\src\AgentGuard.App\AgentGuard.App.csproj `
    -c $Configuration `
    -r $Runtime `
    --self-contained false `
    -o $appOut

  dotnet publish .\src\AgentGuard.HookBridge\AgentGuard.HookBridge.csproj `
    -c $Configuration `
    -r $Runtime `
    --self-contained false `
    -o $bridgeOut

  Copy-Item (Join-Path $bridgeOut "*") $appOut -Recurse -Force
  Compress-Archive -Path (Join-Path $appOut "*") -DestinationPath $packagePath -Force
  Write-Host "Package created: $packagePath"
}
finally {
  Pop-Location
}
