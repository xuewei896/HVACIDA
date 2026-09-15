#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
各省市室外空气参数.md  ->  src/HVACIDA.Core/Resources/weather-db.csv

源文件:仓库根目录 `各省市室外空气参数.md`(GB 50736-2012 附录A 表A「室外空气计算参数」的 HTML 表格转录)。
产出:嵌入 HVACIDA.Core.dll 的气象数据库 CSV(单一数据源,插件运行时读它)。

为什么要走"生成器"而不是手抄:
  1. 附录A 是 294 个台站 × 20+ 个参数,手抄必然出错;
  2. 源文件是分块 HTML 表格,块与块之间**共享省名上下文**,还有两种缺省形状(见下),
     解析规则必须集中在一处、可复核、可重跑;
  3. 生成器带校验(省台站数 checksum、湿球≤干球、数值范围、台站号唯一),
     可疑数据一律报出来而不是静默采纳。

源文件的三个坑(2026-09-15 逐个摸清,解析规则据此实现):
  A. 行标签(rowspan)在后续 <tr> 里不再出现 —— 不按列位补偿,数据列会整体左移、台站号与温度串列。
     故 parse_grid 完整展开 rowspan/colspan。
  B. 分块表格里有些块的"省/直辖市/自治区"格只留了计数、丢了省名,形如 `(2)`(天津续块)、`(10)`(河北续块)。
     规则:左起逐格走,**`(N)` 一律视为"当前省的续块"**;若当前省还没记下计数,就用 N 补上,
     并参与后面的台站数 checksum(c1 的 `(2)` 正是 天津 的 2 个台站:天津 + 塘沽)。
  C. 少数块整行没有省行(39 行 = 40 行布局少一行),如 湖南/云南/西藏/新疆 的续块;省会沿用它前面那一段。
     另有一块是 80 行(两块被合并),按 40 行切开。

自检口径:每个省(自治区/直辖市)解析出的台站数,必须等于源文件里声明的计数
(显式 `省名(N)`,或由 `(N)` 续块补出的 N)—— 这是一条覆盖全表的 checksum。

用法:
  python tools/weatherdb/build_weather_db.py            # 生成 CSV + 校验报告
  python tools/weatherdb/build_weather_db.py --check    # 只校验(不写文件),供门禁用
