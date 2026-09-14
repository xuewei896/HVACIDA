# HVACIDA Ribbon 结构自检(不需要打开 Revit):
# 反射加载 HVACIDA.Revit.dll(引用本机 Revit 2020 API),检查:
#   1) App 的 22 个模块键与 Core.ModuleCatalog 完全一致(键 → 命令一一对应);
#   2) 每个命令类型都实现 IExternalCommand;
#   3) 每个命令类型都标注了 [Autodesk.Revit.Attributes.Transaction](Revit 2020 硬性要求)。
# 用法: powershell.exe -STA -ExecutionPolicy Bypass -File tools\HVACIDA.Smoke\ribbon-smoke.ps1 -RevitDir <...> -BinDir <...>

param(
    [string]$RevitDir = 'C:\Program Files\Autodesk\Revit 2020',
    [Parameter(Mandatory = $true)][string]$BinDir,
    [switch]$Child,
    [int]$TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

# =============================================================================
# 父进程:只做"起子进程 + 读结论 + 给退出码",**本进程绝不加载 RevitAPI**
#
# 为什么必须拆两层:RevitAPIUI.dll 是混合模式(C++/CLI)程序集,实测在 Windows PowerShell 5.1 下
# 只要加载它,进程在输出完所有结论后就**无法退出**(`exit`、`[Environment]::Exit()` 都无效 —— CLR 关闭挂起)。
# 经逐步二分确认:只加载 RevitAPI+RevitAPIUI 就会挂(与本仓库任何代码无关),而 WPF 图像解码本身不挂。
# 于是:子进程干脏活(允许它挂),父进程只收结论 —— 门禁的退出码因此始终干净可用。
# =============================================================================
if (-not $Child) {
    if (-not (Test-Path -LiteralPath $BinDir)) {
        Write-Host ('找不到 BinDir: ' + $BinDir)
        exit 1
    }

    $self = $MyInvocation.MyCommand.Path
    $stdout = [System.IO.Path]::GetTempFileName()
    $stderr = [System.IO.Path]::GetTempFileName()
    try {
        # 路径含空格(如 C:\Program Files\...),Start-Process 不会自动加引号 → 必须显式带上
        $childArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $self + '"'),
                       '-Child', '-BinDir', ('"' + (Resolve-Path -LiteralPath $BinDir).Path + '"'),
                       '-RevitDir', ('"' + $RevitDir + '"'))
        $proc = Start-Process -FilePath 'powershell.exe' -ArgumentList $childArgs `
            -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru -NoNewWindow

        if (-not $proc.WaitForExit($TimeoutSeconds * 1000)) {
            try { $proc.Kill() } catch { }
            Write-Host ("子进程超时 {0}s,已终止。" -f $TimeoutSeconds)
        }

        $lines = @(Get-Content -LiteralPath $stdout -Encoding UTF8 -ErrorAction SilentlyContinue)
        foreach ($l in $lines) { Write-Host $l }

        $verdict = $lines | Where-Object { $_ -match '^GATE-RESULT:' } | Select-Object -Last 1
        $failCount = @($lines | Where-Object { $_ -match '^FAIL' }).Count
        $errText = ((Get-Content -LiteralPath $stderr -Encoding UTF8 -ErrorAction SilentlyContinue) -join "`n").Trim()

        Write-Host '=================================================='
        if ($verdict -match 'PASS' -and $failCount -eq 0) {
            Write-Host 'Ribbon 结构自检全部通过'
            exit 0
        }

        if ($verdict) { Write-Host ('Ribbon 结构自检失败 ' + $failCount + ' 项') }
        else { Write-Host 'Ribbon 结构自检未产出结论(子进程异常/超时)' }
        if ($errText) { Write-Host '---- 子进程 stderr ----'; Write-Host $errText }
        exit 1
    }
    finally {
        Remove-Item -LiteralPath $stdout, $stderr -Force -ErrorAction SilentlyContinue
    }
}

# =============================================================================
# 子进程:真正加载 RevitAPI 做反射自检
# =============================================================================

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

