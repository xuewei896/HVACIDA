# HVACIDA WPF 窗口装载自检(不需要 Revit):
# 在 STA 线程里把每个窗口真正构造 + Show + UpdateLayout + Close,
# 用于捕获 XAML 解析错误、StaticResource 缺失、绑定路径致命错误。
# 用法: powershell.exe -STA -ExecutionPolicy Bypass -File tools\HVACIDA.Smoke\window-smoke.ps1 -UiDir <dir with HVACIDA.UI.dll + HVACIDA.Core.dll>

param(
    [Parameter(Mandatory = $true)][string]$UiDir
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase

$ui = Join-Path $UiDir 'HVACIDA.UI.dll'
$core = Join-Path $UiDir 'HVACIDA.Core.dll'
if (-not (Test-Path $ui)) { throw "找不到 $ui" }
if (-not (Test-Path $core)) { throw "找不到 $core" }

Add-Type -Path $core
Add-Type -Path $ui

$app = New-Object System.Windows.Application
$app.ShutdownMode = 'OnExplicitShutdown'

$fail = 0
function Test-Window([string]$name, [scriptblock]$factory) {
    try {
        $w = & $factory
        $w.Show()
        $w.UpdateLayout()
        $title = $w.Title
        $w.Close()
        Write-Host ("PASS  {0}  标题=""{1}""  尺寸={2}x{3}" -f $name, $title, [int]$w.Width, [int]$w.Height)
    } catch {
        Write-Host ("FAIL  {0}  {1}" -f $name, $_.Exception.Message)
        $script:fail++
    }
}

$uiNs = 'HVACIDA.UI.Views'
$vmNs = 'HVACIDA.UI.ViewModels'

Test-Window '工程信息 ProjectInfoWindow'      { New-Object "$uiNs.ProjectInfoWindow" }
Test-Window '气象参数 WeatherWindow'          { New-Object "$uiNs.WeatherWindow" }
Test-Window '公共区参数 PublicAreaWindow'      { New-Object "$uiNs.PublicAreaWindow" }
Test-Window '大系统负荷计算 LargeSystemWindow' { New-Object "$uiNs.LargeSystemWindow" }
Test-Window '大系统计算结果 LargeResultWindow' { New-Object "$uiNs.LargeSystemResultWindow" }
Test-Window '小系统负荷计算 SmallSystemWindow' { New-Object "$uiNs.SmallSystemWindow" }
Test-Window '小系统计算结果 SmallResultWindow' { New-Object "$uiNs.SmallSystemResultWindow" }
Test-Window '规范知识库 KnowledgeWindow'       { New-Object "$uiNs.KnowledgeWindow" }
Test-Window '操作指南 InfoWindow(Guide)'      {
    $vm = [HVACIDA.UI.ViewModels.InfoViewModel]::Guide()
    New-Object "$uiNs.InfoWindow" -ArgumentList $vm
}
Test-Window '帮助 InfoWindow(Help)'           {
    $vm = [HVACIDA.UI.ViewModels.InfoViewModel]::Help()
    New-Object "$uiNs.InfoWindow" -ArgumentList $vm
}
Test-Window '待实现说明 InfoWindow(排烟计算)'   {
    $m = [HVACIDA.Core.Services.ModuleCatalog]::Get('large-smoke')
    $vm = [HVACIDA.UI.ViewModels.InfoViewModel]::ForModule($m)
    New-Object "$uiNs.InfoWindow" -ArgumentList $vm
}

# 知识库问答闭环(不依赖窗口)
try {
    $kvm = New-Object "$vmNs.KnowledgeViewModel"
    $kvm.Question = '排烟风机怎么选?'
    $kvm.Ask()
    $last = $kvm.Lines[$kvm.Lines.Count - 1].Text
    if ($last -match '1\.2') { Write-Host "PASS  知识库问答闭环(含选型系数 1.2)"; }
    else { Write-Host "FAIL  知识库问答闭环: $last"; $fail++ }
} catch {
    Write-Host ("FAIL  知识库问答闭环  {0}" -f $_.Exception.Message)
    $fail++
}

# =====================================================================
# 气象参数联动(项目信息 → 大系统 C5/F4/F6,不需要 Revit)
# 用临时目录当仓库,绝不碰 %AppData%\HVACIDA 里的真实数据
# =====================================================================
$tmp = Join-Path $env:TEMP ("HVACIDA-WinSmoke-" + [guid]::NewGuid().ToString('N'))
try {
    $repo = New-Object HVACIDA.Core.Services.XmlProjectRepository -ArgumentList $tmp
    $proj = $repo.LoadProject()
    $proj.Design.LargeSystemOutdoor.SummerACWetBulbC = 25.0
    $proj.Design.LargeSystemIndoor.HallDryBulbC = 29.0
    $proj.Design.LargeSystemIndoor.PlatformDryBulbC = 27.0
    $repo.SaveProject($proj)

    $lvm = New-Object "$vmNs.LargeSystemViewModel" -ArgumentList $repo
    if ($lvm.AutoSyncWeather -eq $true -and $lvm.WeatherLinked -eq $true) {
        Write-Host "PASS  大系统窗默认开启气象联动"
    } else { Write-Host "FAIL  大系统窗气象联动默认值"; $fail++ }

    if ($lvm.Input.OutdoorWetBulbC -eq 25.0 -and $lvm.Input.HallDesignTempC -eq 29.0 -and $lvm.Input.PlatformDesignTempC -eq 27.0) {
        Write-Host ("PASS  大系统窗自动回填 C5/F4/F6 = {0}/{1}/{2}" -f $lvm.Input.OutdoorWetBulbC, $lvm.Input.HallDesignTempC, $lvm.Input.PlatformDesignTempC)
    } else {
        Write-Host ("FAIL  大系统窗回填 C5/F4/F6 = {0}/{1}/{2}" -f $lvm.Input.OutdoorWetBulbC, $lvm.Input.HallDesignTempC, $lvm.Input.PlatformDesignTempC)
        $fail++
    }

    $lvm.AutoSyncWeather = $false
    if ($lvm.Input.WeatherManuallyOverridden -eq $true -and $lvm.WeatherLinked -eq $false) {
        Write-Host "PASS  取消勾选后转为手工输入(字段可编辑)"
    } else { Write-Host "FAIL  取消气象联动未生效"; $fail++ }

    $lvm.AutoSyncWeather = $true
    if ($lvm.Input.WeatherManuallyOverridden -eq $false) {
        Write-Host "PASS  重新勾选后恢复联动"
    } else { Write-Host "FAIL  恢复气象联动未生效"; $fail++ }

    # DataTrigger 实际生效校验(联动时 C5 只读,取消后转为可编辑)
    $wsW = New-Object "$uiNs.LargeSystemWindow" -ArgumentList $lvm
    $wsW.Show(); $wsW.UpdateLayout()
    $wb = $wsW.FindName('WetBulbBox')
    if ($wb -ne $null -and $wb.IsReadOnly -eq $true) {
        Write-Host "PASS  联动期间 C5 字段只读灰底(WeatherField 触发器生效)"
    } else { Write-Host "FAIL  C5 字段联动锁定: IsReadOnly=$($wb.IsReadOnly)"; $fail++ }

    $lvm.AutoSyncWeather = $false
    $wsW.UpdateLayout()
    if ($wb.IsReadOnly -eq $false) {
        Write-Host "PASS  取消联动后 C5 字段转为可编辑"
    } else { Write-Host "FAIL  取消联动后 C5 仍只读"; $fail++ }
    $lvm.AutoSyncWeather = $true
    $wsW.Close()

    # 计算结果窗与负荷计算窗必须看到同一份联动值(此前会各自读文件导致口径分叉)
    $rrepo = New-Object HVACIDA.Core.Services.XmlProjectRepository -ArgumentList $tmp
    $rvm = New-Object "$vmNs.LargeResultViewModel" -ArgumentList $rrepo
    if ($rvm.Input.OutdoorWetBulbC -eq 25.0) {
        Write-Host "PASS  计算结果窗共享同一份气象联动值"
    } else { Write-Host ("FAIL  计算结果窗 C5 = {0}" -f $rvm.Input.OutdoorWetBulbC); $fail++ }
} catch {
    Write-Host ("FAIL  气象参数联动自检  {0}" -f $_.Exception.Message)
    $fail++
} finally {
    try { Remove-Item $tmp -Recurse -Force -ErrorAction Stop } catch { }
}

# =====================================================================
# 公共区几何:模型空间取值(不需要 Revit,用 SpaceSnapshot 直接构造)
# =====================================================================
try {
    $spaces = New-Object 'System.Collections.Generic.List[HVACIDA.Core.Models.SpaceSnapshot]'
    function Add-Space([string]$name, [string]$number, [string]$level, [double]$area, [double]$h, [double]$len) {
        $s = New-Object HVACIDA.Core.Models.SpaceSnapshot
        $s.Name = $name; $s.Number = $number; $s.LevelName = $level
        $s.AreaM2 = $area; $s.HeightM = $h; $s.VolumeM3 = $area * $h
        $s.MinXM = 0; $s.MinYM = 0; $s.MaxXM = $len; $s.MaxYM = 20
        $script:spaces.Add($s)
    }
    Add-Space '站厅层-公共区' '101' '站厅层' 2000 4.9 101
    Add-Space '站厅层-付费区' '102' '站厅层' 300 4.9 20
    Add-Space '站台层-公共区' '201' '站台层' 1620 4.5 140
    Add-Space '活塞风道' '301' '设备层' 40 4.0 8

    $tmp2 = Join-Path $env:TEMP ("HVACIDA-WinSmoke-" + [guid]::NewGuid().ToString('N'))
    $repo2 = New-Object HVACIDA.Core.Services.XmlProjectRepository -ArgumentList $tmp2
    $pvm = New-Object "$vmNs.PublicAreaViewModel" -ArgumentList $repo2, $spaces, '自检注入的空间', $true

    if ($pvm.IsGeometryLocked -eq $true) { Write-Host "PASS  未取模型值前几何字段只读(灰底)" }
    else { Write-Host "FAIL  几何字段应默认只读"; $fail++ }

    $pvm.AutoDetectCommand.Execute($null)
    if ($pvm.Input.HallAreaM2 -eq 2000 -and $pvm.Input.PlatformAreaM2 -eq 1620) {
        Write-Host ("PASS  自动识别回填 D55/D56 = {0}/{1} m²" -f $pvm.Input.HallAreaM2, $pvm.Input.PlatformAreaM2)
    } else {
        Write-Host ("FAIL  自动识别回填 D55/D56 = {0}/{1}" -f $pvm.Input.HallAreaM2, $pvm.Input.PlatformAreaM2)
        $fail++
    }

    if ($pvm.Input.HallHeightM -eq 4.9 -and $pvm.Input.HallLengthM -eq 101) {
        Write-Host ("PASS  自动识别回填 C13/C14 = {0}/{1}" -f $pvm.Input.HallHeightM, $pvm.Input.HallLengthM)
    } else {
        Write-Host ("FAIL  自动识别回填 C13/C14 = {0}/{1}" -f $pvm.Input.HallHeightM, $pvm.Input.HallLengthM)
        $fail++
    }

    if ($pvm.GeometryFromModel -eq $true -and $pvm.IsGeometryLocked -eq $false) {
        Write-Host "PASS  取到模型值后几何字段解锁可手改"
    } else { Write-Host "FAIL  取模型值后应解锁"; $fail++ }

    if ($pvm.SpaceRows.Count -eq 4) {
        $cats = ($pvm.SpaceRows | ForEach-Object { $_.Category }) -join ' | '
        Write-Host ("PASS  明细列出全部 4 个空间(含被筛掉的): {0}" -f $cats)
    } else { Write-Host ("FAIL  明细行数 = {0}" -f $pvm.SpaceRows.Count); $fail++ }

    # 手动拾取回填(命令层拾取完成后调用;此处直接喂快照)
    $picked = New-Object 'System.Collections.Generic.List[HVACIDA.Core.Models.SpaceSnapshot]'
    $ps = New-Object HVACIDA.Core.Models.SpaceSnapshot
    $ps.Name = '站台层-公共区'; $ps.Number = '201'; $ps.LevelName = '站台层'
    $ps.AreaM2 = 1700; $ps.HeightM = 4.6; $ps.VolumeM3 = 1700 * 4.6
    $picked.Add($ps)
    $pvm.RequestPick([HVACIDA.Core.Services.PublicAreaTarget]::Platform)
    if ($pvm.PendingPickTarget -eq [HVACIDA.Core.Services.PublicAreaTarget]::Platform) {
        Write-Host "PASS  拾取请求把目标交回命令层"
    } else { Write-Host "FAIL  拾取目标未记录"; $fail++ }
    $pvm.ClearPendingPick()
    $pvm.ApplyPick([HVACIDA.Core.Services.PublicAreaTarget]::Platform, $picked, '拾取 1 个站台空间')
    if ($pvm.Input.PlatformAreaM2 -eq 1700) {
        Write-Host "PASS  手动拾取回填 D56 = 1700 m²"
    } else { Write-Host ("FAIL  手动拾取回填 D56 = {0}" -f $pvm.Input.PlatformAreaM2); $fail++ }

    Test-Window '公共区参数(带模型空间) PublicAreaWindow' { New-Object "$uiNs.PublicAreaWindow" -ArgumentList $pvm }

    # DataTrigger 是否真的生效(绑定写错不会抛异常,只会静默不生效 —— 必须实际读控件属性)
    $pw = New-Object "$uiNs.PublicAreaWindow" -ArgumentList $pvm
    $pw.Show(); $pw.UpdateLayout()
    $hallBox = $pw.FindName('HallAreaBox')
    if ($hallBox -ne $null -and $hallBox.IsReadOnly -eq $false) {
        Write-Host "PASS  取模型值后 D55 字段可编辑(GeometryField 触发器生效)"
    } else { Write-Host "FAIL  D55 字段可编辑性: IsReadOnly=$($hallBox.IsReadOnly)"; $fail++ }
    $pw.Close()

    $lockedVm = New-Object "$vmNs.PublicAreaViewModel" -ArgumentList $repo2, $spaces, '自检注入的空间', $true
    $lw = New-Object "$uiNs.PublicAreaWindow" -ArgumentList $lockedVm
    $lw.Show(); $lw.UpdateLayout()
    $lockedBox = $lw.FindName('HallAreaBox')
    if ($lockedBox -ne $null -and $lockedBox.IsReadOnly -eq $true) {
        Write-Host "PASS  未取模型值前 D55 字段只读灰底(GeometryField 触发器生效)"
    } else { Write-Host "FAIL  D55 字段锁定: IsReadOnly=$($lockedBox.IsReadOnly)"; $fail++ }
    $lw.Close()
    try { Remove-Item $tmp2 -Recurse -Force -ErrorAction Stop } catch { }
} catch {
    Write-Host ("FAIL  公共区模型取值自检  {0}" -f $_.Exception.Message)
    $fail++
}

# 待实现模块遍历:22 个模块都应能生成说明窗
try {
    $n = 0
    foreach ($m in [HVACIDA.Core.Services.ModuleCatalog]::All) {
        $vm = [HVACIDA.UI.ViewModels.InfoViewModel]::ForModule($m)
        if ($vm.Sections.Count -gt 0) { $n++ }
    }
    if ($n -eq 22) { Write-Host "PASS  22 个模块均可生成说明内容" }
    else { Write-Host "FAIL  模块说明内容生成: $n/22"; $fail++ }
} catch {
    Write-Host ("FAIL  模块说明内容生成  {0}" -f $_.Exception.Message)
    $fail++
}

$app.Shutdown()
Write-Host "=================================================="
if ($fail -eq 0) { Write-Host '窗口装载自检全部通过'; [System.Environment]::Exit(0) }
Write-Host ("窗口装载自检失败 " + $fail + " 项"); [System.Environment]::Exit(1)
