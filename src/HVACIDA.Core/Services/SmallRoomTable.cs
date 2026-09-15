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
                    cols.Add(Col("面积 m²", "AreaM2", 2));
                    cols.Add(Col("层高 m", "HeightM", 2, 70));
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
                    cols.Add(Col("面积 m²", "AreaM2", 2));
                    cols.Add(Col("层高 m", "HeightM", 2, 70));
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
                    cols.Add(Col("面积 m²", "AreaM2", 2));
                    cols.Add(Col("层高 m", "HeightM", 2, 70));
                    cols.Add(Col("换气次数 次/h", "AirChangePerHour", 0, 100));
                    cols.Add(Col("计算排风量 m³/h", "ExhaustM3H", 0, 130));
                    break;
                case SmallSystemType.SmokeExhaust:
                    cols.Add(Col("面积 m²", "AreaM2", 2));
                    cols.Add(Col("计算排烟量 m³/h", "SmokeM3H", 0, 130));
                    cols.Add(Col("计算补风量 m³/h", "MakeupAirM3H", 0, 130));
                    break;
                case SmallSystemType.SupplyExhaustSmoke:
                    cols.Add(Col("面积 m²", "AreaM2", 2));
                    cols.Add(Col("层高 m", "HeightM", 2, 70));
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
                    cols.Add(new RoomColumn { Header = "与土壤接触外墙长度 m", Property = "WallLengthM", Decimals = 2, Width = 130 });
                    cols.Add(new RoomColumn { Header = "与土壤接触屋顶面积 m²", Property = "RoofAreaM2", Decimals = 2, Width = 140 });
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

        private static string Cell(SmallRoomResult room, RoomColumn col)
        {
            var property = typeof(SmallRoomResult).GetProperty(col.Property);
            object value = property == null ? null : property.GetValue(room, null);
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
