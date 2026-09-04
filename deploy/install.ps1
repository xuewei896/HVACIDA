# 编译 HVACIDA 解决方案并安装 .addin 到 Revit 2020。
# 用法(在 D:\DSH 目录下执行):
#   powershell -ExecutionPolicy Bypass -File .\deploy\install.ps1 [-Configuration Debug] [-RevitYear 2020]
param(
    [string]$Configuration = "Debug",
    [int]$RevitYear = 2020
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

# 1) 编译(优先用 VS MSBuild,退回 dotnet CLI)
Write-Host "== 编译 $Configuration ==" -ForegroundColor Cyan
$msbuild = Get-ChildItem "C:\Program Files\Microsoft Visual Studio\2022\*\MSBuild\Current\Bin\MSBuild.exe" `
    -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $msbuild) {
    $msbuild = Get-ChildItem "C:\Program Files (x86)\Microsoft Visual Studio\2019\*\MSBuild\Current\Bin\MSBuild.exe" `
        -ErrorAction SilentlyContinue | Select-Object -First 1
}

$sln = Join-Path $root "HVACIDA.sln"
if ($msbuild) {
    & $msbuild.FullName $sln /restore /p:Configuration=$Configuration /m
    if ($LASTEXITCODE -ne 0) { throw "MSBuild 失败,退出码 $LASTEXITCODE" }
}
else {
    dotnet build $sln -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "dotnet build 失败,退出码 $LASTEXITCODE" }
}

# 2) 生成 .addin(绝对路径写入)
$dll = Join-Path $root "src\HVACIDA.Revit\bin\$Configuration\HVACIDA.Revit.dll"
if (-not (Test-Path $dll)) { throw "未找到输出程序集: $dll" }

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
    <VendorDescription>暖通空调智能设计助手(骨架版)</VendorDescription>
  </AddIn>
</RevitAddIns>
"@

$target = Join-Path $addinDir "HVACIDA.addin"
Set-Content -Path $target -Value $xml -Encoding UTF8
Write-Host "== 已安装 $target ==" -ForegroundColor Green
Write-Host "输出程序集: $dll"
Write-Host "提示:安装到 ProgramData 可能需要管理员权限;请重启 Revit 2020 后查看 HVACIDA 页。"
