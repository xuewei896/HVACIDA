# -*- coding: utf-8 -*-
import os, shutil
from PIL import Image
SRC = r"D:\DSH\docs\report-images"

def trim_bottom(name, bottom_f, out_name):
    im = Image.open(os.path.join(SRC, name + ".png"))
    w, h = im.size
    out = im.crop((0, 0, w, int(bottom_f * h)))
    if out.width > 1700:
        r = 1700.0 / out.width
        out = out.resize((1700, int(out.height * r)), Image.LANCZOS)
    out.save(os.path.join(SRC, out_name + ".png"), optimize=True)
    print("%-22s %4d x %4d" % (out_name, out.width, out.height))

def crop(name, l, t, r, b, out_name, maxw=1400):
    im = Image.open(os.path.join(SRC, name + ".png"))
    w, h = im.size
    out = im.crop((int(l * w), int(t * h), int(r * w), int(b * h)))
    if out.width > maxw:
        rr = maxw * 1.0 / out.width
        out = out.resize((maxw, int(out.height * rr)), Image.LANCZOS)
    out.save(os.path.join(SRC, out_name + ".png"), optimize=True)
    print("%-22s %4d x %4d" % (out_name, out.width, out.height))

trim_bottom("ui-small-allair", 1.0, "fig-allair-window")
trim_bottom("ui-small-vrf", 1.0, "fig-vrf-window")
trim_bottom("ui-large-result", 0.66, "fig-large-result")
trim_bottom("ui-large-load", 1.0, "fig-large-load")
trim_bottom("ui-material", 0.73, "fig-material")
trim_bottom("ui-sheets", 0.73, "fig-sheets")
crop("ui-small-allair", 0.004, 0.405, 0.452, 0.790, "fig-allair-table", 1300)
