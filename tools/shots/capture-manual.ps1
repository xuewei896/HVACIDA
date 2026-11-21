# =============================================================================
#  capture-manual.ps1 -- render the real WPF windows (and a Ribbon mock built
#  from ModuleCatalog + the shipped 32px icons) to PNG for the user manual.
#  No Revit required: every window image is the actual Window + ViewModel.
# =============================================================================
param(
    [string]$UiDir = 'D:\DSH\src\HVACIDA.UI\bin\Release\net48',
    [string]$CoreDir = 'D:\DSH\src\HVACIDA.Core\bin\Release\net48',
    [string]$OutDir = 'D:\DSH\docs\manual-images',
    [string]$IconDir = 'D:\DSH\src\HVACIDA.Revit\Resources\Icons',
    [int]$Scale = 2
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase, System.Xaml
Add-Type -Path (Join-Path $CoreDir 'HVACIDA.Core.dll')
Add-Type -Path (Join-Path $UiDir 'HVACIDA.UI.dll')

$vmNs = 'HVACIDA.UI.ViewModels'
$uiNs = 'HVACIDA.UI.Views'
if (-not (Test-Path -LiteralPath $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }

function New-TempRepo {
    $dir = Join-Path $env:TEMP ("HVACIDA-Manual-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir | Out-Null
    return New-Object HVACIDA.Core.Services.XmlProjectRepository -ArgumentList $dir
}

function Save-Shot($window, [string]$name, [int]$w = 0, [int]$h = 0, [switch]$RightEnd) {
    try {
        if ($w -gt 0) { $window.Width = $w }
        if ($h -gt 0) { $window.Height = $h }
        $window.WindowStartupLocation = 'Manual'
        $window.Left = 0
        $window.Top = 0
        $window.Show()
        $window.UpdateLayout()
        Start-Sleep -Milliseconds 250
        try { $window.BeginAnimation([System.Windows.UIElement]::OpacityProperty, $null) } catch { }
        $window.Opacity = 1.0
        Reset-Scroll $window
        if ($RightEnd) {
            foreach ($sv in @(Get-VisualDescendants $window | Where-Object { $_ -is [System.Windows.Controls.ScrollViewer] })) {
                try { if ($sv.ScrollableWidth -gt 1) { $sv.ScrollToRightEnd() } } catch { }
            }
        }
        $window.UpdateLayout()
        $aw = [int][Math]::Ceiling($window.ActualWidth)
        $ah = [int][Math]::Ceiling($window.ActualHeight)
        if ($aw -le 0 -or $ah -le 0) { throw "window has no size ($aw x $ah)" }
        $bmp = New-Object System.Windows.Media.Imaging.RenderTargetBitmap (
            $aw * $Scale), ($ah * $Scale), (96 * $Scale), (96 * $Scale),
            ([System.Windows.Media.PixelFormats]::Pbgra32)
        $bmp.Render($window)

        # 铺白底(Flatten):窗口淡入/透明会让 PNG 带 alpha,插进 Word 会显得发白
        $dv = New-Object System.Windows.Media.DrawingVisual
        $dc = $dv.RenderOpen()
        $dc.DrawRectangle([System.Windows.Media.Brushes]::White, $null, (New-Object System.Windows.Rect(0, 0, $aw, $ah)))
        $dc.DrawImage($bmp, (New-Object System.Windows.Rect(0, 0, $aw, $ah)))
        $dc.Close()
        $flat = New-Object System.Windows.Media.Imaging.RenderTargetBitmap (
            $aw * $Scale), ($ah * $Scale), (96 * $Scale), (96 * $Scale),
            ([System.Windows.Media.PixelFormats]::Pbgra32)
        $flat.Render($dv)

        $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
        $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($flat))
        $path = Join-Path $OutDir ($name + '.png')
        $fs = [System.IO.File]::Create($path)
        $enc.Save($fs)
        $fs.Close()
        Write-Host ("SHOT  {0,-20} {1} x {2}" -f $name, ($aw * $Scale), ($ah * $Scale))
    }
    finally {
        try { $window.Close() } catch { }
    }
}

function Get-VisualDescendants($root) {
    $list = New-Object System.Collections.ArrayList
    $stack = New-Object System.Collections.Stack
    $stack.Push($root)
    while ($stack.Count -gt 0) {
        $node = $stack.Pop()
        if ($node -eq $null) { continue }
        [void]$list.Add($node)
        $n = [System.Windows.Media.VisualTreeHelper]::GetChildrenCount($node)
        for ($i = 0; $i -lt $n; $i++) {
            $stack.Push([System.Windows.Media.VisualTreeHelper]::GetChild($node, $i))
        }
    }
    return $list
}

# 截图前把所有 ScrollViewer 回到顶部(否则焦点滚动会让首段标题被裁掉)
function Reset-Scroll($window) {
    foreach ($sv in @(Get-VisualDescendants $window | Where-Object { $_ -is [System.Windows.Controls.ScrollViewer] })) {
        try { $sv.ScrollToTop(); $sv.ScrollToLeftEnd() } catch { }
    }
}

function Load-Icon([string]$key) {
    $p = Join-Path $IconDir ($key + '_32.png')
    if (-not (Test-Path -LiteralPath $p)) { return $null }
    $bi = New-Object System.Windows.Media.Imaging.BitmapImage
    $bi.BeginInit()
    $bi.CacheOption = [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad
    $bi.UriSource = New-Object System.Uri($p)
    $bi.EndInit()
    return $bi
}

# ---------------------------------------------------------------- 0. Ribbon
try {
    $panelOrder = [HVACIDA.Core.Services.ModuleCatalog]::PanelOrder
    $win = New-Object System.Windows.Window
    $win.WindowStyle = [System.Windows.WindowStyle]::None
    $win.ResizeMode = [System.Windows.ResizeMode]::NoResize
    $win.Background = [System.Windows.Media.Brushes]::White
    $win.ShowInTaskbar = $false

    $root = New-Object System.Windows.Controls.StackPanel
    $root.Orientation = 'Horizontal'

    foreach ($panelName in $panelOrder) {
        $mods = @([HVACIDA.Core.Services.ModuleCatalog]::ByPanel($panelName))
        $panel = New-Object System.Windows.Controls.Grid
        $panel.Margin = '2,0,2,0'
        $r1 = New-Object System.Windows.Controls.RowDefinition
        $r1.Height = New-Object System.Windows.GridLength(1, [System.Windows.GridUnitType]::Star)
        $r2 = New-Object System.Windows.Controls.RowDefinition
        $r2.Height = [System.Windows.GridLength]::Auto
        $panel.RowDefinitions.Add($r1) | Out-Null
        $panel.RowDefinitions.Add($r2) | Out-Null

        $wrap = New-Object System.Windows.Controls.WrapPanel
        $wrap.Orientation = 'Horizontal'
        $wrap.ItemHeight = 64
        $wrap.Width = (([Math]::Min($mods.Count, 3)) * 78 + 2)

        foreach ($m in $mods) {
            $btn = New-Object System.Windows.Controls.StackPanel
            $btn.Width = 76
            $btn.Height = 62
            $btn.Margin = '1,1,1,1'
            $btn.Background = [System.Windows.Media.Brushes]::Transparent
            $ico = Load-Icon $m.Key
            if ($ico -ne $null) {
                $img = New-Object System.Windows.Controls.Image
                $img.Source = $ico
                $img.Width = 26
                $img.Height = 26
                $img.Margin = '0,4,0,2'
                $btn.Children.Add($img) | Out-Null
            }
            $t = New-Object System.Windows.Controls.TextBlock
            $t.Text = $m.TitleOneLine
            $t.FontSize = 10
            $t.TextWrapping = 'Wrap'
            $t.TextAlignment = 'Center'
            $t.Foreground = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(0x3A, 0x3A, 0x3A))
            $btn.Children.Add($t) | Out-Null
            $wrap.Children.Add($btn) | Out-Null
        }
        [System.Windows.Controls.Grid]::SetRow($wrap, 0)
        $panel.Children.Add($wrap) | Out-Null

        $name = New-Object System.Windows.Controls.TextBlock
        $name.Text = $panelName
        $name.FontSize = 10
        $name.HorizontalAlignment = 'Center'
        $name.Margin = '0,2,0,3'
        $name.Foreground = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(0x66, 0x66, 0x66))
        [System.Windows.Controls.Grid]::SetRow($name, 1)
        $panel.Children.Add($name) | Out-Null
        $root.Children.Add($panel) | Out-Null

        $sep = New-Object System.Windows.Controls.Border
        $sep.Width = 1
        $sep.Margin = '2,6,2,6'
        $sep.Background = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(0xDD, 0xE3, 0xEA))
        $root.Children.Add($sep) | Out-Null
    }

    $host2 = New-Object System.Windows.Controls.Border
    $host2.Background = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(0xF7, 0xF9, 0xFC))
    $host2.BorderBrush = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(0xC9, 0xDC, 0xF2))
    $host2.BorderThickness = '0,0,0,1'
    $host2.Padding = '6,4,6,2'
    $host2.Child = $root
    $win.Content = $host2
    Save-Shot $win 'm-ribbon' 1480 200
} catch { Write-Host ("FAIL ribbon: {0}" -f $_.Exception.Message) }

