# HVACIDA Ribbon 结构自检(不需要打开 Revit):
# 反射加载 HVACIDA.Revit.dll(引用本机 Revit 2020 API),检查:
#   1) App 的 22 个模块键与 Core.ModuleCatalog 完全一致(键 → 命令一一对应);
#   2) 每个命令类型都实现 IExternalCommand;
#   3) 每个命令类型都标注了 [Autodesk.Revit.Attributes.Transaction](Revit 2020 硬性要求)。
# 用法: powershell.exe -STA -ExecutionPolicy Bypass -File tools\HVACIDA.Smoke\ribbon-smoke.ps1 -RevitDir <...> -BinDir <...>

param(
    [string]$RevitDir = 'C:\Program Files\Autodesk\Revit 2020',
    [Parameter(Mandatory = $true)][string]$BinDir
)

$ErrorActionPreference = 'Stop'

$revitApi = Join-Path $RevitDir 'RevitAPI.dll'
$revitUi = Join-Path $RevitDir 'RevitAPIUI.dll'
$core = Join-Path $BinDir 'HVACIDA.Core.dll'
$revitAsm = Join-Path $BinDir 'HVACIDA.Revit.dll'
foreach ($p in @($revitApi, $revitUi, $core, $revitAsm)) {
    if (-not (Test-Path $p)) { throw "找不到 $p" }
}

Add-Type -Path $revitApi
Add-Type -Path $revitUi
Add-Type -Path $core
Add-Type -Path $revitAsm

$fail = 0
function Fail([string]$msg) { Write-Host ("FAIL  " + $msg); $script:fail++ }
function Pass([string]$msg) { Write-Host ("PASS  " + $msg) }

# ---- 1. 命令映射覆盖检查 ----
$appType = [HVACIDA.Revit.App]
$field = $appType.GetField('CommandMap', [System.Reflection.BindingFlags]'NonPublic,Static')
if ($null -eq $field) { throw '未找到 App.CommandMap(私有静态字段)' }

$map = $field.GetValue($null)
$mapKeys = @($map.Keys | Sort-Object)
$catalogKeys = @([HVACIDA.Core.Services.ModuleCatalog]::Keys | Sort-Object)

if ($mapKeys.Count -eq 22) { Pass "命令映射条目 = 22" } else { Fail "命令映射条目 = $($mapKeys.Count)(应为 22)" }
if (($mapKeys -join ',') -eq ($catalogKeys -join ',')) { Pass '映射键与 ModuleCatalog 键完全一致' }
else {
    Fail '映射键与 ModuleCatalog 不一致'
    Write-Host ('  仅目录有: ' + (($catalogKeys | Where-Object { $mapKeys -notcontains $_ }) -join ','))
    Write-Host ('  仅映射有: ' + (($mapKeys | Where-Object { $catalogKeys -notcontains $_ }) -join ','))
}

# ---- 2/3. 每个命令:实现 IExternalCommand + 标注 [Transaction] ----
$extCmd = [Autodesk.Revit.UI.IExternalCommand]
$txAttrName = 'Autodesk.Revit.Attributes.TransactionAttribute'
$notExternal = @()
$notTransaction = @()
$rows = @()
foreach ($k in ($map.Keys | Sort-Object)) {
    $t = $map[$k]
    if (-not $extCmd.IsAssignableFrom($t)) { $notExternal += "$k → $($t.Name)" }
    $hasTx = @($t.GetCustomAttributes($true) | Where-Object { $_.GetType().FullName -eq $txAttrName }).Count -gt 0
    if (-not $hasTx) { $notTransaction += "$k → $($t.Name)" }
    $rows += [pscustomobject]@{ Key = $k; Command = $t.Name; ExternalCommand = $extCmd.IsAssignableFrom($t); Transaction = $hasTx }
}

if ($notExternal.Count -eq 0) { Pass '22 个命令均实现 IExternalCommand' } else { Fail ('未实现 IExternalCommand: ' + ($notExternal -join '; ')) }
if ($notTransaction.Count -eq 0) { Pass '22 个命令均标注 [Transaction Manual]' } else { Fail ('缺少 [Transaction]: ' + ($notTransaction -join '; ')) }

# ---- 面板/按钮数复核(按目录,与 App 建面板逻辑同源) ----
foreach ($p in [HVACIDA.Core.Services.ModuleCatalog]::PanelOrder) {
    $n = ([HVACIDA.Core.Services.ModuleCatalog]::ByPanel($p)).Count
    Write-Host ("      面板「{0}」按钮数 = {1}" -f $p, $n)
}

Write-Host '---- 键 → 命令 一览 ----'
$rows | Format-Table -AutoSize | Out-String -Width 200 | Write-Host

Write-Host '=================================================='
if ($fail -eq 0) { Write-Host 'Ribbon 结构自检全部通过'; exit 0 }
Write-Host ("Ribbon 结构自检失败 " + $fail + " 项"); exit 1
