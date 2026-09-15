# HVACIDA 气象数据库同步门禁
#
# 校验 src\HVACIDA.Core\Resources\weather-db.csv(生成物)与源文件「各省市室外空气参数.md」是否一致:
#   1) CSV 头部记录的源文件 SHA256 与当前源文件是否相符(源文件改了没重跑生成器 -> 拦住);
#   2) CSV 头部记录的台站数是否与实际数据行数相符;
#   3) 重跑生成器的 --check(解析 + 校验, 退出码 0 = 无错误)。
#
# 退出码 0 = 一致;1 = 过期/有错。
#
# 用法: powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\weatherdb\check-weather-db-sync.ps1
#       过期时自动重生成: 加 -Regenerate

param(
    [switch]$Regenerate
)

$ErrorActionPreference = 'Stop'

$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$source = Join-Path $root '各省市室外空气参数.md'
$csv = Join-Path $root 'src\HVACIDA.Core\Resources\weather-db.csv'
$builder = Join-Path $PSScriptRoot 'build_weather_db.py'

$fail = 0

if (-not (Test-Path -LiteralPath $source)) { Write-Host ("FAIL  找不到源文件: " + $source); exit 1 }
if (-not (Test-Path -LiteralPath $csv)) { Write-Host ("FAIL  找不到生成物: " + $csv + "(先跑 python tools\weatherdb\build_weather_db.py)"); exit 1 }

# ---- 1/2. 头部记录的 SHA256 与台站数 ----
$head = Get-Content -LiteralPath $csv -TotalCount 10 -Encoding UTF8
$recordedSha = ($head | Where-Object { $_ -match '^#\s*源文件 SHA256:\s*([0-9a-fA-F]{64})' } |
    ForEach-Object { [regex]::Match($_, '([0-9a-fA-F]{64})').Groups[1].Value } | Select-Object -First 1)
$recordedRows = ($head | Where-Object { $_ -match '^#\s*台站数:\s*(\d+)' } |
    ForEach-Object { [int][regex]::Match($_, '(\d+)').Groups[1].Value } | Select-Object -First 1)

$actualSha = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLower()
$dataRows = (Get-Content -LiteralPath $csv -Encoding UTF8 | Where-Object { $_ -and $_[0] -ne '#' }).Count - 1

if ($recordedSha -eq $actualSha) {
    Write-Host ("PASS  weather-db.csv 与源文件 SHA256 一致(" + $actualSha.Substring(0, 16) + "…)")
}
else {
    Write-Host ("FAIL  源文件已变更但未重跑生成器")
    Write-Host ("      记录: " + $recordedSha)
    Write-Host ("      实际: " + $actualSha)
    $fail++
}

if ($recordedRows -eq $dataRows) {
    Write-Host ("PASS  台站数一致 = " + $dataRows)
}
else {
    Write-Host ("FAIL  台站数不符: 头部记录 " + $recordedRows + ", 实际数据行 " + $dataRows)
    $fail++
}

# ---- 3. 重跑解析校验 ----
if ($Regenerate) {
    Write-Host '---- 重新生成 ----'
    & python $builder
    if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL  重新生成失败'; exit 1 }
    $actualSha = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLower()
    $head = Get-Content -LiteralPath $csv -TotalCount 10 -Encoding UTF8
    $recordedSha = ($head | Where-Object { $_ -match '^#\s*源文件 SHA256' } |
        ForEach-Object { [regex]::Match($_, '([0-9a-fA-F]{64})').Groups[1].Value } | Select-Object -First 1)
    if ($recordedSha -eq $actualSha) { Write-Host 'PASS  重生成后 SHA256 已对齐' } else { Write-Host 'FAIL  重生成后仍不一致'; $fail++ }
}
else {
    & python $builder --check | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Write-Host 'PASS  解析/校验通过(294 台站,省台站数 checksum 全部相符)'
    }
    else {
        Write-Host 'FAIL  解析/校验有问题(详见 tools\weatherdb\weather-db-report.txt)'
        $fail++
    }
}

Write-Host '=================================================='
if ($fail -eq 0) { Write-Host '气象数据库同步校验通过'; exit 0 }
Write-Host ("气象数据库同步校验失败 " + $fail + " 项"); exit 1
