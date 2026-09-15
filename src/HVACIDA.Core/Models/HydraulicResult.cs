using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace HVACIDA.Core.Models
{
    /// <summary>
    /// 单个管段的水力计算结果(界面「管段明细」表一行;与计算书同源)。
    /// <para>
    /// 计算口径(<see cref="Services.HydraulicCalculator"/>):
    /// 断面积 → 流速 = 流量 ÷ 3600 ÷ 断面积 → 雷诺数 → 摩擦系数 λ → 比摩阻 → 沿程 + 局部。
    /// </para>
    /// </summary>
    public class HydraulicSegmentResult
    {
        /// <summary>段名(含元素 Id,可回模型核对)。</summary>
        public string Name { get; set; } = "";

        /// <summary>Revit 元素 Id(0 = 手工行)。</summary>
        public int ElementId { get; set; }

        /// <summary>断面文字(如「1200×400 m」「Φ0.500 m」)。</summary>
        public string SectionText { get; set; } = "";

        /// <summary>流量 m³/h。</summary>
        public double FlowM3H { get; set; }

        /// <summary>断面面积 m²。</summary>
        public double AreaM2 { get; set; }

        /// <summary>水力直径 m。</summary>
        public double HydraulicDiameterM { get; set; }

        /// <summary>流速 m/s。</summary>
        public double VelocityMs { get; set; }

        /// <summary>雷诺数(无量纲)。</summary>
        public double Reynolds { get; set; }

        /// <summary>摩擦系数 λ。</summary>
        public double FrictionFactor { get; set; }

        /// <summary>实际采用的绝对粗糙度 K mm(0 输入 = 按介质取默认,这里给算完的实际值)。</summary>
        public double RoughnessMm { get; set; }

        /// <summary>比摩阻 Pa/m(沿程阻力梯度)。</summary>
        public double SpecificFrictionPaPerM { get; set; }

        /// <summary>段长 m。</summary>
        public double LengthM { get; set; }

        /// <summary>沿程阻力 Pa。</summary>
        public double FrictionLossPa { get; set; }

        /// <summary>局部阻力系数之和 Σζ。</summary>
        public double LocalZetaSum { get; set; }

        /// <summary>局部阻力 Pa。</summary>
        public double LocalLossPa { get; set; }

        /// <summary>段合计阻力 Pa(沿程 + 局部)。</summary>
        public double TotalLossPa { get; set; }

        /// <summary>动压 Pa(ρv²/2;出口动压与局部阻力都按它算)。</summary>
        public double DynamicPressurePa { get; set; }

        /// <summary>是否在最不利环路上。</summary>
        public bool OnCriticalPath { get; set; }

        /// <summary>该段管件组成说明(取值可追溯)。</summary>
        public string LocalNote { get; set; } = "";

        /// <summary>数据来源(模型 / 手工)。</summary>
        public string Source { get; set; } = "";
    }

    /// <summary>环路上末端 / 设备 / 出口动压的一项(结果表一行)。</summary>
    public class HydraulicItemResult
    {
        /// <summary>名称。</summary>
        public string Name { get; set; } = "";

        /// <summary>类别(末端 / 设备 / 出口动压)。</summary>
        public HydraulicItemKind Kind { get; set; }

        /// <summary>类别文字(界面列)。</summary>
        public string KindText { get; set; } = "";

        /// <summary>阻力 Pa。</summary>
        public double ResistancePa { get; set; }

        /// <summary>阻力来源说明(模型参数 / 设备样本 / 用户输入 / 出口动压)。</summary>
        public string Source { get; set; } = "";

        /// <summary>是否在最不利环路上。</summary>
        public bool OnCriticalPath { get; set; }
    }

    /// <summary>
    /// 水力计算结果:最不利环路的阻力累加 → **需求风压(Pa)** 或 **需求扬程(m)**,
    /// 并与模型里读到的风机额定全压 / 水泵额定扬程做校核。
    /// </summary>
    public class HydraulicResult
    {
        /// <summary>介质。</summary>
        public HydraulicKind Kind { get; set; } = HydraulicKind.AirDuct;

        /// <summary>系统名。</summary>
        public string SystemName { get; set; } = "";

        /// <summary>逐段结果(全部管段,含不在环路上的)。</summary>
        public List<HydraulicSegmentResult> Segments { get; set; } = new List<HydraulicSegmentResult>();

        /// <summary>环路阻力项(末端 / 设备 / 出口动压)。</summary>
        public List<HydraulicItemResult> Items { get; set; } = new List<HydraulicItemResult>();

        /// <summary>最不利环路名称(末端名 / 说明)。</summary>
        public string CriticalPathName { get; set; } = "";

        /// <summary>最不利环路上管段数。</summary>
        public int CriticalSegmentCount { get; set; }

        /// <summary>最不利环路沿程阻力合计 Pa。</summary>
        public double FrictionTotalPa { get; set; }

        /// <summary>最不利环路局部阻力合计 Pa。</summary>
        public double LocalTotalPa { get; set; }

        /// <summary>末端阻力合计 Pa。</summary>
        public double TerminalTotalPa { get; set; }

        /// <summary>设备阻力合计 Pa。</summary>
        public double EquipmentTotalPa { get; set; }

        /// <summary>出口动压损失 Pa(风系统开式出口;不计入时为 0)。</summary>
        public double OutletDynamicPa { get; set; }

        /// <summary>静压差 Pa(水系统高差;闭式环路为 0)。</summary>
        public double StaticPa { get; set; }

        /// <summary>计算总阻力 Pa(上述各项之和,未乘富余系数)。</summary>
        public double TotalResistancePa { get; set; }

        /// <summary>富余系数。</summary>
        public double ExtraFactor { get; set; }

        /// <summary>需求风机全压 Pa(风系统;= 计算总阻力 × 富余系数)。</summary>
        public double RequiredPressurePa { get; set; }

        /// <summary>需求水泵扬程 m(水系统;= 计算总阻力 × 富余系数 ÷ (ρ·g))。</summary>
        public double RequiredHeadM { get; set; }

        /// <summary>介质密度 kg/m³(按温度修正后的实际取值)。</summary>
        public double DensityKgM3 { get; set; }

        /// <summary>介质运动粘度 m²/s(按温度修正后的实际取值)。</summary>
        public double KinematicViscosityM2S { get; set; }

        /// <summary>模型里读到的风机额定全压 Pa(0 = 未读到 / 不适用)。</summary>
        public double RatedPressurePa { get; set; }

        /// <summary>模型里读到的水泵额定扬程 m(0 = 未读到 / 不适用)。</summary>
        public double RatedHeadM { get; set; }

        /// <summary>额定值与需求值的余量百分比(额定 − 需求)÷ 需求 × 100;无法校核时为 NaN。</summary>
        public double MarginPct { get; set; } = double.NaN;

        /// <summary>校核结论一句话(满足 / 偏紧 / 不足 / 未读到额定参数)。</summary>
        public string CheckVerdict { get; set; } = "";

        /// <summary>口径说明(公式与系数来源,计算书与界面都显示)。</summary>
        public string Note { get; set; } = "";

        /// <summary>待补项 / 本次计算的局限(界面红字,绝不静默)。</summary>
        public string PendingNote { get; set; } = "";

        /// <summary>是否为水系统(界面据此切换单位与列标题)。</summary>
        [XmlIgnore]
        public bool IsWater => Kind == HydraulicKind.WaterPipe;

        /// <summary>是否有可展示的管段结果。</summary>
        [XmlIgnore]
        public bool HasSegments => Segments != null && Segments.Count > 0;
    }
}
