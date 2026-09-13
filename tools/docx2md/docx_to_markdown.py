#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
docx → Markdown 正文提取(只取正文,不处理图片/页眉页脚/批注)。

用途:仓库里的需求类 .docx 是"交付件",本脚本把它抽成可 diff / 可检索的 Markdown 副本,
     保证 Git 里能看清文字改动(二进制 docx 只能整体覆盖,无法合并)。

用法:
    python tools/docx2md/docx_to_markdown.py <输入.docx> <输出.md> [--title 标题]
                                             [--auto-headings] [--toc]

映射规则:
    Title / 标题            → #
    Heading N / 标题 N      → '#' * N
    带项目符号/编号的段落    → 统一输出 '- '(原自动编号的序号无法从正文可靠还原,以 docx 为准)
    正文段落                → 原样(加粗/斜体还原为 ** / *)
    表格                    → 管道表格,首行作表头;单元格内换行转为 <br>,竖线转义

可选:
    --auto-headings  对"没有用标题样式"的文档,把**标签式行**(短、以冒号结尾、不含分号句号)
                     提升为 ### 小标题,便于导航。文档原文一字不改,只改行首标记。
    --toc            在开头生成目录(基于本次识别出的标题)。
"""

import argparse
import hashlib
import os
import re
import sys

try:
    from docx import Document
    from docx.oxml.ns import qn
    from docx.table import Table
    from docx.text.paragraph import Paragraph
except ImportError:  # pragma: no cover
    sys.stderr.write("需要 python-docx:  pip install python-docx\n")
    raise

# 标签式行的判定:短 + 以冒号结尾 + 不含分号/句号(即"标题:"而不是"标题:正文…")
# 用 Unicode 转义显式区分全角/半角,避免源码里两种冒号看起来一样。
LABEL_MAX_LEN = 40
COLON_FULL = "\uff1a"      # :
COLON_HALF = ":"           # :
SENTENCE_MARKS = ("\uff1b", ";", "\u3002")   # ; ; 。


def looks_like_label(text):
    """判断一行是否是"标签式小标题"(仅用于 --auto-headings)。"""
    t = text.strip()
    if not t or len(t) > LABEL_MAX_LEN:
        return False
    if not (t.endswith(COLON_FULL) or t.endswith(COLON_HALF)):
        return False
    if any(mark in t for mark in SENTENCE_MARKS):
        return False
    return True


def slugify(text):
    """生成 GitHub 风格锚点(保留中英文与数字)。"""
    t = strip_inline_md(text).strip().lower()
    t = re.sub(r"[^\w\u4e00-\u9fff \-]", "", t)
    t = re.sub(r"\s+", "-", t).strip("-")
    return t or "section"


def iter_block_items(document):
    """按文档顺序产出段落与表格(保持原有先后关系)。"""
    body = document.element.body
    for child in body.iterchildren():
        if child.tag == qn("w:p"):
            yield Paragraph(child, document)
        elif child.tag == qn("w:tbl"):
            yield Table(child, document)


def run_to_md(run):
    text = run.text
    if not text:
        return ""
    if run.bold and run.italic:
        return "***%s***" % text
    if run.bold:
        return "**%s**" % text
    if run.italic:
        return "*%s*" % text
    return text


def strip_inline_md(text):
    return text.replace("**", "").replace("***", "").replace("*", "")


def is_list_paragraph(paragraph):
    pPr = paragraph._p.pPr
    return pPr is not None and pPr.numPr is not None


def heading_level(style_name):
    """返回标题级别;非标题返回 None。"""
    name = (style_name or "").strip()
    if not name:
        return None
    if re.match(r"^(Title|标题)$", name, re.IGNORECASE):
        return 1
    m = re.match(r"^(?:Heading|标题)\s*(\d+)$", name, re.IGNORECASE)
    if m:
        return max(1, min(int(m.group(1)), 6))
    return None


def paragraph_to_md(paragraph, auto_headings=False):
    raw = "".join(run_to_md(r) for r in paragraph.runs).strip()
    if not raw:
        return ""
    level = heading_level(paragraph.style.name if paragraph.style is not None else "")
    if level is not None:
        return "#" * level + " " + strip_inline_md(raw).strip()
    if auto_headings and looks_like_label(raw):
        return "### " + strip_inline_md(raw).strip()
    if is_list_paragraph(paragraph):
        return "- " + raw
    return raw


def cell_text(cell):
    parts = []
    for p in cell.paragraphs:
        t = "".join(run_to_md(r) for r in p.runs).strip()
        if t:
            parts.append(t)
    return "<br>".join(parts).replace("|", "\\|")


def table_to_md(table):
    rows = []
    for row in table.rows:
        rows.append([cell_text(c) for c in row.cells])
    rows = [r for r in rows if any(c.strip() for c in r)]
    if not rows:
        return []
    width = max(len(r) for r in rows)
    rows = [r + [""] * (width - len(r)) for r in rows]
    out = ["| " + " | ".join(rows[0]) + " |", "|" + "---|" * width]
    for r in rows[1:]:
        out.append("| " + " | ".join(r) + " |")
    return out


def convert(src_path, dst_path, title=None, auto_headings=False, toc=False):
    document = Document(src_path)
    lines = []

    for block in iter_block_items(document):
        if isinstance(block, Paragraph):
            md = paragraph_to_md(block, auto_headings)
            if md:
                lines.append(md)
                lines.append("")
        elif isinstance(block, Table):
            tbl = table_to_md(block)
            if tbl:
                lines.extend(tbl)
                lines.append("")

    # 压缩连续空行
    body = []
    for line in lines:
        if line == "" and body and body[-1] == "":
            continue
        body.append(line.rstrip())
    while body and body[-1] == "":
        body.pop()

    with open(src_path, "rb") as f:
        digest = hashlib.sha256(f.read()).hexdigest()
    size = os.path.getsize(src_path)

    notes = [
        "> **来源**:`%s`(交付件,二进制;Git 中只能整体覆盖,不便 diff)" % os.path.basename(src_path),
        "> **本文件**:正文 Markdown 副本 —— 由 `tools/docx2md/docx_to_markdown.py` 自动提取,供检索、评审与逐行 diff。",
        "> **注意**:本文件是**派生件**,改正文请改 docx 后重新提取;表格以首行作表头,列表统一为 `- `(自动编号的序号以 docx 为准);图片/页眉页脚/批注不提取。",
    ]
    if auto_headings:
        notes.append("> **结构说明**:源文档未使用标题样式(全为 Normal 段落),本副本用 `--auto-headings` 把"
                     "**标签式行**(短、以冒号结尾、不含分号句号)提升为 `###` 小标题,便于导航;**正文一字未改**。")
    notes += [
        "> **源文件指纹**:SHA256 `%s`(%s 字节)" % (digest, format(size, ",")),
        "> **重新生成**:`python tools/docx2md/docx_to_markdown.py \"%s\" \"%s\"%s`"
        % (os.path.basename(src_path), dst_path.replace("\\", "/"),
           (" --auto-headings" + (" --toc" if toc else "")) if auto_headings else ""),
        "",
    ]

    if title:
        notes.append("# " + title)
        notes.append("")

    if toc:
        seen = {}
        toc_lines = ["## 目录", ""]
        for line in body:
            if not line.startswith("#"):
                continue
            text = strip_inline_md(line.lstrip("#").strip())
            anchor = slugify(text)
            seen[anchor] = seen.get(anchor, 0) + 1
            if seen[anchor] > 1:
                anchor = "%s-%d" % (anchor, seen[anchor])
            depth = len(line) - len(line.lstrip("#"))
            toc_lines.append("%s- [%s](#%s)" % ("  " * max(0, depth - 3), text, anchor))
        if len(toc_lines) > 2:
            toc_lines.append("")
            notes += toc_lines

    os.makedirs(os.path.dirname(os.path.abspath(dst_path)), exist_ok=True)
    with open(dst_path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(notes + body) + "\n")

    headings = [l for l in body if l.startswith("#")]
    tables = sum(1 for l in body if l.startswith("|---"))
    print("已生成: %s" % dst_path)
    print("  正文字符数: %d" % len("\n".join(body)))
    print("  标题行: %d 个;表格: %d 个" % (len(headings), tables))
    for h in headings[:25]:
        print("    " + h)
    if len(headings) > 25:
        print("    … 其余 %d 个标题" % (len(headings) - 25))


def main():
    parser = argparse.ArgumentParser(description="docx 正文 → Markdown(可重复运行的提取器)")
    parser.add_argument("source", help="输入 .docx 路径")
    parser.add_argument("target", help="输出 .md 路径")
    parser.add_argument("--title", help="可选:在文件开头插入的一级标题", default=None)
    parser.add_argument("--auto-headings", action="store_true",
                        help="把标签式行(短、以冒号结尾)提升为 ### 小标题(正文不改字)")
    parser.add_argument("--toc", action="store_true", help="在开头生成目录")
    args = parser.parse_args()

    if not os.path.isfile(args.source):
        sys.stderr.write("找不到输入文件: %s\n" % args.source)
        return 2
    convert(args.source, args.target, args.title, args.auto_headings, args.toc)
    return 0


if __name__ == "__main__":
    sys.exit(main())
