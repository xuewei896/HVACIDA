# -*- coding: utf-8 -*-
"""把 capture-manual.ps1 产出的 PNG 重新编码成干净的 RGB PNG。

原因:WPF 的 PngBitmapEncoder 会带 gamma/ICC 元数据,LibreOffice/Word 里会把
截图渲染得发白;用 Pillow 转 RGB 后体积更小、颜色也不再被 gamma 修正。
"""
import glob
import os
import sys

from PIL import Image

out_dir = sys.argv[1] if len(sys.argv) > 1 else r"D:\DSH\docs\manual-images"
count = 0
for path in sorted(glob.glob(os.path.join(out_dir, 'm-*.png'))):
    im = Image.open(path)
    if im.mode in ('RGBA', 'LA', 'P'):
        background = Image.new('RGB', im.size, (255, 255, 255))
        rgba = im.convert('RGBA')
        background.paste(rgba, mask=rgba.split()[-1])
        im = background
    else:
        im = im.convert('RGB')
    im.save(path, 'PNG', optimize=True)
    count += 1
print('flattened', count)
