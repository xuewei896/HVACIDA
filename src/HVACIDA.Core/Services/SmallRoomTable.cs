using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>小系统"房间明细"表的一列(界面按此生成表格列,计算书按此排版)。</summary>
    public class RoomColumn
    {
        public string Header { get; set; } = "";

        /// <summary><see cref="SmallRoomResult"/> 或 <see cref="SmallRoomInput"/> 的属性名(界面据此建立绑定)。</summary>
        public string Property { get; set; } = "";

        /// <summary>小数位(0 = 整数)。</summary>
        public int Decimals { get; set; } = 1;

        /// <summary>是否可编辑(参考表列用:录入项可改、计算项只读)。</summary>
        public bool IsEditable { get; set; }

        /// <summary>宽度提示(界面用)。</summary>
        public double Width { get; set; } = 90;

        /// <summary>输入列类型:text / number / bool(界面据此选编辑控件)。</summary>
        public string Kind { get; set; } = "number";
    }

    /// <summary>
    /// 小系统"房间明细"表定义 —— 每类系统用得着的列不同(与《小系统空调负荷、送排风、排烟计算公式.docx》
    /// 的示例表列一致),由本类统一给出,界面与计算书共用。
    /// </summary>
    public static class SmallRoomTable
    {
        private static RoomColumn Col(string header, string property, int decimals = 1, double width = 90)
        {
            return new RoomColumn { Header = header, Property = property, Decimals = decimals, Width = width };
        }

        /// <summary>某系统类型的房间明细列。</summary>
        public static IList<RoomColumn> ColumnsFor(SmallSystemType type)
        {
            var cols = new List<RoomColumn> { Col("序号", "Index", 0, 46), Col("房间/分区", "Name", 1, 150) };
            switch (type)
            {
                case SmallSystemType.AllAirOnceReturn:
                    cols.Add(Col("面积 m²", "AreaM2", 1));
                    cols.Add(Col("层高 m", "HeightM", 1, 70));
                    cols.Add(Col("设备冷负荷 W", "EquipmentCoolingW", 0, 100));
                    cols.Add(Col("照明冷负荷 W", "LightingCoolingW", 1, 100));
                    cols.Add(Col("人数", "Occupants", 0, 60));
                    cols.Add(Col("人员冷负荷 W", "PeopleCoolingW", 0, 100));
                    cols.Add(Col("冷负荷 kW", "TotalCoolingKw", 2, 90));
                    cols.Add(Col("湿负荷 g/s", "TotalMoistureGps", 5, 90));
                    cols.Add(Col("消除余热风量 m³/h", "HeatVentilationM3H", 0, 120));
                    cols.Add(Col("换气风量 m³/h", "AchVentilationM3H", 0, 110));
                    cols.Add(Col("实际通风量 m³/h", "ActualVentilationM3H", 0, 120));
                    cols.Add(Col("实际换气次数", "ActualAch", 1, 100));
                    cols.Add(Col("新风量 m³/h", "FreshAirPersonM3H", 0, 100));
                    cols.Add(Col("空调器冷量 kW", "UnitCoolingKw", 2, 110));
                    cols.Add(Col("回风量 m³/h", "ReturnAirM3H", 0, 100));
                    break;
                case SmallSystemType.VrfWithFreshAir:
                    cols.Add(Col("面积 m²", "AreaM2", 1));
                    cols.Add(Col("层高 m", "HeightM", 1, 70));
                    cols.Add(Col("设备冷负荷 W", "EquipmentCoolingW", 0, 100));
                    cols.Add(Col("照明冷负荷 W", "LightingCoolingW", 1, 100));
                    cols.Add(Col("人数", "Occupants", 0, 60));
                    cols.Add(Col("人员冷负荷 W", "PeopleCoolingW", 0, 100));
                    cols.Add(Col("冷负荷 kW", "TotalCoolingKw", 2, 90));
                    cols.Add(Col("湿负荷 g/s", "TotalMoistureGps", 5, 90));
                    cols.Add(Col("消除余热风量 m³/h", "HeatVentilationM3H", 0, 120));
                    cols.Add(Col("换气风量 m³/h", "AchVentilationM3H", 0, 110));
                    cols.Add(Col("实际通风量 m³/h", "ActualVentilationM3H", 0, 120));
                    cols.Add(Col("实际换气次数", "ActualAch", 1, 100));
                    cols.Add(Col("人员新风量 m³/h", "FreshAirPersonM3H", 0, 110));
                    cols.Add(Col("新风冷负荷 kW", "UnitCoolingKw", 2, 110));
                    break;
                case SmallSystemType.ExhaustVentilation:
                    cols.Add(Col("房间类型", "RoomType", 1, 100));
                    cols.Add(Col("面积 m²", "AreaM2", 1));
                    cols.Add(Col("层高 m", "HeightM", 1, 70));
                    cols.Add(Col("换气次数 次/h", "AirChangePerHour", 0, 100));
                    cols.Add(Col("计算排风量 m³/h", "ExhaustM3H", 0, 130));
                    break;
                case SmallSystemType.SmokeExhaust:
                    cols.Add(Col("面积 m²", "AreaM2", 1));
                    cols.Add(Col("计算排烟量 m³/h", "SmokeM3H", 0, 130));
                    cols.Add(Col("计算补风量 m³/h", "MakeupAirM3H", 0, 130));
                    break;
                case SmallSystemType.SupplyExhaustSmoke:
                    cols.Add(Col("面积 m²", "AreaM2", 1));
                    cols.Add(Col("层高 m", "HeightM", 1, 70));
                    cols.Add(Col("换气次数 次/h", "AirChangePerHour", 0, 100));
                    cols.Add(Col("计算排风量 m³/h", "ExhaustM3H", 0, 120));
                    cols.Add(Col("计算送风量 m³/h", "SupplyM3H", 0, 120));
                    cols.Add(Col("计算排烟量 m³/h", "SmokeM3H", 0, 120));
                    cols.Add(Col("计算补风量 m³/h", "MakeupAirM3H", 0, 120));
                    break;
                default:
                    break;
            }
            return cols;
        }

        /// <summary>
        /// 某系统类型的**输入**列(界面"房间/分区"录入表用)。与 <see cref="ColumnsFor"/> 的区别:
        /// 这里列的是用户要填的字段(面积/层高/设备/人数/换气次数…),后者是算完的明细。
        /// </summary>
        public static IList<RoomColumn> InputColumnsFor(SmallSystemType type)
        {
            var cols = new List<RoomColumn>
            {
                new RoomColumn { Header = "房间/分区名称", Property = "Name", Kind = "text", Width = 160 },
                new RoomColumn { Header = "面积 m²", Property = "AreaM2", Decimals = 2, Width = 80 },
                new RoomColumn { Header = "层高 m", Property = "HeightM", Decimals = 2, Width = 70 }
            };

            switch (type)
            {
                case SmallSystemType.AllAirOnceReturn:
                case SmallSystemType.VrfWithFreshAir:
                    cols.Add(new RoomColumn { Header = "与土壤接触外墙长度 m", Property = "WallLengthM", Decimals = 1, Width = 130 });
                    cols.Add(new RoomColumn { Header = "与土壤接触屋顶面积 m²", Property = "RoofAreaM2", Decimals = 1, Width = 140 });
                    cols.Add(new RoomColumn { Header = "设备冷负荷 W", Property = "EquipmentCoolingW", Decimals = 0, Width = 100 });
                    cols.Add(new RoomColumn { Header = "预测人数 人", Property = "Occupants", Decimals = 0, Width = 90 });
                    cols.Add(new RoomColumn { Header = "换气次数 次/h", Property = "AirChangePerHour", Decimals = 0, Width = 100 });
                    break;
                case SmallSystemType.ExhaustVentilation:
                    cols.Add(new RoomColumn { Header = "房间类型", Property = "RoomType", Kind = "text", Width = 110 });
                    cols.Add(new RoomColumn { Header = "换气次数 次/h", Property = "AirChangePerHour", Decimals = 0, Width = 100 });
                    break;
                case SmallSystemType.SupplyExhaustSmoke:
                    cols.Add(new RoomColumn { Header = "房间类型", Property = "RoomType", Kind = "text", Width = 110 });
                    cols.Add(new RoomColumn { Header = "换气次数 次/h", Property = "AirChangePerHour", Decimals = 0, Width = 100 });
                    cols.Add(new RoomColumn { Header = "参与送/排烟/补风", Property = "IsSmokeZone", Kind = "bool", Width = 120 });
                    break;
                case SmallSystemType.SmokeExhaust:
                    // 排烟按防烟分区出量:只需名称与面积(层高不参与面积×60 口径)
                    break;
                case SmallSystemType.PressurizationSupply:
                    cols.Clear();   // 加压送风按楼梯间门参数计算,无房间行
                    break;
                default:
                    break;
            }
            return cols;
        }

        /// <summary>
        /// 全站汇总"逐系统一行"的列(界面表格用)。零值列表示该系统不涉及该项,界面显示「—」。
        /// </summary>
        /// <summary>
        /// 「全空气一次回风」房间表**参考列**(2026-09-24 用户提供的《全空气一次回风系统计算参数展示格式.xlsx》):
        /// **23 列 = 序号 + 房间名称 + 录入项(可编辑)+ 逐房间计算值(只读)**,表头与顺序与参考表逐字一致。
        /// <para>
        /// 绑定目标是界面侧的合并行 <c>HVACIDA.UI.ViewModels.SmallRoomRow</c>(录入项写回 SmallRoomInput、
        /// 计算项取自 SmallRoomResult)—— 界面与计算书都不另算,数值仍来自 Core。
        /// </para>
        /// </summary>
        public static IList<RoomColumn> ReferenceColumnsForAllAir()
        {
            return new List<RoomColumn>
            {
                new RoomColumn { Header = "序号", Property = "Index", Decimals = 0, Width = 46 },
                new RoomColumn { Header = "房间名称", Property = "Name", Decimals = 0, Width = 170, IsEditable = true, Kind = "text" },
                new RoomColumn { Header = "房间面积(㎡)", Property = "AreaM2", Decimals = 1, Width = 90, IsEditable = true },
                new RoomColumn { Header = "层高(m)", Property = "HeightM", Decimals = 1, Width = 70, IsEditable = true },
                new RoomColumn { Header = "与土壤接触外墙长度(m)", Property = "WallLengthM", Decimals = 1, Width = 130, IsEditable = true },
                new RoomColumn { Header = "与土壤接触屋顶面积(m2)", Property = "RoofAreaM2", Decimals = 1, Width = 140, IsEditable = true },
                new RoomColumn { Header = "设备冷负荷(w)", Property = "EquipmentCoolingW", Decimals = 0, Width = 100, IsEditable = true },
                new RoomColumn { Header = "照明冷负荷 (w)", Property = "LightingCoolingW", Decimals = 0, Width = 100 },
                new RoomColumn { Header = "房间预测人数(人)", Property = "Occupants", Decimals = 0, Width = 100, IsEditable = true },
                new RoomColumn { Header = "人员冷负荷 (w)", Property = "PeopleCoolingW", Decimals = 0, Width = 100 },
                new RoomColumn { Header = "人员湿负荷 (g/h)", Property = "PeopleMoistureGH", Decimals = 0, Width = 100 },
                new RoomColumn { Header = "结构湿负荷 (g/h)", Property = "StructureMoistureGH", Decimals = 0, Width = 100 },
                new RoomColumn { Header = "房间冷负荷 (kw)", Property = "TotalCoolingKw", Decimals = 2, Width = 100 },
                new RoomColumn { Header = "湿负荷 (g/s)", Property = "TotalMoistureGps", Decimals = 3, Width = 90 },
                new RoomColumn { Header = "通风量  (m3/h)", Property = "HeatVentilationM3H", Decimals = 0, Width = 100 },
                new RoomColumn { Header = "换气次数", Property = "AirChangePerHour", Decimals = 2, Width = 70, IsEditable = true },
                new RoomColumn { Header = "换气次数通风量(m3/h)", Property = "AchVentilationM3H", Decimals = 0, Width = 130 },
                new RoomColumn { Header = "实际通风量(m3/h)", Property = "ActualVentilationM3H", Decimals = 0, Width = 120 },
                new RoomColumn { Header = "实际换气次数", Property = "ActualAch", Decimals = 2, Width = 90 },
                new RoomColumn { Header = "人员新风量", Property = "FreshAirPersonM3H", Decimals = 0, Width = 90 },
                new RoomColumn { Header = "10%系统新风量", Property = "FreshAirSystemM3H", Decimals = 0, Width = 110 },
                new RoomColumn { Header = "房间空调器冷量 (kw)", Property = "UnitCoolingKw", Decimals = 2, Width = 120 },
                new RoomColumn { Header = "房间回风风量", Property = "ReturnAirM3H", Decimals = 0, Width = 110 }
            };
        }

        public static IList<RoomColumn> SummaryColumns()
        {
            return new List<RoomColumn>
            {
                new RoomColumn { Header = "系统类型", Property = "TypeName", Kind = "text", Width = 150 },
                new RoomColumn { Header = "系统编号", Property = "SystemCode", Kind = "text", Width = 110 },
                new RoomColumn { Header = "房间/分区", Property = "RoomCount", Decimals = 0, Width = 80 },
                new RoomColumn { Header = "面积 m²", Property = "TotalAreaM2", Decimals = 1, Width = 90 },
                new RoomColumn { Header = "冷负荷 kW", Property = "TotalCoolingKw", Decimals = 2, Width = 95 },
                new RoomColumn { Header = "送风量 m³/h", Property = "TotalSupplyM3H", Decimals = 0, Width = 105 },
                new RoomColumn { Header = "回风量 m³/h", Property = "TotalReturnM3H", Decimals = 0, Width = 105 },
                new RoomColumn { Header = "新风量 m³/h", Property = "TotalFreshAirM3H", Decimals = 0, Width = 105 },
                new RoomColumn { Header = "排风量 m³/h", Property = "TotalExhaustM3H", Decimals = 0, Width = 105 },
                new RoomColumn { Header = "排烟量 m³/h", Property = "TotalSmokeM3H", Decimals = 0, Width = 105 },
                new RoomColumn { Header = "补风量 m³/h", Property = "TotalMakeupAirM3H", Decimals = 0, Width = 105 },
                new RoomColumn { Header = "设备冷量 kW", Property = "TotalUnitCoolingKw", Decimals = 2, Width = 110 },
                new RoomColumn { Header = "设备 台", Property = "EquipmentCount", Decimals = 0, Width = 70 }
            };
        }

        /// <summary>房间明细的等宽文本(计算书用;列宽按显示宽度补齐,CJK 记 2 列)。</summary>
        public static string ToText(SmallSystemInput x, SmallSystemResult r)
        {
            var cols = ColumnsFor(r.SystemType);
            if (cols.Count == 0 || r.Rooms.Count == 0) return "";

            var sb = new StringBuilder();
            sb.AppendLine("—— 房间明细 ——");
            foreach (var c in cols) sb.Append(Pad(c.Header, Math.Max(c.Header.Length + 2, 14)));
            sb.AppendLine();
            foreach (var room in r.Rooms)
            {
                foreach (var c in cols)
                {
                    sb.Append(Pad(Cell(room, c), Math.Max(c.Header.Length + 2, 14)));
                }
                sb.AppendLine();
            }
            sb.AppendLine("—— 设备选型 ——");
            sb.AppendLine(Pad("系统代码", 16) + Pad("设备", 16) + Pad("系数", 8) + Pad("风量 m³/h", 16) + "冷量 kW");
            foreach (var e in r.Equipments)
            {
                sb.AppendLine(Pad(e.Code, 16) + Pad(e.Name, 16) + Pad(Num(e.Factor, 2), 8) +
                              Pad(Num(e.FlowM3H, 0), 16) +
                              (e.HasCooling ? Num(e.CoolingKw, 2) : "—"));
            }
            return sb.ToString();
        }

        /// <summary>
        /// 取某列在**房间结果**上的原始值(Excel 导出与文本计算书共用;数值给 double / int,文本给 string)。
        /// 返回值保持"数值就是数值",以便 Excel 里还能继续参与计算。
        /// </summary>
        public static object ValueOf(SmallRoomResult room, RoomColumn col)
        {
            if (room == null || col == null) return null;
            var property = typeof(SmallRoomResult).GetProperty(col.Property);
            return property == null ? null : property.GetValue(room, null);
        }

        /// <summary>取某列在**全站汇总行**上的原始值(Excel 导出用;列定义见 <see cref="SummaryColumns"/>)。</summary>
        public static object ValueOf(SmallSystemSummaryRow row, RoomColumn col)
        {
            if (row == null || col == null) return null;
            var property = typeof(SmallSystemSummaryRow).GetProperty(col.Property);
            return property == null ? null : property.GetValue(row, null);
        }

        private static string Cell(SmallRoomResult room, RoomColumn col)
        {
            object value = ValueOf(room, col);
            if (value == null) return "";
            if (value is int i) return i.ToString(CultureInfo.InvariantCulture);
            if (value is double d) return Num(d, col.Decimals);
            return value.ToString();
        }

        private static string Num(double v, int decimals)
        {
            return v.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        private static string Pad(string text, int width)
        {
            text = text ?? "";
            int w = 0;
            foreach (char c in text) w += c > 0x2E80 ? 2 : 1;
            return w >= width ? text + " " : text + new string(' ', width - w);
        }
    }
}
