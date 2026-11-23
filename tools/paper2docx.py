# -*- coding: utf-8 -*-
"""论文 LaTeX → Word(docx)。

流程:预处理(图表编号、引用编号、TikZ 占位)→ pandoc → python-docx 排版。
"""
import io
import os
import re
import subprocess
import sys

from docx import Document
from docx.enum.table import WD_TABLE_ALIGNMENT
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.oxml.ns import qn
from docx.shared import Cm, Pt, RGBColor

PANDOC = r"C:\Program Files\Pandoc\pandoc.exe"


def read(p):
    return io.open(p, encoding="utf-8").read()


def write(p, s):
    io.open(p, "w", encoding="utf-8", newline="").write(s)


def build(paper_dir, out_docx, tikz_png, tikz_name, title, author_name, affiliation):
    tex = read(os.path.join(paper_dir, "main.tex"))
    body = tex.split(r"\begin{document}", 1)[1].split(r"\end{document}", 1)[0]
    body = body.replace(r"\maketitle", "")
    # 行内数学换成 Unicode(Word 里比 OMML 稳,且这些只是上下标与乘号)
    body = (body.replace("$^2$", "²").replace("$^3$", "³")
                .replace(r"$\times$", "×").replace(r"\,", " "))

    bib = ""
    m = re.search(r"\\begin\{thebibliography\}.*?\\end\{thebibliography\}", body, re.S)
    if m:
        bib = m.group(0)
        body = body.replace(bib, "")
    items = re.findall(r"\\bibitem\{([^}]+)\}\s*(.*?)(?=\\bibitem|\\end\{thebibliography\})", bib, re.S)

    # 图/表编号:按出现顺序
    fig_labels = re.findall(r"\\begin\{figure\}(?:\[H\])?[\s\S]*?\\label\{([^}]+)\}", body)
    tab_labels = re.findall(r"\\begin\{table\}(?:\[H\])?[\s\S]*?\\label\{([^}]+)\}", body)
    ref_map = {}
    for i, k in enumerate(fig_labels, 1):
        ref_map[k.strip()] = " %d" % i
    for i, k in enumerate(tab_labels, 1):
        ref_map[k.strip()] = " %d" % i

    # TikZ 图 → 标记(带图号,因为 LaTeX 的图号是自动生成的)
    fig_no = {"n": 0}

    def fig_repl(match):
        block = match.group(0)
        if "tikzpicture" not in block:
            return block
        fig_no["n"] += 1
        cap = re.search(r"\\caption\{([^}]*)\}", block)
        caption = cap.group(1) if cap else ""
        return "\n\n[[FIG:%s|%s]]\n\n" % (tikz_name, caption)

    body = re.sub(r"\\begin\{figure\}(?:\[H\])?[\s\S]*?\\end\{figure\}", fig_repl, body)

    # 交叉引用:图/表编号
    def ref_repl(match):
        key = match.group(1).strip()
        return ref_map.get(key, "")

    body = re.sub(r"\\ref\{([^}]+)\}", ref_repl, body)
    body = body.replace("(图 )", "(图 )")

    # abstract → 摘要
    body = re.sub(r"\\begin\{abstract\}(.*?)\\end\{abstract\}",
                  lambda m: "\n\\section*{摘要}\n" + m.group(1) + "\n", body, flags=re.S)

    order = {}

    def cite_repl(match):
        keys = [k.strip() for k in match.group(1).split(",")]
        nums = []
        for k in keys:
            if k not in order:
                order[k] = len(order) + 1
            nums.append(order[k])
        return "[" + ",".join(str(n) for n in sorted(nums)) + "]"

    body = re.sub(r"\\cite\{([^}]+)\}", cite_repl, body)

    for key, _ in items:
        key = key.strip()
        if key not in order:
            order[key] = len(order) + 1

    tmp_tex = os.path.join(paper_dir, "_word_body.tex")
    head = "\\documentclass{article}\n\\title{%s}\n\\author{%s}\n\\begin{document}\n\\maketitle\n" % (title, author_name)
    write(tmp_tex, head + body + "\n\\end{document}\n")

    if os.path.exists(out_docx):
        os.remove(out_docx)
    subprocess.run([PANDOC, tmp_tex, "-f", "latex", "-t", "docx",
                    "--resource-path", paper_dir, "-o", out_docx], check=True)

    doc = Document(out_docx)
    sec = doc.sections[0]
    sec.page_width, sec.page_height = Cm(21.0), Cm(29.7)
    for attr, val in (("left_margin", 2.4), ("right_margin", 2.4),
                      ("top_margin", 2.4), ("bottom_margin", 2.2)):
        setattr(sec, attr, Cm(val))

    def set_font(target, ascii_font="Times New Roman", cjk="宋体", size=None, bold=None):
        f = target.font
        f.name = ascii_font
        if size is not None:
            f.size = Pt(size)
        if bold is not None:
            f.bold = bold
        f.color.rgb = RGBColor(0, 0, 0)
        rPr = target.element.get_or_add_rPr()
        rFonts = rPr.find(qn("w:rFonts"))
        if rFonts is None:
            rFonts = rPr.makeelement(qn("w:rFonts"), {})
            rPr.append(rFonts)
        rFonts.set(qn("w:ascii"), ascii_font)
        rFonts.set(qn("w:hAnsi"), ascii_font)
        rFonts.set(qn("w:eastAsia"), cjk)

    styles = doc.styles
    normal = styles["Normal"]
    set_font(normal, size=12, cjk="宋体")
    normal.paragraph_format.line_spacing = 1.5
    normal.paragraph_format.space_before = Pt(0)
    normal.paragraph_format.space_after = Pt(0)

    for name, size, align, cjk in (("Title", 18, WD_ALIGN_PARAGRAPH.CENTER, "黑体"),
                                   ("Author", 12, WD_ALIGN_PARAGRAPH.CENTER, "宋体")):
        try:
            st = styles[name]
        except KeyError:
            continue
        set_font(st, cjk=cjk, size=size, bold=(name == "Title"))
        st.paragraph_format.alignment = align
        st.paragraph_format.space_after = Pt(4)

    for name, size, before, after in (("Heading 1", 15, 12, 6),
                                      ("Heading 2", 13, 10, 4),
                                      ("Heading 3", 12, 8, 3)):
        try:
            st = styles[name]
        except KeyError:
            continue
        set_font(st, cjk="黑体", size=size, bold=True)
        st.paragraph_format.space_before = Pt(before)
        st.paragraph_format.space_after = Pt(after)
        st.paragraph_format.line_spacing = 1.3
        st.paragraph_format.keep_with_next = True

    for name in ("Caption", "Image Caption", "Table Caption"):
        try:
            st = styles[name]
        except KeyError:
            continue
        set_font(st, size=10.5, cjk="宋体")
        st.font.italic = False
        st.paragraph_format.alignment = WD_ALIGN_PARAGRAPH.CENTER
        st.paragraph_format.space_before = Pt(3)
        st.paragraph_format.space_after = Pt(6)

    # 标题:作者分两行;摘要标题居中
    for p in doc.paragraphs:
        if p.style.name == "Author":
            for r in list(p.runs):
                r._r.getparent().remove(r._r)
            r1 = p.add_run(author_name)
            r1.bold = True
            r1.font.size = Pt(12)
            r1.font.name = "Times New Roman"
            r1._element.get_or_add_rPr()
            br = p.add_run()
            br.add_break()
            r2 = p.add_run(affiliation)
            r2.font.size = Pt(10.5)
        if p.style.name == "Heading 1" and p.text.strip() == "摘要":
            p.style = doc.styles["Normal"]
            p.alignment = WD_ALIGN_PARAGRAPH.CENTER
            p.paragraph_format.first_line_indent = Pt(0)
            for r in p.runs:
                r.bold = True
                r.font.size = Pt(12)
            p.paragraph_format.space_before = Pt(12)
            p.paragraph_format.space_after = Pt(6)

    # TikZ 占位 → 图片 + 图注
    for p in list(doc.paragraphs):
        if not p.text.strip().startswith("[[FIG:"):
            continue
        spec = p.text.strip()[6:-2]
        _, caption = spec.split("|", 1)
        for r in list(p.runs):
            r._r.getparent().remove(r._r)
        run = p.add_run()
        run.add_picture(tikz_png, width=Cm(15))
        p.alignment = WD_ALIGN_PARAGRAPH.CENTER
        p.paragraph_format.first_line_indent = Pt(0)
        cap_para = doc.add_paragraph(caption, style="Caption")
        p._p.addnext(cap_para._p)

    # 图注/表注编号(在 TikZ 图注插入之后做,保证顺序)
    fig_i = tab_i = 0
    for p in doc.paragraphs:
        style = p.style.name
        text = p.text.strip()
        if style == "Table Caption":
            tab_i += 1
            if not text.startswith("表"):
                p.text = "表 %d %s" % (tab_i, text)
        elif style in ("Caption", "Image Caption"):
            fig_i += 1
            if not text.startswith("图"):
                p.text = "图 %d %s" % (fig_i, text)

    # 正文首行缩进(不含标题、图注、表注、参考文献)
    for p in doc.paragraphs:
        t = p.text.strip()
        if not t or len(t) < 40:
            continue
        if p.style.name.startswith(("Heading", "Title", "Caption", "Image Caption", "Table Caption")):
            continue
        if t.startswith(("图 ", "表 ", "[", "关键词")):
            continue
        p.paragraph_format.first_line_indent = Pt(24)


    for table in doc.tables:
        table.alignment = WD_TABLE_ALIGNMENT.CENTER
        table.autofit = True
        for row in table.rows:
            for cell in row.cells:
                for para in cell.paragraphs:
                    para.paragraph_format.line_spacing = 1.15
                    para.paragraph_format.space_after = Pt(0)
                    for run in para.runs:
                        run.font.size = Pt(9)

    if items:
        doc.add_paragraph("参考文献", style="Heading 1")
        by_key = {k.strip(): txt for k, txt in items}
        for key, num in sorted(order.items(), key=lambda kv: kv[1]):
            txt = by_key.get(key, "")
            txt = re.sub(r"\s+", " ", txt.replace("\n", " ")).strip()
            p = doc.add_paragraph("[%d] %s" % (num, txt))
            p.paragraph_format.first_line_indent = Pt(0)
            p.paragraph_format.left_indent = Pt(21)
            p.paragraph_format.space_after = Pt(2)
            p.paragraph_format.line_spacing = 1.2
            for run in p.runs:
                run.font.size = Pt(10.5)

    doc.save(out_docx)
    os.remove(tmp_tex)
    print("built", out_docx)


if __name__ == "__main__":
    which = sys.argv[1] if len(sys.argv) > 1 else "1"
    name = "韩学伟"
    org = "中铁第六勘察设计院集团有限公司电气化设计院分公司,天津"
    if which == "1":
        build(r"D:\DSH\paper",
              r"D:\DSH\paper\论文一-基于Revit二次开发的地铁车站机电系统BIM正向设计软件研发与应用.docx",
              r"D:\DSH\.tmp-docx\fig1\png288\page-0001.png", "arch",
              "基于 Revit 二次开发的地铁车站机电系统BIM 正向设计软件研发与应用", name, org)
    else:
        build(r"D:\DSH\paper2",
              r"D:\DSH\paper2\论文二-面向地铁暖通设计的规范知识库与大模型工具调用方法及实现.docx",
              r"D:\DSH\.tmp-docx\fig2\png\page-0001.png", "flow",
              "面向地铁暖通设计的规范知识库与大模型工具调用方法及实现", name, org)
