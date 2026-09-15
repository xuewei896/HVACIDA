using System;
using System.Collections.Generic;
using HVACIDA.Core.Utils;

namespace HVACIDA.Core.Models
{
    /// <summary>小系统类型(需求文档 2.2.3.2,与 Ribbon 六键一一对应)。</summary>
    public enum SmallSystemType
    {
        /// <summary>全空气一次回风系统(多个弱电/强电房间共用一台柜式空调机组)</summary>
        AllAirOnceReturn,

        /// <summary>多联机 + 新风系统(多个人员房间)</summary>
        VrfWithFreshAir,

        /// <summary>排风系统(卫生间、泵房等,多房间排风)</summary>
        ExhaustVentilation,

        /// <summary>排烟系统(多个防烟分区排烟 + 补风)</summary>
        SmokeExhaust,

        /// <summary>送风排风排烟系统(环控机房排风/排烟/补风 + 气瓶间排风)</summary>
        SupplyExhaustSmoke,

        /// <summary>加压送风系统(楼梯间)</summary>
        PressurizationSupply
    }

    /// <summary>
    /// 小系统计算输入(需求文档 2.2.3.2;公式权威 =《小系统空调负荷、送排风、排烟计算公式.docx》)。
    /// <para>
    /// 结构:**系统级参数**(室外/室内温度、温差、指标、选型系数、加压送风门参数)+ **逐房间列表**
    /// (<see cref="Rooms"/>)。一个系统负责多个房间,故房间相关量都在 <see cref="SmallRoomInput"/> 里。
    /// </para>
    /// <para>
    /// 室内/室外计算温度、湿球温度可从「项目信息 → 气象参数」回填(见 <see cref="Services.SmallSystemInputService"/>),
    /// 默认值即公式文档给定值。
    /// </para>
    /// </summary>
    [Serializable]
    public class SmallSystemInput
    {
        public SmallSystemInput()
        {
            SetDocumentDefaults();
        }

        /// <summary>系统类型(决定走哪条公式链)。</summary>
        public SmallSystemType SystemType { get; set; }

        /// <summary>系统编号(如 AHU-A101 / PEU-A216 / EAF-A601;输出与选型表用)。</summary>
        public string SystemCode { get; set; } = "";

        /// <summary>系统负责的房间(至少 1 个,可为空列表由界面提示)。</summary>
        public List<SmallRoomInput> Rooms { get; set; } = new List<SmallRoomInput>();

        // ---------- 室外参数(可从项目信息回填) ----------

        /// <summary>E4 夏季空调室外计算干球温度 ℃(项目信息)。</summary>
        public double OutdoorDryBulbC { get; set; }

        /// <summary>E5 夏季空调室外计算湿球温度 ℃(项目信息)。</summary>
        public double OutdoorWetBulbC { get; set; }

        /// <summary>N8 过渡季通风室外计算干球温度 ℃(多联机+新风用)。</summary>
        public double TransitionOutdoorC { get; set; }

        /// <summary>
        /// 是否已脱离「项目信息 → 气象参数」的室外参数自动联动(默认 false = 自动回填 E4/E5)。
        /// 取名"覆盖"而非"自动",是为了让旧版 small-system.xml 缺元素时默认开启联动。
        /// </summary>
        public bool WeatherManuallyOverridden { get; set; }

        // ---------- 室内 / 送风参数 ----------

        /// <summary>E7 室内计算干球温度 ℃(默认 27)。</summary>
        public double IndoorTempC { get; set; }

        /// <summary>N4 送风温差 ℃(默认 10)。</summary>
        public double SupplyTempDiffC { get; set; }

        /// <summary>E9 管道温升 ℃(默认 1.5)。</summary>
        public double DuctTempRiseC { get; set; }

        /// <summary>D44 露点相对湿度 %(全空气一次回风,默认 95)。</summary>
        public double DewPointRhPct { get; set; }

        /// <summary>C83 室内相对湿度 %(多联机+新风,默认 50)。</summary>
        public double IndoorRhPct { get; set; }

        // ---------- 负荷指标(系统级) ----------

        /// <summary>A21 照明指标 W/m²(默认 8;文档示例工程用 20)。</summary>
        public double LightingIndexWm2 { get; set; }

        /// <summary>A17 壁面单位面积产湿量 g/(m²·h)(默认 2)。</summary>
        public double WallMoistureEmission { get; set; }

        /// <summary>人员冷负荷 W/人(默认 134,文档 J27)。</summary>
        public double PersonCoolingW { get; set; }

        /// <summary>人员湿负荷 g/(h·人)(默认 115,文档 K27)。</summary>
        public double PersonMoistureGH { get; set; }

        /// <summary>人员新风量 m³/(h·人)(默认 30,文档 T27)。</summary>
        public double FreshAirPerPersonM3H { get; set; }

        // ---------- 选型系数 ----------