# ---------------------------------------------------------------- 1. 工程信息
try {
    $repo = New-TempRepo
    $vm = New-Object "$vmNs.ProjectInfoViewModel" -ArgumentList $repo
    $vm.Model.Basic.ProjectName = '苏州地铁 5 号线一期工程'
    $vm.SelectedProvince = '江苏'
    $vm.SelectedCity = '苏州'
    $vm.Model.Basic.StationName = '太湖香山站'
    $vm.Model.Basic.DesignStage = '施工图设计'
    $vm.Model.Basic.Remark = '机电系统 BIM 正向设计试点站'
    $win = New-Object "$uiNs.ProjectInfoWindow" -ArgumentList $vm
    Save-Shot $win 'm-project' 1180 520
} catch { Write-Host ("FAIL project: {0}" -f $_.Exception.Message) }

# ---------------------------------------------------------------- 2. 气象参数
try {
    $repo = New-TempRepo
    $vm = New-Object "$vmNs.ProjectInfoViewModel" -ArgumentList $repo
    $vm.SelectedProvince = '江苏'
    $vm.SelectedCity = '苏州'
    $vm.Model.Design.LargeSystemOutdoor.SummerACDryBulbC = 34.8
    $vm.Model.Design.LargeSystemOutdoor.SummerACWetBulbC = 28.1
    $vm.Model.Design.LargeSystemOutdoor.SummerVentDryBulbC = 32.0
    $vm.Model.Design.LargeSystemOutdoor.WinterACDryBulbC = -2.0
    $vm.Model.Design.LargeSystemOutdoor.WinterVentDryBulbC = 4.0
    $vm.Model.Design.SmallSystemOutdoor.SummerACDryBulbC = 34.8
    $vm.Model.Design.SmallSystemOutdoor.SummerACWetBulbC = 28.1
    $vm.Model.Design.SmallSystemOutdoor.SummerVentDryBulbC = 32.0
    $win = New-Object "$uiNs.WeatherWindow" -ArgumentList $vm
    Save-Shot $win 'm-weather' 1180 640
} catch { Write-Host ("FAIL weather: {0}" -f $_.Exception.Message) }

