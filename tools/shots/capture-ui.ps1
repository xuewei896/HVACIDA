# =============================================================================
#  capture-ui.ps1 -- render the real WPF windows to PNG (no Revit required)
#  Every shot is produced by instantiating the actual window + view model from
#  HVACIDA.UI.dll and rendering the visual tree with RenderTargetBitmap.
# =============================================================================
param(
    [string]$UiDir = 'D:\DSH\src\HVACIDA.UI\bin\Release\net48',
    [string]$CoreDir = 'D:\DSH\src\HVACIDA.Core\bin\Release\net48',
    [string]$OutDir = 'D:\DSH\docs\report-images',
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
    $dir = Join-Path $env:TEMP ("HVACIDA-Shot-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir | Out-Null
    return New-Object HVACIDA.Core.Services.XmlProjectRepository -ArgumentList $dir
}

function Save-Shot($window, [string]$name, [int]$w = 0, [int]$h = 0) {
    try {
        if ($w -gt 0) { $window.Width = $w }
        if ($h -gt 0) { $window.Height = $h }
        $window.WindowStartupLocation = 'Manual'
        $window.Left = 0
        $window.Top = 0
        $window.Show()
        $window.UpdateLayout()
        Start-Sleep -Milliseconds 300
        $window.UpdateLayout()
        $aw = [int][Math]::Ceiling($window.ActualWidth)
        $ah = [int][Math]::Ceiling($window.ActualHeight)
        if ($aw -le 0 -or $ah -le 0) { throw "window has no size ($aw x $ah)" }
        $bmp = New-Object System.Windows.Media.Imaging.RenderTargetBitmap (
            $aw * $Scale), ($ah * $Scale), (96 * $Scale), (96 * $Scale),
            ([System.Windows.Media.PixelFormats]::Pbgra32)
        $bmp.Render($window)
        $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
        $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
        $path = Join-Path $OutDir ($name + '.png')
        $fs = [System.IO.File]::Create($path)
        $enc.Save($fs)
        $fs.Close()
        Write-Host ("SHOT  {0,-22} {1} x {2}" -f $name, ($aw * $Scale), ($ah * $Scale))
    }
    finally {
        try { $window.Close() } catch { }
    }
}

function Seed-LargeSystem($repo) {
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
    return $large
}

# ---------------------------------------------------------------- 1. 工程信息
try {
    $repo = New-TempRepo
    $vm = New-Object "$vmNs.ProjectInfoViewModel" -ArgumentList $repo
    $vm.Model.Basic.ProjectName = '无锡地铁 5 号线一期工程'
    $vm.Model.Basic.LocationProvince = '江苏省'
    $vm.Model.Basic.LocationCity = '无锡市'
    $vm.Model.Basic.StationName = '宝龙广场站'
    $vm.Model.Basic.DesignStage = '施工图设计'
    $vm.Model.Basic.Remark = '机电系统 BIM 正向设计试点站'
    $win = New-Object "$uiNs.ProjectInfoWindow" -ArgumentList $vm
    Save-Shot $win 'ui-project' 1180 700
} catch { Write-Host ("FAIL project: {0}" -f $_.Exception.Message) }

# ---------------------------------------------------------------- 2. 大系统负荷
try {
    $repo = New-TempRepo
    Seed-LargeSystem $repo | Out-Null
    $vm = New-Object "$vmNs.LargeSystemViewModel" -ArgumentList $repo
    $win = New-Object "$uiNs.LargeSystemWindow" -ArgumentList $vm
    Save-Shot $win 'ui-large-load' 1560 900
} catch { Write-Host ("FAIL large-load: {0}" -f $_.Exception.Message) }

# ---------------------------------------------------------------- 3. 大系统计算结果
try {
    $repo = New-TempRepo
    Seed-LargeSystem $repo | Out-Null
    $vm = New-Object "$vmNs.LargeResultViewModel" -ArgumentList $repo
    $win = New-Object "$uiNs.LargeSystemResultWindow" -ArgumentList $vm
    Save-Shot $win 'ui-large-result' 1360 600
} catch { Write-Host ("FAIL large-result: {0}" -f $_.Exception.Message) }

# ---------------------------------------------------------------- 4. 小系统(全空气)
try {
    $repo = New-TempRepo
    $vm = New-Object "$vmNs.SmallSystemViewModel" -ArgumentList `
        ([HVACIDA.Core.Models.SmallSystemType]::AllAirOnceReturn), $repo, $false, $OutDir
    $vm.Input.OutdoorDryBulbC = 34.8
    $vm.Input.OutdoorWetBulbC = 28.1
    $vm.Input.LightingIndexWm2 = 8
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
    Save-Shot $win 'ui-small-allair' 1720 810
} catch { Write-Host ("FAIL small-allair: {0}" -f $_.Exception.Message) }

# ---------------------------------------------------------------- 5. 小系统(多联机+新风)
try {
    $repo = New-TempRepo
    $vm = New-Object "$vmNs.SmallSystemViewModel" -ArgumentList `
        ([HVACIDA.Core.Models.SmallSystemType]::VrfWithFreshAir), $repo, $false, $OutDir
    $vm.Input.OutdoorDryBulbC = 34.8
    $vm.Input.OutdoorWetBulbC = 28.1
    $vm.Input.LightingIndexWm2 = 20
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
    Save-Shot $win 'ui-small-vrf' 1720 810
} catch { Write-Host ("FAIL small-vrf: {0}" -f $_.Exception.Message) }

# ---------------------------------------------------------------- 6. 小系统计算结果
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
    Save-Shot $win 'ui-small-result' 1500 1000
} catch { Write-Host ("FAIL small-result: {0}" -f $_.Exception.Message) }

# ---------------------------------------------------------------- 7. 材料表
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
    Save-Shot $win 'ui-material' 1420 840
} catch { Write-Host ("FAIL material: {0}" -f $_.Exception.Message) }

# ---------------------------------------------------------------- 8. 图框
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
    Save-Shot $win 'ui-sheets' 1420 840
} catch { Write-Host ("FAIL sheets: {0}" -f $_.Exception.Message) }

# ---------------------------------------------------------------- 9. 规范知识库
try {
    $kbDir = Join-Path $env:TEMP ("HVACIDA-ShotKB-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $kbDir | Out-Null
    $kvm = New-Object "$vmNs.KnowledgeViewModel" -ArgumentList $kbDir
    $kvm.Question = '卫生间排风换气次数'
    $kvm.AskCommand.Execute($null)
    foreach ($entry in $kvm.Entries) {
        if ($entry.Answer -notmatch '\*\*') { $kvm.SelectedEntry = $entry; break }
    }
    $win = New-Object "$uiNs.KnowledgeWindow" -ArgumentList $kvm
    Save-Shot $win 'ui-knowledge' 1520 960
} catch { Write-Host ("FAIL knowledge: {0}" -f $_.Exception.Message) }

Write-Host '--- files ---'
Get-ChildItem -LiteralPath $OutDir -Filter '*.png' | ForEach-Object { "  {0}  {1:N0} KB" -f $_.Name, ($_.Length / 1KB) }
