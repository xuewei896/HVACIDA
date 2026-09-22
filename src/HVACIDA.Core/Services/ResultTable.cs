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

        /// <summary>
        /// 「大系统 → 计算结果」窗的**两段小结**:一、计算参数(8 项)+ 二、选型参数(5 项)。
        /// <para>
        /// 与界面表格、文本计算书、Excel 计算书**同源**(§4.8):界面与 Excel 都渲染这一份,
        /// 不各写一套数字。口径:计算参数取公式文档的**计算量**(C171/D171 为面积×60 的火灾计算排烟量,
        /// 不含选型系数);选型参数的排烟风机取「排烟计算」窗的**单台选型风量**(含选型系数,见 large-smoke.xml),
        /// 未做排烟计算时退回公式文档口径 E178(MAX/2)并如实在单元格代号里标出。
        /// </para>
        /// <para>
        /// 「小新风选型风量」= 公式文档 A136 实际新风量 = max(公共区人数 × 空调季新风量指标, 总送风量 × 10%);
        /// 插件不另乘选型余量(要加余量请给系数,不猜)。
        /// </para>
        /// </summary>
        public static ResultTable ForLargeSystemSummary(LargeSystemResult r, LargeSmokeResult smoke)
        {
            var t = new ResultTable
            {
                Title = "大系统计算结果:计算参数 / 选型参数",
                Note = "计算参数为公式文档计算量(排烟为面积 × 60 m³/(h·m²),不含选型系数);" +
                       "选型参数为单台量(总送风/总制冷/总回风各取一半);排烟风机选型风量取自「排烟计算」窗(含选型系数)。" +
                       "小新风选型风量按公式文档 A136 实际新风量(人数新风与总送风 10% 取大),未另乘余量。"
            };
            if (r == null) return t;

            var calc = t.Section("一、计算参数");
            calc.Add("站厅站台公共区计算总冷负荷", "E159", r.TotalCoolingKw, "kW", 2);
            calc.Add("站厅层公共区空调计算送风量", "A125", r.HallSupplyFlowM3H, "m³/h", 0);
            calc.Add("站台层公共区空调计算送风量", "B125", r.PlatformSupplyFlowM3H, "m³/h", 0);
            calc.Add("车站公共区总空调计算送风量", "C125", r.TotalSupplyFlowM3H, "m³/h", 0);
            calc.Add("空调计算新风量", "A136", r.ActualFreshAirM3H, "m³/h", 0);
            calc.Add("空调计算回排风量", "E136", r.TotalReturnFlowM3H, "m³/h", 0);
            calc.Add("站厅火灾计算排烟量", "C171", r.HallSmokeFlowM3H, "m³/h", 0);
            calc.Add("站台火灾计算排烟量", "D171", r.PlatformSmokeFlowM3H, "m³/h", 0);

            var units = t.Section("二、选型参数");
            units.Add("空调机组选型风量", "A165", r.UnitSupplyFlowM3H, "m³/h", 0);
            units.Add("空调机组选型制冷量", "B165", r.UnitCoolingKw, "kW", 2);
            units.Add("小新风选型风量", "A136", r.ActualFreshAirM3H, "m³/h", 0);
            units.Add("回排风机选型风量", "C178", r.UnitReturnFlowM3H, "m³/h", 0);
            if (smoke != null)
                units.Add("排烟风机选型风量", "排烟窗单台选型", smoke.UnitSelectionFlowM3H, "m³/h", 0);
            else
                units.Add("排烟风机选型风量", "E178", r.UnitSmokeFlowM3H, "m³/h", 0);
            return t;
        }

        /// <summary>
        /// 大系统**输入参数**表(计算书用,界面不显示)。分组与「大系统 → 负荷计算」窗一致,
        /// 单元格代号保留在 <see cref="ResultRow.Cell"/> 里(供开发自检逐格核对,§6.3)。
        /// </summary>
        public static ResultTable ForLargeSystemInput(LargeSystemInput x)
        {
            var t = new ResultTable
            {
                Title = "大系统输入参数(与「大系统 → 负荷计算」窗同一份 large-system.xml)",
                Note = "C5/F4/F6 由「项目信息 → 气象参数」联动(可取消);D55/D56/C13/C14 可由「公共区参数」从模型空间取值。"
            };
            if (x == null) return t;

            var air = t.Section("一、空气计算参数及标准");
            air.Add("夏季空调室外湿球温度", "C5", x.OutdoorWetBulbC, "℃", 2);
            air.Add("站厅空调计算干球温度", "F4", x.HallDesignTempC, "℃", 2);
            air.Add("站台空调计算干球温度", "F6", x.PlatformDesignTempC, "℃", 2);
            air.Add("站厅公共区风温差", "C8", x.SupplyTempDiffC, "℃", 2);
            air.Add("管道温升", "C10", x.DuctTempRiseC, "℃", 2);
            air.Add("露点相对湿度", "C118", x.DewPointRelativeHumidityPercent, "%", 1);
            air.Add("壁面产湿量", "A91", x.WallMoistureEmission, "g/(m²·h)", 2);
            air.Add("空调季新风量指标", "B132", x.FreshAirPerPersonM3H, "m³/(h·人)", 1);

            var geo = t.Section("二、车站基础资料");
            geo.Add("站厅公共区面积", "D55", x.HallAreaM2, "m²", 1);
            geo.Add("站台公共区面积", "D56", x.PlatformAreaM2, "m²", 1);
            geo.Add("站厅公共区层高", "C13", x.HallHeightM, "m", 2);
            geo.Add("站厅公共区长度", "C14", x.HallLengthM, "m", 2);
            geo.Add("出入口A 宽", "", x.EntranceAWidthM, "m", 2);
            geo.Add("出入口A 高", "", x.EntranceAHeightM, "m", 2);
            geo.Add("出入口B 宽", "", x.EntranceBWidthM, "m", 2);
            geo.Add("出入口B 高", "", x.EntranceBHeightM, "m", 2);
            geo.Add("出入口C 宽", "", x.EntranceCWidthM, "m", 2);
            geo.Add("出入口C 高", "", x.EntranceCHeightM, "m", 2);
            geo.Add("出入口D 宽", "", x.EntranceDWidthM, "m", 2);
            geo.Add("出入口D 高", "", x.EntranceDHeightM, "m", 2);
            geo.Add("出入口负荷指标", "B82", x.EntranceLoadIndexW, "W", 0);

            var flow = t.Section("三、高峰客流资料");
            flow.Add("上行线 上客量", "A27", x.UpLineBoardCount, "人次/h", 0);
            flow.Add("上行线 下客量", "B27", x.UpLineAlightCount, "人次/h", 0);
            flow.Add("下行线 上客量", "C27", x.DownLineBoardCount, "人次/h", 0);
            flow.Add("下行线 下客量", "D27", x.DownLineAlightCount, "人次/h", 0);
            flow.Add("换乘 上客量", "E27", x.TransferBoardCount, "人次/h", 0);
            flow.Add("换乘 下客量", "F27", x.TransferAlightCount, "人次/h", 0);
            flow.Add("站厅 上车停站", "D29", x.HallBoardStayMin, "min", 2);
            flow.Add("站厅 下车停站", "D30", x.HallAlightStayMin, "min", 2);
            flow.Add("站厅 换乘上车停站", "D31", x.HallTransferBoardStayMin, "min", 2);
            flow.Add("站厅 换乘下车停站", "D32", x.HallTransferAlightStayMin, "min", 2);
            flow.Add("站台 上车停站", "F29", x.PlatformBoardStayMin, "min", 2);
            flow.Add("站台 下车停站", "F30", x.PlatformAlightStayMin, "min", 2);
            flow.Add("站台 换乘上车停站", "F31", x.PlatformTransferBoardStayMin, "min", 2);
            flow.Add("站台 换乘下车停站", "F32", x.PlatformTransferAlightStayMin, "min", 2);
            flow.Add("集群系数", "C37", x.ClusterFactor, "—", 3);
            flow.Add("超高峰小时系数", "F37", x.SuperPeakHourFactor, "—", 2);

            var people = t.Section("四、人员散热、散湿量标准");
            people.Add("站厅 显热", "D45", x.HallOccupantSensibleW, "W/人", 0);
            people.Add("站厅 潜热", "E45", x.HallOccupantLatentW, "W/人", 0);
            people.Add("站厅 散湿量", "F45", x.HallOccupantMoistureGH, "g/h", 0);
            people.Add("站台 显热", "D46", x.PlatformOccupantSensibleW, "W/人", 0);
            people.Add("站台 潜热", "E46", x.PlatformOccupantLatentW, "W/人", 0);
            people.Add("站台 散湿量", "F46", x.PlatformOccupantMoistureGH, "g/h", 0);

            var heat = t.Section("五、照明 / 广告 / 设备发热量");
            heat.Add("站厅照明指标", "C55", x.HallLightingWm2, "W/m²", 1);
            heat.Add("站台照明指标", "C56", x.PlatformLightingWm2, "W/m²", 1);
            heat.Add("站厅广告牌发热量", "C58", x.HallAdvertKw, "kW", 2);
            heat.Add("站台广告牌发热量", "C59", x.PlatformAdvertKw, "kW", 2);
            heat.Add("公共区扶梯指标", "B62", x.EscalatorKwPerUnit, "kW/台", 2);
            heat.Add("公共区扶梯数量", "B63", x.EscalatorCount, "台", 0);
            heat.Add("公共区直梯指标", "D62", x.ElevatorKwPerUnit, "kW/台", 2);
            heat.Add("公共区直梯数量", "D63", x.ElevatorCount, "台", 0);
            heat.Add("AFC 设备指标", "E62", x.AfcKwPerUnit, "kW/台", 2);
            heat.Add("AFC 设备数量", "E63", x.AfcCount, "台", 0);

            var psd = t.Section("六、屏蔽门传热 / 漏风 / 发热");
            psd.Add("传热系数", "A71", x.PsdHeatTransferCoeffWm2C, "W/(m²·℃)", 2);
            psd.Add("屏蔽门高", "B71", x.PsdHeightM, "m", 2);
            psd.Add("屏蔽门长", "C71", x.PsdLengthM, "m", 1);
            psd.Add("内外温差", "D71", x.PsdTempDiffC, "℃", 1);
            psd.Add("传热安全系数", "E71", x.PsdHeatSafetyFactor, "—", 2);
            psd.Add("站厅屏蔽门传热量", "D103", x.HallPsdTransferKw, "kW", 2);
            psd.Add("站厅屏蔽门漏风", "A75", x.HallPsdLeakKw, "kW", 2);
            psd.Add("站厅屏蔽门发热", "D105", x.HallPsdHeatKw, "kW", 2);
            psd.Add("站台屏蔽门漏风", "B75", x.PlatformPsdLeakKw, "kW", 2);
            psd.Add("屏蔽门系统发热", "C76", x.PlatformPsdSystemHeatKw, "kW", 2);
            psd.Add("站厅其他补充发热", "D106", x.HallExtraHeatKw, "kW", 2);
            psd.Add("站台其他补充发热", "E106", x.PlatformExtraHeatKw, "kW", 2);

            var moisture = t.Section("七、其他湿负荷");
            moisture.Add("站厅其他湿负荷", "D110", x.HallOtherMoisture, "g/s", 3);
            moisture.Add("站台其他湿负荷", "E110", x.PlatformOtherMoisture, "g/s", 3);
            moisture.Add("站台结构散湿(覆盖,0=不计)", "E109", x.PlatformStructureMoistureOverride, "g/s", 3);
            return t;
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

        // ================================================================== 水力计算(风系统 / 水系统)

        /// <summary>
        /// 水力计算结果表(需求 2.3 风系统 / 2.4 水系统):系统与介质 → 最不利环路阻力汇总 → 需求值 → 设备校核。
        /// <para>
        /// 逐段明细(断面/流速/比摩阻/沿程/局部)不在这里 —— 那是"一段一行、多列数值",
        /// 与排烟同理走专用表格(<see cref="HydraulicResult.Segments"/>),文本版见 <see cref="ResultFormatter.FormatHydraulic"/>。
        /// </para>
        /// </summary>
        public static ResultTable ForHydraulic(HydraulicInput x, HydraulicResult r)
        {
            bool water = r.Kind == HydraulicKind.WaterPipe;
            var t = new ResultTable
            {
                Title = water ? "水系统水力计算结果" : "风系统水力计算结果",
                Note = r.Note + (string.IsNullOrEmpty(r.BalanceNote) ? "" : " " + r.BalanceNote)
            };

            var system = t.Section("一、系统与介质");
            system.AddText("系统名称", string.IsNullOrEmpty(r.SystemName) ? "(未命名)" : r.SystemName);
            system.AddText("介质", water ? "水" : "空气");
            system.Add("介质温度", "", x.MediumTempC, "℃");
            system.Add("密度 ρ", "", r.DensityKgM3, "kg/m³", water ? 2 : 3);
            system.AddText("运动粘度 ν", r.KinematicViscosityM2S.ToString("0.000e+0", CultureInfo.InvariantCulture) + " m²/s");
            system.Add("最不利环路管段数", "", r.CriticalSegmentCount, "段", 0);
            system.Add("富余系数", "", r.ExtraFactor, "", 2);

            var path = t.Section("二、最不利环路阻力");
            path.Add("沿程阻力合计", "", r.FrictionTotalPa, "Pa");
            path.Add("局部阻力合计", "", r.LocalTotalPa, "Pa");
            path.Add("末端阻力合计", "", r.TerminalTotalPa, "Pa");
            path.Add("设备阻力合计", "", r.EquipmentTotalPa, "Pa");
            if (!water) path.Add("出口动压损失", "", r.OutletDynamicPa, "Pa");
            if (water) path.Add("静压(高差)", "", r.StaticPa, "Pa");
            path.AddTotal("计算总阻力", "", r.TotalResistancePa, "Pa");

            var required = t.Section(water ? "三、需求水泵扬程" : "三、需求风机全压");
            if (water)
            {
                required.Add("计算总阻力", "", r.TotalResistancePa / 1000.0, "kPa", 2);
                required.AddTotal("需求扬程(计算总阻力 × 富余 ÷ ρg)", "", r.RequiredHeadM, "m", 2);
            }
            else
            {
                required.AddTotal("需求全压(计算总阻力 × 富余系数)", "", r.RequiredPressurePa, "Pa", 1);
            }

            var check = t.Section("四、设备校核(模型参数)");
            if (water)
            {
                check.Add("水泵额定扬程", "", r.RatedHeadM, "m", 2);
                check.Add("需求扬程", "", r.RequiredHeadM, "m", 2);
            }
            else
            {
                check.Add("风机额定全压", "", r.RatedPressurePa, "Pa", 1);
                check.Add("需求全压", "", r.RequiredPressurePa, "Pa", 1);
            }
            if (!double.IsNaN(r.MarginPct)) check.AddTotal("余量", "", r.MarginPct, "%", 1);
            check.AddText("结论", r.CheckVerdict);

            // 五、并联环路平衡(逐支路明细在结果窗与计算书里给,这里给汇总指标)
            var balance = t.Section("五、并联环路平衡");
            if (r.HasBranches)
            {
                balance.Add("允许不平衡率", "", r.ImbalanceLimitPct, "%", 1);
                balance.Add("并联支路数", "", r.Branches.Count, "条", 0);
                balance.Add("最大不平衡率", "", r.MaxImbalancePct, "%", 1);
                balance.AddTotal("超出允许值的支路数", "", r.UnbalancedBranchCount, "条", 0);
            }
            else
            {
                balance.AddText("并联支路数", "—(没有支路拓扑数据,未做并联平衡分析)");
            }
            balance.AddText("平衡口径", r.BalanceNote);

            // 六、系统阻力特性曲线(点表在结果窗与计算书里给)
            var curve = t.Section("六、系统阻力特性曲线");
            curve.AddText("曲线", r.HasCurve
                ? r.Curve.Count + " 点(设计流量的 50%~130%),点表见结果窗与计算书"
                : "—(没有流量数据,未给出曲线)");

            return t;
        }

        // ================================================================== 水力计算 · 全站汇总

        /// <summary>
        /// 水力计算**全站汇总**表(需求 2.3 / 2.4):只给**可加量**的合计 + 压力类的最大值。
        /// <para>
        /// ⚠ 风机全压 / 水泵扬程**不能相加**(各系统管网相互独立),所以这里不做"看起来漂亮"的求和;
        /// 逐系统明细走专用表格(见 <see cref="HydraulicSummary.Rows"/> 与计算书里的逐系统表)。
        /// </para>
        /// </summary>
        public static ResultTable ForHydraulicSummary(HydraulicSummary s)
        {
            var t = new ResultTable
            {
                Title = "全站水力计算汇总",
                Note = (s.Note ?? "") + (string.IsNullOrEmpty(s.PendingNote) ? "" : " " + s.PendingNote)
            };

            var total = t.Section("一、全站合计(可加量)");
            total.Add("系统总数", "", s.SystemCount, "套", 0);
            total.Add("风系统", "", s.AirCount, "套", 0);
            total.Add("水系统", "", s.WaterCount, "套", 0);
            total.Add("管段总数", "", s.SegmentCount, "段", 0);
            total.Add("管段总长", "", s.TotalLengthM, "m", 1);
            total.Add("风系统设计风量合计", "", s.TotalAirFlowM3H, "m³/h", 0);
            total.Add("水系统设计水量合计", "", s.TotalWaterFlowM3H, "m³/h", 1);
            total.Add("末端 / 设备阻力项", "", s.EquipmentCount, "项", 0);
            total.Add("存在超限并联支路的系统数", "", s.UnbalancedSystemCount, "套", 0);
            total.Add("未做设备校核的系统数", "", s.UncheckedSystemCount, "套", 0);

            var pressure = t.Section("二、需求值(不可加:逐系统列出,此处只给最大值)");
            pressure.Add("最大需求全压(风系统)", "", s.MaxRequiredPressurePa, "Pa", 1);
            pressure.Add("最大需求扬程(水系统)", "", s.MaxRequiredHeadM, "m", 2);
            pressure.AddText("为什么不求和",
                "风机全压与水泵扬程对应的是各系统相互独立的管网,阻力不能相加;" +
                "逐系统的计算总阻力 / 需求值 / 校核结论见「逐系统」表与各系统的计算书。");

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