# ---------------------------------------------------------------- 3. 公共区参数
try {
    $repo = New-TempRepo
    $spaces = New-Object 'System.Collections.Generic.List[HVACIDA.Core.Models.SpaceSnapshot]'
    $defs = @(
        @('01', '站厅层公共区', '标高 1', 2860.0, 12870.0),
        @('02', '站台层公共区', '标高 2', 1980.0, 8910.0),
        @('03', '站厅层付费区', '标高 1', 1120.0, 5040.0),
        @('04', '站台层设备区走道', '标高 2', 320.0, 1440.0)
    )
    foreach ($d in $defs) {
        $s = New-Object HVACIDA.Core.Models.SpaceSnapshot
        $s.Number = $d[0]; $s.Name = $d[1]; $s.LevelName = $d[2]
        $s.AreaM2 = [double]$d[3]; $s.VolumeM3 = [double]$d[4]
        $s.HeightM = [Math]::Round($s.VolumeM3 / $s.AreaM2, 2)
        $spaces.Add($s)
    }
    $vm = New-Object "$vmNs.PublicAreaViewModel" -ArgumentList $repo, $spaces, '模型读取:公共区空间 4 个', $false
    $vm.Input.HallAreaM2 = 2860.0
    $vm.Input.HallHeightM = 4.5
    $vm.Input.HallLengthM = 168.0
    $vm.Input.PlatformAreaM2 = 1980.0
    $vm.Input.UpLineBoardCount = 1180
    $vm.Input.UpLineAlightCount = 1040
    $vm.Input.DownLineBoardCount = 1120
    $vm.Input.DownLineAlightCount = 1010
    $win = New-Object "$uiNs.PublicAreaWindow" -ArgumentList $vm
    Save-Shot $win 'm-public-area' 1180 980
} catch { Write-Host ("FAIL public-area: {0}" -f $_.Exception.Message) }

