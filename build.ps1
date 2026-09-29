$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskOldAppData = $env:APPDATA
$taskOldCliHome = $env:DOTNET_CLI_HOME
try {
    $env:APPDATA = Join-Path $taskRoot '.build/appdata'
    $env:DOTNET_CLI_HOME = Join-Path $taskRoot '.build/dotnet'
    $taskProject = Join-Path $taskRoot 'src/Launcher/Launcher.csproj'
    $taskConfig = Join-Path $taskRoot 'src/NuGet.Config'
    dotnet restore $taskProject --configfile $taskConfig -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
    dotnet publish $taskProject -c Release --no-restore -o (Join-Path $taskRoot 'app')
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    Write-Host 'Build complete: app/PCL-OpenClaw-Launcher.exe'
} finally {
    $env:APPDATA = $taskOldAppData
    $env:DOTNET_CLI_HOME = $taskOldCliHome
}
