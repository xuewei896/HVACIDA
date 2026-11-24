# -*- coding: utf-8 -*-
"""把工程源码导出为 Word(.docx) 汇编。

规则:
- 收录 .cs / .xaml / .ps1 / .py / .csproj / .sln / .addin,排除 bin、obj、.git、node_modules;
- 按目录分组,每个文件一节(标题=相对路径),正文为等宽字体代码;
- 首页统计,次页文件清单,页脚页码。
"""
import io
import os
import sys

from docx import Document
from docx.enum.section import WD_SECTION
from docx.enum.table import WD_TABLE_ALIGNMENT
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.shared import Cm, Pt, RGBColor

ROOT = r"D:\DSH"
OUT = os.path.join(ROOT, "docs", "源代码汇编-机电系统BIM正向设计软件.docx")
EXTS = (".cs", ".xaml", ".ps1", ".py", ".csproj", ".sln", ".addin")
SKIP = ("\\bin\\", "\\obj\\", "\\.git\\", "\\node_modules\\", "\\.tmp", "\\docs\\")

GROUPS = [
    ("解决方案与部署", ["HVACIDA.sln", "deploy"]),
    ("HVACIDA.Core(计算内核)", ["src\\HVACIDA.Core"]),
    ("HVACIDA.UI(WPF 界面)", ["src\\HVACIDA.UI"]),
    ("HVACIDA.Revit(Revit 交互)", ["src\\HVACIDA.Revit"]),
    ("工具与自检(tools)", ["tools"]),
]


def collect():
    files = []
    for dirpath, dirnames, filenames in os.walk(ROOT):
        dirnames[:] = [d for d in dirnames if d not in ("bin", "obj", ".git", "node_modules")]
        for name in filenames:
            if os.path.splitext(name)[1].lower() not in EXTS:
                continue
            full = os.path.join(dirpath, name)
            rel = os.path.relpath(full, ROOT)
            if any(s in ("\\" + rel + "\\") or rel.startswith(s.strip("\\")) for s in ["docs"]):
                continue
            files.append((rel, full))
    return sorted(files, key=lambda x: x[0].lower())


def group_of(rel):
    low = rel.lower()
    for title, prefixes in GROUPS:
        for p in prefixes:
            if low.startswith(p.lower()):
                return title
    return "其他"


def read_text(path):
    data = io.open(path, "rb").read()
    for enc in ("utf-8-sig", "utf-8", "gbk"):
        try:
            return data.decode(enc)
        except UnicodeDecodeError:
            continue
    return data.decode("utf-8", "replace")


def add_page_number(paragraph):
    run = paragraph.add_run()
    fld = OxmlElement("w:fldSimple")
    fld.set(qn("w:instr"), "PAGE")
    r = OxmlElement("w:r")
    t = OxmlElement("w:t")
    t.text = "1"
    r.append(t)
    fld.append(r)
    run._r.addnext(fld)


def set_east_asia(style_or_run, cjk="宋体", ascii_font=None):
    rPr = style_or_run.element.get_or_add_rPr()
    rFonts = rPr.find(qn("w:rFonts"))
    if rFonts is None:
        rFonts = rPr.makeelement(qn("w:rFonts"), {})
        rPr.append(rFonts)
    if ascii_font:
        rFonts.set(qn("w:ascii"), ascii_font)
        rFonts.set(qn("w:hAnsi"), ascii_font)
    rFonts.set(qn("w:eastAsia"), cjk)


