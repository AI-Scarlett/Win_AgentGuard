param(
  [string]$Configuration = "Release",
  [string]$Runtime = "win-x64",
  [string]$Version = "0.1.0"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root "artifacts"
$publishRoot = Join-Path $artifacts "publish"
$appOut = Join-Path $publishRoot "AgentGuard"
$bridgeOut = Join-Path $publishRoot "HookBridge"
$packagePath = Join-Path $artifacts "AgentGuard-Windows-$Configuration-$Runtime.zip"
$installerPath = Join-Path $artifacts "AgentGuardSetup.exe"
$iconPath = Join-Path $root "src\AgentGuard.App\Assets\AppIcon.ico"

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
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=false `
    -o $appOut

  @("WindowsBase.dll", "PresentationCore.dll", "PresentationFramework.dll") | ForEach-Object {
    $path = Join-Path $appOut $_
    if (-not (Test-Path $path)) {
      throw "WPF runtime dependency missing from publish output: $_"
    }
  }

  dotnet publish .\src\AgentGuard.HookBridge\AgentGuard.HookBridge.csproj `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=false `
    -o $bridgeOut

  $wpfHashBefore = (Get-FileHash (Join-Path $appOut "WindowsBase.dll") -Algorithm SHA256).Hash
  Copy-Item (Join-Path $bridgeOut "agentguard-bridge.*") $appOut -Force
  @("WindowsBase.dll", "PresentationCore.dll", "PresentationFramework.dll") | ForEach-Object {
    $path = Join-Path $appOut $_
    if (-not (Test-Path $path)) {
      throw "WPF runtime dependency missing after bridge merge: $_"
    }
  }
  $wpfHashAfter = (Get-FileHash (Join-Path $appOut "WindowsBase.dll") -Algorithm SHA256).Hash
  if ($wpfHashBefore -ne $wpfHashAfter) {
    throw "Bridge merge replaced the WPF WindowsBase.dll runtime."
  }
  Compress-Archive -Path (Join-Path $appOut "*") -DestinationPath $packagePath -Force
  Write-Host "Package created: $packagePath"

  $iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue
  if (-not $iscc) {
    $candidate = Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"
    if (Test-Path $candidate) {
      $iscc = Get-Item $candidate
    }
  }

  if (-not $iscc) {
    throw "Inno Setup compiler (ISCC.exe) was not found. Install Inno Setup 6 before building the installer."
  }

  $env:AGENTGUARD_INSTALLER_SOURCE = $appOut
  $env:AGENTGUARD_INSTALLER_OUTPUT = $artifacts
  $env:AGENTGUARD_INSTALLER_VERSION = $Version
  $env:AGENTGUARD_INSTALLER_ICON = $iconPath
  & $iscc.Path .\scripts\installer.iss
  if (-not (Test-Path $installerPath)) {
    throw "Installer was not created: $installerPath"
  }

  Write-Host "Installer created: $installerPath"
}
finally {
  Remove-Item Env:\AGENTGUARD_INSTALLER_SOURCE -ErrorAction SilentlyContinue
  Remove-Item Env:\AGENTGUARD_INSTALLER_OUTPUT -ErrorAction SilentlyContinue
  Remove-Item Env:\AGENTGUARD_INSTALLER_VERSION -ErrorAction SilentlyContinue
  Remove-Item Env:\AGENTGUARD_INSTALLER_ICON -ErrorAction SilentlyContinue
  Pop-Location
}
