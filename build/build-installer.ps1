$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$installerDir = Join-Path $repoRoot 'installer'
$installerScript = Join-Path $installerDir 'installer.nsi'
$publishedExe = Join-Path $repoRoot 'publish\RoomSetupLoader.exe'
if (-not (Test-Path -LiteralPath $publishedExe -PathType Leaf)) {
    throw "Published application not found: $publishedExe. Run build\publish.ps1 first."
}
$makensis = Get-Command makensis.exe -ErrorAction SilentlyContinue
if (-not $makensis) {
    $defaultPath = Join-Path ${env:ProgramFiles(x86)} 'NSIS\makensis.exe'
    if (Test-Path -LiteralPath $defaultPath) { $makensisPath = $defaultPath }
    else { throw 'NSIS makensis.exe not found. Install NSIS or add makensis.exe to PATH.' }
} else { $makensisPath = $makensis.Source }
Push-Location $installerDir
try {
    & $makensisPath $installerScript
    if ($LASTEXITCODE -ne 0) { throw "NSIS compilation failed ($LASTEXITCODE)" }
} finally { Pop-Location }
Write-Host 'NSIS installer compilation completed.'
