using System;
using System.Collections.Generic;

namespace HVACIDA.Core.Models
{
    /// <summary>
    /// 小系统计算结果(公式权威 =《小系统空调负荷、送排风、排烟计算公式.docx》)。
    /// <list type="bullet">
    ///   <item><see cref="Rooms"/>:逐房间结果 → 界面"房间明细"表(与公式文档的示例表一一对应);</item>
    ///   <item><see cref="Equipments"/>:设备选型 → 界面"设备编号及选型"表(系统代码/系数/风量/冷量);</item>
    ///   <item>其余标量为系统级结果 → 界面"系统结果"指标表(分区显示)。</item>
    /// </list>
    /// </summary>
    public class SmallSystemResult
    {
        public SmallSystemType SystemType { get; set; }

        /// <summary>逐房间结果行。</summary>
        public List<SmallRoomResult> Rooms { get; } = new List<SmallRoomResult>();

        /// <summary>设备选型行(按公式文档"设备编号及选型"表:系统代码 / 系数 / 风量 / 冷量)。</summary>
        public List<SmallEquipmentSelection> Equipments { get; } = new List<SmallEquipmentSelection>();

        // ---------- 合计 ----------
        public double TotalAreaM2 { get; set; }
        public double TotalCoolingKw { get; set; }
        public double TotalMoistureGps { get; set; }

        /// <summary>热湿比 ε = 冷负荷合计 / 湿负荷合计 × 1000(文档 C39:kJ/kg)。</summary>
        public double HeatHumidityRatio { get; set; }

        /// <summary>总送风量 / 实际通风量合计 m³/h(全空气 R37;多联机 N79;排风为计算排风量合计)。</summary>
        public double TotalSupplyM3H { get; set; }

        /// <summary>总回风量 m³/h(全空气 W37;多联机按新风比折算)。</summary>
        public double TotalReturnM3H { get; set; }

        /// <summary>计算排风量合计 m³/h。</summary>
        public double TotalExhaustM3H { get; set; }

        /// <summary>计算排烟量合计 m³/h。</summary>
        public double TotalSmokeM3H { get; set; }

        /// <summary>计算补风量合计 m³/h。</summary>
        public double TotalMakeupAirM3H { get; set; }

        /// <summary>人员新风量合计 / 新风机组送风量 m³/h(T37 / M79)。</summary>
        public double TotalFreshAirM3H { get; set; }

        /// <summary>系统新风量(10% 实际通风量)合计 m³/h(U37)。</summary>
        public double TotalSystemFreshAirM3H { get; set; }

        /// <summary>取用的新风量 = MAX(人员新风量, 10% 系统新风量)(文档 C52/E39 中的 MAX(T37,U37))。</summary>
        public double DesignFreshAirM3H { get; set; }

        /// <summary>新风比 E39。</summary>
        public double FreshAirRatio { get; set; }

        /// <summary>空调器/多联机冷量合计 kW(V37 / I79)。</summary>
        public double TotalUnitCoolingKw { get; set; }

        // ---------- 状态点(焓湿过程,文档"各点参数统计") ----------
        public double SupplyTempC { get; set; }
        public double DewPointTempC { get; set; }
        public double DewPointHumidityGkg { get; set; }
        public double SupplyEnthalpy { get; set; }
        public double IndoorHumidityGkg { get; set; }
        public double IndoorEnthalpy { get; set; }
        public double DewPointEnthalpy { get; set; }
        public double FreshEnthalpy { get; set; }
        public double MixEnthalpy { get; set; }

        // ---------- 加压送风(楼梯间) ----------
        public double DoorAreaM2 { get; set; }
        public double DoorOpenFlowM3H { get; set; }
        public double DoorLeakFlowM3H { get; set; }
        public double ReliefValveLeakFlowM3H { get; set; }
        public double PressurizationFlowM3H { get; set; }

        /// <summary>系统级口径说明(界面状态栏 / 计算书抬头)。</summary>
        public string Note { get; set; } = "";

        /// <summary>待补项 / 口径存疑说明(界面必须显示)。</summary>
        public string PendingNote { get; set; } = "";
    }

    /// <summary>小系统逐房间计算结果(字段并集;不同系统用到的列见 <c>SmallRoomColumns</c>)。</summary>
    public class SmallRoomResult
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";
        public string RoomType { get; set; } = "";