# ---------------------------------------------------------------- 4. 大系统负荷计算
try {
    $repo = New-TempRepo
    $large = $repo.LoadLargeSystem()
    $large.HallAreaM2 = 2860.0
    $large.HallHeightM = 4.5
    $large.HallLengthM = 168.0
    $large.PlatformAreaM2 = 1980.0
    $large.OutdoorWetBulbC = 28.1
    $large.UpLineBoardCount = 1180
    $large.UpLineAlightCount = 1040
    $large.DownLineBoardCount = 1120
    $large.DownLineAlightCount = 1010
    $repo.SaveLargeSystem($large)
    $vm = New-Object "$vmNs.LargeSystemViewModel" -ArgumentList $repo
    $win = New-Object "$uiNs.LargeSystemWindow" -ArgumentList $vm
    Save-Shot $win 'm-large-load' 1560 900
} catch { Write-Host ("FAIL large-load: {0}" -f $_.Exception.Message) }

# ---------------------------------------------------------------- 5. 大系统排烟
try {
    $repo = New-TempRepo
    $large = $repo.LoadLargeSystem()
    $large.HallAreaM2 = 2860.0
    $large.HallHeightM = 4.5
    $large.PlatformAreaM2 = 1980.0
    $repo.SaveLargeSystem($large)
    $vm = New-Object "$vmNs.LargeSmokeViewModel" -ArgumentList $repo, $OutDir
    $vm.Areas.HallAreaM2 = 2860.0
    $vm.Areas.PlatformAreaM2 = 1980.0
    $vm.CalculateCommand.Execute($null)
    $win = New-Object "$uiNs.LargeSmokeWindow" -ArgumentList $vm
    Save-Shot $win 'm-smoke' 1520 620
} catch { Write-Host ("FAIL smoke: {0}" -f $_.Exception.Message) }

# ---------------------------------------------------------------- 6. 大系统计算结果
try {
    $repo = New-TempRepo
    $large = $repo.LoadLargeSystem()
    $large.HallAreaM2 = 2860.0
    $large.HallHeightM = 4.5
    $large.HallLengthM = 168.0
    $large.PlatformAreaM2 = 1980.0
    $large.OutdoorWetBulbC = 28.1
    $large.UpLineBoardCount = 1180
    $large.UpLineAlightCount = 1040
    $large.DownLineBoardCount = 1120
    $large.DownLineAlightCount = 1010
    $repo.SaveLargeSystem($large)
    $vm = New-Object "$vmNs.LargeResultViewModel" -ArgumentList $repo
    $win = New-Object "$uiNs.LargeSystemResultWindow" -ArgumentList $vm
    Save-Shot $win 'm-large-result' 1360 760
} catch { Write-Host ("FAIL large-result: {0}" -f $_.Exception.Message) }

