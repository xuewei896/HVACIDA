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

# =====================================================================
# 静态扫描:XAML 的可见文本(Text/Header/Content)不得出现公式文档单元格编号
# —— 编号只允许出现在 ToolTip(悬停提示)里;这是"插件不体现单元格编号"的防回归闸门
# =====================================================================
try {
    $uiRoot = Split-Path (Split-Path (Split-Path (Split-Path (Resolve-Path -LiteralPath $UiDir).Path -Parent) -Parent) -Parent) -Parent
    $srcDir = Join-Path $uiRoot 'src'
    if (-not (Test-Path -LiteralPath $srcDir)) { $srcDir = Join-Path (Split-Path $uiRoot -Parent) 'src' }
    $xamls = @(Get-ChildItem -LiteralPath $srcDir -Recurse -Filter *.xaml -File | Where-Object { $_.FullName -notmatch '\\obj\\' })
    $bad = @()
    foreach ($x in $xamls) {
        $hit = Select-String -Path $x.FullName -Pattern '(Text|Header|Content)="[^"]*(?:[（(]\s*[A-Z]{1,2}[0-9]{1,3}|\b[A-Z]{1,2}[0-9]{2,3}\b)'
        foreach ($h in $hit) { $bad += ((Split-Path $x.FullName -Leaf) + ':' + $h.LineNumber) }
    }
    if ($bad.Count -eq 0) { Write-Host ("PASS  XAML 可见文本无单元格编号(已扫描 {0} 个 .xaml)" -f $xamls.Count) }
    else { Write-Host ("FAIL  XAML 可见文本含单元格编号: " + ($bad -join ', ')); $fail++ }
} catch {
    Write-Host ("FAIL  单元格编号扫描  {0}" -f $_.Exception.Message)
    $fail++
}