def main():
    files = collect()
    total_lines = 0
    meta = []
    for rel, full in files:
        text = read_text(full)
        lines = text.count("\n") + (0 if text.endswith("\n") else 1)
        total_lines += lines
        meta.append((rel, full, text, lines, os.path.getsize(full)))

    doc = Document()
    sec = doc.sections[0]
    sec.page_width, sec.page_height = Cm(21.0), Cm(29.7)
    sec.left_margin = sec.right_margin = Cm(1.6)
    sec.top_margin, sec.bottom_margin = Cm(1.8), Cm(1.6)

    normal = doc.styles["Normal"]
    normal.font.name = "Times New Roman"
    normal.font.size = Pt(10.5)
    set_east_asia(normal, cjk="宋体")

    # 页脚页码
    footer_p = sec.footer.paragraphs[0]
    footer_p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    add_page_number(footer_p)

    # 封面
    for _ in range(6):
        doc.add_paragraph()
    p = doc.add_paragraph()
    p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    r = p.add_run("机电系统 BIM 正向设计软件")
    r.bold = True
    r.font.size = Pt(24)
    r.font.name = "Times New Roman"
    set_east_asia(r, cjk="黑体", ascii_font="Times New Roman")

    p = doc.add_paragraph()
    p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    r = p.add_run("源代码汇编")
    r.bold = True
    r.font.size = Pt(22)
    set_east_asia(r, cjk="黑体")

    doc.add_paragraph()
    p = doc.add_paragraph()
    p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    r = p.add_run("%d 个文件 · %s 行 · 生成于 2026-11-24" % (len(files), format(total_lines, ",")))
    r.font.size = Pt(11)
    set_east_asia(r)

    cs = sum(1 for m in meta if m[0].lower().endswith(".cs"))
    xaml = sum(1 for m in meta if m[0].lower().endswith(".xaml"))
    other = len(meta) - cs - xaml
    p = doc.add_paragraph()
    p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    r = p.add_run("C# %d 个 · XAML %d 个 · 其他脚本/工程文件 %d 个" % (cs, xaml, other))
    r.font.size = Pt(11)
    set_east_asia(r)

    doc.add_page_break()

    # 文件清单
    h = doc.add_paragraph()
    r = h.add_run("文件清单")
    r.bold = True
    r.font.size = Pt(16)
    set_east_asia(r, cjk="黑体")

    table = doc.add_table(rows=1, cols=3)
    table.style = "Table Grid"
    table.alignment = WD_TABLE_ALIGNMENT.CENTER
    hdr = table.rows[0].cells
    for cell, label in zip(hdr, ("序号", "文件", "行数")):
        cell.text = ""
        rr = cell.paragraphs[0].add_run(label)
        rr.bold = True
        rr.font.size = Pt(9)
        set_east_asia(rr)

    for i, (rel, _full, _text, lines, _size) in enumerate(meta, 1):
        cells = table.add_row().cells
        for cell, val in zip(cells, (str(i), rel, str(lines))):
            cell.text = ""
            rr = cell.paragraphs[0].add_run(val)
            rr.font.size = Pt(8)
            set_east_asia(rr, ascii_font="Consolas" if val == rel else None)

    # 固定列宽,避免长路径折行
    table.autofit = False
    widths = (Cm(1.2), Cm(15.6), Cm(1.6))
    for col, w in zip(table.columns, widths):
        col.width = w
    for row in table.rows:
        for cell, w in zip(row.cells, widths):
            cell.width = w

    doc.add_page_break()

    # 正文:按分组
    current_group = None
    idx = 0
    for rel, _full, text, lines, _size in meta:
        group = group_of(rel)
        if group != current_group:
            current_group = group
            g = doc.add_paragraph()
            r = g.add_run(group)
            r.bold = True
            r.font.size = Pt(18)
            set_east_asia(r, cjk="黑体")
            g.paragraph_format.space_before = Pt(6)
            g.paragraph_format.space_after = Pt(10)

        idx += 1
        cap = doc.add_paragraph()
        r = cap.add_run("%d. %s  (%d 行)" % (idx, rel, lines))
        r.bold = True
        r.font.size = Pt(9.5)
        set_east_asia(r, cjk="黑体", ascii_font="Consolas")
        cap.paragraph_format.keep_with_next = True
        cap.paragraph_format.space_before = Pt(10)
        cap.paragraph_format.space_after = Pt(2)

        code = doc.add_paragraph()
        fmt = code.paragraph_format
        fmt.space_before = Pt(0)
        fmt.space_after = Pt(4)
        fmt.line_spacing = 1.0
        fmt.left_indent = Cm(0.2)
        run = code.add_run()
        run.font.name = "Consolas"
        run.font.size = Pt(7.5)
        set_east_asia(run, cjk="宋体", ascii_font="Consolas")

        body = text.replace("\r\n", "\n").replace("\r", "\n").replace("\t", "    ")
        if body.endswith("\n"):
            body = body[:-1]
        parts = body.split("\n")
        for j, line in enumerate(parts):
            if j:
                run.add_break()
            if line:
                run.add_text(line)

    # 所有 w:t 保留空格
    for t in doc.element.body.iter(qn("w:t")):
        t.set(qn("xml:space"), "preserve")

    doc.save(OUT)
    print("saved", OUT, os.path.getsize(OUT) // 1024, "KB", len(meta), "files", total_lines, "lines")


if __name__ == "__main__":
    main()