        /// <summary>是否防烟分区行(排烟/送排风排烟系统;界面可据此分组显示)。</summary>
        public bool IsSmokeZone { get; set; }

        public double AreaM2 { get; set; }
        public double HeightM { get; set; }
        public double WallLengthM { get; set; }
        public double RoofAreaM2 { get; set; }

        /// <summary>设备冷负荷 W(G27/E64)。</summary>
        public double EquipmentCoolingW { get; set; }

        /// <summary>房间预测人数 人(I27/G64)。</summary>
        public double Occupants { get; set; }

        /// <summary>房间换气次数 次/h(设计输入 P27/K64;实际换气次数见 <see cref="ActualAch"/>)。</summary>
        public double AirChangePerHour { get; set; }

        /// <summary>照明冷负荷 W(H27/F64 = 照明指标 × 面积)。</summary>
        public double LightingCoolingW { get; set; }

        /// <summary>人员冷负荷 W(J27/H64 = 134 × 人数)。</summary>
        public double PeopleCoolingW { get; set; }

        /// <summary>房间冷负荷 kW(M27/I64)。</summary>
        public double TotalCoolingKw { get; set; }

        /// <summary>人员湿负荷 g/h(K27)。</summary>
        public double PeopleMoistureGH { get; set; }

        /// <summary>结构湿负荷 g/h(L27 = (层高×外墙长 + 屋顶面积) × 壁面产湿量)。</summary>
        public double StructureMoistureGH { get; set; }

        /// <summary>房间湿负荷 g/s(N27)。</summary>
        public double TotalMoistureGps { get; set; }

        /// <summary>消除余热通风量 m³/h(O27/J64)。</summary>
        public double HeatVentilationM3H { get; set; }

        /// <summary>换气次数通风量 m³/h(Q27/L64)。</summary>
        public double AchVentilationM3H { get; set; }

        /// <summary>实际通风量 m³/h(R27/N64 = MAX(消除余热, 换气次数))。</summary>
        public double ActualVentilationM3H { get; set; }

        /// <summary>实际换气次数 次/h(S27/O64)。</summary>
        public double ActualAch { get; set; }

        /// <summary>人员新风量 m³/h(T27/M64 = 30 × 人数)。</summary>
        public double FreshAirPersonM3H { get; set; }

        /// <summary>10% 系统新风量 m³/h(U27)。</summary>
        public double FreshAirSystemM3H { get; set; }

        /// <summary>房间冷量 kW(V27 空调器冷量 / 多联机冷量)。</summary>
        public double UnitCoolingKw { get; set; }

        /// <summary>房间回风量 m³/h(W27)。</summary>
        public double ReturnAirM3H { get; set; }

        /// <summary>计算排风量 m³/h(排风/送排风系统)。</summary>
        public double ExhaustM3H { get; set; }

        /// <summary>计算送风量 m³/h(送排风排烟系统)。</summary>
        public double SupplyM3H { get; set; }

        /// <summary>计算排烟量 m³/h(排烟/送排风系统)。</summary>
        public double SmokeM3H { get; set; }

        /// <summary>计算补风量 m³/h(排烟/送排风系统)。</summary>
        public double MakeupAirM3H { get; set; }
    }

    /// <summary>小系统设备选型行(公式文档"设备编号及选型"表)。</summary>
    public class SmallEquipmentSelection
    {
        /// <summary>系统代码(如 AHU-A101 / RAF-A101 / PEU-A216 / FAF-A301 / EAF-A301 / SEF-A501)。</summary>
        public string Code { get; set; } = "";

        /// <summary>设备名称/说明(柜式空调机组 / 回排风机 / 新风机组 / 送风机 / 排风机 / 排烟风机 / 补风机)。</summary>
        public string Name { get; set; } = "";

        /// <summary>选型系数。</summary>
        public double Factor { get; set; }

        /// <summary>选型风量 m³/h。</summary>
        public double FlowM3H { get; set; }

        /// <summary>选型冷量 kW(通风类设备为 0,界面显示"—")。</summary>
        public double CoolingKw { get; set; }

        /// <summary>是否有冷量(界面决定显示数值还是"—")。</summary>
        public bool HasCooling { get; set; }
    }
}