        /// <summary>
        /// 主设备选型系数:小于 0 表示按系统类型取文档值(全空气 1.1、多联机 1.1、排风 1.1、排烟 1.2、
        /// 送排风 1.1、加压 1.2)。界面可直接覆盖。
        /// </summary>
        public double SelectionFactor { get; set; }

        /// <summary>排烟选型系数(默认 1.2)。</summary>
        public double SmokeSelectionFactor { get; set; }

        /// <summary>补风比例(默认 0.6)。</summary>
        public double MakeupAirRatio { get; set; }

        /// <summary>补风选型系数(默认 1.1)。</summary>
        public double MakeupAirSelectionFactor { get; set; }

        /// <summary>送风比例(送排风排烟系统:送风 = 排风 × 0.9)。</summary>
        public double SupplyFromExhaustRatio { get; set; }

        // ---------- 加压送风系统(楼梯间)参数 ----------

        /// <summary>B388 一层内可开启门宽度 m(默认 1.5)。</summary>
        public double DoorWidthM { get; set; }

        /// <summary>C388 一层内可开启门高度 m(默认 2.1)。</summary>
        public double DoorHeightM { get; set; }

        /// <summary>E388 门缝隙漏风风速 m/s(默认 1)。</summary>
        public double DoorLeakageVelocityMs { get; set; }

        /// <summary>F388 N1 疏散门开启数量(默认 1)。</summary>
        public double OpenDoorCount { get; set; }

        /// <summary>M388 N2 漏风疏散门数量(负担楼层数 − 1,默认 1)。</summary>
        public double LeakDoorCount { get; set; }

        /// <summary>O388 Af 单个余压阀面积 m²(默认 0.5)。</summary>
        public double ReliefValveAreaM2 { get; set; }

        /// <summary>P388 N3 余压阀数量(默认 2)。</summary>
        public double ReliefValveCount { get; set; }

        /// <summary>K388 ΔP 计算漏风量的平均压力差 Pa(默认 12)。</summary>
        public double PressureDiffPa { get; set; }

        /// <summary>恢复公式文档默认值(房间列表不动)。</summary>
        public void SetDocumentDefaults()
        {
            TransitionOutdoorC = HvacConstants.SmallTransitionOutdoorC;
            IndoorTempC = HvacConstants.SmallIndoorTempC;
            SupplyTempDiffC = HvacConstants.SmallSupplyTempDiffC;
            DuctTempRiseC = HvacConstants.SmallDuctTempRiseC;
            DewPointRhPct = HvacConstants.SmallDewPointRhPct;
            IndoorRhPct = HvacConstants.SmallIndoorRhPct;
            LightingIndexWm2 = HvacConstants.SmallLightingIndexWm2;
            WallMoistureEmission = HvacConstants.SmallWallMoistureEmission;
            PersonCoolingW = HvacConstants.SmallPersonCoolingW;
            PersonMoistureGH = HvacConstants.SmallPersonMoistureGH;
            FreshAirPerPersonM3H = HvacConstants.SmallFreshAirPerPersonM3H;

            WeatherManuallyOverridden = false;
            SelectionFactor = -1;   // 按系统类型取文档值
            SmokeSelectionFactor = HvacConstants.SmokeSystemSelectionFactor;
            MakeupAirRatio = HvacConstants.MakeupAirRatio;
            MakeupAirSelectionFactor = HvacConstants.MakeupAirSelectionFactor;
            SupplyFromExhaustRatio = HvacConstants.SupplyFromExhaustRatio;

            DoorWidthM = 1.5;
            DoorHeightM = 2.1;
            DoorLeakageVelocityMs = 1.0;
            OpenDoorCount = 1;
            LeakDoorCount = 1;
            ReliefValveAreaM2 = 0.5;
            ReliefValveCount = 2;
            PressureDiffPa = 12;
        }

        /// <summary>该系统的默认选型系数(文档值;全空气/多联机/排风/送排风 = 1.1,排烟 = 1.2,加压 = 1.2)。</summary>
        public double EffectiveSelectionFactor()
        {
            if (SelectionFactor >= 0) return SelectionFactor;
            switch (SystemType)
            {
                case SmallSystemType.SmokeExhaust: return HvacConstants.SmokeSystemSelectionFactor;
                case SmallSystemType.PressurizationSupply: return HvacConstants.PressurizationSelectionFactor;
                default: return HvacConstants.ExhaustSelectionFactor;   // 1.1
            }
        }

        /// <summary>按房间类型取默认换气次数(文档给定值)。</summary>
        public static double DefaultAirChangePerHour(string roomType)
        {
            string t = roomType ?? "";
            if (t.Contains("卫生间") || t.Contains("男卫") || t.Contains("女卫") || t.Contains("厕所")) return 20.0;
            if (t.Contains("淋浴")) return 10.0;
            if (t.Contains("环控机房") || t.Contains("通风空调机房")) return 6.0;
            if (t.Contains("气瓶")) return 4.0;
            if (t.Contains("电缆引入")) return 4.0;
            if (t.Contains("泵房")) return 4.0;
            return 4.0;
        }
    }
}
