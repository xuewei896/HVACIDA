# HVACIDA deployment: build solution and install .addin into Revit Addins folder.
# Usage (run from D:\DSH):
#   powershell -ExecutionPolicy Bypass -File .\deploy\install.ps1 [-Configuration Debug] [-RevitYear 2020]
# NOTE: pure ASCII on purpose (Windows PowerShell 5.1 + ANSI codepage safety).

param(
    [string]$Configuration = "Debug",
    [int]$RevitYear = 2020
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

# 1) Build (prefer VS MSBuild; fallback dotnet CLI)
Write-Host "== Build $Configuration ==" -ForegroundColor Cyan
$msbuild = Get-ChildItem "C:\Program Files\Microsoft Visual Studio\2022\*\MSBuild\Current\Bin\MSBuild.exe" `
    -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $msbuild) {
    $msbuild = Get-ChildItem "C:\Program Files (x86)\Microsoft Visual Studio\2019\*\MSBuild\Current\Bin\MSBuild.exe" `
        -ErrorAction SilentlyContinue | Select-Object -First 1
}

$sln = Join-Path $root "HVACIDA.sln"
if ($msbuild) {
    # /nodeReuse:false + /m:1: avoid MSBuild worker nodes (named pipes) which fail inside sandboxes/CI.
    & $msbuild.FullName $sln /restore /p:Configuration=$Configuration /nodeReuse:false /m:1 /p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw "MSBuild failed with exit code $LASTEXITCODE" }
}
else {
    dotnet build $sln -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }
}

# 2) Write .addin manifest with absolute DLL path (SDK-style projects output under TFM subfolder)
$dll = Join-Path $root "src\HVACIDA.Revit\bin\$Configuration\net48\HVACIDA.Revit.dll"
if (-not (Test-Path $dll)) { throw "Output assembly not found: $dll" }

$addinDir = Join-Path $env:ProgramData "Autodesk\Revit\Addins\$RevitYear"
New-Item -ItemType Directory -Force -Path $addinDir | Out-Null

$xml = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>HVACIDA</Name>
    <Assembly>$dll</Assembly>
    <AddInId>D257A0A5-CF45-416B-8B08-A1B3CB8A40AB</AddInId>
    <FullClassName>HVACIDA.Revit.App</FullClassName>
    <VendorId>HVACIDA</VendorId>
    <VendorDescription>HVACIDA</VendorDescription>
  </AddIn>
</RevitAddIns>
"@

$target = Join-Path $addinDir "HVACIDA.addin"
Set-Content -Path $target -Value $xml -Encoding UTF8
Write-Host "== Installed: $target ==" -ForegroundColor Green
Write-Host "Output assembly: $dll"
Write-Host "Note: writing to ProgramData may need admin rights. Restart Revit $RevitYear and check the HVACIDA tab."