# ---------------------------------------------------------------- 7. 小系统(全空气)
try {
    $repo = New-TempRepo
    $vm = New-Object "$vmNs.SmallSystemViewModel" -ArgumentList `
        ([HVACIDA.Core.Models.SmallSystemType]::AllAirOnceReturn), $repo, $false, $OutDir
    $vm.Input.OutdoorDryBulbC = 34.8
    $vm.Input.OutdoorWetBulbC = 28.1
    $vm.Input.LightingIndexWm2 = 8
    $vm.Input.SelectionFactor = 1.1
    $rooms = @(
        @('站厅层弱电间', 18.20, 4.50, 2000, 1, 6),
        @('0.4kV 开关柜室', 133.73, 4.55, 40400, 0, 14),
        @('35kV 开关柜室', 134.97, 4.55, 27800, 0, 10),
        @('整流变压器室 1', 37.58, 4.55, 25000, 0, 30),
        @('环控机房', 62.40, 4.55, 6000, 2, 6),
        @('车站备品库', 11.12, 4.55, 1000, 0, 7),
        @('公安通信设备室', 20.30, 4.55, 7000, 0, 19),
        @('照明配电室', 21.17, 4.55, 2400, 0, 9)
    )
    foreach ($r in $rooms) {
        $room = [HVACIDA.Core.Models.SmallRoomInput]::Create($r[0], $r[1], $r[2])
        $room.EquipmentCoolingW = $r[3]
        $room.Occupants = $r[4]
        $room.AirChangePerHour = $r[5]
        $vm.Systems[0].Rooms.Add($room) | Out-Null
    }
    $vm.CalculateCommand.Execute($null)
    $win = New-Object "$uiNs.SmallSystemWindow" -ArgumentList $vm
    Save-Shot $win 'm-small-allair' 1720 810
} catch { Write-Host ("FAIL small-allair: {0}" -f $_.Exception.Message) }

# ---------------------------------------------------------------- 8. 小系统(多联机+新风)
try {
    $repo = New-TempRepo
    $vm = New-Object "$vmNs.SmallSystemViewModel" -ArgumentList `
        ([HVACIDA.Core.Models.SmallSystemType]::VrfWithFreshAir), $repo, $false, $OutDir
    $vm.Input.OutdoorDryBulbC = 34.8
    $vm.Input.OutdoorWetBulbC = 28.1
    $vm.Input.LightingIndexWm2 = 20
    $vm.Input.SelectionFactor = 1.1
    $rooms = @(
        @('驻站男更衣室', 8.49, 3.50, 1000, 3, 6, 1),
        @('驻站女更衣室', 9.26, 3.50, 1000, 3, 6, 1),
        @('维修中心更衣室', 10.12, 3.50, 1000, 3, 6, 1),
        @('女更衣室', 20.01, 3.50, 1000, 3, 6, 2),
        @('会议交接班室', 28.12, 3.50, 1000, 20, 6, 4),
        @('区域站长室', 14.95, 3.50, 1000, 2, 6, 1),
        @('AFC 票务室', 25.36, 3.50, 1000, 3, 6, 2),
        @('警务室', 17.79, 3.50, 1000, 4, 6, 2)
    )
    foreach ($r in $rooms) {
        $room = [HVACIDA.Core.Models.SmallRoomInput]::Create($r[0], $r[1], $r[2])
        $room.EquipmentCoolingW = $r[3]
        $room.Occupants = $r[4]
        $room.AirChangePerHour = $r[5]
        $room.IndoorUnitCount = $r[6]
        $vm.Systems[0].Rooms.Add($room) | Out-Null
    }
    $vm.CalculateCommand.Execute($null)
    $win = New-Object "$uiNs.SmallSystemWindow" -ArgumentList $vm
    Save-Shot $win 'm-small-vrf' 1720 810
    $win2 = New-Object "$uiNs.SmallSystemWindow" -ArgumentList $vm
    Save-Shot $win2 'm-small-vrf-right' 1720 400 -RightEnd
} catch { Write-Host ("FAIL small-vrf: {0}" -f $_.Exception.Message) }