# =====================================================================
# 静态绑定一致性门禁(开发流程 §5.2):窗口 XAML 里每个 {Binding 路径} 都必须能在
# 「ViewModel + 其集合属性的元素类型」上反射解析出来 —— WPF 绑定失败是**静默**的
# (只写调试跟踪、不抛异常),所以要把它变成硬断言,否则"标签在、数字空"会溜过去。
# 元素类型(drill-down)是为了覆盖 DataGrid 列模板:那里的 DataContext 是行对象,不是 VM。
# =====================================================================
function Get-BindingCandidateTypes([System.Type]$root) {
    $list = New-Object 'System.Collections.Generic.List[System.Type]'
    $seen = New-Object 'System.Collections.Generic.HashSet[System.Type]'
    $queue = New-Object 'System.Collections.Generic.Queue[System.Type]'
    $queue.Enqueue($root)
    $enumerableOf = [System.Collections.Generic.IEnumerable``1]
    $flags = [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Instance
    while ($queue.Count -gt 0) {
        $t = $queue.Dequeue()
        if ($t -eq $null) { continue }
        if (-not $seen.Add($t)) { continue }
        $list.Add($t)
        foreach ($p in $t.GetProperties($flags)) {
            $pt = $p.PropertyType
            if ($pt -eq [string] -or $pt.IsPrimitive -or $pt.IsEnum) { continue }
            $elem = $null
            if ($pt.IsArray) { $elem = $pt.GetElementType() }
            else {
                foreach ($i in $pt.GetInterfaces()) {
                    if ($i.IsGenericType -and $i.GetGenericTypeDefinition() -eq $enumerableOf) {
                        $elem = $i.GetGenericArguments()[0]; break
                    }
                }
            }
            # 集合属性 → 元素类型(DataGrid 列模板的 DataContext);嵌套对象属性 → 该类型本身
            # (如 Coefficients.LocalLossItems 的元素类型要从 HydraulicCoefficients 再下一层才拿得到)
            if ($elem -ne $null -and $elem.Namespace -ne $null -and $elem.Namespace.StartsWith('HVACIDA')) { $queue.Enqueue($elem) }
            elseif ($elem -eq $null -and $pt.Namespace -ne $null -and $pt.Namespace.StartsWith('HVACIDA')) { $queue.Enqueue($pt) }
        }
    }
    return $list
}

function Test-WindowBindings([string]$xamlFile, [string]$viewModelTypeName) {
    $vmType = $uiAsm.GetType('HVACIDA.UI.ViewModels.' + $viewModelTypeName)
    if ($vmType -eq $null) { Write-Host ("FAIL  绑定门禁:找不到 ViewModel " + $viewModelTypeName); $script:fail++; return }
    $candidates = Get-BindingCandidateTypes $vmType

    $text = Get-Content -LiteralPath $xamlFile -Raw
    $paths = @()
    $skipKeywords = @('Mode', 'StringFormat', 'UpdateSourceTrigger', 'Converter', 'ConverterParameter',
                      'IsAsync', 'NotifyOnTargetUpdated', 'FallbackValue', 'TargetNullValue',
                      'ValidatesOnExceptions', 'BindsDirectlyToSource', 'XPath', 'BindingGroupName', 'Delay')
    foreach ($m in [regex]::Matches($text, '\{Binding([^}]*)\}')) {
        $inner = $m.Groups[1].Value
        # 自引用/元素名/显式源不指向 ViewModel,跳过(如 ToolTip 取自身 Text 的 Self 绑定)
        if ($inner -match 'RelativeSource|ElementName|Source=') { continue }
        $p = $null
        if ($inner -match 'Path\s*=\s*([A-Za-z_][A-Za-z0-9_.]*)') { $p = $Matches[1] }
        elseif ($inner -match '^\s*([A-Za-z_][A-Za-z0-9_.]*)') {
            $head = $Matches[1]
            if ($skipKeywords -notcontains $head) { $p = $head }
        }
        if ($p -ne $null -and -not ($paths -contains $p)) { $paths += $p }
    }

    $bad = @()
    $flags = [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Instance
    foreach ($path in $paths) {
        $resolved = $false
        foreach ($candidate in $candidates) {
            $cur = $candidate
            $ok = $true
            foreach ($part in $path.Split('.')) {
                $prop = $cur.GetProperty($part, $flags)
                if ($prop -eq $null) { $ok = $false; break }
                $cur = $prop.PropertyType
            }
            if ($ok) { $resolved = $true; break }
        }
        if (-not $resolved) { $bad += $path }
    }

    $leaf = Split-Path $xamlFile -Leaf
    if ($bad.Count -eq 0) {
        Write-Host ("PASS  绑定一致性 {0}:{1} 条绑定路径全部命中 {2}" -f $leaf, $paths.Count, $viewModelTypeName)
    } else {
        Write-Host ("FAIL  绑定一致性 {0}:解析不到的路径 {1}" -f $leaf, ($bad -join ', '))
        $script:fail++
    }
}

try {
    $uiAsm = [System.Reflection.Assembly]::LoadFrom($ui)
    $uiRootForBindings = Split-Path (Split-Path (Split-Path (Split-Path (Resolve-Path -LiteralPath $UiDir).Path -Parent) -Parent) -Parent) -Parent
    $viewDir = Join-Path $uiRootForBindings 'src\HVACIDA.UI\Views'
    if (-not (Test-Path -LiteralPath $viewDir)) { $viewDir = Join-Path (Split-Path $uiRootForBindings -Parent) 'src\HVACIDA.UI\Views' }

    # 窗口 XAML → 其 DataContext 的 ViewModel(ResultTableView.xaml 是 UserControl,绑定走 DP,不在本表)
    $bindingMap = @(
        @('ProjectInfoWindow.xaml', 'ProjectInfoViewModel'),
        @('WeatherWindow.xaml', 'ProjectInfoViewModel'),
        @('PublicAreaWindow.xaml', 'PublicAreaViewModel'),
        @('LargeSystemWindow.xaml', 'LargeSystemViewModel'),
        @('LargeSmokeWindow.xaml', 'LargeSmokeViewModel'),
        @('LargeSystemResultWindow.xaml', 'LargeResultViewModel'),
        @('SmallSystemWindow.xaml', 'SmallSystemViewModel'),
        @('SmallSystemResultWindow.xaml', 'SmallResultViewModel'),
        @('HydraulicSystemWindow.xaml', 'HydraulicSystemViewModel'),
        @('HydraulicResultWindow.xaml', 'HydraulicResultViewModel'),
        @('KnowledgeWindow.xaml', 'KnowledgeViewModel'),
        @('InfoWindow.xaml', 'InfoViewModel')
    )
    foreach ($pair in $bindingMap) {
        $file = Join-Path $viewDir $pair[0]
        if (Test-Path -LiteralPath $file) { Test-WindowBindings $file $pair[1] }
        else { Write-Host ("FAIL  绑定门禁:找不到 " + $pair[0]); $fail++ }
    }
} catch {
    Write-Host ("FAIL  绑定一致性门禁  {0}" -f $_.Exception.Message)
    $fail++
}

Test-Window '工程信息 ProjectInfoWindow'      { New-Object "$uiNs.ProjectInfoWindow" }
Test-Window '气象参数 WeatherWindow'          { New-Object "$uiNs.WeatherWindow" }
Test-Window '公共区参数 PublicAreaWindow'      { New-Object "$uiNs.PublicAreaWindow" }
Test-Window '大系统负荷计算 LargeSystemWindow' { New-Object "$uiNs.LargeSystemWindow" }
Test-Window '大系统排烟计算 LargeSmokeWindow'  { New-Object "$uiNs.LargeSmokeWindow" }
Test-Window '大系统计算结果 LargeResultWindow' { New-Object "$uiNs.LargeSystemResultWindow" }
Test-Window '小系统负荷计算 SmallSystemWindow' { New-Object "$uiNs.SmallSystemWindow" }
Test-Window '小系统计算结果 SmallResultWindow' { New-Object "$uiNs.SmallSystemResultWindow" }
Test-Window '水力计算 风系统 HydraulicSystemWindow' { New-Object "$uiNs.HydraulicSystemWindow" }
Test-Window '水力计算 水系统 HydraulicSystemWindow(水)' {
    New-Object "$uiNs.HydraulicSystemWindow" -ArgumentList (New-Object "$vmNs.HydraulicSystemViewModel" -ArgumentList ([HVACIDA.Core.Models.HydraulicKind]::WaterPipe))
}
Test-Window '水力计算结果 HydraulicResultWindow' { New-Object "$uiNs.HydraulicResultWindow" }
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
# 小系统六类:多房间录入 + 三张结果表(系统结果 / 房间明细 / 设备选型)
# =====================================================================
function New-SmallVm([string]$typeName, $repo) {
    $t = [HVACIDA.Core.Models.SmallSystemType]::$typeName
    return New-Object "$vmNs.SmallSystemViewModel" -ArgumentList $t, $repo
}
function Clear-Rooms($vm) { while ($vm.Rooms.Count -gt 0) { $vm.Rooms.RemoveAt(0) } }
function Add-Room($vm, [string]$name, [double]$area, [double]$height, [double]$equip, [double]$people, [double]$ach, [string]$roomType) {
    $r = [HVACIDA.Core.Models.SmallRoomInput]::Create($name, $area, $height)
    $r.EquipmentCoolingW = $equip
    $r.Occupants = $people
    $r.AirChangePerHour = $ach
    $r.RoomType = $roomType
    $vm.Rooms.Add($r)
    return $r
}

try {
    $tmp6 = Join-Path $env:TEMP ("HVACIDA-SmallWin-" + [guid]::NewGuid().ToString('N'))
    $repo6 = New-Object HVACIDA.Core.Services.XmlProjectRepository -ArgumentList $tmp6

    # ---- 全空气一次回风:2 房间 ----
    $air = New-SmallVm 'AllAirOnceReturn' $repo6
    Clear-Rooms $air
    Add-Room $air '弱电间1' 50 5.9 5000 0 6 '' | Out-Null
    Add-Room $air '弱电间2' 30 5.9 3000 0 6 '' | Out-Null
    $air.CalculateCommand.Execute($null)
    if ($air.RoomRows.Count -eq 2 -and $air.EquipmentRows.Count -eq 2 -and $air.Table.Sections.Count -ge 4) {
        Write-Host ("PASS  全空气一次回风:{0} 房间 → 结果表 {1} 分区 / 明细 {2} 行 / 设备 {3} 台" -f `
            $air.Rooms.Count, $air.Table.Sections.Count, $air.RoomRows.Count, $air.EquipmentRows.Count)
    } else {
        Write-Host ("FAIL  全空气: rooms={0} table={1} rows={2} equip={3}" -f $air.Rooms.Count, $air.Table.Sections.Count, $air.RoomRows.Count, $air.EquipmentRows.Count)
        $fail++
    }
    $airW = New-Object "$uiNs.SmallSystemWindow" -ArgumentList $air
    $airW.Show(); $airW.UpdateLayout()
    $ig = $airW.FindName('InputGrid')
    $rd = $airW.FindName('RoomDetailGrid')
    $eg = $airW.FindName('EquipmentGrid')
    $rt = $airW.FindName('ResultTableHost')
    $expectIn = [HVACIDA.Core.Services.SmallRoomTable]::InputColumnsFor([HVACIDA.Core.Models.SmallSystemType]::AllAirOnceReturn).Count
    $expectOut = [HVACIDA.Core.Services.SmallRoomTable]::ColumnsFor([HVACIDA.Core.Models.SmallSystemType]::AllAirOnceReturn).Count
    if ($ig -ne $null -and $ig.Columns.Count -eq $expectIn -and $rd -ne $null -and $rd.Columns.Count -eq $expectOut -and
        $eg -ne $null -and $eg.Columns.Count -eq 5 -and $rt -ne $null -and $rt.Table -ne $null) {
        Write-Host ("PASS  小系统窗表格渲染:录入 {0} 列 / 明细 {1} 列 / 设备 5 列,结果表已绑定" -f $ig.Columns.Count, $rd.Columns.Count)
    } else {
        Write-Host ("FAIL  小系统窗表格: in={0} out={1} equip={2} host={3}" -f $ig.Columns.Count, $rd.Columns.Count, $eg.Columns.Count, ($rt -ne $null))
        $fail++
    }
    $airW.Close()

    # ---- 排烟系统:2 个防烟分区(文档示例口径) ----
    $smoke = New-SmallVm 'SmokeExhaust' $repo6
    Clear-Rooms $smoke
    Add-Room $smoke '防烟分区2' 300 0 0 0 0 '' | Out-Null
    Add-Room $smoke '防烟分区3' 277 0 0 0 0 '' | Out-Null
    $smoke.CalculateCommand.Execute($null)
    if ($smoke.RoomRows.Count -eq 2 -and [math]::Abs($smoke.EquipmentRows[0].FlowM3H - 41544) -lt 0.5 -and
        [math]::Abs($smoke.EquipmentRows[1].FlowM3H - 22849.2) -lt 0.5) {
        Write-Host ("PASS  排烟系统:排烟风机 {0:N0} / 补风机 {1:N0} m³/h(文档示例口径)" -f `
            $smoke.EquipmentRows[0].FlowM3H, $smoke.EquipmentRows[1].FlowM3H)
    } else { Write-Host "FAIL  排烟系统选型"; $fail++ }

    # ---- 排风系统:8 房间(文档示例合计 5386) ----
    $ex = New-SmallVm 'ExhaustVentilation' $repo6
    Clear-Rooms $ex
    $exRooms = @(@(5.83,5.90,'男卫'),@(6.82,5.90,'女卫'),@(4.23,4.55,'淋浴间'),@(4.40,4.55,'淋浴间'),
                  @(11.46,4.55,'男卫生间'),@(15.62,4.55,'女卫生间'),@(7.87,4.55,'无障碍卫生间'),@(17.62,4.55,'污水泵房'))
    foreach ($row in $exRooms) {
        $ach = [HVACIDA.Core.Models.SmallSystemInput]::DefaultAirChangePerHour($row[2])
        Add-Room $ex $row[2] $row[0] $row[1] 0 0 $ach $row[2] | Out-Null
    }
    $ex.CalculateCommand.Execute($null)
    $exTotal = 0.0
    foreach ($r in $ex.RoomRows) { $exTotal += $r.ExhaustM3H }
    if ([math]::Abs($exTotal - 5386) -lt 1.0) {
        Write-Host ("PASS  排风系统:按房间类型默认换气次数算得排风量合计 {0:N0} m³/h(文档示例 5386)" -f $exTotal)
    } else { Write-Host ("FAIL  排风系统合计 = {0}" -f $exTotal); $fail++ }

    # ---- 加压送风:无房间行,按门参数算 ----
    $pr = New-SmallVm 'PressurizationSupply' $repo6
    $pr.CalculateCommand.Execute($null)
    if ($pr.IsPressurization -eq $true -and $pr.Table.Sections.Count -ge 3 -and
        $pr.EquipmentRows.Count -eq 1 -and [math]::Abs($pr.EquipmentRows[0].FlowM3H - 14412.4) -lt 1.0) {
        Write-Host ("PASS  加压送风:无房间行,加压送风机 {0:N0} m³/h(默认门参数)" -f $pr.EquipmentRows[0].FlowM3H)
    } else { Write-Host ("FAIL  加压送风: isPress={0} equip={1}" -f $pr.IsPressurization, $pr.EquipmentRows.Count); $fail++ }
    $prW = New-Object "$uiNs.SmallSystemWindow" -ArgumentList $pr
    $prW.Show(); $prW.UpdateLayout()
    if ($prW.FindName('ResultTableHost') -ne $null) { Write-Host "PASS  加压送风窗可正常构造并显示结果表" }
    else { Write-Host "FAIL  加压送风窗缺结果表"; $fail++ }
    $prW.Close()

    try { Remove-Item $tmp6 -Recurse -Force -ErrorAction Stop } catch { }
} catch {
    Write-Host ("FAIL  小系统六类窗口自检  {0}" -f $_.Exception.Message)
    $fail++
}

# =====================================================================
# 多系统汇总 + 模型拾取协议(不需要 Revit:拾取由命令层注入结果,这里只验 VM 契约)
# =====================================================================
try {
    $tmp7 = Join-Path $env:TEMP ("HVACIDA-Sum-" + [guid]::NewGuid().ToString('N'))
    $repo7 = New-Object HVACIDA.Core.Services.XmlProjectRepository -ArgumentList $tmp7
    $svc7 = New-Object HVACIDA.Core.Services.SmallSystemInputService -ArgumentList $repo7

    # 存 3 套系统(两类空调 + 一套排风 + 一套排烟)
    $a1 = New-Object HVACIDA.Core.Models.SmallSystemInput
    $a1.SystemType = [HVACIDA.Core.Models.SmallSystemType]::AllAirOnceReturn; $a1.SystemCode = 'AHU-A101'
    $a1.Rooms.Add([HVACIDA.Core.Models.SmallRoomInput]::Create('弱电间1', 50, 5.9))
    $svc7.Save($a1) | Out-Null
    $a2 = New-Object HVACIDA.Core.Models.SmallSystemInput
    $a2.SystemType = [HVACIDA.Core.Models.SmallSystemType]::AllAirOnceReturn; $a2.SystemCode = 'AHU-A201'
    $a2.Rooms.Add([HVACIDA.Core.Models.SmallRoomInput]::Create('强电间1', 80, 4.55))
    $svc7.Save($a2) | Out-Null
    $ef = New-Object HVACIDA.Core.Models.SmallSystemInput
    $ef.SystemType = [HVACIDA.Core.Models.SmallSystemType]::ExhaustVentilation; $ef.SystemCode = 'EAF-A601'
    $r = [HVACIDA.Core.Models.SmallRoomInput]::Create('男卫', 5.83, 5.9); $r.RoomType = '男卫生间'; $ef.Rooms.Add($r)
    $count7 = $svc7.Save($ef)
    if ($count7 -eq 3) { Write-Host "PASS  小系统按类型+编号 upsert 保存:当前 3 套" }
    else { Write-Host ("FAIL  保存后套数 = {0}" -f $count7); $fail++ }

    # ---- 汇总窗 ----
    $sum = New-Object "$vmNs.SmallResultViewModel" -ArgumentList $repo7
    $sum.CalculateCommand.Execute($null)
    if ($sum.Summary.SystemCount -eq 3 -and $sum.SummaryRows.Count -eq 3 -and $sum.Table.Sections.Count -eq 3) {
        Write-Host ("PASS  多系统汇总:{0} 套 / 逐系统 {1} 行 / 结果表 {2} 分区(冷负荷合计 {3:N2} kW)" -f `
            $sum.Summary.SystemCount, $sum.SummaryRows.Count, $sum.Table.Sections.Count, $sum.Summary.TotalCoolingKw)
    } else {
        Write-Host ("FAIL  汇总: sys={0} rows={1} table={2}" -f $sum.Summary.SystemCount, $sum.SummaryRows.Count, $sum.Table.Sections.Count)
        $fail++
    }

    $sumW = New-Object "$uiNs.SmallSystemResultWindow" -ArgumentList $sum
    $sumW.Show(); $sumW.UpdateLayout()
    $sg = $sumW.FindName('SummaryGrid')
    $expectSum = [HVACIDA.Core.Services.SmallRoomTable]::SummaryColumns().Count
    if ($sg -ne $null -and $sg.Columns.Count -eq $expectSum -and $sg.Items.Count -eq 3 -and
        $sumW.FindName('ResultTableHost') -ne $null) {
        Write-Host ("PASS  汇总窗渲染:汇总表 {0} 列 × {1} 行 + 合计表" -f $sg.Columns.Count, $sg.Items.Count)
    } else {
        Write-Host ("FAIL  汇总窗: cols={0} items={1}" -f $sg.Columns.Count, $sg.Items.Count); $fail++
    }

    # 选中某系统行 → 明细三块刷新(房间明细/设备选型/计算书)
    $sum.SelectedSummaryRow = $sum.SummaryRows | Where-Object { $_.SystemCode -eq 'EAF-A601' } | Select-Object -First 1
    $sumW.UpdateLayout()
    if ($sum.DetailTitle -match 'EAF-A601' -and $sum.RoomRows.Count -eq 1 -and
        $sum.EquipmentRows.Count -eq 1 -and $sum.ResultText -match '设备选型') {
        Write-Host ("PASS  选中系统行后明细刷新:{0} · 房间 {1} 行 · 设备 {2} 台" -f $sum.DetailTitle, $sum.RoomRows.Count, $sum.EquipmentRows.Count)
    } else {
        Write-Host ("FAIL  选中行明细: title='{0}' rooms={1} equip={2}" -f $sum.DetailTitle, $sum.RoomRows.Count, $sum.EquipmentRows.Count)
        $fail++
    }
    $sumW.Close()

    # 「—」显示:排风系统不涉及冷负荷/排烟量,应显示「—」而不是 0.00
    $eafRow = $sum.SummaryRows | Where-Object { $_.SystemCode -eq 'EAF-A601' } | Select-Object -First 1
    $airRow = $sum.SummaryRows | Where-Object { $_.SystemCode -eq 'AHU-A101' } | Select-Object -First 1
    if ($eafRow.TotalCoolingKw -eq '—' -and $eafRow.TotalSmokeM3H -eq '—' -and $eafRow.TotalExhaustM3H -ne '—' -and
        $airRow.TotalCoolingKw -ne '—') {
        Write-Host ("PASS  不涉及的量显示「—」:排风系统 冷负荷={0} 排烟={1} 排风={2};空调系统 冷负荷={3}" -f `
            $eafRow.TotalCoolingKw, $eafRow.TotalSmokeM3H, $eafRow.TotalExhaustM3H, $airRow.TotalCoolingKw)
    } else {
        Write-Host ("FAIL  「—」显示: eaf cooling={0} smoke={1} exhaust={2} | ahu cooling={3}" -f `
            $eafRow.TotalCoolingKw, $eafRow.TotalSmokeM3H, $eafRow.TotalExhaustM3H, $airRow.TotalCoolingKw)
        $fail++
    }

    # ---- 拾取协议(VM 侧契约)----
    $svm2 = New-Object "$vmNs.SmallSystemViewModel" -ArgumentList ([HVACIDA.Core.Models.SmallSystemType]::AllAirOnceReturn), $repo7, $true
    if ($svm2.IsPickAvailable -eq $true) { Write-Host "PASS  拾取可用时按钮使能(IsPickAvailable)" }
    else { Write-Host "FAIL  IsPickAvailable 应为 true"; $fail++ }

    $svm2.RequestPickSpaces()
    if ($svm2.PickSpacesRequested -eq $true) { Write-Host "PASS  请求拾取空间 → 置标记(命令层据此拾取)" }
    else { Write-Host "FAIL  PickSpacesRequested 未置位"; $fail++ }
    $svm2.ClearPickRequests()
    if ($svm2.PickSpacesRequested -eq $false) { Write-Host "PASS  ClearPickRequests 复位" }
    else { Write-Host "FAIL  拾取标记未复位"; $fail++ }

    # 注入 "拾取到的空间" → 建房间行(面积/层高/屋顶面积取模型值)
    $spaces7 = New-Object 'System.Collections.Generic.List[HVACIDA.Core.Models.SpaceSnapshot]'
    $sp = New-Object HVACIDA.Core.Models.SpaceSnapshot
    $sp.Name = '信号设备室'; $sp.Number = '101'; $sp.LevelName = '站厅层'
    $sp.AreaM2 = 32.18; $sp.HeightM = 5.9; $sp.VolumeM3 = 32.18 * 5.9
    $spaces7.Add($sp)
    $before = $svm2.Rooms.Count
    $svm2.ApplyPickedSpaces($spaces7, '拾取 1 个房间空间')
    $newRoom = $svm2.Rooms | Where-Object { $_.Name -eq '信号设备室' } | Select-Object -First 1
    if ($svm2.Rooms.Count -eq $before + 1 -and $newRoom -ne $null -and [math]::Abs($newRoom.AreaM2 - 32.18) -lt 1e-9 -and
        [math]::Abs($newRoom.HeightM - 5.9) -lt 1e-9 -and [math]::Abs($newRoom.RoofAreaM2 - 32.18) -lt 1e-9) {
        Write-Host "PASS  拾取空间 → 建房间行(面积/层高/屋顶面积=面积 均取模型值)"
    } else { Write-Host ("FAIL  拾取空间建行: count={0}" -f $svm2.Rooms.Count); $fail++ }

    # 同名空间再拾取一次 → 跳过不重复
    $svm2.ApplyPickedSpaces($spaces7, '再来一次')
    if ($svm2.Rooms.Count -eq $before + 1) { Write-Host "PASS  同名房间重复拾取被跳过(不产生重复行)" }
    else { Write-Host ("FAIL  重复拾取后房间数 = {0}" -f $svm2.Rooms.Count); $fail++ }

    # 拾取墙体 → 写入当前选中行的外墙长度
    $svm2.SelectedRoom = $newRoom
    $svm2.ApplyPickedWallLength(32.5, '已选 3 段墙体,合计长度 32.5 m')
    if ([math]::Abs($newRoom.WallLengthM - 32.5) -lt 1e-9) {
        Write-Host "PASS  拾取墙体 → 选中行外墙长度 = 32.5 m(长度之和)"
    } else { Write-Host ("FAIL  外墙长度 = {0}" -f $newRoom.WallLengthM); $fail++ }

    # 未选中行时拾取墙体:只提示,不改数据
    $svm2.SelectedRoom = $null
    $svm2.ApplyPickedWallLength(99, '无选中行')
    if ([math]::Abs($newRoom.WallLengthM - 32.5) -lt 1e-9) { Write-Host "PASS  未选中行时不落值(仅提示)" }
    else { Write-Host "FAIL  未选中行却改了外墙长度"; $fail++ }

    # 不允许拾取时(无活动文档)按钮应禁用
    $svm3 = New-Object "$vmNs.SmallSystemViewModel" -ArgumentList ([HVACIDA.Core.Models.SmallSystemType]::AllAirOnceReturn), $repo7, $false
    if ($svm3.IsPickAvailable -eq $false) { Write-Host "PASS  无活动文档时拾取按钮禁用" }
    else { Write-Host "FAIL  IsPickAvailable 应为 false"; $fail++ }

    try { Remove-Item $tmp7 -Recurse -Force -ErrorAction Stop } catch { }
} catch {
    Write-Host ("FAIL  多系统汇总/拾取协议自检  {0}" -f $_.Exception.Message)
    $fail++
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

    # ---- 小系统计算结果窗:空工程必须"不摆结果,只给指引"(不做兜底假结果) ----
    $srvm = New-Object "$vmNs.SmallResultViewModel" -ArgumentList $repo5
    $srvm.CalculateCommand.Execute($null)
    if ($srvm.Summary.Rows.Count -eq 0 -and $srvm.Table -eq $null -and $srvm.SummaryRows.Count -eq 0 -and
        $srvm.Note -match '还没有保存过' -and $srvm.Status -match '还没有保存过') {
        Write-Host "PASS  小系统计算结果窗(空工程):汇总表为空、不摆结果、只给"去录入并保存"的指引"
    } else {
        Write-Host ("FAIL  空工程汇总: rows={0} table={1} note='{2}'" -f `
            $srvm.Summary.Rows.Count, ($srvm.Table -ne $null), $srvm.Note)
        $fail++
    }

    $srw = New-Object "$uiNs.SmallSystemResultWindow" -ArgumentList $srvm
    $srw.Show(); $srw.UpdateLayout()
    if ($srw.FindName('ResultTableHost') -ne $null) { Write-Host "PASS  小系统计算结果窗已换成结果表控件" }
    else { Write-Host "FAIL  小系统窗未找到结果表控件"; $fail++ }
    $srw.Close()

    # ---- 文本计算书仍与表格同源(逐行抽查)----
    if ($blvm.ResultText -match '站厅冷负荷合计' -and $blvm.ResultText -match '总制冷量') {
        Write-Host "PASS  导出计算书与结果表同源(分区标题 + 合计行均在文本计算书中)"
    } else { Write-Host "FAIL  计算书文本与表格不同源"; $fail++ }

    # 计算结果**不体现公式文档单元格编号**(2026-09-15 评审):界面表格列与计算书正文都不含
    $codePattern = '(?<![-A-Z])\b[A-Z]{1,2}[0-9]{2,3}\b'   # 排除 EAF-A601 这类设备编号
    $textHit = [regex]::IsMatch($blvm.ResultText, $codePattern) -or [regex]::IsMatch($srvm.ResultText, $codePattern)
    $rowHit = $false
    foreach ($r in $allRows) { if ($r.Label -match $codePattern -or $r.Display -match $codePattern) { $rowHit = $true } }
    if (-not $textHit -and -not $rowHit) {
        Write-Host "PASS  界面表格与计算书均不体现单元格编号(仅悬停提示可见)"
    } else { Write-Host "FAIL  仍有单元格编号出现在正文: text=$textHit row=$rowHit"; $fail++ }
    if ($allRows[0].Hint -match '单元格') { Write-Host "PASS  单元格编号仅在悬停提示(Hint)中提供" }
    else { Write-Host "FAIL  悬停提示未提供编号"; $fail++ }

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

# =====================================================================
# 「计算结果」窗打开即算 + 录入窗"计算即保存"
# (2026-09-15 用户口径:计算完成后结果直接出现在「计算结果」窗,不需要再点一次计算)
#   ① 每个「计算结果」窗 / 负荷计算窗:构造出来(打开)就已有结果,且只是打开不写盘;
#   ② 录入窗点【计 算】= 先落盘再计算,故「计算结果」窗读到的必然是刚算的那一份;
#   ③ 小系统没有房间行时点【计 算】只算不存(避免汇总里留一套空系统)。
# =====================================================================
try {
    $tmp8 = Join-Path $env:TEMP ("HVACIDA-AutoCalc-" + [guid]::NewGuid().ToString('N'))
    $repo8 = New-Object HVACIDA.Core.Services.XmlProjectRepository -ArgumentList $tmp8

    # ---- 大系统负荷计算窗:打开即算 ----
    $aLoad = New-Object "$vmNs.LargeSystemViewModel" -ArgumentList $repo8
    if ($aLoad.Table -ne $null -and $aLoad.Table.Sections.Count -eq 7 -and
        $aLoad.ResultText -match '总制冷量' -and $aLoad.Status -ne '') {
        Write-Host ("PASS  大系统负荷计算窗打开即出结果:结果表 {0} 个分区 + 计算书已生成" -f $aLoad.Table.Sections.Count)
    } else {
        Write-Host ("FAIL  大系统负荷计算窗打开未出结果: table={0} text={1}" -f ($aLoad.Table -ne $null), ($aLoad.ResultText -ne ''))
        $fail++
    }
    if (-not (Test-Path (Join-Path $tmp8 'large-system.xml'))) {
        Write-Host "PASS  只是打开窗不写盘(内部重算不落盘,不会覆盖已保存参数)"
    } else { Write-Host "FAIL  打开窗就写了 large-system.xml"; $fail++ }

    # ---- 大系统计算结果窗:打开即算(此前必须进来再点一次【计 算】才出结果)----
    $aResult = New-Object "$vmNs.LargeResultViewModel" -ArgumentList $repo8
    if ($aResult.Table -ne $null -and $aResult.Table.Sections.Count -eq 7 -and
        $aResult.SmokeRows.Count -eq 2 -and $aResult.ResultText -match '总制冷量' -and
        $aResult.SmokeSummary -match '选型基准') {
        Write-Host ("PASS  大系统计算结果窗打开即出结果:负荷表 {0} 个分区 + 排烟表 {1} 行,无需再点【计 算】" -f `
            $aResult.Table.Sections.Count, $aResult.SmokeRows.Count)
    } else {
        Write-Host ("FAIL  大系统计算结果窗打开未出结果: table={0} smoke={1}" -f ($aResult.Table -ne $null), $aResult.SmokeRows.Count)
        $fail++
    }

    # ---- 负荷计算窗点【计 算】= 计算 + 落盘 ----
    $aLoad.Input.HallAreaM2 = 1888
    $aLoad.CalculateCommand.Execute($null)
    $reload8 = (New-Object HVACIDA.Core.Services.LargeSystemInputService -ArgumentList $repo8).Load()
    if ($reload8.HallAreaM2 -eq 1888 -and $aLoad.Status -match '已同时保存') {
        Write-Host "PASS  大系统负荷计算窗点【计 算】已把本次输入落盘(D55 = 1888 已写入 large-system.xml)"
    } else {
        Write-Host ("FAIL  计算后未落盘: HallAreaM2 = {0} / status='{1}'" -f $reload8.HallAreaM2, $aLoad.Status)
        $fail++
    }

    # 紧接着打开「计算结果」窗:读到的就是刚算的那一份(1888×60 = 113280)
    $aAfter = New-Object "$vmNs.LargeResultViewModel" -ArgumentList $repo8
    if ($aAfter.Input.HallAreaM2 -eq 1888 -and $aAfter.SmokeRows[0].CalculatedFlowM3H -eq 113280) {
        Write-Host "PASS  紧接着打开计算结果窗:读到的就是刚算的参数(站厅计算排烟量 1888×60 = 113280 m³/h)"
    } else {
        Write-Host ("FAIL  计算结果窗未读到刚算的参数: D55={0} 排烟量={1}" -f `
            $aAfter.Input.HallAreaM2, $aAfter.SmokeRows[0].CalculatedFlowM3H)
        $fail++
    }

    # ---- 排烟计算窗点【计 算】= 计算 + 落盘 → 计算结果窗排烟表同步 ----
    $aSmoke = New-Object "$vmNs.LargeSmokeViewModel" -ArgumentList $repo8
    $aSmoke.Input.SmokeRateM3HPerM2 = 72
    $aSmoke.CalculateCommand.Execute($null)
    $saved8 = $repo8.LoadLargeSmoke()
    $aR2 = New-Object "$vmNs.LargeResultViewModel" -ArgumentList $repo8
    if ($saved8.SmokeRateM3HPerM2 -eq 72 -and $aR2.SmokeRows[0].CalculatedFlowM3H -eq 135936) {
        Write-Host "PASS  排烟计算窗点【计 算】已落盘(72 m³/(m²·h)):计算结果窗排烟表同步为 1888×72 = 135936 m³/h"
    } else {
        Write-Host ("FAIL  排烟参数未同步: saved={0} 计算结果窗={1}" -f `
            $saved8.SmokeRateM3HPerM2, $aR2.SmokeRows[0].CalculatedFlowM3H)
        $fail++
    }

    try { Remove-Item $tmp8 -Recurse -Force -ErrorAction Stop } catch { }

    # ---- 小系统:打开即算 / 计算即保存 / 空房间不落盘 ----
    $tmp9 = Join-Path $env:TEMP ("HVACIDA-AutoCalcSmall-" + [guid]::NewGuid().ToString('N'))
    $repo9 = New-Object HVACIDA.Core.Services.XmlProjectRepository -ArgumentList $tmp9
    $sAir = New-Object "$vmNs.SmallSystemViewModel" -ArgumentList ([HVACIDA.Core.Models.SmallSystemType]::AllAirOnceReturn), $repo9
    if ($sAir.Table -ne $null -and $sAir.Table.Sections.Count -ge 4) {
        Write-Host ("PASS  小系统录入窗打开即出结果:结果表 {0} 个分区" -f $sAir.Table.Sections.Count)
    } else { Write-Host "FAIL  小系统录入窗打开未出结果"; $fail++ }
    if (-not (Test-Path (Join-Path $tmp9 'small-systems.xml'))) {
        Write-Host "PASS  只是打开小系统窗不写盘(不会在汇总里留下空系统)"
    } else { Write-Host "FAIL  打开小系统窗就写了 small-systems.xml"; $fail++ }

    $sAir.CalculateCommand.Execute($null)
    $proj9Empty = $repo9.LoadSmallSystems()
    if ($proj9Empty.Systems.Count -eq 0 -and $sAir.Status -match '未保存') {
        Write-Host "PASS  没有房间行时点【计 算】只算不存(状态栏说明原因)"
    } else {
        Write-Host ("FAIL  空系统被落盘: {0} 套 / status='{1}'" -f $proj9Empty.Systems.Count, $sAir.Status)
        $fail++
    }

    $sAir.Input.SystemCode = 'AHU-B101'
    $sAir.Rooms.Add([HVACIDA.Core.Models.SmallRoomInput]::Create('信号设备室', 32.18, 5.9)) | Out-Null
    $sAir.CalculateCommand.Execute($null)
    $saved9 = $repo9.LoadSmallSystems().Find([HVACIDA.Core.Models.SmallSystemType]::AllAirOnceReturn, 'AHU-B101')
    if ($saved9 -ne $null -and $saved9.Rooms.Count -eq 1 -and $sAir.Status -match '已同时保存') {
        Write-Host "PASS  小系统点【计 算】已同时保存(按「类型 + 编号」落盘 AHU-B101,1 个房间)"
    } else {
        Write-Host ("FAIL  小系统计算后未落盘: found={0}" -f ($saved9 -ne $null)); $fail++
    }

    # 端到端:刚算完就打开汇总窗,该系统已在表里(不需要再点保存、也不需要再点计算)
    $sum9 = New-Object "$vmNs.SmallResultViewModel" -ArgumentList $repo9
    $row9 = $sum9.SummaryRows | Where-Object { $_.SystemCode -eq 'AHU-B101' } | Select-Object -First 1
    if ($sum9.Summary.SystemCount -eq 1 -and $row9 -ne $null -and $sum9.Summary.TotalCoolingKw -gt 0) {
        Write-Host ("PASS  端到端:录入窗算完 → 打开汇总窗即有该系统(冷负荷合计 {0:N2} kW)" -f $sum9.Summary.TotalCoolingKw)
    } else {
        Write-Host ("FAIL  汇总窗未出现刚算的系统: count={0} row={1}" -f $sum9.Summary.SystemCount, ($row9 -ne $null)); $fail++
    }

    try { Remove-Item $tmp9 -Recurse -Force -ErrorAction Stop } catch { }
} catch {
    Write-Host ("FAIL  打开即算/计算即保存自检  {0}" -f $_.Exception.Message)
    $fail++
}

# =====================================================================
# 水力计算(需求 2.3 / 2.4):窗口契约 + 打开即算 + 计算即保存 + 端到端
#   Core 端的公式与数值已在 HVACIDA.Smoke 场景13 独立复算(手算期望值);
#   这里验窗口/VM 契约:拾取协议、模型数据回填、按介质切换、「—」、落盘与端到端读数
# =====================================================================
try {
    $tmpH = Join-Path $env:TEMP ("HVACIDA-Hyd-" + [guid]::NewGuid().ToString('N'))
    $repoH = New-Object HVACIDA.Core.Services.XmlProjectRepository -ArgumentList $tmpH

    # ---- 未拾取:录入窗不摆结果,只给指引 ----
    $hAir = New-Object "$vmNs.HydraulicSystemViewModel" -ArgumentList ([HVACIDA.Core.Models.HydraulicKind]::AirDuct), $repoH, $true
    if ($hAir.IsAir -eq $true -and $hAir.IsPickAvailable -eq $true -and $hAir.Table -eq $null -and
        $hAir.Segments.Count -eq 0 -and $hAir.Status -match '还没有管网数据') {
        Write-Host "PASS  风系统水力窗(未拾取):不摆结果,只给「去拾取 / 加行」的指引"
    } else {
        Write-Host ("FAIL  空水力窗: table={0} status='{1}'" -f ($hAir.Table -ne $null), $hAir.Status)
        $fail++
    }

    # ---- 拾取协议 ----
    $hAir.RequestPick()
    if ($hAir.PickRequested -eq $true) { Write-Host "PASS  请求读取系统 → 置标记(命令层据此拾取)" }
    else { Write-Host "FAIL  PickRequested 未置位"; $fail++ }
    $hAir.ClearPickRequest()
    if ($hAir.PickRequested -eq $false) { Write-Host "PASS  拾取后清标记" }
    else { Write-Host "FAIL  拾取标记未清"; $fail++ }

    # ---- 注入「命令层从模型读到的」管网 → 立即出结果 ----
    $airIn = New-Object HVACIDA.Core.Models.HydraulicInput
    $airIn.Kind = [HVACIDA.Core.Models.HydraulicKind]::AirDuct
    $airIn.SystemName = '机械送风 1'; $airIn.MediumTempC = 20; $airIn.FromModel = $true
    $airIn.SourceNote = '数据来自模型:自检注入'; $airIn.CriticalPathName = '散流器 1'
    $airSeg = New-Object HVACIDA.Core.Models.HydraulicSegment
    $airSeg.Name = '送风主管 #101'; $airSeg.ElementId = 101
    $airSeg.Shape = [HVACIDA.Core.Models.HydraulicShape]::Round
    $airSeg.DiameterM = 0.5; $airSeg.LengthM = 10; $airSeg.FlowM3H = 3600
    $airSeg.LocalZetaSum = 1.0; $airSeg.LocalNote = '90°弯头(ζ=0.25)'; $airSeg.OnCriticalPath = $true
    $airIn.Segments.Add($airSeg)
    $airTerm = New-Object HVACIDA.Core.Models.HydraulicTerminal
    $airTerm.Name = '散流器 1'; $airTerm.Kind = [HVACIDA.Core.Models.HydraulicItemKind]::Terminal
    $airTerm.ResistancePa = 0; $airTerm.Source = 'ζ=2.0 × 动压'; $airTerm.OnCriticalPath = $true
    $airIn.Terminals.Add($airTerm)
    $hAir.ApplyPickedSystem($airIn, '已从模型读入 1 段风管')
    if ($hAir.Table -ne $null -and $hAir.Segments.Count -eq 1 -and $hAir.SegmentRows.Count -eq 1 -and
        $hAir.Summary -match '需求全压' -and $hAir.Summary -match '最不利环路 1 段') {
        Write-Host ("PASS  读入模型数据后立即出结果:{0}" -f $hAir.Summary)
    } else {
        Write-Host ("FAIL  模型数据回填后未出结果: table={0} summary='{1}'" -f ($hAir.Table -ne $null), $hAir.Summary)
        $fail++
    }

    # ---- 窗口渲染:管段表 11 列 / 系数表 25 条 / 结果表已绑定 ----
    $hAirW = New-Object "$uiNs.HydraulicSystemWindow" -ArgumentList $hAir
    $hAirW.Show(); $hAirW.UpdateLayout()
    $segGridH = $hAirW.FindName('SegmentGrid')
    $lossGridH = $hAirW.FindName('LossGrid')
    $tableHostH = $hAirW.FindName('ResultTableHost')
    if ($segGridH -ne $null -and $segGridH.Columns.Count -eq 11 -and $lossGridH -ne $null -and
        $lossGridH.Columns.Count -eq 4 -and $tableHostH -ne $null -and $tableHostH.Table -ne $null -and
        $hAir.Coefficients.LocalLossItems.Count -eq 25) {
        Write-Host ("PASS  水力录入窗渲染:管段表 {0} 列 / 系数表 {1} 列(共 {2} 条管件)/ 结果表已绑定" -f `
            $segGridH.Columns.Count, $lossGridH.Columns.Count, $hAir.Coefficients.LocalLossItems.Count)
    } else {
        Write-Host "FAIL  水力录入窗控件/绑定不符"; $fail++
    }
    $hAirW.Close()

    # ---- 计算即保存:点【计算并保存】→ 重开窗仍是同一份 ----
    $hAir.CalculateCommand.Execute($null)
    $hAirReload = New-Object "$vmNs.HydraulicSystemViewModel" -ArgumentList ([HVACIDA.Core.Models.HydraulicKind]::AirDuct), $repoH
    if ($hAirReload.Segments.Count -eq 1 -and $hAir.Status -match '已同时保存' -and
        [math]::Abs($hAirReload.Segments[0].FlowM3H - 3600) -lt 1e-9) {
        Write-Host "PASS  风系统点【计算并保存】已落盘(重开窗读到 1 段、流量 3600 m³/h)"
    } else {
        Write-Host ("FAIL  计算即保存: reload={0} status='{1}'" -f $hAirReload.Segments.Count, $hAir.Status)
        $fail++
    }

    # ---- 水系统:静压高差 + 额定扬程校核 ----
    $hWater = New-Object "$vmNs.HydraulicSystemViewModel" -ArgumentList ([HVACIDA.Core.Models.HydraulicKind]::WaterPipe), $repoH, $true
    $waterIn = New-Object HVACIDA.Core.Models.HydraulicInput
    $waterIn.Kind = [HVACIDA.Core.Models.HydraulicKind]::WaterPipe
    $waterIn.SystemName = '冷冻水供回水'; $waterIn.MediumTempC = 10; $waterIn.FromModel = $true
    $waterIn.RatedHeadM = 8; $waterIn.CriticalPathName = '风机盘管末端'
    $waterSeg = New-Object HVACIDA.Core.Models.HydraulicSegment
    $waterSeg.Name = '供水干管 #201'; $waterSeg.ElementId = 201
    $waterSeg.Shape = [HVACIDA.Core.Models.HydraulicShape]::Round
    $waterSeg.DiameterM = 0.1; $waterSeg.LengthM = 50; $waterSeg.FlowM3H = 36
    $waterSeg.LocalZetaSum = 2.0; $waterSeg.OnCriticalPath = $true
    $waterIn.Segments.Add($waterSeg)
    $chiller = New-Object HVACIDA.Core.Models.HydraulicTerminal
    $chiller.Name = '空调机组盘管'; $chiller.Kind = [HVACIDA.Core.Models.HydraulicItemKind]::Equipment
    $chiller.ResistancePa = 30000; $chiller.Source = '设备样本 30 kPa'; $chiller.OnCriticalPath = $true
    $waterIn.Terminals.Add($chiller)
    $fanCoil = New-Object HVACIDA.Core.Models.HydraulicTerminal
    $fanCoil.Name = '风机盘管末端'; $fanCoil.Kind = [HVACIDA.Core.Models.HydraulicItemKind]::Terminal
    $fanCoil.ElementId = 401
    $fanCoil.ResistancePa = 20000; $fanCoil.Source = '样本 20 kPa'; $fanCoil.OnCriticalPath = $true
    $waterIn.Terminals.Add($fanCoil)

    # 并联支路 B(管径流量相同、管长更短 → 阻力小 19.18%,应判超限并给出平衡阀 Kv ≈ 145.83)
    $waterSegB = New-Object HVACIDA.Core.Models.HydraulicSegment
    $waterSegB.Name = '支路 B 供水管 #202'; $waterSegB.ElementId = 202
    $waterSegB.Shape = [HVACIDA.Core.Models.HydraulicShape]::Round
    $waterSegB.DiameterM = 0.1; $waterSegB.LengthM = 20; $waterSegB.FlowM3H = 36
    $waterSegB.LocalZetaSum = 2.0; $waterSegB.OnCriticalPath = $false
    $waterIn.Segments.Add($waterSegB)
    $fanCoilB = New-Object HVACIDA.Core.Models.HydraulicTerminal
    $fanCoilB.Name = '风机盘管 B'; $fanCoilB.Kind = [HVACIDA.Core.Models.HydraulicItemKind]::Terminal
    $fanCoilB.ElementId = 402
    $fanCoilB.ResistancePa = 20000; $fanCoilB.Source = '样本 20 kPa'; $fanCoilB.OnCriticalPath = $false
    $waterIn.Terminals.Add($fanCoilB)
    $branchA = New-Object HVACIDA.Core.Models.HydraulicBranch
    $branchA.Name = '风机盘管末端'; $branchA.TerminalElementId = 401; $branchA.IsCritical = $true
    $branchA.SegmentElementIds.Add(201) | Out-Null
    $branchA.SegmentSummary = '供水干管 #201'
    $waterIn.Branches.Add($branchA)
    $branchB = New-Object HVACIDA.Core.Models.HydraulicBranch
    $branchB.Name = '风机盘管 B'; $branchB.TerminalElementId = 402
    $branchB.SegmentElementIds.Add(202) | Out-Null
    $branchB.SegmentSummary = '支路 B 供水管 #202'
    $waterIn.Branches.Add($branchB)

    $hWater.ApplyPickedSystem($waterIn, '已从模型读入 2 段水管')
    if ($hWater.IsWater -eq $true -and $hWater.Summary -match '需求扬程 6\.93' -and
        $hWater.CheckVerdict -match '满足' -and $hWater.Table.Sections.Count -eq 6 -and
        $hWater.Summary -match '并联支路 2 条' -and $hWater.Summary -match '最大不平衡率 19\.2%') {
        Write-Host ("PASS  水系统读数:{0}" -f $hWater.Summary)
        Write-Host ("PASS  水系统校核:{0}" -f $hWater.CheckVerdict)
    } else {
        Write-Host ("FAIL  水系统读数: summary='{0}' verdict='{1}'" -f $hWater.Summary, $hWater.CheckVerdict)
        $fail++
    }
    $hWater.CalculateCommand.Execute($null)

    # ---- 计算结果窗:打开即算 / 全站汇总 / 「—」 / 选中行刷新 ----
    $hr = New-Object "$vmNs.HydraulicResultViewModel" -ArgumentList $repoH
    if ($hr.Rows.Count -eq 2 -and $hr.Table -ne $null -and $hr.Table.Sections.Count -eq 2 -and
        $hr.SegmentRows.Count -ge 1 -and $hr.SummaryTitle -match '全站共 2 套系统') {
        Write-Host ("PASS  水力计算结果窗打开即出结果:{0}" -f $hr.SummaryTitle)
    } else {
        Write-Host ("FAIL  水力结果窗: rows={0} table={1}" -f $hr.Rows.Count, ($hr.Table -ne $null))
        $fail++
    }

    $airRowH = $hr.Rows | Where-Object { $_.KindName -eq '风系统' } | Select-Object -First 1
    $waterRowH = $hr.Rows | Where-Object { $_.KindName -eq '水系统' } | Select-Object -First 1
    if ($airRowH -ne $null -and $waterRowH -ne $null -and $airRowH.StaticText -eq '—' -and
        $waterRowH.OutletText -eq '—' -and $airRowH.RequiredText -match 'Pa$' -and
        $waterRowH.RequiredText -match 'm$' -and $waterRowH.MarginText -match '%') {
        Write-Host ("PASS  不涉及的量显示「—」且单位按介质切换:风 需求={0} 静压={1};水 需求={2} 出口动压={3} 余量={4}" -f `
            $airRowH.RequiredText, $airRowH.StaticText, $waterRowH.RequiredText, $waterRowH.OutletText, $waterRowH.MarginText)
    } else {
        Write-Host "FAIL  介质单位/「—」显示不符"; $fail++
    }

    $hr.SelectedRow = $waterRowH
    if ($hr.DetailTitle -match '水系统' -and $hr.SegmentRows.Count -eq 2 -and $hr.ItemRows.Count -eq 3 -and
        $hr.ResultText -match '需求扬程' -and $hr.ResultText -match '管段明细') {
        Write-Host "PASS  选中水系统行 → 逐段明细 / 阻力项 / 计算书同步刷新"
    } else {
        Write-Host ("FAIL  选中行刷新: title='{0}' segs={1} items={2}" -f $hr.DetailTitle, $hr.SegmentRows.Count, $hr.ItemRows.Count)
        $fail++
    }

    # ---- 并联环路平衡 + 阻力特性曲线(结果窗两张新表) ----
    $branchBRow = $hr.BranchRows | Where-Object { $_.Name -eq '风机盘管 B' } | Select-Object -First 1
    if ($hr.BranchRows.Count -eq 2 -and $branchBRow -ne $null -and
        [math]::Abs($branchBRow.ImbalancePct - 19.1775) -lt 0.01 -and
        [math]::Abs($branchBRow.ValveKv - 145.8295) -lt 0.02 -and
        [math]::Abs($branchBRow.ValveAuthority - 0.19178) -lt 0.0001 -and
        $branchBRow.WithinLimit -eq $false -and $hr.CurveRows.Count -eq 9 -and
        $hr.BalanceNote -match '工况点') {
        Write-Host ("PASS  并联环路平衡:支路 B 不平衡 {0:N2}% → 平衡阀 Kv={1:N2}、阀权度 {2:N4};曲线 {3} 点,口径写明工况点需与厂家曲线求交" -f `
            $branchBRow.ImbalancePct, $branchBRow.ValveKv, $branchBRow.ValveAuthority, $hr.CurveRows.Count)
    } else {
        Write-Host ("FAIL  并联平衡/曲线: branches={0} curve={1}" -f $hr.BranchRows.Count, $hr.CurveRows.Count)
        $fail++
    }

    $hrW = New-Object "$uiNs.HydraulicResultWindow" -ArgumentList $hr
    $hrW.Show(); $hrW.UpdateLayout()
    $sumGridH = $hrW.FindName('SummaryGrid')
    $segGridR = $hrW.FindName('SegmentGrid')
    $itemGridH = $hrW.FindName('ItemGrid')
    $branchGridH = $hrW.FindName('BranchGrid')
    $curveGridH = $hrW.FindName('CurveGrid')
    if ($sumGridH -ne $null -and $sumGridH.Columns.Count -eq 20 -and $sumGridH.Items.Count -eq 2 -and
        $segGridR -ne $null -and $segGridR.Columns.Count -eq 14 -and
        $itemGridH -ne $null -and $itemGridH.Columns.Count -eq 5 -and
        $branchGridH -ne $null -and $branchGridH.Columns.Count -eq 12 -and $branchGridH.Items.Count -eq 2 -and
        $curveGridH -ne $null -and $curveGridH.Columns.Count -eq 4 -and $curveGridH.Items.Count -eq 9) {
        Write-Host ("PASS  水力结果窗渲染:全站汇总 {0} 列 × {1} 行 / 逐段 {2} 列 / 阻力项 {3} 列 / 并联平衡 {4} 列 × {5} 行 / 特性曲线 {6} 列 × {7} 点" -f `
            $sumGridH.Columns.Count, $sumGridH.Items.Count, $segGridR.Columns.Count, $itemGridH.Columns.Count, `
            $branchGridH.Columns.Count, $branchGridH.Items.Count, $curveGridH.Columns.Count, $curveGridH.Items.Count)
    } else {
        Write-Host "FAIL  水力结果窗控件/绑定不符"; $fail++
    }
    $hrW.Close()

    # ---- 多系统汇总 + Excel 导出(写到临时目录,不碰真实 %AppData%) ----
    $hReports = Join-Path $env:TEMP ("HVACIDA-HydReports-" + [guid]::NewGuid().ToString('N'))
    $hr2 = New-Object "$vmNs.HydraulicResultViewModel" -ArgumentList $repoH, $hReports
    if ($hr2.Summary.SystemCount -eq 2 -and $hr2.Rows.Count -eq 2 -and $hr2.Table -ne $null -and
        $hr2.Table.Sections.Count -eq 2 -and $hr2.SummaryTitle -match '全站共 2 套系统' -and
        $hr2.Note -match '不可相加') {
        Write-Host ("PASS  多系统汇总:{0}" -f $hr2.SummaryTitle)
    } else {
        $tableSections = -1
        if ($hr2.Table -ne $null) { $tableSections = $hr2.Table.Sections.Count }
        Write-Host ("FAIL  多系统汇总: systems={0} rows={1} tableSections={2}" -f `
            $hr2.Summary.SystemCount, $hr2.Rows.Count, $tableSections)
        $fail++
    }

    $hr2.ExportExcelCommand.Execute($null)
    $xlsx = @(Get-ChildItem -LiteralPath $hReports -Filter *.xlsx -ErrorAction SilentlyContinue)
    if ($xlsx.Count -eq 1 -and $hr2.Status -match 'Excel 已生成') {
        Write-Host ("PASS  全站汇总 Excel 已导出:{0} 字节(状态:{1})" -f $xlsx[0].Length, $hr2.Status)
    } else {
        Write-Host ("FAIL  全站 Excel 导出: files={0} status='{1}'" -f $xlsx.Count, $hr2.Status); $fail++
    }

    # 录入窗的 Excel 导出(单系统 6 页)
    $hAirExcel = New-Object "$vmNs.HydraulicSystemViewModel" -ArgumentList ([HVACIDA.Core.Models.HydraulicKind]::AirDuct), $repoH, $true, $hReports
    $hAirExcel.ExportExcelCommand.Execute($null)
    $xlsx2 = @(Get-ChildItem -LiteralPath $hReports -Filter *.xlsx -ErrorAction SilentlyContinue)
    if ($xlsx2.Count -eq 2 -and $hAirExcel.Status -match '6 个工作表') {
        Write-Host ("PASS  单系统 Excel 已导出(6 页):{0}" -f $hAirExcel.Status)
    } else {
        Write-Host ("FAIL  单系统 Excel 导出: files={0} status='{1}'" -f $xlsx2.Count, $hAirExcel.Status); $fail++
    }
    try { Remove-Item $hReports -Recurse -Force -ErrorAction Stop } catch { }

    # ---- 空工程:汇总窗不摆结果;空系统不落盘 ----
    $tmpH2 = Join-Path $env:TEMP ("HVACIDA-Hyd-" + [guid]::NewGuid().ToString('N'))
    $repoH2 = New-Object HVACIDA.Core.Services.XmlProjectRepository -ArgumentList $tmpH2
    $hrEmpty = New-Object "$vmNs.HydraulicResultViewModel" -ArgumentList $repoH2
    if ($hrEmpty.Rows.Count -eq 0 -and $hrEmpty.Table -eq $null -and $hrEmpty.Status -match '还没有水力系统') {
        Write-Host "PASS  水力计算结果窗(空工程):汇总表为空、不摆结果、只给去拾取的指引"
    } else {
        Write-Host ("FAIL  空工程水力结果窗: rows={0}" -f $hrEmpty.Rows.Count); $fail++
    }

    $hEmpty = New-Object "$vmNs.HydraulicSystemViewModel" -ArgumentList ([HVACIDA.Core.Models.HydraulicKind]::AirDuct), $repoH2, $true
    $hEmpty.CalculateCommand.Execute($null)
    if (-not (Test-Path (Join-Path $tmpH2 'hydraulic.xml')) -and $hEmpty.Status -match '未保存') {
        Write-Host "PASS  没有管段时点【计算并保存】只算不存(不会在结果窗里留空系统)"
    } else {
        Write-Host "FAIL  空系统被落盘"; $fail++
    }

    # ---- 三个水力键必须是「真功能」而不是「待实现说明窗」(防回归:类名搬回 ModuleInfoCommands.cs 即 FAIL) ----
    try {
        $thisScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path      # tools\HVACIDA.Smoke
        $repoRoot = Split-Path (Split-Path $thisScriptDir -Parent) -Parent     # 仓库根
        $srcRoot = Join-Path $repoRoot 'src'
        $hydCmdFile = Join-Path $srcRoot 'HVACIDA.Revit\Commands\HydraulicCommands.cs'
        $infoCmdFile = Join-Path $srcRoot 'HVACIDA.Revit\Commands\ModuleInfoCommands.cs'
        if ((Test-Path -LiteralPath $hydCmdFile) -and (Test-Path -LiteralPath $infoCmdFile)) {
            $hydText = [string](Get-Content -LiteralPath $hydCmdFile -Raw)
            $infoText = [string](Get-Content -LiteralPath $infoCmdFile -Raw)
            $moved = $hydText.Contains('class ShowAirHydraulicCommand') -and
                     $hydText.Contains('class ShowWaterHydraulicCommand') -and
                     $hydText.Contains('class ShowHydraulicResultCommand') -and
                     $hydText.Contains('TransactionMode.Manual') -and
                     $hydText.Contains('RevitHydraulicReader')
            $stillInfo = $infoText.Contains('ShowAirHydraulicCommand') -or
                         $infoText.Contains('ShowWaterHydraulicCommand') -or
                         $infoText.Contains('ShowHydraulicResultCommand')
            if ($moved -and -not $stillInfo) {
                Write-Host "PASS  水力三键已是真实功能(命令在 HydraulicCommands.cs 且走 RevitHydraulicReader,未留在待实现说明窗)"
            } else {
                Write-Host ("FAIL  水力三键接线: moved={0} stillInInfoWindow={1}" -f $moved, $stillInfo); $fail++
            }
        } else {
            Write-Host ("FAIL  找不到水力命令源文件: {0} / {1}" -f $hydCmdFile, $infoCmdFile); $fail++
        }
    } catch {
        Write-Host ("FAIL  水力三键接线检查  {0}" -f $_.Exception.Message); $fail++
    }

    try { Remove-Item $tmpH -Recurse -Force -ErrorAction Stop } catch { }
    try { Remove-Item $tmpH2 -Recurse -Force -ErrorAction Stop } catch { }
} catch {
    Write-Host ("FAIL  水力计算窗口自检  {0}" -f $_.Exception.Message)
    $fail++
}

# =====================================================================
# 计算书 Excel 导出(大系统负荷 / 排烟 / 小系统 / 小系统全站汇总):写到临时目录,不碰真实 %AppData%
# =====================================================================
try {
    $xlDir = Join-Path $env:TEMP ("HVACIDA-XlReports-" + [guid]::NewGuid().ToString('N'))
    $xlRepoDir = Join-Path $env:TEMP ("HVACIDA-XlRepo-" + [guid]::NewGuid().ToString('N'))
    $repoX = New-Object HVACIDA.Core.Services.XmlProjectRepository -ArgumentList $xlRepoDir

    $loadVm = New-Object "$vmNs.LargeSystemViewModel" -ArgumentList $repoX, $xlDir
    $loadVm.ExportExcelCommand.Execute($null)
    $smokeVm = New-Object "$vmNs.LargeSmokeViewModel" -ArgumentList $repoX, $xlDir
    $smokeVm.ExportExcelCommand.Execute($null)
    $largeResultVm = New-Object "$vmNs.LargeResultViewModel" -ArgumentList $repoX, $xlDir
    $largeResultVm.ExportExcelCommand.Execute($null)

    $smallVm = New-Object "$vmNs.SmallSystemViewModel" -ArgumentList ([HVACIDA.Core.Models.SmallSystemType]::AllAirOnceReturn), $repoX, $false, $xlDir
    $smallVm.Input.SystemCode = 'AHU-XL1'
    $smallVm.Rooms.Add([HVACIDA.Core.Models.SmallRoomInput]::Create('弱电间1', 50, 5.9)) | Out-Null
    $smallVm.SaveCommand.Execute($null)
    $smallVm.ExportExcelCommand.Execute($null)
    $smallSumVm = New-Object "$vmNs.SmallResultViewModel" -ArgumentList $repoX, $xlDir
    $smallSumVm.ExportExcelCommand.Execute($null)

    $xl = @(Get-ChildItem -LiteralPath $xlDir -Filter *.xlsx -ErrorAction SilentlyContinue)
    if ($xl.Count -eq 5) {
        Write-Host ("PASS  五个模块的 Excel 计算书都写出了(共 {0} 个 .xlsx)" -f $xl.Count)
    } else {
        Write-Host ("FAIL  Excel 导出文件数 = {0}(期望 5)" -f $xl.Count); $fail++
    }
    if ($smallVm.Status -match '4 个工作表' -and $loadVm.Status -match '2 个工作表' -and
        $smokeVm.Status -match '3 个工作表' -and $smallSumVm.Status -match '个工作表') {
        Write-Host "PASS  各窗状态栏都报出了工作表数(小系统 4 页 / 大系统负荷 2 页 / 排烟 3 页 / 全站汇总多页)"
    } else {
        Write-Host ("FAIL  状态栏: small='{0}' smoke='{1}' load='{2}' sum='{3}'" -f `
            $smallVm.Status, $smokeVm.Status, $loadVm.Status, $smallSumVm.Status)
        $fail++
    }
    try { Remove-Item $xlDir -Recurse -Force -ErrorAction Stop } catch { }
    try { Remove-Item $xlRepoDir -Recurse -Force -ErrorAction Stop } catch { }
} catch {
    Write-Host ("FAIL  计算书 Excel 导出自检  {0}" -f $_.Exception.Message)
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
