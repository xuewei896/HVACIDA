using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HVACIDA.Core.Models;
using HVACIDA.Core.Utils;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// 计算结果<strong>表格模型</strong>:指标行(名称 / 数值 / 单位 / 公式文档单元格代号)按分区组织。
    /// <para>
    /// 存在的意义:**界面表格与导出计算书用同一份结构** —— 一份定义两处渲染,
    /// 不会出现"窗口改了、计算书没改"或两边数字口径不一致。文本计算书由 <see cref="ToText"/> 生成。
    /// </para>
    /// <para>
    /// 适用"一个指标一行"的结果(负荷 / 风量 / 选型 / 焓湿参数)。排烟那种"一个区域一行、多列数值"的表
    /// 由 <see cref="LargeSmokeResult"/> 自己表达(见 <see cref="ResultFormatter.FormatLargeSmoke"/>)。
    /// </para>
    /// </summary>
    public class ResultTable
    {
        /// <summary>表标题。</summary>
        public string Title { get; set; } = "";

        /// <summary>分区(客流 / 冷负荷 / 风量 / 设备选型…)。</summary>
        public List<ResultSection> Sections { get; } = new List<ResultSection>();

        /// <summary>表尾口径与校验说明(界面按提示条显示,计算书写在末尾)。</summary>
        public string Note { get; set; } = "";

        /// <summary>加一个分区并返回它,便于链式构建。</summary>
        public ResultSection Section(string title)
        {
            var section = new ResultSection { Title = title };
            Sections.Add(section);
            return section;
        }

        /// <summary>
        /// 渲染为等宽文本(供计算书导出;列宽按显示宽度补齐,CJK 记 2 列)。
        /// <para>
        /// <paramref name="includeCellCodes"/> 默认 <c>false</c> —— **交付件与插件界面都不体现公式文档单元格编号**
        /// (2026-09-15 评审要求);开发自检需要与 Excel 逐格核对时可传 <c>true</c>。
        /// </para>
        /// </summary>
        public string ToText(bool includeCellCodes = false)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(Title)) sb.AppendLine("【" + Title + "】");
            foreach (var section in Sections)
            {
                sb.AppendLine("—— " + section.Title + " ——");
                foreach (var row in section.Rows)
                {
                    sb.Append("  ").Append(Pad(row.Label, 26));
                    sb.Append(Pad(row.Display, 16));
                    if (!string.IsNullOrEmpty(row.Unit)) sb.Append(Pad(row.Unit, 8));
                    if (includeCellCodes && !string.IsNullOrEmpty(row.Cell)) sb.Append(row.Cell);
                    sb.AppendLine();
                }
            }
            if (!string.IsNullOrEmpty(Note))
            {
                sb.AppendLine();
                sb.AppendLine("注:" + Note);
            }
            return sb.ToString();
        }

        private static string Pad(string text, int width)
        {
            text = text ?? "";
            int w = DisplayWidth(text);
            return w >= width ? text + " " : text + new string(' ', width - w);
        }

        private static int DisplayWidth(string text)
        {
            int w = 0;
            foreach (char c in text) w += c > 0x2E80 ? 2 : 1;
            return w;
        }

        // ================================================================== 大系统负荷

        /// <summary>大系统负荷计算结果表(需求 2.2.3.1;分区与公式文档一致,带单元格代号便于逐格核对)。</summary>
        public static ResultTable ForLargeSystem(LargeSystemInput x, LargeSystemResult r)
        {
            var t = new ResultTable
            {
                Title = "大系统负荷计算结果",
                Note = "口径与《大系统负荷计算公式.docx》一致,已通过北京站算例《-示例.xls》30 项逐格核对(2026-09-04)。" +
                       "总制冷量按**露点焓**取值(公式文档该处引用的一格系笔误,已在决策记录中说明);站厅/站台送风温度一致。"
            };

            var flow = t.Section("一、高峰客流(个/min)");
            flow.Add("站厅公共区高峰客流", "C39", r.HallPeakFlowPm, "个/min");
            flow.Add("站台公共区高峰客流", "C40", r.PlatformPeakFlowPm, "个/min");
            flow.Add("站厅上车高峰客流", "C35", r.HallBoardingFlowPm, "个/min");
            flow.Add("站厅下车高峰客流", "C36", r.HallAlightingFlowPm, "个/min");
            flow.Add("站台上车高峰客流", "F35", r.PlatformBoardingFlowPm, "个/min");
            flow.Add("站台下车高峰客流", "F36", r.PlatformAlightingFlowPm, "个/min");

            var hall = t.Section("二、站厅冷负荷(kW)");
            hall.Add("乘客显热", "D95", r.HallSensibleKw, "kW");
            hall.Add("乘客潜热", "D96", r.HallLatentKw, "kW");
            hall.Add("照明", "D97", r.HallLightingKw, "kW");
            hall.Add("广告灯箱", "D98", r.HallAdvertKw, "kW");
            hall.Add("自动扶梯", "D99", r.HallEscalatorKw, "kW");
            hall.Add("垂直电梯", "D100", r.HallElevatorKw, "kW");
            hall.Add("AFC 设备", "D101", r.HallAfcKw, "kW");
            hall.Add("出入口渗透", "D102", r.HallEntranceInfiltrationKw, "kW");
            hall.Add("屏蔽门传热(输入)", "D103", r.HallPsdTransferKw, "kW");
            hall.Add("屏蔽门漏风", "D104", r.HallPsdLeakKw, "kW");
            hall.Add("屏蔽门发热(输入)", "D105", r.HallPsdHeatKw, "kW");
            hall.AddTotal("站厅冷负荷合计", "D107", r.HallTotalCoolingKw, "kW");

            var platform = t.Section("三、站台冷负荷(kW)");
            platform.Add("乘客显热", "E95", r.PlatformSensibleKw, "kW");
            platform.Add("乘客潜热", "E96", r.PlatformLatentKw, "kW");
            platform.Add("照明", "E97", r.PlatformLightingKw, "kW");
            platform.Add("广告灯箱", "E98", r.PlatformAdvertKw, "kW");
            platform.Add("自动扶梯", "E99", r.PlatformEscalatorKw, "kW");
            platform.Add("垂直电梯", "E100", r.PlatformElevatorKw, "kW");
            platform.Add("屏蔽门传热", "E103", r.PlatformPsdTransferKw, "kW");
            platform.Add("屏蔽门漏风", "E104", r.PlatformPsdLeakKw, "kW");
            platform.Add("屏蔽门系统发热", "E105", r.PlatformPsdHeatKw, "kW");
            platform.AddTotal("站台冷负荷合计", "E107", r.PlatformTotalCoolingKw, "kW");

            var moisture = t.Section("四、湿负荷");
            moisture.Add("站厅人员散湿", "D108", r.HallPeopleMoisture, "g/s");
            moisture.Add("站厅结构表面积", "B91", r.HallStructureSurfaceM2, "m²");
            moisture.Add("站厅结构散湿", "D109", r.HallStructureMoisture, "g/s");
            moisture.AddTotal("站厅湿负荷合计", "D112", r.HallTotalMoistureGps, "g/s");
            moisture.Add("站台人员散湿", "E108", r.PlatformPeopleMoisture, "g/s");
            moisture.AddTotal("站台湿负荷合计", "E112", r.PlatformTotalMoistureGps, "g/s");

            var ratio = t.Section("五、热湿比与焓湿过程");
            ratio.Add("站厅热湿比", "D113", r.HallHeatHumidityRatio, "kJ/kg");
            ratio.Add("站台热湿比", "E113", r.PlatformHeatHumidityRatio, "kJ/kg");
            ratio.Add("站厅送风温度(两区一致)", "A118", r.HallSupplyTempC, "℃");
            ratio.Add("露点温度", "B118", r.DewPointTempC, "℃");
            ratio.Add("饱和含湿量(露点)", "D118", r.SaturatedMoistureAtDewGkg, "g/kg");
            ratio.Add("露点含湿量", "E118", r.DewPointMoistureGkg, "g/kg");
            ratio.Add("送风点焓", "A121", r.HallSupplyEnthalpy, "kJ/kg");
            ratio.Add("站厅室内含湿量", "B121", r.HallIndoorHumidityGkg, "g/kg");
            ratio.Add("站厅室内焓", "C121", r.HallIndoorEnthalpy, "kJ/kg");
            ratio.Add("站台室内含湿量", "D121", r.PlatformIndoorHumidityGkg, "g/kg");
            ratio.Add("站台室内焓", "E121", r.PlatformIndoorEnthalpy, "kJ/kg");
            ratio.Add("回风混合焓", "C145", r.ReturnMixEnthalpy, "kJ/kg");
            ratio.Add("新风饱和含湿量", "D149", r.FreshSaturatedMoistureGkg, "g/kg");
            ratio.Add("新风焓", "C149", r.FreshEnthalpy, "kJ/kg");
            ratio.Add("新回风混合焓", "C146", r.FreshReturnMixEnthalpy, "kJ/kg");
            ratio.Add("露点焓(制冷量取此值)", "C143", r.DewPointEnthalpy, "kJ/kg");

            var air = t.Section("六、风量与制冷量");
            air.Add("站厅送风量", "A125", r.HallSupplyFlowM3H, "m³/h");
            air.Add("站台送风量", "B125", r.PlatformSupplyFlowM3H, "m³/h");
            air.AddTotal("总送风量", "C125", r.TotalSupplyFlowM3H, "m³/h");
            air.Add("公共区人数", "A132", r.PublicAreaPeople, "人", 0);
            air.Add("实际新风量", "A136", r.ActualFreshAirM3H, "m³/h");
            air.Add("新风比例", "B136", r.FreshAirRatio, "—", 2);
            air.Add("站厅回风量", "C136", r.HallReturnFlowM3H, "m³/h");
            air.Add("站台回风量", "D136", r.PlatformReturnFlowM3H, "m³/h");
            air.AddTotal("总回风量", "E136", r.TotalReturnFlowM3H, "m³/h");
            air.AddTotal("总制冷量", "E159", r.TotalCoolingKw, "kW");

            var units = t.Section("七、设备选型(单台;需求指定 4 项)");
            units.Add("组合式空调机组送风量(总送风量的一半)", "A165", r.UnitSupplyFlowM3H, "m³/h");
            units.Add("组合式空调机组制冷量(总制冷量的一半)", "B165", r.UnitCoolingKw, "kW");
            units.Add("回排风机回风量(总回风量的一半)", "C178", r.UnitReturnFlowM3H, "m³/h");
            units.Add("排烟风机排烟风量(两区排烟量大者的一半)", "E178", r.UnitSmokeFlowM3H, "m³/h");
            units.Add("站厅排烟量", "C171", r.HallSmokeFlowM3H, "m³/h");
            units.Add("站台排烟量", "D171", r.PlatformSmokeFlowM3H, "m³/h");

            return t;
        }

        // ================================================================== 小系统

        /// <summary>
        /// 小系统系统级结果表(需求 2.2.3.2)。分区按系统类型取舍:
        /// 合计 → 风量与冷量 → 状态点(空调类)→ 加压送风(加压类)→ 设备选型。
        /// 逐房间明细另见 <see cref="SmallRoomTable"/>(列与顺序按系统类型)。
        /// </summary>
        public static ResultTable ForSmallSystem(SmallSystemInput x, SmallSystemResult r)
        {
            var t = new ResultTable
            {
                Title = "小系统计算结果 · " + SystemTypeName(r.SystemType) +
                        (x != null && !string.IsNullOrEmpty(x.SystemCode) ? " · " + x.SystemCode : ""),
                Note = r.Note
            };

            var totals = t.Section("一、合计");
            totals.Add("房间/分区数", "", r.Rooms.Count, "个", 0);
            totals.Add("总面积", "", r.TotalAreaM2, "m²", 2);
            if (r.TotalCoolingKw > 0)
            {
                totals.AddTotal("冷负荷合计", "", r.TotalCoolingKw, "kW", 2);
            }
            if (r.TotalMoistureGps > 0)
            {
                totals.AddTotal("湿负荷合计", "", r.TotalMoistureGps, "g/s", 5);
            }
            if (r.TotalCoolingKw > 0 && r.TotalMoistureGps > 0 && !double.IsInfinity(r.HeatHumidityRatio) && !double.IsNaN(r.HeatHumidityRatio))
            {
                totals.Add("热湿比", "", r.HeatHumidityRatio, "kJ/kg", 0);
            }

            var air = t.Section("二、风量与冷量");
            if (r.TotalSupplyM3H > 0) air.Add("总送风量", "", r.TotalSupplyM3H, "m³/h", 0);
            if (r.TotalReturnM3H > 0) air.Add("总回风量", "", r.TotalReturnM3H, "m³/h", 0);
            if (r.TotalFreshAirM3H > 0) air.Add("人员新风量合计", "", r.TotalFreshAirM3H, "m³/h", 0);
            if (r.TotalSystemFreshAirM3H > 0) air.Add("10% 系统新风量合计", "", r.TotalSystemFreshAirM3H, "m³/h", 0);
            if (r.DesignFreshAirM3H > 0) air.Add("取用新风量(取大)", "", r.DesignFreshAirM3H, "m³/h", 0);
            if (r.FreshAirRatio > 0) air.Add("新风比", "", r.FreshAirRatio, "—", 3);
            if (r.TotalUnitCoolingKw > 0) air.AddTotal("空调器/多联机冷量合计", "", r.TotalUnitCoolingKw, "kW", 2);
            if (r.TotalExhaustM3H > 0) air.AddTotal("计算排风量合计", "", r.TotalExhaustM3H, "m³/h", 0);
            if (r.TotalSmokeM3H > 0) air.AddTotal("计算排烟量合计", "", r.TotalSmokeM3H, "m³/h", 0);
            if (r.TotalMakeupAirM3H > 0) air.AddTotal("计算补风量合计", "", r.TotalMakeupAirM3H, "m³/h", 0);
            if (r.TotalSupplyM3H <= 0 && r.TotalExhaustM3H <= 0 && r.TotalSmokeM3H <= 0)
            {
                air.AddText("风量", "本系统类型不涉及风量计算(见下方加压送风)");
            }

            if (r.SupplyEnthalpy > 0 || r.IndoorEnthalpy > 0)
            {
                var points = t.Section("三、状态点(焓湿过程)");
                if (r.SupplyTempC > 0) points.Add("送风温度", "", r.SupplyTempC, "℃", 2);
                if (r.DewPointTempC > 0) points.Add("露点温度", "", r.DewPointTempC, "℃", 2);
                if (r.DewPointHumidityGkg > 0) points.Add("露点含湿量", "", r.DewPointHumidityGkg, "g/kg", 3);
                if (r.SupplyEnthalpy > 0) points.Add("送风点焓", "", r.SupplyEnthalpy, "kJ/kg", 2);
                if (r.IndoorHumidityGkg > 0) points.Add("室内含湿量", "", r.IndoorHumidityGkg, "g/kg", 3);
                if (r.IndoorEnthalpy > 0) points.Add("室内状态点焓", "", r.IndoorEnthalpy, "kJ/kg", 2);
                if (r.DewPointEnthalpy > 0) points.Add("露点焓", "", r.DewPointEnthalpy, "kJ/kg", 2);
                if (r.FreshEnthalpy > 0) points.Add("新风状态点焓", "", r.FreshEnthalpy, "kJ/kg", 2);
                if (r.MixEnthalpy > 0) points.Add("新回风点焓", "", r.MixEnthalpy, "kJ/kg", 2);
            }

            if (r.PressurizationFlowM3H > 0)
            {
                var press = t.Section("三、加压送风(楼梯间)");
                press.Add("一层内可开启门面积", "", r.DoorAreaM2, "m²", 3);
                press.Add("门开启风量(L1)", "", r.DoorOpenFlowM3H, "m³/h", 0);
                press.Add("门缝漏风量(L2)", "", r.DoorLeakFlowM3H, "m³/h", 0);
                press.Add("余压阀漏风量(L3)", "", r.ReliefValveLeakFlowM3H, "m³/h", 0);
                press.AddTotal("楼梯间加压送风量", "", r.PressurizationFlowM3H, "m³/h", 0);
            }

            if (r.Equipments.Count > 0)
            {
                var units = t.Section("四、设备选型(选型系数见各行)");
                foreach (var e in r.Equipments)
                {
                    string label = e.Code + " " + e.Name + "(×" + Num(e.Factor) + ")";
                    units.Add(label + " 风量", "", e.FlowM3H, "m³/h", 0);
                    if (e.HasCooling)
                    {
                        units.Add(label + " 冷量", "", e.CoolingKw, "kW", 2);
                    }
                }
            }

            return t;
        }

        /// <summary>
        /// 小系统**全站汇总**结果表(需求 2.2.3.2「小系统计算结果」):逐系统一行 + 全站合计。
        /// 逐系统明细由界面表格呈现(列见 <see cref="SmallRoomTable.SummaryColumns"/>),这里给合计指标。
        /// </summary>
        public static ResultTable ForSmallSystemSummary(SmallSystemSummary s)
        {
            var t = new ResultTable
            {
                Title = "小系统全站汇总",
                Note = s == null ? "" : s.Note
            };
            if (s == null || s.Rows.Count == 0) return t;

            var totals = t.Section("一、全站合计");
            totals.Add("小系统套数", "", s.SystemCount, "套", 0);
            totals.Add("房间/分区数", "", s.RoomCount, "个", 0);
            totals.Add("设备台数", "", s.EquipmentCount, "台", 0);
            totals.Add("总面积", "", s.TotalAreaM2, "m²", 2);
            if (s.TotalCoolingKw > 0) totals.AddTotal("冷负荷合计", "", s.TotalCoolingKw, "kW", 2);
            if (s.TotalUnitCoolingKw > 0) totals.AddTotal("设备冷量合计(空调器/多联机)", "", s.TotalUnitCoolingKw, "kW", 2);

            var air = t.Section("二、全站风量合计");
            if (s.TotalSupplyM3H > 0) air.Add("总送风量", "", s.TotalSupplyM3H, "m³/h", 0);
            if (s.TotalReturnM3H > 0) air.Add("总回风量", "", s.TotalReturnM3H, "m³/h", 0);
            if (s.TotalFreshAirM3H > 0) air.Add("新风量合计", "", s.TotalFreshAirM3H, "m³/h", 0);
            if (s.TotalExhaustM3H > 0) air.Add("排风量合计", "", s.TotalExhaustM3H, "m³/h", 0);
            if (s.TotalSmokeM3H > 0) air.Add("排烟量合计", "", s.TotalSmokeM3H, "m³/h", 0);
            if (s.TotalMakeupAirM3H > 0) air.Add("补风量合计", "", s.TotalMakeupAirM3H, "m³/h", 0);

            var byType = t.Section("三、逐系统(点界面表格可看该系统的计算书)");
            foreach (var row in s.Rows)
            {
                string label = row.TypeName + (string.IsNullOrEmpty(row.SystemCode) ? "" : " " + row.SystemCode);
                byType.Add(label + " · 房间数", "", row.RoomCount, "个", 0);
                if (row.TotalCoolingKw > 0) byType.Add(label + " · 冷负荷", "", row.TotalCoolingKw, "kW", 2);
                if (row.TotalSupplyM3H > 0) byType.Add(label + " · 送风量", "", row.TotalSupplyM3H, "m³/h", 0);
                if (row.TotalExhaustM3H > 0) byType.Add(label + " · 排风量", "", row.TotalExhaustM3H, "m³/h", 0);
                if (row.TotalSmokeM3H > 0) byType.Add(label + " · 排烟量", "", row.TotalSmokeM3H, "m³/h", 0);
            }
            return t;
        }

        /// <summary>系统类型中文名(界面与计算书统一用词)。</summary>
        public static string SystemTypeName(SmallSystemType type)
        {
            switch (type)
            {
                case SmallSystemType.AllAirOnceReturn: return "全空气一次回风系统";
                case SmallSystemType.VrfWithFreshAir: return "多联机+新风系统";
                case SmallSystemType.ExhaustVentilation: return "排风系统";
                case SmallSystemType.SmokeExhaust: return "排烟系统";
                case SmallSystemType.SupplyExhaustSmoke: return "送风排风排烟系统";
                case SmallSystemType.PressurizationSupply: return "加压送风系统";
                default: return type.ToString();
            }
        }

        // ================================================================== 大系统排烟

        /// <summary>大系统排烟计算结果表(每个区域一行;与 <see cref="LargeSmokeResult.Zones"/> 同源)。</summary>
        public static ResultTable ForLargeSmoke(LargeSmokeInput x, LargeSmokeResult r)
        {
            var t = new ResultTable
            {
                Title = "大系统排烟计算结果",
                Note = r.Note + " " + r.PendingNote
            };

            var zones = t.Section("一、分区排烟量");
            foreach (var z in r.Zones)
            {
                zones.Add(z.ZoneName + " 计算排烟量", "面积 × " + Num(x.SmokeRateM3HPerM2), z.CalculatedFlowM3H, "m³/h");
                zones.Add(z.ZoneName + " 选型排烟量", "× " + Num(x.SelectionFactor), z.SelectionFlowM3H, "m³/h");
                zones.Add(z.ZoneName + " 单台风机风量", "选型 ÷ " + Num(x.FanUnitCount), z.UnitFlowM3H, "m³/h");
            }

            var fans = t.Section("二、排烟风机选型");
            fans.AddTotal("风机选型基准区", "", r.GoverningCalculatedFlowM3H, "m³/h");
            fans.AddText("基准区名称", r.GoverningZoneName);
            fans.Add("排烟风机台数", "", r.FanUnitCount, "台", 0);
            fans.AddTotal("单台选型风量(基准区选型 ÷ 台数)", "", r.UnitSelectionFlowM3H, "m³/h");
            fans.Add("参考:公式文档口径(不含选型系数)", "E178", r.UnitFlowPerFormulaDocM3H, "m³/h");

            return t;
        }

        private static string Num(double v)
        {
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>结果表的一个分区。</summary>
    public class ResultSection
    {
        public string Title { get; set; } = "";

        public List<ResultRow> Rows { get; } = new List<ResultRow>();

        /// <summary>加一行数值。</summary>
        public ResultRow Add(string label, string cell, double value, string unit, int decimals = 1)
        {
            var row = new ResultRow
            {
                Label = label,
                Cell = cell ?? "",
                Value = value,
                Unit = unit ?? "",
                Decimals = decimals
            };
            Rows.Add(row);
            return row;
        }

        /// <summary>加一行小计/合计(界面加粗)。</summary>
        public ResultRow AddTotal(string label, string cell, double value, string unit, int decimals = 1)
        {
            var row = Add(label, cell, value, unit, decimals);
            row.IsTotal = true;
            return row;
        }

        /// <summary>加一行纯文本(无数值)。</summary>
        public ResultRow AddText(string label, string text)
        {
            var row = new ResultRow { Label = label, Text = text ?? "" };
            Rows.Add(row);
            return row;
        }
    }

    /// <summary>结果表的一行。</summary>
    public class ResultRow
    {
        public string Label { get; set; } = "";

        /// <summary>公式文档单元格代号(可为空;界面作行尾灰字列)。</summary>
        public string Cell { get; set; } = "";

        public double? Value { get; set; }

        public string Unit { get; set; } = "";

        /// <summary>纯文本行的内容(与 <see cref="Value"/> 互斥)。</summary>
        public string Text { get; set; } = "";

        /// <summary>小数位(千分位固定)。</summary>
        public int Decimals { get; set; } = 1;

        /// <summary>小计/合计行。</summary>
        public bool IsTotal { get; set; }

        /// <summary>无数值的文本行(界面左对齐、不加粗)。</summary>
        public bool IsText => !Value.HasValue;

        /// <summary>
        /// 界面悬停提示:业务名称 + (可选)对应公式文档单元格 —— 编号只在悬停时出现,不占正文。
        /// </summary>
        public string Hint => string.IsNullOrEmpty(Cell)
            ? Label
            : Label + "   ·   对应《大系统负荷计算公式》单元格 " + Cell;

        /// <summary>数值列显示内容(数值千分位;文本行显示文本)。</summary>
        public string Display => Value.HasValue
            ? Value.Value.ToString("N" + Decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)
            : Text;
    }
}