# ---------------------------------------------------------------- 9. 小系统计算结果
try {
    $repo = New-TempRepo
    foreach ($pair in @(
            @('AllAirOnceReturn', 'AHU-A101', 8),
            @('VrfWithFreshAir', 'VRV-A216', 6),
            @('ExhaustVentilation', 'EAF-A601', 5))) {
        $type = [HVACIDA.Core.Models.SmallSystemType]::$($pair[0])
        $vm = New-Object "$vmNs.SmallSystemViewModel" -ArgumentList $type, $repo, $false, $OutDir
        for ($i = 1; $i -le $pair[2]; $i++) {
            $room = [HVACIDA.Core.Models.SmallRoomInput]::Create(("房间" + $i), (10 + $i * 2.5), 4.5)
            $room.EquipmentCoolingW = 1500 + $i * 400
            $room.Occupants = ($i % 3) + 1
            $room.AirChangePerHour = 6
            $vm.Systems[0].Rooms.Add($room) | Out-Null
        }
        $vm.Systems[0].Code = $pair[1]
        $vm.CalculateCommand.Execute($null)
    }
    $vm2 = New-Object "$vmNs.SmallResultViewModel" -ArgumentList $repo
    $win = New-Object "$uiNs.SmallSystemResultWindow" -ArgumentList $vm2
    Save-Shot $win 'm-small-result' 1500 1000
} catch { Write-Host ("FAIL small-result: {0}" -f $_.Exception.Message) }

