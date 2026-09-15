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
    /// 并联支路的平衡计算结果(结果窗「并联环路平衡」表一行)。
    /// <para>
    /// 判据与建议:支路阻力与**最不利环路**的差额占比超过允许不平衡率时,该支路需要设平衡装置吸收多余压差 ——
    /// 水系统给出**平衡阀 Kv**(m³/h @ 1 bar)与**阀权度**(需吸收压差 ÷ 最不利环路总阻力);
    /// 风系统给出**需增加的局部阻力系数 ζ**(按该支路末端管段动压折算,对应多叶调节阀的开度调节)。
    /// </para>
    /// <para>
    /// Kv 定义式:<c>Kv = Q ÷ √(ΔP[bar])</c>(Q 为 m³/h、ΔP 为阀两端压差);
    /// 风阀所需 ζ:<c>ζ = ΔP ÷ (ρv²/2)</c>,v 取该支路末端管段流速。
    /// </para>
    /// </summary>
    public class HydraulicBranchResult
    {
        /// <summary>支路名(末端名)。</summary>
        public string Name { get; set; } = "";

        /// <summary>末端元素 Id。</summary>
        public int TerminalElementId { get; set; }

        /// <summary>支路上管段数。</summary>
        public int SegmentCount { get; set; }

        /// <summary>支路沿程 + 局部阻力 Pa。</summary>
        public double SegmentLossPa { get; set; }

        /// <summary>该支路末端 / 设备阻力 Pa。</summary>
        public double TerminalPa { get; set; }

        /// <summary>支路合计阻力 Pa(= 管段 + 末端/设备)。</summary>
        public double TotalLossPa { get; set; }

        /// <summary>与最不利环路的差额 Pa(最不利 − 本支路;正值 = 本支路阻力小,多出来的压差要吸收)。</summary>
        public double ImbalancePa { get; set; }

        /// <summary>不平衡率 %(= 差额 ÷ 最不利环路 × 100,按绝对值比较允许值)。</summary>
        public double ImbalancePct { get; set; }

        /// <summary>是否最不利环路。</summary>
        public bool IsCritical { get; set; }

        /// <summary>是否在允许不平衡率以内(在以内 → 不需要平衡装置)。</summary>
        public bool WithinLimit { get; set; }

        /// <summary>需要吸收的压差 Pa(超出允许范围时才给;不超范围为 0)。</summary>
        public double RequiredAbsorbPa { get; set; }

        /// <summary>水系统:平衡阀 Kv(m³/h @ 1 bar)。</summary>
        public double ValveKv { get; set; }

        /// <summary>水系统:阀权度 = 需吸收压差 ÷ 最不利环路总阻力。</summary>
        public double ValveAuthority { get; set; }

        /// <summary>风系统:需增加的局部阻力系数 ζ(按末端管段动压折算)。</summary>
        public double ZetaToAdd { get; set; }

        /// <summary>折算用的参考动压 Pa(风系统)。</summary>
        public double ReferenceDynamicPa { get; set; }

        /// <summary>结论一句话(含数值,直接可读)。</summary>
        public string Conclusion { get; set; } = "";

        /// <summary>该支路路径说明(经过哪些管段)。</summary>
        public string Path { get; set; } = "";
    }

    /// <summary>
    /// **系统阻力特性曲线**上的一个点:系统阻力随流量的变化(工程通用近似 —— 阻力与流量的平方成正比,
    /// 但**静压不随流量变化**,故 <c>ΔP(Q) = 静压 + (总阻力 − 静压) × (Q ÷ Q设计)²</c>)。
    /// <para>
    /// 用途:与**厂家风机/水泵性能曲线求交点**即工况点。插件不内置设备曲线,故这里只给系统侧曲线,
    /// 交点由设计人用样本曲线核对 —— 这一点在界面上写明,不假装算了工况点。
    /// </para>
    /// </summary>
    public class HydraulicCurvePoint
    {
        /// <summary>流量占设计流量的百分比 %。</summary>
        public double FlowRatioPct { get; set; }

        /// <summary>对应流量 m³/h。</summary>
        public double FlowM3H { get; set; }

        /// <summary>系统阻力 Pa(计算值,未乘富余系数)。</summary>
        public double ResistancePa { get; set; }

        /// <summary>需求值 Pa(× 富余系数;风 = 需求全压,水 = 需求扬程折算的 Pa)。</summary>
        public double RequiredPa { get; set; }
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

        /// <summary>并联支路平衡结果(每末端一行;没有拓扑信息时为空)。</summary>
        public List<HydraulicBranchResult> Branches { get; set; } = new List<HydraulicBranchResult>();

        /// <summary>系统阻力特性曲线(50%~130% 设计流量,每 10% 一点)。</summary>
        public List<HydraulicCurvePoint> Curve { get; set; } = new List<HydraulicCurvePoint>();

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

        /// <summary>允许不平衡率 %(来自系数集;界面上可改)。</summary>
        public double ImbalanceLimitPct { get; set; }

        /// <summary>超出允许不平衡率的支路数(0 = 各并联环路基本平衡)。</summary>
        public int UnbalancedBranchCount { get; set; }

        /// <summary>最大不平衡率 %(绝对值;没有支路数据时为 0)。</summary>
        public double MaxImbalancePct { get; set; }

        /// <summary>并联平衡与特性曲线的口径说明(界面与计算书显示)。</summary>
        public string BalanceNote { get; set; } = "";

        /// <summary>是否有并联支路数据(没有则不做平衡分析)。</summary>
        [XmlIgnore]
        public bool HasBranches => Branches != null && Branches.Count > 0;

        /// <summary>是否有阻力特性曲线。</summary>
        [XmlIgnore]
        public bool HasCurve => Curve != null && Curve.Count > 0;

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
