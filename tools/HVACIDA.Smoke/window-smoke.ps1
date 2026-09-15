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
Test-Window '大系统排烟计算 LargeSmokeWindow'  { New-Object "$uiNs.LargeSmokeWindow" }
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
# 计算结果表格化:大系统负荷 / 小系统 结果是否真的以"分组表格"渲染
# =====================================================================
try {
    $tmp5 = Join-Path $env:TEMP ("HVACIDA-WinSmoke-" + [guid]::NewGuid().ToString('N'))
    $repo5 = New-Object HVACIDA.Core.Services.XmlProjectRepository -ArgumentList $tmp5

    # ---- 大系统负荷计算窗(右栏结果表)----
    $blvm = New-Object "$vmNs.LargeSystemViewModel" -ArgumentList $repo5
    $blvm.CalculateCommand.Execute($null)
    if ($blvm.Table -ne $null -and $blvm.Table.Sections.Count -eq 7) {
        $rows = ($blvm.Table.Sections | ForEach-Object { $_.Rows.Count } | Measure-Object -Sum).Sum
        Write-Host ("PASS  大系统负荷结果表:{0} 个分区 / 共 {1} 行(客流/冷负荷/湿负荷/焓湿/风量与制冷/选型)" -f $blvm.Table.Sections.Count, $rows)
    } else { Write-Host ("FAIL  大系统结果表分区数={0}" -f $blvm.Table.Sections.Count); $fail++ }

    $allRows = @($blvm.Table.Sections | ForEach-Object { $_.Rows })
    $cooling = $allRows | Where-Object { $_.Cell -eq 'E159' }
    $supply = $allRows | Where-Object { $_.Cell -eq 'C125' }
    if ($cooling -ne $null -and $cooling.IsTotal -eq $true -and $cooling.Value -gt 0 -and $cooling.Unit -eq 'kW') {
        Write-Host ("PASS  合计行带标记与单元格代号:总制冷量 {0} kW ({1})" -f $cooling.Display, $cooling.Cell)
    } else { Write-Host "FAIL  合计行(E159)缺失或未标记"; $fail++ }

    if ($supply -ne $null -and $supply.Value -gt 1000 -and $supply.Display -match '^[0-9,]+[.][0-9]$') {
        Write-Host ("PASS  大数值按千分位显示:总送风量 {0} m3/h" -f $supply.Display)
    } else { Write-Host ("FAIL  数值格式: {0}" -f $supply.Display); $fail++ }

    $bw = New-Object "$uiNs.LargeSystemWindow" -ArgumentList $blvm
    $bw.Show(); $bw.UpdateLayout()
    $tv = $bw.FindName('ResultTableHost')
    if ($tv -ne $null -and $tv.Table -ne $null -and $tv.Table.Sections.Count -eq 7) {
        Write-Host ("PASS  大系统负荷窗右栏结果表控件已绑定到数据(Table DP 解析出 {0} 个分区)" -f $tv.Table.Sections.Count)
    } else { Write-Host "FAIL  右栏结果表控件未绑定到 Table"; $fail++ }
    $bw.Close()

    # ---- 小系统负荷计算窗(全空气一次回风:右栏结果表)----
    $sload = New-Object "$vmNs.SmallSystemViewModel" -ArgumentList ([HVACIDA.Core.Models.SmallSystemType]::AllAirOnceReturn), $repo5
    $sload.CalculateCommand.Execute($null)
    if ($sload.Table -ne $null -and $sload.Table.Sections.Count -ge 4) {
        Write-Host ("PASS  full-air window result table: {0} sections" -f $sload.Table.Sections.Count)
    } else { Write-Host ("FAIL  full-air result table sections={0}" -f $sload.Table.Sections.Count); $fail++ }

    $slw = New-Object "$uiNs.SmallSystemWindow" -ArgumentList $sload
    $slw.Show(); $slw.UpdateLayout()
    if ($slw.FindName('ResultTableHost') -ne $null) { Write-Host "PASS  full-air window uses the shared result table view" }
    else { Write-Host "FAIL  full-air window has no result table host"; $fail++ }
    $slw.Close()

    # ---- 大系统计算结果窗(表格 + 排烟表)----
    $brvm = New-Object "$vmNs.LargeResultViewModel" -ArgumentList $repo5
    $brvm.CalculateCommand.Execute($null)
    $brw = New-Object "$uiNs.LargeSystemResultWindow" -ArgumentList $brvm
    $brw.Show(); $brw.UpdateLayout()
    if ($brw.FindName('ResultTableHost') -ne $null -and $brw.FindName('SmokeGrid') -ne $null -and
        $brw.FindName('SmokeGrid').Columns.Count -eq 5) {
        Write-Host "PASS  大系统计算结果窗:负荷结果表 + 排烟表(5 列)同窗呈现"
    } else { Write-Host "FAIL  计算结果窗缺少结果表"; $fail++ }
    $brw.Close()

    # ---- 小系统计算结果窗 ----
    $srvm = New-Object "$vmNs.SmallResultViewModel" -ArgumentList $repo5
    $srvm.CalculateCommand.Execute($null)
    if ($srvm.Table -ne $null -and $srvm.Table.Sections.Count -ge 4) {
        $srows = ($srvm.Table.Sections | ForEach-Object { $_.Rows.Count } | Measure-Object -Sum).Sum
        Write-Host ("PASS  小系统结果表:{0} 个分区 / 共 {1} 行" -f $srvm.Table.Sections.Count, $srows)
    } else { Write-Host ("FAIL  小系统结果表分区数={0}" -f $srvm.Table.Sections.Count); $fail++ }

    $sText = @($srvm.Table.Sections | ForEach-Object { $_.Rows }) | Where-Object { $_.IsText -eq $true }
    if ($sText -ne $null -and $sText.Display.Length -gt 0) {
        Write-Host ("PASS  table text row routed to text branch: {0}" -f $sText.Label)
    } else { Write-Host "FAIL  small-system table has no text row"; $fail++ }

    $srw = New-Object "$uiNs.SmallSystemResultWindow" -ArgumentList $srvm
    $srw.Show(); $srw.UpdateLayout()
    if ($srw.FindName('ResultTableHost') -ne $null) { Write-Host "PASS  小系统计算结果窗已换成结果表控件" }
    else { Write-Host "FAIL  小系统窗未找到结果表控件"; $fail++ }
    $srw.Close()

    # ---- 文本计算书仍与表格同源(逐行抽查)----
    if ($srvm.ResultText -match '四、设备选型' -and $blvm.ResultText -match 'D107' -and $blvm.ResultText -match 'E159') {
        Write-Host "PASS  导出计算书与结果表同源(分区标题 + 单元格代号均在)"
    } else { Write-Host "FAIL  计算书文本与表格不同源"; $fail++ }

    try { Remove-Item $tmp5 -Recurse -Force -ErrorAction Stop } catch { }
} catch {
    Write-Host ("FAIL  结果表格化自检  {0}" -f $_.Exception.Message)
    $fail++
}
# =====================================================================
# 大系统排烟计算:参数 → 表格结果(不需要 Revit)
# =====================================================================
try {
    $tmp4 = Join-Path $env:TEMP ("HVACIDA-WinSmoke-" + [guid]::NewGuid().ToString('N'))
    $repo4 = New-Object HVACIDA.Core.Services.XmlProjectRepository -ArgumentList $tmp4
    $svm = New-Object "$vmNs.LargeSmokeViewModel" -ArgumentList $repo4

    # 默认:面积取 LargeSystemInput 默认 1500/1200;参数 60 / 1.2 / 2 台
    # 站厅 1500×60=90000 → ×1.2=108000 → /2=54000;站台 1200×60=72000 → 86400 → 43200
    if ($svm.Rows.Count -eq 2 -and
        $svm.Rows[0].CalculatedFlowM3H -eq 90000 -and $svm.Rows[0].SelectionFlowM3H -eq 108000 -and $svm.Rows[0].UnitFlowM3H -eq 54000 -and
        $svm.Rows[1].CalculatedFlowM3H -eq 72000 -and $svm.Rows[1].SelectionFlowM3H -eq 86400 -and $svm.Rows[1].UnitFlowM3H -eq 43200) {
        Write-Host "PASS  排烟计算表数值(面积×60 → 选型×1.2 → 2 台):90000/108000/54000 与 72000/86400/43200"
    } else {
        Write-Host ("FAIL  排烟表: {0}/{1}/{2} | {3}/{4}/{5}" -f $svm.Rows[0].CalculatedFlowM3H, $svm.Rows[0].SelectionFlowM3H,
            $svm.Rows[0].UnitFlowM3H, $svm.Rows[1].CalculatedFlowM3H, $svm.Rows[1].SelectionFlowM3H, $svm.Rows[1].UnitFlowM3H)
        $fail++
    }

    if ($svm.Rows[0].IsGoverning -eq $true -and $svm.Rows[1].IsGoverning -eq $false) {
        Write-Host "PASS  风机选型基准区标记在站厅行(取大者)"
    } else { Write-Host "FAIL  基准区标记不正确"; $fail++ }

    if ($svm.PendingNote -match '防烟分区') {
        Write-Host "PASS  过渡口径(防烟分区几何待补)在界面可见"
    } else { Write-Host "FAIL  未显示过渡口径"; $fail++ }

    # 改参数重算:单位面积 72、系数 1.0、4 台
    $svm.Input.SmokeRateM3HPerM2 = 72
    $svm.Input.SelectionFactor = 1.0
    $svm.Input.FanUnitCount = 4
    $svm.CalculateCommand.Execute($null)
    if ($svm.Rows[0].CalculatedFlowM3H -eq 108000 -and $svm.Rows[0].SelectionFlowM3H -eq 108000 -and $svm.Rows[0].UnitFlowM3H -eq 27000) {
        Write-Host "PASS  改参数(72 / ×1.0 / 4 台)后重算:108000 / 108000 / 27000"
    } else {
        Write-Host ("FAIL  重算: {0}/{1}/{2}" -f $svm.Rows[0].CalculatedFlowM3H, $svm.Rows[0].SelectionFlowM3H, $svm.Rows[0].UnitFlowM3H)
        $fail++
    }

    $svm.ResetCommand.Execute($null)
    if ($svm.Input.SmokeRateM3HPerM2 -eq 60 -and $svm.Input.SelectionFactor -eq 1.2 -and $svm.Input.FanUnitCount -eq 2) {
        Write-Host "PASS  恢复默认参数(60 / 1.2 / 2 台)"
    } else { Write-Host "FAIL  恢复默认未生效"; $fail++ }

    # 表格是否真的以表格形式渲染:5 列 × 2 行
    $sw = New-Object "$uiNs.LargeSmokeWindow" -ArgumentList $svm
    $sw.Show(); $sw.UpdateLayout()
    $grid = $sw.FindName('SmokeGrid')
    if ($grid -ne $null -and $grid.Columns.Count -eq 5 -and $grid.Items.Count -eq 2) {
        Write-Host ("PASS  排烟结果以表格呈现({0} 列 × {1} 行)" -f $grid.Columns.Count, $grid.Items.Count)
    } else {
        Write-Host ("FAIL  结果表: 列={0} 行={1}" -f $grid.Columns.Count, $grid.Items.Count)
        $fail++
    }
    $sw.Close()

    # 「大系统 → 计算结果」窗同样带排烟表格
    $rvm = New-Object "$vmNs.LargeResultViewModel" -ArgumentList $repo4
    $rvm.CalculateCommand.Execute($null)
    if ($rvm.SmokeRows.Count -eq 2 -and $rvm.SmokeSummary -match '选型基准') {
        Write-Host ("PASS  计算结果窗含排烟表({0} 行)+ 选型结论" -f $rvm.SmokeRows.Count)
    } else { Write-Host "FAIL  计算结果窗排烟表缺失"; $fail++ }

    $rw = New-Object "$uiNs.LargeSystemResultWindow" -ArgumentList $rvm
    $rw.Show(); $rw.UpdateLayout()
    $rgrid = $rw.FindName('SmokeGrid')
    if ($rgrid -ne $null -and $rgrid.Columns.Count -eq 5 -and $rgrid.Items.Count -eq 2) {
        Write-Host ("PASS  计算结果窗排烟表渲染({0} 列 × {1} 行)" -f $rgrid.Columns.Count, $rgrid.Items.Count)
    } else { Write-Host ("FAIL  计算结果窗表格: 列={0} 行={1}" -f $rgrid.Columns.Count, $rgrid.Items.Count); $fail++ }
    $rw.Close()

    try { Remove-Item $tmp4 -Recurse -Force -ErrorAction Stop } catch { }
} catch {
    Write-Host ("FAIL  排烟计算自检  {0}" -f $_.Exception.Message)
    $fail++
}
# =====================================================================
# 省市气象数据库:省市级联 + 选市自动回填(不需要 Revit)
# =====================================================================
try {
    $wdb = [HVACIDA.Core.Services.WeatherDatabase]::Default
    Write-Host ("      气象库: {0} 个台站 / {1} 个省级行政区" -f $wdb.All.Count, $wdb.Provinces.Count)

    $tmp3 = Join-Path $env:TEMP ("HVACIDA-WinSmoke-" + [guid]::NewGuid().ToString('N'))
    $repo3 = New-Object HVACIDA.Core.Services.XmlProjectRepository -ArgumentList $tmp3
    $pivm = New-Object "$vmNs.ProjectInfoViewModel" -ArgumentList $repo3, $wdb

    if ($pivm.Provinces.Count -eq 31 -and $pivm.Provinces[0] -eq '北京') {
        Write-Host "PASS  工程信息窗省下拉来自气象库(31 个,首个为北京)"
    } else { Write-Host ("FAIL  省下拉: {0} 个, 首个 {1}" -f $pivm.Provinces.Count, $pivm.Provinces[0]); $fail++ }

    if ($pivm.HasCities -eq $false) { Write-Host "PASS  未选省时城市下拉为空(禁用)" }
    else { Write-Host "FAIL  未选省时城市下拉应为空"; $fail++ }

    $pivm.SelectedProvince = '广东'
    if ($pivm.Cities.Count -eq 15 -and $pivm.Cities -contains '深圳') {
        Write-Host "PASS  选省后城市下拉级联更新(广东 15 个,含深圳)"
    } else { Write-Host ("FAIL  级联: {0} 个" -f $pivm.Cities.Count); $fail++ }
    if ($pivm.Model.Basic.LocationProvince -eq '广东') { Write-Host "PASS  省写入 Model.Basic.LocationProvince" }
    else { Write-Host "FAIL  省未写入模型"; $fail++ }

    # 选定城市 -> 自动回填气象参数(需求:选择市后自动把该市气象参数输入到项目中)
    $pivm.SelectedCity = '深圳'
    $d = $pivm.Model.Design
    if ($pivm.Model.Basic.LocationCity -eq '深圳' -and
        $d.LargeSystemOutdoor.SummerACDryBulbC -eq 33.7 -and
        $d.LargeSystemOutdoor.SummerACWetBulbC -eq 27.5 -and
        $d.LargeSystemOutdoor.SummerVentDryBulbC -eq 31.2 -and
        $d.LargeSystemOutdoor.WinterACDryBulbC -eq 6.0 -and
        $d.LargeSystemOutdoor.WinterVentDryBulbC -eq 14.9 -and
        $d.Common.AtmosphericPressureKPa -eq 100.24 -and
        $d.Common.OutdoorRelativeHumidityPercent -eq 70) {
        Write-Host "PASS  选市自动回填深圳气象参数(干球 33.7 / 湿球 27.5 / 夏季通风 31.2 / 冬季 6.0·14.9 / 100.24 kPa / 70%)"
    } else {
        Write-Host ("FAIL  深圳回填: {0}/{1}/{2}/{3}/{4}/{5}/{6}" -f $d.LargeSystemOutdoor.SummerACDryBulbC,
            $d.LargeSystemOutdoor.SummerACWetBulbC, $d.LargeSystemOutdoor.SummerVentDryBulbC,
            $d.LargeSystemOutdoor.WinterACDryBulbC, $d.LargeSystemOutdoor.WinterVentDryBulbC,
            $d.Common.AtmosphericPressureKPa, $d.Common.OutdoorRelativeHumidityPercent)
        $fail++
    }

    if ($d.SmallSystemOutdoor.SummerACWetBulbC -eq 27.5 -and $d.SmallSystemOutdoor.SummerVentDryBulbC -eq 31.2) {
        Write-Host "PASS  小系统室外参数与大系统夏季同源"
    } else { Write-Host "FAIL  小系统室外参数未同源"; $fail++ }

    if ($pivm.WeatherFromDatabase -eq $true -and $pivm.StationInfo -match '59493' -and $pivm.StationInfo -match 'GB 50736') {
        Write-Host ("PASS  台站信息来源可见: {0}" -f $pivm.StationInfo.Substring(0, [Math]::Min(52, $pivm.StationInfo.Length)))
    } else { Write-Host "FAIL  台站信息未显示"; $fail++ }

    # 换到缺湿球温度的台站:不得覆盖原值,且必须给告警
    $pivm.SelectedProvince = '陕西'
    $pivm.SelectedCity = '咸阳'
    $wetKept = $pivm.Model.Design.LargeSystemOutdoor.SummerACWetBulbC
    if ($pivm.WeatherWarning -ne '' -and $wetKept -eq 27.5) {
        Write-Host "PASS  标准未记录湿球温度的台站:给出告警且不覆盖原值"
    } else { Write-Host ("FAIL  咸阳: warning='{0}' wet={1}" -f $pivm.WeatherWarning, $wetKept); $fail++ }

    Test-Window '工程信息(带气象库) ProjectInfoWindow' { New-Object "$uiNs.ProjectInfoWindow" -ArgumentList $pivm }
    try { Remove-Item $tmp3 -Recurse -Force -ErrorAction Stop } catch { }
} catch {
    Write-Host ("FAIL  省市气象库自检  {0}" -f $_.Exception.Message)
    $fail++
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