# ---- 4. 图标:每个模块必须有 16/32 两套内嵌 PNG,尺寸正确且能解码成已冻结的 ImageSource ----
Add-Type -AssemblyName PresentationCore, WindowsBase
$resNames = [HVACIDA.Revit.ModuleIcons].Assembly.GetManifestResourceNames()
$missing16 = @(); $missing32 = @(); $badSize = @(); $undecodable = @(); $badCoverage = @(); $coverage = @()
foreach ($k in ($map.Keys | Sort-Object)) {
    if ($resNames -notcontains "HVACIDA.Revit.Resources.Icons.${k}_16.png") { $missing16 += $k }
    if ($resNames -notcontains "HVACIDA.Revit.Resources.Icons.${k}_32.png") { $missing32 += $k }

    foreach ($size in @(16, 32)) {
        try {
            $img = [HVACIDA.Revit.ModuleIcons]::Get($k, $size)
            if ($null -eq $img) { $undecodable += "$k/$size(null)"; continue }
            if ($img.PixelWidth -ne $size -or $img.PixelHeight -ne $size) {
                $badSize += ("{0}/{1}({2}x{3})" -f $k, $size, $img.PixelWidth, $img.PixelHeight)
            }
            if (-not $img.IsFrozen) { $undecodable += "$k/$size(未冻结)" }

            # 覆盖率:抓"整张全透明(图标空白)"与"整块实心(糊成一坨)"两种坏图
            $conv = New-Object System.Windows.Media.Imaging.FormatConvertedBitmap(
                $img, [System.Windows.Media.PixelFormats]::Bgra32, $null, 0)
            $stride = $size * 4
            $bytes = New-Object byte[] ($stride * $size)
            $conv.CopyPixels($bytes, $stride, 0)
            $inked = 0
            for ($i = 3; $i -lt $bytes.Length; $i += 4) { if ($bytes[$i] -gt 8) { $inked++ } }
            $pct = [math]::Round(100.0 * $inked / ($size * $size), 1)
            $coverage += [pscustomobject]@{ Key = $k; Size = $size; Percent = $pct }
            if ($pct -lt 5 -or $pct -gt 90) { $badCoverage += ("{0}/{1}={2}%" -f $k, $size, $pct) }
        } catch {
            $undecodable += "$k/$size($($_.Exception.Message))"
        }
    }
}

if ($missing16.Count -eq 0) { Pass '22 个模块均有 16×16 内嵌图标' } else { Fail ('缺 16×16 图标: ' + ($missing16 -join ',')) }
if ($missing32.Count -eq 0) { Pass '22 个模块均有 32×32 内嵌图标' } else { Fail ('缺 32×32 图标: ' + ($missing32 -join ',')) }
if ($badSize.Count -eq 0) { Pass '图标尺寸均为 16×16 / 32×32' } else { Fail ('尺寸不符: ' + ($badSize -join '; ')) }
if ($undecodable.Count -eq 0) { Pass '图标可解码为已冻结的 ImageSource(可跨线程用于 Ribbon)' } else { Fail ('解码失败: ' + ($undecodable -join '; ')) }
if ($badCoverage.Count -eq 0) {
    $min = ($coverage | Measure-Object Percent -Minimum).Minimum
    $max = ($coverage | Measure-Object Percent -Maximum).Maximum
    Pass ("图标覆盖率正常(要求 5%~90%,实测 {0}%~{1}%)" -f $min, $max)
} else {
    Fail ('图标覆盖率异常(空白或糊成一坨): ' + ($badCoverage -join '; '))
}
Write-Host ("      内嵌图标资源共 {0} 个({1} 个模块 × 2 尺寸)" -f $resNames.Count, $map.Keys.Count)

Write-Host '---- 键 → 命令 一览 ----'
$rows | Format-Table -AutoSize | Out-String -Width 200 | Write-Host

Write-Host '=================================================='
if ($fail -eq 0) { Write-Host 'GATE-RESULT: PASS' }
else { Write-Host ('GATE-RESULT: FAIL ' + $fail) }

# 收尾:本进程已加载 RevitAPIUI(混合模式程序集),CLR 关闭会挂起 ——
# `exit` / `[Environment]::Exit()` 在这里都无法结束进程(见文件头说明),故显式强制结束自身。
# 结论已通过 GATE-RESULT 行交给父进程,退出码由父进程给出。
[System.Diagnostics.Process]::GetCurrentProcess().Kill()
