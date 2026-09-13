# HVACIDA 文档同步门禁:校验「交付 docx」与「Markdown 正文副本」是否一致。
#
# 原理:由 docx2md 工具生成的 Markdown 头部会记录源 docx 的 SHA256;
#       本脚本扫描仓库里的这类副本,重算源 docx 的 SHA256 并比对。
#       —— docx 一改,副本立刻被判为"过期",避免"文档看的是旧版"。
#
# 用法:
#   powershell.exe -ExecutionPolicy Bypass -File tools\docx2md\check-docx-sync.ps1
#   powershell.exe -ExecutionPolicy Bypass -File tools\docx2md\check-docx-sync.ps1 -Regenerate   # 过期则直接重生成
#
# 退出码:0 = 全部最新(或没有副本);1 = 存在过期/缺失(CI 与交付前必须为 0)

param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [switch]$Regenerate
)

$ErrorActionPreference = 'Stop'

$fail = 0
$checked = 0
$generated = 0

# 扫描 docs 下所有 Markdown,找出带 docx2md 头部的"正文副本"
$docsDir = Join-Path $RepoRoot 'docs'
if (-not (Test-Path $docsDir)) { Write-Host "找不到 docs 目录: $docsDir"; exit 1 }

$mirrors = Get-ChildItem -Path $docsDir -Filter '*.md' -Recurse -File | Where-Object {
    (Get-Content $_.FullName -TotalCount 12 -Encoding UTF8) -match '\*\*源文件指纹\*\*:SHA256'
}

if (-not $mirrors) {
    Write-Host '没有找到 docx 正文副本(不需要校验)'
    exit 0
}

function Find-SourceDocx([string]$name, [string]$mdDir) {
    $candidates = @(
        (Join-Path $RepoRoot $name),
        (Join-Path $mdDir $name),
        (Join-Path (Split-Path $RepoRoot -Parent) $name)
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return (Resolve-Path $c).Path } }
    return $null
}

foreach ($md in $mirrors) {
    $head = (Get-Content $md.FullName -TotalCount 12 -Encoding UTF8) -join "`n"

    $srcName = [regex]::Match($head, '> \*\*来源\*\*:`([^`]+)`').Groups[1].Value
    $recorded = [regex]::Match($head, 'SHA256\s+`?([0-9a-fA-F]{64})`?').Groups[1].Value
    $regen = [regex]::Match($head, '> \*\*重新生成\*\*:`([^`]+)`').Groups[1].Value

    $mdRel = $md.FullName.Substring($RepoRoot.Length).TrimStart('\', '/')
    if (-not $srcName -or -not $recorded) {
        Write-Host ("FAIL  {0} —— 头部缺少「来源/指纹」信息,无法校验" -f $mdRel)
        $script:fail++
        continue
    }

    $srcPath = Find-SourceDocx $srcName $md.DirectoryName
    if (-not $srcPath) {
        Write-Host ("FAIL  {0} —— 找不到源文件 {1}" -f $mdRel, $srcName)
        $script:fail++
        continue
    }

    $script:checked++
    $actual = (Get-FileHash $srcPath -Algorithm SHA256).Hash.ToLower()

    if ($actual -eq $recorded.ToLower()) {
        Write-Host ("PASS  {0}  ←  {1}(SHA256 一致)" -f $mdRel, $srcName)
        continue
    }

    Write-Host ("STALE {0}  ←  {1} 已修改" -f $mdRel, $srcName)
    Write-Host ("        记录: {0}" -f $recorded.ToLower())
    Write-Host ("        实际: {0}" -f $actual)

    if (-not $Regenerate) {
        Write-Host  "        修复: 重跑 tools/docx2md/docx_to_markdown.py,或加 -Regenerate 参数自动重生成"
        $script:fail++
        continue
    }

    # 依据副本头部记录的"重新生成"命令自动重生成(保证与当初生成方式一致)
    $m = [regex]::Match($regen, 'python\s+(\S+)\s+"([^"]+)"\s+"([^"]+)"(.*)$')
    if (-not $m.Success) {
        Write-Host  "        无法从头部解析重生成命令,请手工执行;头部命令: $regen"
        $script:fail++
        continue
    }

    $scriptPath = $m.Groups[1].Value
    $srcArg = $m.Groups[2].Value
    $dstArg = $m.Groups[3].Value
    $extra = ($m.Groups[4].Value -split '\s+' | Where-Object { $_ })

    $cmd = @($scriptPath, $srcArg, $dstArg) + $extra
    Write-Host  ("        重生成: python " + ($cmd -join ' '))
    & python @cmd | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host  "        重生成失败(退出码 $LASTEXITCODE)"
        $script:fail++
        continue
    }

    $newHash = (Get-FileHash $srcPath -Algorithm SHA256).Hash.ToLower()
    $newRecorded = [regex]::Match((Get-Content $md.FullName -TotalCount 12 -Encoding UTF8) -join "`n",
                                  'SHA256\s+`?([0-9a-fA-F]{64})`?').Groups[1].Value.ToLower()
    if ($newRecorded -eq $newHash) {
        Write-Host  "        已重生成并校验通过"
        $script:generated++
    } else {
        Write-Host  "        重生成后指纹仍不一致,请检查"
        $script:fail++
    }
}

Write-Host '=================================================='
if ($fail -eq 0) {
    Write-Host ("文档同步校验通过:副本 {0} 个{1}" -f $checked, $(if ($generated) { ",其中自动重生成 $generated 个" } else { '' }))
    exit 0
}
Write-Host ("文档同步校验失败 {0} 项(副本 {1} 个)" -f $fail, $checked)
exit 1
