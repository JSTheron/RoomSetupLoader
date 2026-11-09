$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repoRoot
Write-Host 'Publishing self-contained Windows x64 application...'
dotnet publish .\src\RoomSetupLoader.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o .\publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
Write-Host 'Publishing completed. Build installer\installer.nsi using NSIS (makensis.exe).'
Write-Host 'Next run build\build-installer.ps1 to compile the NSIS installer.'
# Keep the NSIS installer version synchronized with the project version.
$projectFile = Join-Path $repoRoot 'src\RoomSetupLoader.csproj'
$installerFile = Join-Path $repoRoot 'installer\installer.nsi'

if (-not (Test-Path -LiteralPath $projectFile -PathType Leaf)) {
    throw "Project file not found: $projectFile"
}
if (-not (Test-Path -LiteralPath $installerFile -PathType Leaf)) {
    throw "NSIS script not found: $installerFile"
}

[xml]$projectXml = Get-Content -LiteralPath $projectFile -Raw
$versionNodes = @($projectXml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })
if ($versionNodes.Count -ne 1) {
    throw "Expected exactly one <Version> value in $projectFile; found $($versionNodes.Count)."
}
$appVersion = [string]$versionNodes[0]
$appVersion = $appVersion.Trim()
if ($appVersion -notmatch '^\d+\.\d+(?:\.\d+){0,2}(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?$') {
    throw "Invalid or unsupported application version '$appVersion'."
}

$installerContent = [System.IO.File]::ReadAllText($installerFile)
$versionPattern = '(?m)^!define[ \t]+APPVER[ \t]+"[^"\r\n]*"[ \t]*$'
$matches = [regex]::Matches($installerContent, $versionPattern)
if ($matches.Count -ne 1) {
    throw "Expected exactly one !define APPVER line in $installerFile; found $($matches.Count)."
}
$updatedContent = [regex]::Replace(
    $installerContent,
    $versionPattern,
    ('!define APPVER "' + $appVersion + '"')
)
if ($updatedContent -ne $installerContent) {
    # Keep the existing text encoding/BOM and avoid changing unrelated NSIS lines.
    $bytes = [System.IO.File]::ReadAllBytes($installerFile)
    $hasUtf8Bom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $encoding = New-Object System.Text.UTF8Encoding($hasUtf8Bom)
    [System.IO.File]::WriteAllText($installerFile, $updatedContent, $encoding)
    Write-Host "Updated installer.nsi APPVER to $appVersion."
} else {
    Write-Host "installer.nsi APPVER already matches $appVersion."
}
