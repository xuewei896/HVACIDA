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
if ($fail -eq 0) { Write-Host '窗口装载自检全部通过'; exit 0 }
Write-Host ("窗口装载自检失败 " + $fail + " 项"); exit 1
