using System;
using System.Collections.Generic;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// **大系统负荷 + 排烟**计算书的 Excel(.xlsx)导出(需求 2.2.3.1 / 2.2.4)。
    /// <para>
    /// 工作簿 4 页:负荷汇总(7 个分区 66 行,直接渲染 <see cref="ResultTable.ForLargeSystem"/>)·
    /// 排烟分区(逐区域宽表)· 排烟选型(<see cref="ResultTable.ForLargeSmoke"/>)· 口径与待补。
    /// 汇总页与界面表格、文本计算书**同源**。
    /// </para>
    /// </summary>
    public static class LargeSystemExcelExporter
    {
        /// <summary>大系统负荷计算书工作簿。</summary>
        public static XlsxWorkbook BuildLoad(LargeSystemInput input, LargeSystemResult result)
        {
            var workbook = new XlsxWorkbook();
            if (result == null) return workbook;

            ExcelReportBuilder.AddResultTable(workbook.AddSheet("负荷汇总"), ResultTable.ForLargeSystem(input, result));
            ExcelReportBuilder.AddNoteSheet(workbook, "大系统负荷计算书",
                "口径与《大系统负荷计算公式.docx》一致;界面表格、文本计算书与本 Excel 均由同一份 ResultTable 渲染。",
                "");
            return workbook;
        }

        /// <summary>大系统排烟计算书工作簿(分区宽表 + 选型 + 口径)。</summary>
        public static XlsxWorkbook BuildSmoke(LargeSmokeInput input, LargeSmokeResult result)
        {
            var workbook = new XlsxWorkbook();
            if (result == null) return workbook;

            var zones = workbook.AddSheet("排烟分区");
            var headers = new List<string> { "区域", "面积 m²", "计算排烟量 m³/h", "选型排烟量 m³/h", "单台风机风量 m³/h", "选型基准区" };
            var rows = new List<object[]>();
            foreach (var zone in result.Zones)
            {
                rows.Add(new object[]
                {
                    zone.ZoneName, zone.AreaM2, zone.CalculatedFlowM3H, zone.SelectionFlowM3H, zone.UnitFlowM3H,
                    zone.IsGoverning ? "★ 基准区" : ""
                });
            }
            ExcelReportBuilder.AddTable(zones, headers, rows);
            zones.AddBlankRow();
            zones.AddRow("风机选型基准区", result.GoverningZoneName);
            zones.AddRow("排烟风机台数", result.FanUnitCount);
            zones.AddRow("单台选型风量 m³/h", result.UnitSelectionFlowM3H);
            zones.AddRow("参考(公式文档口径 MAX/2,不含系数)", result.UnitFlowPerFormulaDocM3H);

            ExcelReportBuilder.AddResultTable(workbook.AddSheet("排烟选型"), ResultTable.ForLargeSmoke(input, result));
            ExcelReportBuilder.AddNoteSheet(workbook, "大系统排烟计算书", result.Note, result.PendingNote);
            return workbook;
        }

        /// <summary>
        /// 负荷 + 排烟合并成一份**完整计算书**工作簿(「大系统 → 计算结果」窗【导出计算书】用)。
        /// <para>
        /// 6 页,含"所有数据":① 计算参数与选型参数(与界面同源的小结)② 输入参数(全部输入)
        /// ③ 负荷汇总(7 分区 66 行)④ 排烟分区(逐区域宽表)⑤ 排烟选型 ⑥ 口径与待补。
        /// </para>
        /// </summary>
        public static XlsxWorkbook BuildLoadAndSmoke(LargeSystemInput input, LargeSystemResult load,
            LargeSmokeInput smokeInput, LargeSmokeResult smoke)
        {
            var workbook = new XlsxWorkbook();

            // ① 计算参数 / 选型参数(界面两段小结,与表格同源)
            ExcelReportBuilder.AddResultTable(workbook.AddSheet("计算参数与选型"),
                ResultTable.ForLargeSystemSummary(load, smoke));

            // ② 输入参数(全部输入,便于复算)
            ExcelReportBuilder.AddResultTable(workbook.AddSheet("输入参数"),
                ResultTable.ForLargeSystemInput(input));

            // ③ 负荷汇总(7 个分区)
            ExcelReportBuilder.AddResultTable(workbook.AddSheet("负荷汇总"),
                ResultTable.ForLargeSystem(input, load));

            // ④⑤ 排烟
            if (smoke != null)
            {
                var zones = workbook.AddSheet("排烟分区");
                var rows = new List<object[]>();
                foreach (var zone in smoke.Zones)
                {
                    rows.Add(new object[]
                    {
                        zone.ZoneName, zone.AreaM2, zone.CalculatedFlowM3H, zone.SelectionFlowM3H, zone.UnitFlowM3H,
                        zone.IsGoverning ? "★ 基准区" : ""
                    });
                }
                ExcelReportBuilder.AddTable(zones,
                    new List<string> { "区域", "面积 m²", "计算排烟量 m³/h", "选型排烟量 m³/h", "单台风机风量 m³/h", "选型基准区" },
                    rows);
                zones.AddBlankRow();
                zones.AddBoldRow("风机选型基准区", smoke.GoverningZoneName);
                zones.AddBoldRow("单台选型风量 m³/h", smoke.UnitSelectionFlowM3H);
                ExcelReportBuilder.AddResultTable(workbook.AddSheet("排烟选型"), ResultTable.ForLargeSmoke(smokeInput, smoke));
            }

            // ⑥ 口径与待补(把两边的口径合在一页,不看界面也知道数是怎么来的)
            ExcelReportBuilder.AddNoteSheet(workbook, "大系统计算书(负荷 + 排烟)",
                "计算参数 / 选型参数、负荷汇总、排烟分区与选型、输入参数全部同源于 Core 的计算结果;" +
                "界面表格、文本计算书与本 Excel 由同一份 ResultTable / LargeSystemResult 渲染。",
                smoke == null ? "" : smoke.PendingNote);
            return workbook;
        }
    }

    /// <summary>
    /// **小系统**计算书的 Excel(.xlsx)导出(需求 2.2.3.2):
    /// 单系统 4 页(系统结果 · 房间明细 · 设备选型 · 口径与待补),
    /// 全站汇总(逐系统一行 · 全站合计 · 每套系统的房间与设备明细页 · 口径)。
    /// </summary>
    public static class SmallSystemExcelExporter
    {
        /// <summary>全站汇总里最多附几套系统的明细页(避免工作簿过大)。</summary>
        public const int MaxDetailSystems = 12;

        /// <summary>单系统小系统计算书工作簿。</summary>
        public static XlsxWorkbook BuildSystem(SmallSystemInput input, SmallSystemResult result)
        {
            var workbook = new XlsxWorkbook();
            if (result == null) return workbook;

            ExcelReportBuilder.AddResultTable(workbook.AddSheet("系统结果"), ResultTable.ForSmallSystem(input, result));

            var rooms = workbook.AddSheet("房间明细");
            AddRoomTable(rooms, result);

            var equipments = workbook.AddSheet("设备选型");
            AddEquipmentTable(equipments, result);

            ExcelReportBuilder.AddNoteSheet(workbook, "小系统计算书(" + ResultTable.SystemTypeName(result.SystemType) + ")",
                result.Note, result.PendingNote);
            return workbook;
        }

        /// <summary>小系统**全站汇总**工作簿。</summary>
        public static XlsxWorkbook BuildSummary(SmallSystemSummary summary)
        {
            var workbook = new XlsxWorkbook();
            if (summary == null) return workbook;

            // 1) 逐系统一行(与界面汇总表同列定义)
            var rowsSheet = workbook.AddSheet("逐系统");
            var columns = SmallRoomTable.SummaryColumns();
            var headers = new List<string>();
            foreach (var column in columns) headers.Add(column.Header);
            var rows = new List<object[]>();
            foreach (var row in summary.Rows)
            {
                var values = new List<object>();
                foreach (var column in columns) values.Add(SmallRoomTable.ValueOf(row, column));
                rows.Add(values.ToArray());
            }
            ExcelReportBuilder.AddTable(rowsSheet, headers, rows);

            // 2) 全站合计(渲染同一份 ResultTable)
            ExcelReportBuilder.AddResultTable(workbook.AddSheet("全站合计"), ResultTable.ForSmallSystemSummary(summary));

            // 3) 每套系统的房间明细 / 设备选型(超过上限只出前几套并注明)
            int detail = 0;
            foreach (var row in summary.Rows)
            {
                if (detail >= MaxDetailSystems) break;
                detail++;
                string label = (string.IsNullOrEmpty(row.SystemCode) ? row.TypeName : row.SystemCode);
                var rooms = workbook.AddSheet("房-" + label);
                AddRoomTable(rooms, row.Result);
                var equipments = workbook.AddSheet("设-" + label);
                AddEquipmentTable(equipments, row.Result);
            }
            if (summary.Rows.Count > MaxDetailSystems)
            {
                var note = workbook.AddSheet("说明");
                note.AddRow("明细页数量上限",
                    "全站共 " + summary.Rows.Count + " 套系统,本工作簿只附了前 " + MaxDetailSystems +
                    " 套的房间与设备明细(避免文件过大);其余系统请在各自的小系统窗点【导出 Excel】。");
            }

            ExcelReportBuilder.AddNoteSheet(workbook, "小系统全站汇总计算书", summary.Note, "");
            return workbook;
        }

        private static void AddRoomTable(XlsxSheet sheet, SmallSystemResult result)
        {
            if (result == null) return;
            var columns = SmallRoomTable.ColumnsFor(result.SystemType);
            var headers = new List<string>();
            foreach (var column in columns) headers.Add(column.Header);
            var rows = new List<object[]>();
            foreach (var room in result.Rooms)
            {
                var values = new List<object>();
                foreach (var column in columns) values.Add(SmallRoomTable.ValueOf(room, column));
                rows.Add(values.ToArray());
            }
            ExcelReportBuilder.AddTable(sheet, headers, rows);
            if (rows.Count == 0) sheet.AddRow("(该系统没有房间 / 分区行)");
        }

        private static void AddEquipmentTable(XlsxSheet sheet, SmallSystemResult result)
        {
            if (result == null) return;
            ExcelReportBuilder.AddTable(sheet,
                new List<string> { "系统代码", "设备", "选型系数", "风量 m³/h", "冷量 kW" },
                BuildEquipmentRows(result));
        }

        private static List<object[]> BuildEquipmentRows(SmallSystemResult result)
        {
            var rows = new List<object[]>();
            foreach (var equipment in result.Equipments)
            {
                rows.Add(new object[]
                {
                    equipment.Code, equipment.Name, equipment.Factor, equipment.FlowM3H,
                    equipment.HasCooling ? (object)equipment.CoolingKw : "—"
                });
            }
            return rows;
        }
    }
}
