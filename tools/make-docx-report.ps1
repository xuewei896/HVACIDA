# =============================================================================
#  make-docx-report.ps1  --  turn a small Markdown subset into a Word (.docx)
#  ASCII-only script on purpose: every Chinese string lives in the Markdown
#  source (read as UTF-8), so this file can stay BOM-less without mojibake.
#
#  Supported syntax
#    [[TITLE]] ...        cover title            (before body)
#    [[SUBTITLE]] ...     cover subtitle
#    [[COVERINFO]] k|v    one cover info line
#    [[HEADER]] ...       running header text
#    [[FOOTER]] ...       footer template, {PAGE} / {NUMPAGES} become fields
#    [[TOCTITLE]] ...     heading above the TOC
#    [[TOC]]              table-of-contents field
#    [[PAGEBREAK]]        explicit page break
#    [[COLS]] 20,30,50    column widths (percent) for the NEXT table
#    [[CAPTION]] ...      caption line (table/figure title)
#    # ## ###             headings 1..3
#    - item               bullet
#    1. item              numbered item
#    > note               shaded note paragraph
#    | a | b |            table (2nd line may be |---|)
#    **bold**  `code`     inline formatting
# =============================================================================
param(
    [Parameter(Mandatory = $true)][string[]]$Source,
    [Parameter(Mandatory = $true)][string]$Out
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$ns = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'
$nsR = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'

# ---------------------------------------------------------------- helpers ----
function Esc([string]$s) {
    if ($null -eq $s) { return '' }
    return $s.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;').Replace('"', '&quot;')
}

# inline: **bold** and `code`
function Runs([string]$text, [string]$baseRpr = '') {
    $sb = New-Object System.Text.StringBuilder
    $parts = [regex]::Split($text, '(\*\*[^*]+\*\*|`[^`]+`)')
    foreach ($p in $parts) {
        if ([string]::IsNullOrEmpty($p)) { continue }
        if ($p.Length -gt 4 -and $p.StartsWith('**') -and $p.EndsWith('**')) {
            $t = Esc $p.Substring(2, $p.Length - 4)
            [void]$sb.Append("<w:r><w:rPr>$baseRpr<w:b/><w:bCs/></w:rPr><w:t xml:space=`"preserve`">$t</w:t></w:r>")
        }
        elseif ($p.Length -gt 2 -and $p.StartsWith('`') -and $p.EndsWith('`')) {
            $t = Esc $p.Substring(1, $p.Length - 2)
            [void]$sb.Append("<w:r><w:rPr>$baseRpr<w:rFonts w:ascii=`"Consolas`" w:hAnsi=`"Consolas`"/><w:sz w:val=`"20`"/></w:rPr><w:t xml:space=`"preserve`">$t</w:t></w:r>")
        }
        else {
            $t = Esc $p
            [void]$sb.Append("<w:r><w:rPr>$baseRpr</w:rPr><w:t xml:space=`"preserve`">$t</w:t></w:r>")
        }
    }
    return $sb.ToString()
}

function P([string]$style, [string]$text, [string]$pExtra = '') {
    return "<w:p><w:pPr><w:pStyle w:val=`"$style`"/>$pExtra</w:pPr>$(Runs $text)</w:p>"
}

function PageBreak() {
    return '<w:p><w:pPr><w:spacing w:before="0" w:after="0"/></w:pPr><w:r><w:br w:type="page"/></w:r></w:p>'
}

function TableXml([object[]]$rows, [int[]]$pct) {
    $cols = $rows[0].Count
    if ($null -eq $pct -or $pct.Count -ne $cols) {
        $pct = @(); for ($k = 0; $k -lt $cols; $k++) { $pct += [int][Math]::Floor(100 / $cols) }
    }
    $sum = ($pct | Measure-Object -Sum).Sum
    if ($sum -le 0) { $sum = 100 }
    $total = 9070
    $widths = @(); foreach ($p in $pct) { $widths += [int][Math]::Round($total * $p / $sum) }

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('<w:tbl><w:tblPr><w:tblW w:w="9070" w:type="dxa"/><w:jc w:val="center"/>')
    [void]$sb.Append('<w:tblBorders>')
    foreach ($b in @('top', 'left', 'bottom', 'right', 'insideH', 'insideV')) {
        [void]$sb.Append("<w:$b w:val=`"single`" w:sz=`"4`" w:space=`"0`" w:color=`"808080`"/>")
    }
    [void]$sb.Append('</w:tblBorders><w:tblLayout w:type="fixed"/>')
    [void]$sb.Append('<w:tblCellMar><w:top w:w="30" w:type="dxa"/><w:left w:w="70" w:type="dxa"/><w:bottom w:w="30" w:type="dxa"/><w:right w:w="70" w:type="dxa"/></w:tblCellMar>')
    [void]$sb.Append('</w:tblPr><w:tblGrid>')
    foreach ($w in $widths) { [void]$sb.Append("<w:gridCol w:w=`"$w`"/>") }
    [void]$sb.Append('</w:tblGrid>')

    for ($r = 0; $r -lt $rows.Count; $r++) {
        $row = $rows[$r]
        $isHead = ($r -eq 0)
        [void]$sb.Append('<w:tr>')
        if ($isHead) { [void]$sb.Append('<w:trPr><w:tblHeader/></w:trPr>') }
        for ($c = 0; $c -lt $cols; $c++) {
            $cellText = if ($c -lt $row.Count) { [string]$row[$c] } else { '' }
            $shd = if ($isHead) { '<w:shd w:val="clear" w:color="auto" w:fill="EDF1F7"/>' } else { '' }
            $jc = if ($isHead -or $cellText.Length -le 12) { 'center' } else { 'left' }
            $rpr = if ($isHead) { '<w:b/><w:bCs/>' } else { '' }
            [void]$sb.Append("<w:tc><w:tcPr><w:tcW w:w=`"$($widths[$c])`" w:type=`"dxa`"/>$shd<w:vAlign w:val=`"center`"/></w:tcPr>")
            [void]$sb.Append("<w:p><w:pPr><w:pStyle w:val=`"TableText`"/><w:jc w:val=`"$jc`"/></w:pPr>$(Runs $cellText $rpr)</w:p></w:tc>")
        }
        [void]$sb.Append('</w:tr>')
    }
    [void]$sb.Append('</w:tbl>')
    [void]$sb.Append('<w:p><w:pPr><w:pStyle w:val="TableGap"/></w:pPr></w:p>')
    return $sb.ToString()
}

# ---------------------------------------------------------------- read  md ----
$all = @()
foreach ($s in $Source) { $all += [System.IO.File]::ReadAllLines($s, [System.Text.Encoding]::UTF8) }

$cover = [ordered]@{ Title = ''; Subtitle = ''; Info = @(); Header = ''; Footer = ''; TocTitle = 'Contents'; TocNote = '(open in Word, press Ctrl+A then F9 to build the table of contents)' }
foreach ($l in $all) {
    if ($l -match '^\[\[TITLE\]\](.*)$') { $cover.Title = $Matches[1].Trim() }
    elseif ($l -match '^\[\[SUBTITLE\]\](.*)$') { $cover.Subtitle = $Matches[1].Trim() }
    elseif ($l -match '^\[\[COVERINFO\]\](.*)$') { $cover.Info += $Matches[1].Trim() }
    elseif ($l -match '^\[\[HEADER\]\](.*)$') { $cover.Header = $Matches[1].Trim() }
    elseif ($l -match '^\[\[FOOTER\]\](.*)$') { $cover.Footer = $Matches[1].Trim() }
    elseif ($l -match '^\[\[TOCNOTE\]\](.*)$') { $cover.TocNote = $Matches[1].Trim() }
    elseif ($l -match '^\[\[TOCTITLE\]\](.*)$') { $cover.TocTitle = $Matches[1].Trim() }
}

# ---------------------------------------------------------------- cover ----
$coverXml = New-Object System.Text.StringBuilder
[void]$coverXml.Append('<w:p><w:pPr><w:pStyle w:val="CoverTitle"/></w:pPr>' + (Runs $cover.Title) + '</w:p>')
if ($cover.Subtitle) {
    [void]$coverXml.Append('<w:p><w:pPr><w:pStyle w:val="CoverSubtitle"/></w:pPr>' + (Runs $cover.Subtitle) + '</w:p>')
}
[void]$coverXml.Append('<w:p><w:pPr><w:pStyle w:val="CoverRule"/></w:pPr><w:r><w:t xml:space="preserve"></w:t></w:r></w:p>')
foreach ($info in $cover.Info) {
    $k = $info; $v = ''
    $idx = $info.IndexOf('|')
    if ($idx -ge 0) { $k = $info.Substring(0, $idx).Trim(); $v = $info.Substring($idx + 1).Trim() }
    [void]$coverXml.Append('<w:p><w:pPr><w:pStyle w:val="CoverInfo"/></w:pPr><w:r><w:t xml:space="preserve">' + (Esc $k) + ':</w:t></w:r><w:r><w:t xml:space="preserve">' + (Esc $v) + '</w:t></w:r></w:p>')
}

# ---------------------------------------------------------------- body ----
$body = New-Object System.Text.StringBuilder
$i = 0
$pendingCols = $null
$charCount = 0
$tableRows = 0
$tableCount = 0
$headingCount = 0

while ($i -lt $all.Count) {
    $line = $all[$i].TrimEnd()
    $trim = $line.Trim()

    if ($trim -eq '') { $i++; continue }

    if ($trim -match '^\[\[(TITLE|SUBTITLE|COVERINFO|HEADER|FOOTER|TOCTITLE|TOCNOTE)\]\]') { $i++; continue }

    if ($trim -eq '[[PAGEBREAK]]') { [void]$body.Append((PageBreak)); $i++; continue }

    if ($trim -eq '[[TOC]]') {
        [void]$body.Append('<w:p><w:pPr><w:pStyle w:val="Heading1"/><w:pageBreakBefore w:val="0"/></w:pPr>' + (Runs $cover.TocTitle) + '</w:p>')
        [void]$body.Append('<w:p><w:pPr><w:pStyle w:val="Normal"/><w:ind w:firstLine="0" w:firstLineChars="0"/></w:pPr>')
        [void]$body.Append('<w:r><w:fldChar w:fldCharType="begin" w:dirty="true"/></w:r>')
        [void]$body.Append('<w:r><w:instrText xml:space="preserve"> TOC \o "1-3" \h \z \u </w:instrText></w:r>')
        [void]$body.Append('<w:r><w:fldChar w:fldCharType="separate"/></w:r>')
        [void]$body.Append('<w:r><w:rPr><w:color w:val="808080"/></w:rPr><w:t xml:space="preserve">' + (Esc $cover.TocNote) + '</w:t></w:r>')
        [void]$body.Append('<w:r><w:fldChar w:fldCharType="end"/></w:r></w:p>')
        $i++; continue
    }

    if ($trim -match '^\[\[COLS\]\](.*)$') {
        $pendingCols = @(); foreach ($t in $Matches[1].Split(',')) { if ($t.Trim() -ne '') { $pendingCols += [int]$t.Trim() } }
        $i++; continue
    }

    if ($trim -match '^\[\[CAPTION\]\](.*)$') {
        [void]$body.Append('<w:p><w:pPr><w:pStyle w:val="Caption"/></w:pPr>' + (Runs $Matches[1].Trim()) + '</w:p>')
        $i++; continue
    }

    if ($trim.StartsWith('|')) {
        $rows = @()
        while ($i -lt $all.Count -and $all[$i].Trim().StartsWith('|')) {
            $t = $all[$i].Trim().Trim('|')
            if ($t -notmatch '^[\s\|:\-]+$') {
                $cells = @(); foreach ($c in $t.Split('|')) { $cells += $c.Trim() }
                $rows += , $cells
                $charCount += ($t.Length)
            }
            $i++
        }
        if ($rows.Count -ge 1) {
            [void]$body.Append((TableXml $rows $pendingCols))
            $tableRows += $rows.Count
            $tableCount++
        }
        $pendingCols = $null
        continue
    }

    if ($trim.StartsWith('### ')) {
        [void]$body.Append((P 'Heading3' $trim.Substring(4))); $headingCount++; $charCount += $trim.Length; $i++; continue
    }
    if ($trim.StartsWith('## ')) {
        [void]$body.Append((P 'Heading2' $trim.Substring(3))); $headingCount++; $charCount += $trim.Length; $i++; continue
    }
    if ($trim.StartsWith('# ')) {
        [void]$body.Append((P 'Heading1' $trim.Substring(2))); $headingCount++; $charCount += $trim.Length; $i++; continue
    }
    if ($trim.StartsWith('- ')) {
        [void]$body.Append((P 'Bullet' $trim.Substring(2))); $charCount += $trim.Length; $i++; continue
    }
    if ($trim -match '^(\d+)\.\s+(.*)$') {
        [void]$body.Append((P 'Bullet' ($Matches[1] + '. ' + $Matches[2]))); $charCount += $trim.Length; $i++; continue
    }
    if ($trim.StartsWith('> ')) {
        [void]$body.Append((P 'Note' $trim.Substring(2))); $charCount += $trim.Length; $i++; continue
    }

    [void]$body.Append((P 'Normal' $trim))
    $charCount += $trim.Length
    $i++
}

# ---------------------------------------------------------------- parts ----
$styleXml = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
<w:docDefaults>
  <w:rPrDefault><w:rPr><w:rFonts w:ascii="Times New Roman" w:eastAsia="\u5b8b\u4f53" w:hAnsi="Times New Roman" w:cs="Times New Roman"/><w:sz w:val="24"/><w:szCs w:val="24"/><w:lang w:val="en-US" w:eastAsia="zh-CN"/></w:rPr></w:rPrDefault>
  <w:pPrDefault><w:pPr><w:spacing w:before="0" w:after="0" w:line="360" w:lineRule="auto"/><w:jc w:val="both"/></w:pPr></w:pPrDefault>
</w:docDefaults>
<w:style w:type="paragraph" w:default="1" w:styleId="Normal">
  <w:name w:val="Normal"/><w:qFormat/>
  <w:pPr><w:snapToGrid w:val="0"/><w:spacing w:before="0" w:after="0" w:line="360" w:lineRule="auto"/><w:ind w:firstLineChars="200" w:firstLine="480"/><w:jc w:val="both"/></w:pPr>
  <w:rPr><w:rFonts w:eastAsia="\u5b8b\u4f53"/><w:sz w:val="24"/><w:szCs w:val="24"/></w:rPr>
</w:style>
<w:style w:type="paragraph" w:styleId="Heading1">
  <w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/>
  <w:pPr><w:keepNext/><w:pageBreakBefore/><w:spacing w:before="240" w:after="240" w:line="360" w:lineRule="auto"/><w:ind w:firstLineChars="0" w:firstLine="0"/><w:jc w:val="center"/><w:outlineLvl w:val="0"/></w:pPr>
  <w:rPr><w:rFonts w:ascii="Times New Roman" w:eastAsia="\u9ed1\u4f53" w:hAnsi="Times New Roman"/><w:b/><w:bCs/><w:sz w:val="36"/><w:szCs w:val="36"/></w:rPr>
</w:style>
<w:style w:type="paragraph" w:styleId="Heading2">
  <w:name w:val="heading 2"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/>
  <w:pPr><w:keepNext/><w:spacing w:before="200" w:after="120" w:line="360" w:lineRule="auto"/><w:ind w:firstLineChars="0" w:firstLine="0"/><w:jc w:val="left"/><w:outlineLvl w:val="1"/></w:pPr>
  <w:rPr><w:rFonts w:ascii="Times New Roman" w:eastAsia="\u9ed1\u4f53" w:hAnsi="Times New Roman"/><w:b/><w:bCs/><w:sz w:val="30"/><w:szCs w:val="30"/></w:rPr>
</w:style>
<w:style w:type="paragraph" w:styleId="Heading3">
  <w:name w:val="heading 3"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/>
  <w:pPr><w:keepNext/><w:spacing w:before="160" w:after="100" w:line="360" w:lineRule="auto"/><w:ind w:firstLineChars="0" w:firstLine="0"/><w:jc w:val="left"/><w:outlineLvl w:val="2"/></w:pPr>
  <w:rPr><w:rFonts w:ascii="Times New Roman" w:eastAsia="\u9ed1\u4f53" w:hAnsi="Times New Roman"/><w:b/><w:bCs/><w:sz w:val="26"/><w:szCs w:val="26"/></w:rPr>
</w:style>
<w:style w:type="paragraph" w:styleId="Bullet">
  <w:name w:val="Bullet"/><w:basedOn w:val="Normal"/>
  <w:pPr><w:spacing w:before="0" w:after="0" w:line="360" w:lineRule="auto"/><w:ind w:left="480" w:leftChars="0" w:firstLine="0" w:firstLineChars="0"/><w:jc w:val="both"/></w:pPr>
</w:style>
<w:style w:type="paragraph" w:styleId="Note">
  <w:name w:val="Note"/><w:basedOn w:val="Normal"/>
  <w:pPr><w:spacing w:before="60" w:after="60" w:line="300" w:lineRule="auto"/><w:ind w:left="480" w:leftChars="0" w:firstLine="0" w:firstLineChars="0"/><w:jc w:val="both"/></w:pPr>
  <w:rPr><w:color w:val="595959"/><w:sz w:val="21"/><w:szCs w:val="21"/></w:rPr>
</w:style>
<w:style w:type="paragraph" w:styleId="TableText">
  <w:name w:val="Table Text"/><w:basedOn w:val="Normal"/>
  <w:pPr><w:spacing w:before="10" w:after="10" w:line="240" w:lineRule="auto"/><w:ind w:firstLine="0" w:firstLineChars="0"/><w:jc w:val="center"/></w:pPr>
  <w:rPr><w:sz w:val="20"/><w:szCs w:val="20"/></w:rPr>
</w:style>
<w:style w:type="paragraph" w:styleId="TableGap">
  <w:name w:val="Table Gap"/><w:basedOn w:val="Normal"/>
  <w:pPr><w:spacing w:before="0" w:after="0" w:line="120" w:lineRule="auto"/><w:ind w:firstLine="0" w:firstLineChars="0"/></w:pPr>
  <w:rPr><w:sz w:val="8"/></w:rPr>
</w:style>
<w:style w:type="paragraph" w:styleId="Caption">
  <w:name w:val="caption"/><w:basedOn w:val="Normal"/><w:qFormat/>
  <w:pPr><w:spacing w:before="120" w:after="60" w:line="240" w:lineRule="auto"/><w:ind w:firstLine="0" w:firstLineChars="0"/><w:jc w:val="center"/></w:pPr>
  <w:rPr><w:rFonts w:eastAsia="\u9ed1\u4f53"/><w:b/><w:bCs/><w:sz w:val="21"/><w:szCs w:val="21"/></w:rPr>
</w:style>
<w:style w:type="paragraph" w:styleId="CoverTitle">
  <w:name w:val="Cover Title"/><w:basedOn w:val="Normal"/>
  <w:pPr><w:spacing w:before="2400" w:after="240" w:line="400" w:lineRule="auto"/><w:ind w:firstLine="0" w:firstLineChars="0"/><w:jc w:val="center"/></w:pPr>
  <w:rPr><w:rFonts w:eastAsia="\u9ed1\u4f53"/><w:b/><w:bCs/><w:sz w:val="52"/><w:szCs w:val="52"/></w:rPr>
</w:style>
<w:style w:type="paragraph" w:styleId="CoverSubtitle">
  <w:name w:val="Cover Subtitle"/><w:basedOn w:val="Normal"/>
  <w:pPr><w:spacing w:before="120" w:after="600" w:line="360" w:lineRule="auto"/><w:ind w:firstLine="0" w:firstLineChars="0"/><w:jc w:val="center"/></w:pPr>
  <w:rPr><w:rFonts w:eastAsia="\u5b8b\u4f53"/><w:sz w:val="32"/><w:szCs w:val="32"/></w:rPr>
</w:style>
<w:style w:type="paragraph" w:styleId="CoverRule">
  <w:name w:val="Cover Rule"/><w:basedOn w:val="Normal"/>
  <w:pPr><w:pBdr><w:bottom w:val="single" w:sz="12" w:space="1" w:color="1F3864"/></w:pBdr><w:spacing w:before="0" w:after="480" w:line="120" w:lineRule="auto"/><w:ind w:firstLine="0" w:firstLineChars="0"/><w:jc w:val="center"/></w:pPr>
  <w:rPr><w:sz w:val="8"/></w:rPr>
</w:style>
<w:style w:type="paragraph" w:styleId="CoverInfo">
  <w:name w:val="Cover Info"/><w:basedOn w:val="Normal"/>
  <w:pPr><w:spacing w:before="80" w:after="80" w:line="360" w:lineRule="auto"/><w:ind w:firstLine="0" w:firstLineChars="0"/><w:jc w:val="center"/></w:pPr>
  <w:rPr><w:rFonts w:eastAsia="\u5b8b\u4f53"/><w:sz w:val="28"/><w:szCs w:val="28"/></w:rPr>
</w:style>
<w:style w:type="paragraph" w:styleId="TOC1">
  <w:name w:val="toc 1"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/>
  <w:pPr><w:spacing w:before="80" w:after="40" w:line="300" w:lineRule="auto"/><w:ind w:firstLine="0" w:firstLineChars="0"/><w:jc w:val="left"/><w:tabs><w:tab w:val="right" w:leader="dot" w:pos="9070"/></w:tabs></w:pPr>
  <w:rPr><w:b/><w:bCs/><w:sz w:val="24"/></w:rPr>
</w:style>
<w:style w:type="paragraph" w:styleId="TOC2">
  <w:name w:val="toc 2"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/>
  <w:pPr><w:spacing w:before="0" w:after="0" w:line="300" w:lineRule="auto"/><w:ind w:left="480" w:firstLine="0" w:firstLineChars="0"/><w:jc w:val="left"/><w:tabs><w:tab w:val="right" w:leader="dot" w:pos="9070"/></w:tabs></w:pPr>
  <w:rPr><w:sz w:val="21"/></w:rPr>
</w:style>
<w:style w:type="paragraph" w:styleId="TOC3">
  <w:name w:val="toc 3"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/>
  <w:pPr><w:spacing w:before="0" w:after="0" w:line="300" w:lineRule="auto"/><w:ind w:left="960" w:firstLine="0" w:firstLineChars="0"/><w:jc w:val="left"/><w:tabs><w:tab w:val="right" w:leader="dot" w:pos="9070"/></w:tabs></w:pPr>
  <w:rPr><w:sz w:val="21"/></w:rPr>
</w:style>
</w:styles>
'@
# unicode escapes in the here-string -> real characters
$conv = New-Object System.Text.StringBuilder
$pos = 0
while ($true) {
    $m = [regex]::Match($styleXml.Substring($pos), '\\u([0-9a-fA-F]{4})')
    if (-not $m.Success) { [void]$conv.Append($styleXml.Substring($pos)); break }
    [void]$conv.Append($styleXml.Substring($pos, $m.Index))
    [void]$conv.Append([char][Convert]::ToInt32($m.Groups[1].Value, 16))
    $pos += $m.Index + $m.Length
}
$styleXml = $conv.ToString()

# header / footer ------------------------------------------------------------
function FieldRun([string]$instr, [string]$placeholder) {
    return '<w:r><w:fldChar w:fldCharType="begin"/></w:r>' +
    '<w:r><w:instrText xml:space="preserve">' + $instr + '</w:instrText></w:r>' +
    '<w:r><w:fldChar w:fldCharType="separate"/></w:r>' +
    '<w:r><w:t>' + $placeholder + '</w:t></w:r>' +
    '<w:r><w:fldChar w:fldCharType="end"/></w:r>'
}

function MakeHeader([string]$text) {
    if (-not $text) { $text = '' }
    return @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:hdr xmlns:w="$ns" xmlns:r="$nsR"><w:p><w:pPr><w:pStyle w:val="Normal"/><w:pBdr><w:bottom w:val="single" w:sz="6" w:space="1" w:color="A6A6A6"/></w:pBdr><w:spacing w:before="0" w:after="0" w:line="240" w:lineRule="auto"/><w:ind w:firstLine="0" w:firstLineChars="0"/><w:jc w:val="center"/></w:pPr><w:r><w:rPr><w:sz w:val="18"/><w:color w:val="595959"/></w:rPr><w:t xml:space="preserve">$(Esc $text)</w:t></w:r></w:p></w:hdr>
"@
}

function MakeFooter([string]$template) {
    $t = if ($template) { $template } else { '{PAGE}' }
    $xml = ''
    $rest = $t
    while ($rest.Length -gt 0) {
        $mPage = $rest.IndexOf('{PAGE}')
        $mNum = $rest.IndexOf('{NUMPAGES}')
        $idx = -1; $kind = ''
        if ($mPage -ge 0 -and ($mNum -lt 0 -or $mPage -le $mNum)) { $idx = $mPage; $kind = 'PAGE' }
        elseif ($mNum -ge 0) { $idx = $mNum; $kind = 'NUMPAGES' }
        if ($idx -lt 0) {
            $xml += '<w:r><w:rPr><w:sz w:val="18"/></w:rPr><w:t xml:space="preserve">' + (Esc $rest) + '</w:t></w:r>'
            break
        }
        if ($idx -gt 0) {
            $xml += '<w:r><w:rPr><w:sz w:val="18"/></w:rPr><w:t xml:space="preserve">' + (Esc $rest.Substring(0, $idx)) + '</w:t></w:r>'
        }
        $xml += (FieldRun " $kind " '1')
        $rest = $rest.Substring($idx + $kind.Length + 2)
    }
    return @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:ftr xmlns:w="$ns" xmlns:r="$nsR"><w:p><w:pPr><w:pStyle w:val="Normal"/><w:jc w:val="center"/><w:ind w:firstLine="0" w:firstLineChars="0"/></w:pPr>$xml</w:p></w:ftr>
"@
}

$emptyHdr = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:hdr xmlns:w="$ns"><w:p><w:pPr><w:pStyle w:val="Normal"/></w:pPr></w:p></w:hdr>
"@
$emptyFtr = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:ftr xmlns:w="$ns"><w:p><w:pPr><w:pStyle w:val="Normal"/></w:pPr></w:p></w:ftr>
"@

$sectPr = @"
<w:sectPr><w:headerReference w:type="default" r:id="rId1"/><w:footerReference w:type="default" r:id="rId2"/><w:headerReference w:type="first" r:id="rId3"/><w:footerReference w:type="first" r:id="rId4"/><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1418" w:right="1418" w:bottom="1418" w:left="1418" w:header="851" w:footer="992" w:gutter="0"/><w:cols w:space="425"/><w:titlePg/><w:docGrid w:type="lines" w:linePitch="312"/></w:sectPr>
"@

$docXml = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:w="$ns" xmlns:r="$nsR"><w:body>$($coverXml.ToString())$($body.ToString())$sectPr</w:body></w:document>
"@

$docRels = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/>
<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer" Target="footer1.xml"/>
<Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header2.xml"/>
<Relationship Id="rId4" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer" Target="footer2.xml"/>
<Relationship Id="rId5" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
<Relationship Id="rId6" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings" Target="settings.xml"/>
</Relationships>
"@

$rootRels = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>
<Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/>
</Relationships>
"@

$contentTypes = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
<Default Extension="xml" ContentType="application/xml"/>
<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
<Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
<Override PartName="/word/settings.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml"/>
<Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>
<Override PartName="/word/footer1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml"/>
<Override PartName="/word/header2.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>
<Override PartName="/word/footer2.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml"/>
<Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
<Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>
</Types>
"@

$settings = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:settings xmlns:w="$ns"><w:zoom w:percent="100"/><w:defaultTabStop w:val="420"/><w:characterSpacingControl w:val="compressPunctuation"/><w:updateFields w:val="true"/><w:compat><w:compatSetting w:name="compatibilityMode" w:uri="http://schemas.microsoft.com/office/word" w:val="15"/></w:compat></w:settings>
"@

$now = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
$coreXml = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"><dc:title>$(Esc $cover.Title)</dc:title><dc:subject>$(Esc $cover.Subtitle)</dc:subject><dc:creator>HVACIDA</dc:creator><cp:lastModifiedBy>HVACIDA</cp:lastModifiedBy><dcterms:created xsi:type="dcterms:W3CDTF">$now</dcterms:created><dcterms:modified xsi:type="dcterms:W3CDTF">$now</dcterms:modified></cp:coreProperties>
"@

$appXml = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"><Application>HVACIDA make-docx-report</Application><Pages>1</Pages></Properties>
"@

# ---------------------------------------------------------------- write ----
if (Test-Path -LiteralPath $Out) { Remove-Item -LiteralPath $Out -Force }
$fs = [System.IO.File]::Open($Out, [System.IO.FileMode]::CreateNew)
$zip = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)
$utf8 = New-Object System.Text.UTF8Encoding($false)

function AddEntry([string]$name, [string]$content) {
    $entry = $zip.CreateEntry($name, [System.IO.Compression.CompressionLevel]::Optimal)
    $s = $entry.Open()
    $bytes = $utf8.GetBytes($content)
    $s.Write($bytes, 0, $bytes.Length)
    $s.Dispose()
}

AddEntry '[Content_Types].xml' $contentTypes
AddEntry '_rels/.rels' $rootRels
AddEntry 'word/document.xml' $docXml
AddEntry 'word/_rels/document.xml.rels' $docRels
AddEntry 'word/styles.xml' $styleXml
AddEntry 'word/settings.xml' $settings
AddEntry 'word/header1.xml' (MakeHeader $cover.Header)
AddEntry 'word/footer1.xml' (MakeFooter $cover.Footer)
AddEntry 'word/header2.xml' $emptyHdr
AddEntry 'word/footer2.xml' $emptyFtr
AddEntry 'docProps/core.xml' $coreXml
AddEntry 'docProps/app.xml' $appXml
$zip.Dispose()
$fs.Dispose()

# ---------------------------------------------------------------- verify ----
$check = [System.IO.Compression.ZipFile]::OpenRead($Out)
$bad = 0
foreach ($e in $check.Entries) {
    if ($e.Name -like '*.xml' -or $e.Name -like '*.rels') {
        $sr = New-Object System.IO.StreamReader($e.Open(), [System.Text.Encoding]::UTF8)
        $txt = $sr.ReadToEnd(); $sr.Dispose()
        try { $null = [xml]$txt } catch { Write-Host ("XML PARSE FAIL: {0} -> {1}" -f $e.FullName, $_.Exception.Message); $bad++ }
    }
}
$entryCount = $check.Entries.Count
$check.Dispose()

$sizeKb = [Math]::Round((Get-Item -LiteralPath $Out).Length / 1KB, 1)
$estPages = [Math]::Round(4 + $charCount / 780 + $tableRows * 0.022 + $headingCount * 0.15, 0)

Write-Host ("OUT   : {0} ({1} KB, {2} parts)" -f $Out, $sizeKb, $entryCount)
Write-Host ("BODY  : {0} chars, {1} tables / {2} rows, {3} headings" -f $charCount, $tableCount, $tableRows, $headingCount)
Write-Host ("EST   : about {0} pages (A4, 12pt body, 1.5 line spacing)" -f $estPages)
if ($bad -gt 0) { Write-Host "XML ERRORS: $bad"; exit 1 }
Write-Host 'XML OK'