"""

import argparse
import hashlib
import html
import io
import json
import os
import re
import sys

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
DEFAULT_SOURCE = os.path.join(REPO_ROOT, "各省市室外空气参数.md")
DEFAULT_OUT = os.path.join(REPO_ROOT, "src", "HVACIDA.Core", "Resources", "weather-db.csv")

# CSV 列(顺序即文件列序;改这里要同步改 C# 侧 WeatherStationRecord / WeatherDatabase)
COLUMNS = [
    "station_id", "province", "city", "station_name", "latitude", "longitude",
    "elevation_m", "stats_period", "annual_mean_temp_c",
    "heating_outdoor_c", "winter_vent_outdoor_c", "winter_ac_outdoor_c", "winter_ac_rh_pct",
    "summer_ac_dry_bulb_c", "summer_ac_wet_bulb_c", "summer_vent_dry_bulb_c",
    "summer_vent_rh_pct", "summer_ac_daily_mean_c",
    "winter_atm_pressure_hpa", "summer_atm_pressure_hpa",
    "extreme_max_c", "extreme_min_c", "max_frost_depth_cm", "winter_sunshine_pct",
]

# 行标签 -> CSV 列(按"包含"匹配,规避源文件里被截断的括号)
ROW_LABELS = {
    "elevation_m": "海拔",
    "stats_period": "统计年份",
    "annual_mean_temp_c": "年平均温度",
    "heating_outdoor_c": "供暖室外计算温度",
    "winter_vent_outdoor_c": "冬季通风室外计算温度",
    "winter_ac_outdoor_c": "冬季空气调节室外计算温度",
    "winter_ac_rh_pct": "冬季空气调节室外计算相对湿度",
    "summer_ac_dry_bulb_c": "夏季空气调节室外计算干球温度",
    "summer_ac_wet_bulb_c": "夏季空气调节室外计算湿球温度",
    "summer_vent_dry_bulb_c": "夏季通风室外计算温度",
    "summer_vent_rh_pct": "夏季通风室外计算相对湿度",
    "summer_ac_daily_mean_c": "夏季空气调节室外计算日平均温度",
    "winter_sunshine_pct": "冬季日照百分率",
    "max_frost_depth_cm": "最大冻土深度",
    "winter_atm_pressure_hpa": "冬季室外大气压力",
    "summer_atm_pressure_hpa": "夏季室外大气压力",
    "extreme_max_c": "极端最高气温",
    "extreme_min_c": "极端最低气温",
}

LABEL_PROVINCE = "省/直辖市/自治区"
LABEL_CITY = "市/区/自治州"
LABEL_STATION = "台站名称及编号"
LABEL_LAT = "北纬"
LABEL_LON = "东经"

# 省份行里的标签格(不是省名);必须精确匹配 —— "新疆维吾尔自治区"这类省名本身含"自治区"
PROVINCE_ROW_LABELS = {LABEL_PROVINCE, LABEL_CITY, LABEL_STATION, LABEL_LAT, LABEL_LON, "海拔(m)", "统计年份"}

# 数值合理性范围。**这是"解析是否可信"的护栏,不是工程判断**:
# 范围按中国实际气候取宽(三亚冬季空调 15.8 ℃、那曲夏季湿球 9.1 ℃ 都合法),
# 只有明显不可能的值才报错 —— 那基本意味着列位串了。
RANGES = {
    "summer_ac_dry_bulb_c": (5.0, 48.0),
    "summer_ac_wet_bulb_c": (5.0, 35.0),
    "summer_vent_dry_bulb_c": (5.0, 48.0),
    "summer_ac_daily_mean_c": (5.0, 45.0),
    "winter_ac_outdoor_c": (-50.0, 25.0),
    "winter_vent_outdoor_c": (-45.0, 30.0),
    "heating_outdoor_c": (-50.0, 25.0),
    "summer_vent_rh_pct": (5.0, 100.0),
    "winter_ac_rh_pct": (5.0, 100.0),
    "summer_atm_pressure_hpa": (500.0, 1060.0),
    "winter_atm_pressure_hpa": (500.0, 1060.0),
    "annual_mean_temp_c": (-10.0, 30.0),
    "extreme_max_c": (0.0, 55.0),
    "extreme_min_c": (-55.0, 10.0),
    "max_frost_depth_cm": (0.0, 400.0),
    "winter_sunshine_pct": (0.0, 100.0),
}

# 中国境内台站海拔上限 m(超过即视为源文件笔误,只提示不拦)
ELEVATION_MAX_M = 5000.0

# 允许缺失且**不拦**的字段:
#  - summer_ac_wet_bulb_c:附录A 条文说明明确"咸阳、黔南州及新疆塔城地区等个别台站的湿球温度无记录,
#    可参考表19的数值选取" —— 源文件没有表19,故这些格留空、界面按"未填"处理,**绝不猜值**;
#  - 其余为装饰性列(南方无冻土、个别台站缺极端值),本插件不参与任何计算,缺失只统计不报错。
OPTIONAL_FIELDS = {
    "summer_ac_wet_bulb_c", "max_frost_depth_cm", "winter_sunshine_pct",
    "extreme_max_c", "extreme_min_c", "annual_mean_temp_c", "elevation_m",
    "heating_outdoor_c", "winter_ac_rh_pct", "summer_ac_daily_mean_c", "stats_period",
}

# 真正驱动工程结果的字段:这些**必须**有值且在合理范围内,否则一定是解析串列了,必须拦下。
REQUIRED_FIELDS = {
    "summer_ac_dry_bulb_c", "summer_vent_dry_bulb_c", "winter_vent_outdoor_c",
    "winter_ac_outdoor_c", "summer_vent_rh_pct",
    "summer_atm_pressure_hpa", "winter_atm_pressure_hpa",
}


# --------------------------------------------------------------------------- HTML 表格

def parse_grid(table_html):
    """HTML 表格 -> 逻辑网格(完整展开 colspan/rowspan,保证列位与源表一致)。

    源表的行标签(如"台站名称及编号"、"室外计算温、湿度")跨行:这些行后续的 `<tr>` 里
    **不会再出现**该格。若不补偿列位,数据列会整体左移 —— 台站号与温度会静默串列。
    """
    rows = re.findall(r"<tr>(.*?)</tr>", table_html, re.S)
    raw = []
    for r in rows:
        cells = []
        for attr, val in re.findall(r"<td([^>]*)>(.*?)</td>", r, re.S):
            cs = re.search(r'colspan="(\d+)"', attr)
            rs = re.search(r'rowspan="(\d+)"', attr)
            text = html.unescape(re.sub(r"<[^>]+>", "", val)).strip()
            cells.append((int(cs.group(1)) if cs else 1, int(rs.group(1)) if rs else 1, text))
        raw.append(cells)

    grid = []
    pending = {}          # 列号 -> 还需向下占用的行数
    pending_val = {}
    for cells in raw:
        line = []
        ci = 0
        nxt = 0
        while True:
            if pending.get(ci, 0) > 0:
                line.append(pending_val.get(ci))
                pending[ci] -= 1
                ci += 1
                continue
            if nxt >= len(cells):
                if any(v > 0 for c, v in pending.items() if c >= ci):
                    line.append(None)
                    ci += 1
                    continue
                break
            cs, rs, text = cells[nxt]
            nxt += 1
            for k in range(cs):
                line.append(text)
                if rs > 1:
                    pending[ci + k] = rs - 1
                    pending_val[ci + k] = text
            ci += cs
        grid.append(line)

    width = max((len(r) for r in grid), default=0)
    for r in grid:
        while len(r) < width:
            r.append(None)
    return grid


def row_labels(grid):
    """{标签文本: 行号}(只看头两列,防止把数据里的同名文字当标签)。

    注意:这里**不能**跳过 PROVINCE_ROW_LABELS —— 那些正是要找的标签
    (省/直辖市/自治区、市/区/自治州、台站名称及编号…);跳过它们会让所有块都被误判成"无标签块"。
    """
    out = {}
    for ri, row in enumerate(grid):
        for ci in range(min(2, len(row))):
            t = row[ci]
            if t:
                out.setdefault(t, ri)
    return out


def split_chunks(tables):
    """80 行的合块按 40 行切开。"""
    chunks = []
    for tb in tables:
        g = parse_grid(tb)
        if len(g) == 80:
            chunks.append(g[:40])
            chunks.append(g[40:])
        else:
            chunks.append(g)
    return chunks


def calibrate(chunks):
    """用"带标签"的块标定:市名行行号 + 各字段相对市名行的行距。"""
    deltas = {}
    city_rows = set()
    labelled = 0
    for g in chunks:
        labels = row_labels(g)
        if LABEL_CITY not in labels:
            continue
        labelled += 1
        crow = labels[LABEL_CITY]
        city_rows.add(crow)
        ref = {
            "station_name": labels.get(LABEL_STATION, crow + 1),
            "station_id": labels.get(LABEL_STATION, crow + 1) + 1,
            "latitude": labels.get(LABEL_LAT, crow + 3),
            "longitude": labels.get(LABEL_LON, crow + 4),
        }
        for col, label in ROW_LABELS.items():
            for text, ri in labels.items():
                if label in text:
                    ref[col] = ri
                    break
        for col, ri in ref.items():
            deltas.setdefault(col, set()).add(ri - crow)

    inconsistent = {k: sorted(v) for k, v in deltas.items() if len(v) > 1}
    city_row = sorted(city_rows)[0] if city_rows else 1
    return {k: sorted(v)[0] for k, v in deltas.items()}, city_row, labelled, inconsistent


# --------------------------------------------------------------------------- 值解析

def to_float(text):
    """从单元格里取出数值。

    源文件里同一列可能混有转录杂质:如 汕头 湿球是「、27.7」(多一个顿号)、咸阳 湿球是「*」(脚注,
    即标准所称"无记录")。故取**首个数字**,取不到才视为缺值 —— 既不被杂质骗成"缺值",
    也不会把 `*` 当成数字。
    """
    if text is None:
        return None
    t = str(text).strip().replace("，", ",")
    if t in ("", "-", "—", "/", "*"):
        return None
    m = re.search(r"-?\d+(?:\.\d+)?", t)
    return float(m.group(0)) if m else None


def fmt_num(v):
    """数值列统一写成紧凑十进制;None -> 空(CSV 里"空"就是标准的无记录,不写 `*` 之类的杂质)。"""
    if v is None:
        return ""
    s = ("%.4f" % v).rstrip("0").rstrip(".")
    return s if s not in ("", "-0") else "0"


def clean(text):
    return "" if text is None else str(text).strip()


# --------------------------------------------------------------------------- 主解析

def build_records(src_text):
    tables = re.findall(r"<table>(.*?)</table>", src_text, re.S)
    chunks = split_chunks(tables)
    delta, city_row_no, labelled, inconsistent = calibrate(chunks)

    records = []
    warnings = []
    totals = {}          # 省 -> 解析出的台站数
    declared = {}        # 省 -> 声明的台站数
    state = {"province": None, "count": None}

    def resolve(cell, idx):
        """省份行里的一格 -> 更新"当前省"。"""
        if not cell or cell in PROVINCE_ROW_LABELS:
            return
        m = re.match(r"^(.+?)\((\d+)\)$", cell)
        if m:
            name, n = m.group(1).strip(), int(m.group(2))
            state["province"], state["count"] = name, n
            if name in declared and declared[name] != n:
                warnings.append("块 %d: %s 的计数前后不一致(%d vs %d)" % (idx, name, declared[name], n))
            declared[name] = n
            return
        m = re.match(r"^\((\d+)\)$", cell)
        if m:
            n = int(m.group(1))
            if state["count"] is None:
                state["count"] = n
                if state["province"]:
                    declared[state["province"]] = n
            elif state["count"] != n:
                warnings.append("块 %d: 续块计数 %d 与当前省 %s 声明的 %d 不符"
                                % (idx, n, state["province"], state["count"]))
            return
        state["province"], state["count"] = cell, None

    for idx, g in enumerate(chunks):
        labelled_chunk = LABEL_CITY in row_labels(g)
        if labelled_chunk:
            labels = row_labels(g)
            crow = labels[LABEL_CITY]
            prow = labels.get(LABEL_PROVINCE)
        else:
            has_province_row = any(re.search(r"\(\d+\)$", c) for c in g[0] if c)
            crow = city_row_no if has_province_row else city_row_no - 1
            prow = 0 if has_province_row else None

        # 逐列定省:左起走,遇新格才更新"当前省"(展开后同一格会跨列重复)
        width = len(g[0])
        col_province = [state["province"]] * width
        if prow is not None:
            last = object()
            for ci in range(width):
                cell = g[prow][ci] if ci < len(g[prow]) else None
                if cell != last:
                    last = cell
                    resolve(cell, idx)
                col_province[ci] = state["province"]

        cities = g[crow]
        for ci in range(width):
            city = clean(cities[ci] if ci < len(cities) else None)
            if not city or city in PROVINCE_ROW_LABELS:
                continue

            rec = {"province": col_province[ci] or "", "city": city}
            for col in ("station_id", "station_name", "latitude", "longitude"):
                off = delta.get(col)
                ri = crow + off if off is not None else None
                rec[col] = clean(g[ri][ci]) if ri is not None and 0 <= ri < len(g) and ci < len(g[ri]) else ""
            for col in ROW_LABELS:
                off = delta.get(col)
                ri = crow + off if off is not None else None
                rec[col] = clean(g[ri][ci]) if ri is not None and 0 <= ri < len(g) and ci < len(g[ri]) else ""

            if not rec["province"]:
                warnings.append("块 %d 第 %d 列(%s)未解析出省名" % (idx, ci, city))
            if not rec["station_id"]:
                warnings.append("块 %d 第 %d 列(%s/%s)缺台站号" % (idx, ci, rec["province"], city))

            # 数值列统一规范化(去掉源文件里的顿号/星号等杂质,取不到的写空)
            for col in RANGES:
                rec[col] = fmt_num(to_float(rec.get(col)))

            records.append(rec)
            totals[rec["province"]] = totals.get(rec["province"], 0) + 1

    meta = {
        "tables": len(tables), "chunks": len(chunks), "labelled_chunks": labelled,
        "city_row_in_labelled": city_row_no, "deltas": delta,
        "delta_inconsistent": inconsistent,
        "totals": totals, "declared": declared,
    }
    return records, warnings, meta


def validate(records, warnings, meta):
    """返回 (错误, 提示)。错误会导致非零退出;提示只记录(标准本身的缺口、装饰列的疑点)。"""
    errors = list(warnings)
    notes = []
    optional_missing = {}          # 字段 -> 缺失台站数(汇总,避免刷屏)
    wet_bulb_gap = []

    seen = {}
    for r in records:
        sid = r["station_id"]
        if not sid:
            errors.append("缺台站号: %s%s" % (r["province"], r["city"]))
            continue
        if sid in seen:
            errors.append("台站号重复: %s (%s%s 与 %s%s)"
                          % (sid, r["province"], r["city"], seen[sid]["province"], seen[sid]["city"]))
        seen[sid] = r

    for r in records:
        tag = "%s%s(%s)" % (r["province"], r["city"], r["station_id"])
        for col, (lo, hi) in RANGES.items():
            v = to_float(r.get(col))
            if v is None:
                if col in REQUIRED_FIELDS:
                    errors.append("缺值 %s: %s" % (col, tag))
                elif col in OPTIONAL_FIELDS:
                    optional_missing[col] = optional_missing.get(col, 0) + 1
                    if col == "summer_ac_wet_bulb_c":
                        wet_bulb_gap.append(tag)
                else:
                    errors.append("缺值 %s: %s" % (col, tag))
            elif not (lo <= v <= hi):
                if col in REQUIRED_FIELDS:
                    errors.append("超范围 %s=%s: %s" % (col, v, tag))
                else:
                    notes.append("装饰列超范围(仅提示) %s=%s: %s" % (col, v, tag))

        dry = to_float(r.get("summer_ac_dry_bulb_c"))
        wet = to_float(r.get("summer_ac_wet_bulb_c"))
        if dry is not None and wet is not None and wet > dry:
            errors.append("湿球>干球: %s(干 %s / 湿 %s)" % (tag, dry, wet))

        for a, b in (("winter_ac_outdoor_c", "winter_vent_outdoor_c"),
                     ("heating_outdoor_c", "winter_vent_outdoor_c")):
            va, vb = to_float(r.get(a)), to_float(r.get(b))
            if va is not None and vb is not None and va > vb + 0.05:
                errors.append("%s(%s) 高于 %s(%s): %s" % (a, va, b, vb, tag))

    if wet_bulb_gap:
        notes.append("夏季空调湿球温度缺记录 %d 个台站(附录A 条文说明同样声明无记录,"
                     "界面按未填处理、不猜测): %s" % (len(wet_bulb_gap), "、".join(wet_bulb_gap)))
    others = {k: v for k, v in optional_missing.items() if k != "summer_ac_wet_bulb_c"}
    if others:
        notes.append("装饰列缺失统计(不影响计算): " +
                     "、".join("%s×%d" % (k, v) for k, v in sorted(others.items())))

    for prov, cnt in sorted(meta["totals"].items()):
        d = meta["declared"].get(prov)
        if d is None:
            notes.append("省台站数无声明可供核对: %s(解析出 %d)" % (prov, cnt))
        elif d != cnt:
            errors.append("省台站数不符: %s 声明 %d,解析出 %d" % (prov, d, cnt))

    return errors, notes


# --------------------------------------------------------------------------- 输出

def csv_cell(v):
    s = "" if v is None else str(v)
    if any(ch in s for ch in ',"\n'):
        return '"' + s.replace('"', '""') + '"'
    return s


def write_csv(path, records, src_path, src_bytes):
    sha = hashlib.sha256(src_bytes).hexdigest()
    lines = [
        "# HVACIDA 气象数据库(GB 50736-2012 附录A 表A 室外空气计算参数)",
        "# 源文件: %s" % os.path.basename(src_path),
        "# 源文件 SHA256: %s" % sha,
        "# 源文件字节数: %d" % len(src_bytes),
        "# 台站数: %d" % len(records),
        "# 重新生成: python tools/weatherdb/build_weather_db.py",
        "# 本文件是生成物,不要手改;改数据请改源文件后重跑生成器",
        "# (门禁 tools/weatherdb/check-weather-db-sync.ps1 会校验源文件 SHA256 与台站数)。",
        ",".join(COLUMNS),
    ]
    for r in records:
        lines.append(",".join(csv_cell(r.get(c, "")) for c in COLUMNS))
    with io.open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    return sha


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--source", default=DEFAULT_SOURCE)
    ap.add_argument("--out", default=DEFAULT_OUT)
    ap.add_argument("--report", default=os.path.join(REPO_ROOT, "tools", "weatherdb", "weather-db-report.txt"))
    ap.add_argument("--check", action="store_true", help="只校验,不写文件")
    args = ap.parse_args()

    src_bytes = open(args.source, "rb").read()
    src_text = src_bytes.decode("utf-8")

    records, warnings, meta = build_records(src_text)
    errors, notes = validate(records, warnings, meta)

    provinces = sorted({r["province"] for r in records if r["province"]})
    with io.open(args.report, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join([
            "源文件: %s" % args.source,
            "表格数: %d,切块后: %d,带标签块: %d" % (meta["tables"], meta["chunks"], meta["labelled_chunks"]),
            "标签块内市名行号: %d" % meta["city_row_in_labelled"],
            "相对市名行的行距: %s" % json.dumps(meta["deltas"], ensure_ascii=False, sort_keys=True),
            "行距不一致的块(应为空): %s" % json.dumps(meta["delta_inconsistent"], ensure_ascii=False),
            "解析出台站数: %d" % len(records),
            "省级行政区数: %d: %s" % (len(provinces), "、".join(provinces)),
            "",
            "%-16s %6s %8s" % ("省级行政区", "声明", "解析"),
        ] + ["%-16s %6s %8d" % (p, meta["declared"].get(p, "-"), meta["totals"].get(p, 0))
             for p in sorted(set(list(meta["totals"].keys()) + list(meta["declared"].keys())))]
          + ["", "错误(%d):" % len(errors)]
          + ["  " + p for p in errors]
          + ["", "提示(%d,不影响构建):" % len(notes)]
          + ["  " + p for p in notes]))

    print("tables=%d chunks=%d records=%d provinces=%d errors=%d notes=%d"
          % (meta["tables"], meta["chunks"], len(records), len(provinces), len(errors), len(notes)))
    for p in errors[:25]:
        print("  E " + p)
    if len(errors) > 25:
        print("  ... 另有 %d 条错误,详见 report" % (len(errors) - 25))
    for p in notes[:10]:
        print("  - " + p)
    if len(notes) > 10:
        print("  ... 另有 %d 条提示,详见 report" % (len(notes) - 10))

    if not args.check:
        sha = write_csv(args.out, records, args.source, src_bytes)
        print("written: %s (source sha256=%s)" % (args.out, sha[:16]))

    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
