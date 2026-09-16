using System;
using System.Collections.Generic;

namespace HVACIDA.Core.Models
{
    /// <summary>图纸上放置的视图(图框内视图清单的一行)。</summary>
    public class SheetViewItem
    {
        /// <summary>视图名。</summary>
        public string ViewName { get; set; } = "";

        /// <summary>视图类型(平面 / 剖面 / 三维 / 明细表…)。</summary>
        public string ViewType { get; set; } = "";

        /// <summary>视图比例(1:100 等;读不到给 0)。</summary>
        public double Scale { get; set; }
    }

    /// <summary>一张图纸(需求 2.6 图纸清单的一行)。</summary>
    public class SheetItem
    {
        /// <summary>图纸编号。</summary>
        public string SheetNumber { get; set; } = "";

        /// <summary>图纸名称。</summary>
        public string SheetName { get; set; } = "";

        /// <summary>图框族名。</summary>
        public string TitleBlockFamily { get; set; } = "";

        /// <summary>图框类型名(如 A1 / A2 标准图框)。</summary>
        public string TitleBlockType { get; set; } = "";

        /// <summary>图幅宽 mm(取图框外框,读不到给 0)。</summary>
        public double WidthMm { get; set; }

        /// <summary>图幅高 mm。</summary>
        public double HeightMm { get; set; }

        /// <summary>图幅文字(如「841 × 594 mm」;读不到给「—」)。</summary>
        public string SizeText => WidthMm > 0 && HeightMm > 0
            ? WidthMm.ToString("0") + " × " + HeightMm.ToString("0") + " mm"
            : "—";

        /// <summary>图框上放置的视图。</summary>
        public List<SheetViewItem> Views { get; set; } = new List<SheetViewItem>();

        /// <summary>视图数。</summary>
        public int ViewCount => Views.Count;

        /// <summary>是否空图框(未放置任何视图)。</summary>
        public bool IsEmpty => Views.Count == 0;

        /// <summary>Revit 图纸元素 Id(0 = 非模型行)。</summary>
        public int ElementId { get; set; }

        /// <summary>备注(读数口径 / 待补)。</summary>
        public string Note { get; set; } = "";
    }

    /// <summary>图框类型小计。</summary>
    public class TitleBlockTotal
    {
        /// <summary>图框族。</summary>
        public string FamilyName { get; set; } = "";

        /// <summary>图框类型。</summary>
        public string TypeName { get; set; } = "";

        /// <summary>图幅文字。</summary>
        public string SizeText { get; set; } = "";

        /// <summary>该图框类型的图纸张数。</summary>
        public int SheetCount { get; set; }

        /// <summary>该图框类型下已放置的视图数。</summary>
        public int ViewCount { get; set; }
    }

    /// <summary>批量出图的一条记录(哪张图、什么格式、是否成功)。</summary>
    public class SheetExportRecord
    {
        /// <summary>图纸编号。</summary>
        public string SheetNumber { get; set; } = "";

        /// <summary>图纸名称。</summary>
        public string SheetName { get; set; } = "";

        /// <summary>导出格式(DWG / DXF / PDF)。</summary>
        public string Format { get; set; } = "";

        /// <summary>输出路径(成功时)。</summary>
        public string OutputPath { get; set; } = "";

        /// <summary>是否成功。</summary>
        public bool Succeeded { get; set; }

        /// <summary>结果说明(失败原因写清)。</summary>
        public string Message { get; set; } = "";
    }

    /// <summary>
    /// **图纸清单与批量出图**结果(需求 2.6):图纸清单 + 图框统计 + 本次导出记录 + 口径。
    /// 纯 Core(不碰 Revit),可被自检直接断言;界面表格、计算书与 Excel 都由它渲染。
    /// </summary>
    public class SheetCatalogResult
    {
        /// <summary>图纸清单(按图纸编号排序)。</summary>
        public List<SheetItem> Sheets { get; set; } = new List<SheetItem>();

        /// <summary>图框类型小计。</summary>
        public List<TitleBlockTotal> TitleBlocks { get; set; } = new List<TitleBlockTotal>();

        /// <summary>本次批量出图的记录(未导出时为空)。</summary>
        public List<SheetExportRecord> Exports { get; set; } = new List<SheetExportRecord>();

        /// <summary>图纸总数。</summary>
        public int SheetCount => Sheets.Count;

        /// <summary>空图框数(未放置视图)。</summary>
        public int EmptySheetCount { get; set; }

        /// <summary>视图总数。</summary>
        public int ViewCount { get; set; }

        /// <summary>导出成功数。</summary>
        public int ExportSucceeded { get; set; }

        /// <summary>导出失败数。</summary>
        public int ExportFailed { get; set; }

        /// <summary>输出目录(本次导出用)。</summary>
        public string OutputDirectory { get; set; } = "";

        /// <summary>口径说明。</summary>
        public string Note { get; set; } = "";

        /// <summary>待补 / 局限。</summary>
        public string PendingNote { get; set; } = "";

        /// <summary>数据来源说明。</summary>
        public string SourceNote { get; set; } = "";

        /// <summary>是否有图纸。</summary>
        public bool HasSheets => Sheets.Count > 0;
    }
}