# ---------------------------------------------------------------- 10. 水力计算(风系统)
$script:hydRepo = $null
try {
    $repo = New-TempRepo
    $script:hydRepo = $repo
    $input = New-Object HVACIDA.Core.Models.HydraulicInput
    $input.Kind = [HVACIDA.Core.Models.HydraulicKind]::AirDuct
    $input.SystemName = '机械送风 1'
    $input.SystemCode = 'SAF-1'
    $input.SystemTypeName = '送风'
    $input.MediumTempC = 20
    $segs = @(
        @('送风主管', 1.0, 0.5, 28, 9800, 1.2, $true),
        @('送风干管', 0.8, 0.4, 22, 7200, 0.8, $true),
        @('站厅支管 1', 0.63, 0.32, 18, 4200, 3.5, $true),
        @('站厅支管 2', 0.5, 0.25, 15, 2600, 2.8, $false),
        @('站台支管 1', 0.5, 0.25, 20, 3000, 2.4, $false)
    )
    foreach ($s in $segs) {
        $seg = New-Object HVACIDA.Core.Models.HydraulicSegment
        $seg.Name = $s[0]
        $seg.Shape = [HVACIDA.Core.Models.HydraulicShape]::Rectangular
        $seg.WidthM = [double]$s[1]
        $seg.HeightM = [double]$s[2]
        $seg.LengthM = [double]$s[3]
        $seg.FlowM3H = [double]$s[4]
        $seg.LocalZetaSum = [double]$s[5]
        $seg.OnCriticalPath = [bool]$s[6]
        $seg.RoughnessMm = 0.15
        $input.Segments.Add($seg)
    }
    $term = New-Object HVACIDA.Core.Models.HydraulicTerminal
    $term.Name = '站厅散流器'
    $term.Kind = [HVACIDA.Core.Models.HydraulicItemKind]::Terminal
    $term.ResistancePa = 50
    $term.OnCriticalPath = $true
    $input.Terminals.Add($term)
    $term2 = New-Object HVACIDA.Core.Models.HydraulicTerminal
    $term2.Name = '站台散流器'
    $term2.Kind = [HVACIDA.Core.Models.HydraulicItemKind]::Terminal
    $term2.ResistancePa = 45
    $input.Terminals.Add($term2)

    $svc = New-Object HVACIDA.Core.Services.HydraulicInputService -ArgumentList $repo
    $svc.Save($input) | Out-Null

    $vm = New-Object "$vmNs.HydraulicSystemViewModel" -ArgumentList `
        ([HVACIDA.Core.Models.HydraulicKind]::AirDuct), $repo, $false, $OutDir
    $vm.ApplyPickedSystem($input, '模型读取:风系统 1 套(送风主干 + 支管)')
    $win = New-Object "$uiNs.HydraulicSystemWindow" -ArgumentList $vm
    Save-Shot $win 'm-hydraulic' 1500 900
} catch { Write-Host ("FAIL hydraulic: {0}" -f $_.Exception.Message) }

# ---------------------------------------------------------------- 11. 水力计算结果
try {
    $repo = if ($script:hydRepo -ne $null) { $script:hydRepo } else { New-TempRepo }
    $vm = New-Object "$vmNs.HydraulicResultViewModel" -ArgumentList $repo, $OutDir
    $win = New-Object "$uiNs.HydraulicResultWindow" -ArgumentList $vm
    Save-Shot $win 'm-hydraulic-result' 1500 900
} catch { Write-Host ("FAIL hydraulic-result: {0}" -f $_.Exception.Message) }

# ---------------------------------------------------------------- 12. 材料表
try {
    $mtVm = New-Object "$vmNs.MaterialTakeoffViewModel" -ArgumentList $OutDir
    $items = New-Object 'System.Collections.Generic.List[HVACIDA.Core.Models.MaterialItem]'
    $rows = @(
        @('Duct', '风管', '矩形风管', '1250×400', 'm', 186.4, 3, '按定位线长度'),
        @('Duct', '风管', '矩形风管', '1000×320', 'm', 142.8, 2, '按定位线长度'),
        @('Duct', '风管', '矩形风管', '800×250', 'm', 96.2, 2, '按定位线长度'),
        @('DuctFitting', '风管管件', '弯头', '90° 1250×400', '个', 6, 6, '按件数计'),
        @('DuctFitting', '风管管件', '三通', '1250×400', '个', 3, 3, '按件数计'),
        @('Pipe', '水管', '镀锌钢管', 'DN100', 'm', 128.5, 2, '按管段长度'),
        @('Pipe', '水管', '镀锌钢管', 'DN80', 'm', 86.3, 2, '按管段长度'),
        @('PipeFitting', '水管管件', '沟槽弯头', 'DN100', '个', 12, 12, '按件数计'),
        @('MechanicalEquipment', '设备', '空调机组', 'AHU-A101', '台', 2, 2, '按台数计'),
        @('MechanicalEquipment', '设备', '多联机室外机', 'VRV-8HP', '台', 3, 3, '按台数计'),
        @('Insulation', '保温', '橡塑保温', 'δ=30', 'm²', 218.6, 1, '按表面积计')
    )
    foreach ($r in $rows) {
        $mi = New-Object HVACIDA.Core.Models.MaterialItem
        $mi.Category = [HVACIDA.Core.Models.MaterialCategory]::$($r[0])
        $mi.CategoryName = $r[1]
        $mi.FamilyName = $r[2]
        $mi.TypeName = $r[3]
        $mi.Unit = $r[4]
        $mi.Quantity = [double]$r[5]
        $mi.Count = [int]$r[6]
        $mi.Note = $r[7]
        $items.Add($mi)
    }
    $mtVm.ApplyTakeoff($items, '模型读取:11 类构件')
    $win = New-Object "$uiNs.MaterialTakeoffWindow" -ArgumentList $mtVm
    Save-Shot $win 'm-material' 1420 840
} catch { Write-Host ("FAIL material: {0}" -f $_.Exception.Message) }

# ---------------------------------------------------------------- 13. 图框
try {
    $shVm = New-Object "$vmNs.SheetCatalogViewModel" -ArgumentList $OutDir
    $sheets = New-Object 'System.Collections.Generic.List[HVACIDA.Core.Models.SheetItem]'
    $defs = @(
        @('A-01', '设计说明', 'A1', 841, 594, '设计说明'),
        @('A-02', '图例与材料表', 'A1', 841, 594, '图例表'),
        @('A-03', '站厅层空调平面图', 'A1', 841, 594, '风管平面'),
        @('A-04', '站台层空调平面图', 'A1', 841, 594, '风管平面'),
        @('A-05', '机房大样图', 'A2', 594, 420, '机房详图'),
        @('A-06', '预留图框', 'A2', 594, 420, '')
    )
    foreach ($d in $defs) {
        $s = New-Object HVACIDA.Core.Models.SheetItem
        $s.SheetNumber = $d[0]; $s.SheetName = $d[1]
        $s.TitleBlockFamily = '标准图框'; $s.TitleBlockType = $d[2]
        $s.WidthMm = $d[3]; $s.HeightMm = $d[4]
        if ($d[5]) {
            $v = New-Object HVACIDA.Core.Models.SheetViewItem
            $v.ViewName = $d[5]; $v.ViewType = 'DraftingView'; $v.Scale = 100
            $s.Views.Add($v)
        }
        $sheets.Add($s)
    }
    $shVm.ApplyCatalog($sheets, '模型读取:6 张图纸')
    $win = New-Object "$uiNs.SheetCatalogWindow" -ArgumentList $shVm
    Save-Shot $win 'm-sheets' 1420 840
} catch { Write-Host ("FAIL sheets: {0}" -f $_.Exception.Message) }

# ---------------------------------------------------------------- 14. 规范知识库
try {
    $kbDir = Join-Path $env:TEMP ("HVACIDA-ManualKB-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $kbDir | Out-Null
    $kvm = New-Object "$vmNs.KnowledgeViewModel" -ArgumentList $kbDir
    $kvm.Question = '卫生间排风换气次数'
    $kvm.AskCommand.Execute($null)
    foreach ($entry in $kvm.Entries) {
        if ($entry.Answer -notmatch '\*\*') { $kvm.SelectedEntry = $entry; break }
    }
    $win = New-Object "$uiNs.KnowledgeWindow" -ArgumentList $kvm
    Save-Shot $win 'm-knowledge' 1520 960
} catch { Write-Host ("FAIL knowledge: {0}" -f $_.Exception.Message) }

# ---------------------------------------------------------------- 15. AI 对话
try {
    $cfgDir = Join-Path $env:TEMP ("HVACIDA-ManualAI-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $cfgDir | Out-Null
    $vm = New-Object "$vmNs.AiAssistantViewModel" -ArgumentList (Join-Path $cfgDir 'ai.xml')
    $vm.Enabled = $true
    $vm.ApiKeyText = 'sk-****************'
    $vm.Lines.Add((New-Object "$vmNs.AiChatLine" -ArgumentList $true, '太湖香山站站厅公共区的计算冷负荷是多少?'))
    $vm.Lines.Add((New-Object "$vmNs.AiChatLine" -ArgumentList $false, '站厅公共区计算总冷负荷 429.38 kW、总送风量 87,768 m³/h,取自当前模型「大系统 → 计算结果」。'))
    $vm.Lines.Add((New-Object "$vmNs.AiChatLine" -ArgumentList $true, '送风温差和管道温升用的是多少?'))
    $vm.Lines.Add((New-Object "$vmNs.AiChatLine" -ArgumentList $false, '大系统当前取送风温差 8 ℃、管道温升 1.5 ℃,可在「大系统 → 负荷计算」里修改。'))
    foreach ($line in $vm.Lines) { $line.Streaming = $false }
    $vm.ToolLog.Add((New-Object "$vmNs.AiToolLogRow" -ArgumentList 'get_large_result', '✓ 完成', 18))
    $vm.ToolLog.Add((New-Object "$vmNs.AiToolLogRow" -ArgumentList 'get_input_params', '✓ 完成', 11))
    $win = New-Object "$uiNs.AiChatWindow" -ArgumentList $vm
    Save-Shot $win 'm-ai' 1200 820
} catch { Write-Host ("FAIL ai: {0}" -f $_.Exception.Message) }

# ---------------------------------------------------------------- 16. 操作指南
try {
    $vm = [HVACIDA.UI.ViewModels.InfoViewModel]::Guide()
    $win = New-Object "$uiNs.InfoWindow" -ArgumentList $vm
    Save-Shot $win 'm-guide' 1100 430
} catch { Write-Host ("FAIL guide: {0}" -f $_.Exception.Message) }

Write-Host '--- files ---'
Get-ChildItem -LiteralPath $OutDir -Filter 'm-*.png' | ForEach-Object { "  {0}  {1:N0} KB" -f $_.Name, ($_.Length / 1KB) }
