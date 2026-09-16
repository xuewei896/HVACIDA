using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using HVACIDA.Core.Utils;

namespace HVACIDA.Core.Models
{
    /// <summary>水力计算的介质:风系统(风管)或水系统(水管)。</summary>
    public enum HydraulicKind
    {
        /// <summary>风系统(送风 / 回风 / 排风 / 排烟风管)。</summary>
        AirDuct = 0,

        /// <summary>水系统(冷冻水 / 冷却水 / 热水管道)。</summary>
        WaterPipe = 1
    }

    /// <summary>管段断面形状。</summary>
    public enum HydraulicShape
    {
        /// <summary>圆形(圆形风管 / 圆管)。</summary>
        Round = 0,

        /// <summary>矩形(矩形风管)。</summary>
        Rectangular = 1
    }

    /// <summary>环路上"非管段"阻力的类别。</summary>
    public enum HydraulicItemKind
    {
        /// <summary>末端(风口 / 风机盘管 / 空调末端):阻力为设计输入。</summary>
        Terminal = 0,

        /// <summary>设备(组合式空调机组 / 冷水机组 / 板式换热器 / 冷却塔等):阻力取设备样本或模型参数。</summary>
        Equipment = 1,

        /// <summary>出口动压损失(风系统开式出口;水系统闭式环路无此项)。</summary>
        OutletDynamic = 2
    }

    /// <summary>
    /// 管段(风管 / 水管)。
    /// <para>
    /// 数据来源两类:① **从 Revit 模型读取**(长度取定位线曲线长度、断面取宽×高或内径、流量取连接件 Flow);
    /// ② **手工补充**(模型未建模管件/管段时的兜底)。两类数据在界面上都可见、可改 ——
    /// 不"背后偷偷取值",也不在缺数据时静默按 0 计。
    /// </para>
    /// </summary>
    public class HydraulicSegment
    {
        /// <summary>段名 / 编号(模型来源时形如「矩形风管 1200×400 #12345」)。</summary>
        public string Name { get; set; } = "";

        /// <summary>Revit 元素 Id(手工行为 0,便于回模型核对)。</summary>
        public int ElementId { get; set; }

        /// <summary>断面形状。</summary>
        public HydraulicShape Shape { get; set; } = HydraulicShape.Round;

        /// <summary>圆形内径 m(矩形段不用)。</summary>
        public double DiameterM { get; set; }

        /// <summary>矩形宽 m。</summary>
        public double WidthM { get; set; }

        /// <summary>矩形高 m。</summary>
        public double HeightM { get; set; }

        /// <summary>段长 m(模型:定位线曲线长度;手工:用户填)。</summary>
        public double LengthM { get; set; }

        /// <summary>设计流量 m³/h(模型:连接件 Flow 换算)。</summary>
        public double FlowM3H { get; set; }

        /// <summary>
        /// 绝对粗糙度 K mm。**0 = 按介质取默认**(风管 0.15 / 水管 0.2,见系数集),
        /// 逐段填了则用填的值(不锈钢/塑料管更小)。算完结果里给出**实际取值**,便于核对。
        /// </summary>
        public double RoughnessMm { get; set; }

        /// <summary>
        /// 本段管件局部阻力系数之和 Σζ。由管件表逐件取值后相加(<see cref="Services.HydraulicLocalLossTable"/>),
        /// 结果表里逐段显示 Σζ 与管件组成,便于核对取值。
        /// </summary>
        public double LocalZetaSum { get; set; }

        /// <summary>本段管件组成说明(如「90°弯头×2(0.25×2)、渐缩×1(0.10)」)。</summary>
        public string LocalNote { get; set; } = "";

        /// <summary>是否位于最不利环路(由读取器按连接关系标记,计算与结果表据此汇总)。</summary>
        public bool OnCriticalPath { get; set; }

        /// <summary>断面积 m²(圆:πd²/4;矩:宽×高)。</summary>
        [XmlIgnore]
        public double AreaM2 => Shape == HydraulicShape.Rectangular
            ? WidthM * HeightM
            : Math.PI * DiameterM * DiameterM / 4.0;

        /// <summary>断面湿周 m(圆:πd;矩:2(宽+高))。</summary>
        [XmlIgnore]
        public double PerimeterM => Shape == HydraulicShape.Rectangular
            ? 2.0 * (WidthM + HeightM)
            : Math.PI * DiameterM;

        /// <summary>
        /// 水力直径 m = 4×断面积 ÷ 湿周(圆管即内径;矩形风管即"流速当量直径" 2ab/(a+b))。
        /// 比摩阻按水力直径计算,与《实用供热空调设计手册》比摩阻线算图口径一致。
        /// </summary>
        [XmlIgnore]
        public double HydraulicDiameterM
        {
            get
            {
                double perimeter = PerimeterM;
                return perimeter > 0 ? 4.0 * AreaM2 / perimeter : 0.0;
            }
        }

        /// <summary>断面文字(结果表与计算书用,如「1200×400」「DN100(内径 0.100 m)」)。</summary>
        [XmlIgnore]
        public string SectionText => Shape == HydraulicShape.Rectangular
            ? WidthM.ToString("0.####") + "×" + HeightM.ToString("0.####") + " m"
            : "Φ" + DiameterM.ToString("0.####") + " m";

        /// <summary>
        /// 按"宽/高/内径填了什么"归一化断面形状:宽高都填且不相等 → 矩形,否则按圆形(取内径)。
        /// 界面上改了尺寸后调用,避免"改了尺寸、形状没跟上"导致断面积取错。
        /// </summary>
        public void NormalizeShape()
        {
            Shape = WidthM > 0 && HeightM > 0 && Math.Abs(WidthM - HeightM) > 1e-9
                ? HydraulicShape.Rectangular
                : HydraulicShape.Round;
        }
    }

    /// <summary>
    /// 环路上的末端 / 设备阻力项。
    /// <para>
    /// 阻力值优先取**模型参数或设备样本**(如机组余压、盘管水阻);取不到时由用户填,
    /// 并写明来源(<see cref="Source"/>),不猜值。
    /// </para>
    /// </summary>
    public class HydraulicTerminal
    {
        /// <summary>名称(末端/设备名,模型来源时带元素 Id)。</summary>
        public string Name { get; set; } = "";

        /// <summary>Revit 元素 Id(手工行为 0)。</summary>
        public int ElementId { get; set; }

        /// <summary>类别(末端 / 设备 / 出口动压)。</summary>
        public HydraulicItemKind Kind { get; set; } = HydraulicItemKind.Terminal;

        /// <summary>阻力 Pa(水系统按 1 kPa = 1000 Pa 折算后存 Pa;界面按介质显示 Pa 或 kPa)。</summary>
        public double ResistancePa { get; set; }

        /// <summary>阻力来源说明(模型参数名 / 设备样本 / 用户输入)。</summary>
        public string Source { get; set; } = "";

        /// <summary>是否位于最不利环路。</summary>
        public bool OnCriticalPath { get; set; }

        /// <summary>类别文字(界面列:末端 / 设备 / 出口动压)。</summary>
        [XmlIgnore]
        public string KindText
        {
            get
            {
                switch (Kind)
                {
                    case HydraulicItemKind.Equipment: return "设备";
                    case HydraulicItemKind.OutletDynamic: return "出口动压";
                    default: return "末端";
                }
            }
        }
    }

    /// <summary>
    /// 并联支路(从风机 / 水泵到某一个末端的一条路径)。
    /// <para>
    /// 由模型读取器按连接件拓扑给出(**同一系统里每个末端一条**);没有拓扑信息时列表为空,
    /// 此时只给最不利环路、不做平衡分析(不猜)。
    /// </para>
    /// </summary>
    public class HydraulicBranch
    {
        /// <summary>支路名(末端名 / 支路编号)。</summary>
        public string Name { get; set; } = "";

        /// <summary>末端元素 Id(与 <see cref="HydraulicTerminal.ElementId"/> 对应)。</summary>
        public int TerminalElementId { get; set; }

        /// <summary>该支路上的管段元素 Id(从起点到末端顺序;计算器据此累加段阻力)。</summary>
        public List<int> SegmentElementIds { get; set; } = new List<int>();

        /// <summary>路径说明(管段名串联,便于人工核对这条支路走了哪几段)。</summary>
        public string SegmentSummary { get; set; } = "";

        /// <summary>是否最不利环路(读取器按拓扑标记;计算器复算后会以计算结果为准重标)。</summary>
        public bool IsCritical { get; set; }
    }

    /// <summary>
    /// 一次水力计算的**输入**(一个风系统或一个水系统)。
    /// <para>
    /// 由「水力计算 → 风系统 / 水系统」窗从 Revit 模型读取后填入,再交给
    /// <see cref="Services.HydraulicCalculator"/> 计算;系数(物性/粗糙度/富余/局部阻力)见
    /// <see cref="HydraulicCoefficients"/>,全部可见可改。
    /// </para>
    /// </summary>
    public class HydraulicInput
    {
        /// <summary>介质(风管 / 水管)。</summary>
        public HydraulicKind Kind { get; set; } = HydraulicKind.AirDuct;

        /// <summary>系统名(模型里的系统名称,如「机械送风 1」「冷冻水供水」)。</summary>
        public string SystemName { get; set; } = "";

        /// <summary>
        /// 系统编号(全站唯一标识,如「SAF-1-1」「CHWS-1」)。
        /// **upsert 键 = 介质 + 系统编号**:同介质同编号覆盖、否则追加;留空时界面按系统名兜底。
        /// </summary>
        public string SystemCode { get; set; } = "";

        /// <summary>系统类别名(送风 / 回风 / 排风 / 冷冻水 / 冷却水…)。</summary>
        public string SystemTypeName { get; set; } = "";

        /// <summary>管段列表(含最不利环路标记)。</summary>
        public List<HydraulicSegment> Segments { get; set; } = new List<HydraulicSegment>();

        /// <summary>末端 / 设备阻力项(含最不利环路标记)。</summary>
        public List<HydraulicTerminal> Terminals { get; set; } = new List<HydraulicTerminal>();

        /// <summary>
        /// 并联支路(每末端一条路径;由模型读取器按连接件拓扑给出)。
        /// 空 = 没有拓扑信息 → 只给最不利环路,不做并联平衡分析(并在待补说明里写明)。
        /// </summary>
        public List<HydraulicBranch> Branches { get; set; } = new List<HydraulicBranch>();

        /// <summary>静压高差 m(水系统:最不利环路最高点与水泵中心的高差;风系统为 0)。</summary>
        public double StaticHeightM { get; set; }

        /// <summary>静压差 Pa(= ρ·g·高差;闭式冷冻水环路通常为 0,开式/冷却水按高差计)。</summary>
        public double StaticPressurePa { get; set; }

        /// <summary>风机额定全压 Pa(从模型风机的「全压」参数读到;0 = 模型无此参数,不做校核)。</summary>
        public double RatedPressurePa { get; set; }

        /// <summary>水泵额定扬程 m(从模型水泵的「扬程」参数读到;0 = 模型无此参数,不做校核)。</summary>
        public double RatedHeadM { get; set; }

        /// <summary>富余系数(需求全压 / 扬程 = 计算值 × 该系数;默认 1.1)。</summary>
        public double ExtraFactor { get; set; } = HvacConstants.HydraulicExtraFactor;

        /// <summary>介质温度 ℃(定密度与运动粘度;空气默认 20、水默认 10)。</summary>
        public double MediumTempC { get; set; } = 20.0;

        /// <summary>是否来自模型(手工录入为 false,界面据此提示"结果按手工数据算")。</summary>
        public bool FromModel { get; set; }

        /// <summary>出口动压是否计入(风系统开式送/排风口应计入;闭式环路不计)。</summary>
        public bool IncludeOutletDynamic { get; set; } = true;

        /// <summary>
        /// 出口所在管段的元素 Id(出口动压按该段的动压 ρv²/2 计)。
        /// 为 0 时计算器按最不利环路上**动压最大**的管段取,并在待补说明里写清这个替代口径。
        /// </summary>
        public int OutletSegmentElementId { get; set; }

        /// <summary>最不利环路的末端名称(由读取器按连接关系判定后写入;手工录入时为空)。</summary>
        public string CriticalPathName { get; set; } = "";

        /// <summary>数据来源说明(选中的系统、读到的段数/件数/末端数,供界面与计算书追溯)。</summary>
        public string SourceNote { get; set; } = "";

        /// <summary>模型里没读到 / 没识别的部分(界面红字提示,绝不静默按 0 计)。</summary>
        public string PendingNote { get; set; } = "";

        /// <summary>读到的管段数(界面摘要用)。</summary>
        [XmlIgnore]
        public int SegmentCount => Segments == null ? 0 : Segments.Count;

        /// <summary>最不利环路上的管段数。</summary>
        [XmlIgnore]
        public int CriticalSegmentCount
        {
            get
            {
                int n = 0;
                if (Segments != null) foreach (var s in Segments) if (s != null && s.OnCriticalPath) n++;
                return n;
            }
        }
    }

    /// <summary>
    /// 水力计算的**系数集**(全局一份,可编辑并落盘;界面「系数」区显示,
    /// 计算书末尾逐项列出**本次计算实际用到的取值**,避免"背后取了个数没人知道")。
    /// <para>
    /// 介质的密度与运动粘度**不在这里** —— 它们由介质 + 温度查物性表得出(见
    /// <see cref="Services.HydraulicCalculator"/>),算完在结果里给出实际取值,不让人手改出"错的物性"。
    /// </para>
    /// </summary>
    public class HydraulicCoefficients
    {
        /// <summary>风管绝对粗糙度 K mm(镀锌钢板 0.15)。</summary>
        public double DuctRoughnessMm { get; set; } = HvacConstants.DuctRoughnessMm;

        /// <summary>水管绝对粗糙度 K mm(焊接钢管 0.2)。</summary>
        public double PipeRoughnessMm { get; set; } = HvacConstants.PipeRoughnessMm;

        /// <summary>风系统富余系数(默认 1.1)。</summary>
        public double AirExtraFactor { get; set; } = HvacConstants.HydraulicExtraFactor;

        /// <summary>水系统富余系数(默认 1.1)。</summary>
        public double WaterExtraFactor { get; set; } = HvacConstants.HydraulicExtraFactor;

        /// <summary>重力加速度 m/s²(水系统静压与扬程换算用)。</summary>
        public double GravityM2S { get; set; } = HvacConstants.GravityM2S;

        /// <summary>
        /// 并联环路**允许不平衡率** %(某支路阻力与最不利环路的差额占比超过它就要设平衡装置)。
        /// 常用控制指标:并联环路压力损失差额宜控制在 15% 以内(工程通行口径,项目可按设计文件调整)。
        /// </summary>
        public double ImbalanceLimitPct { get; set; } = HvacConstants.HydraulicImbalanceLimitPct;

        /// <summary>常用局部阻力系数表(管件 → ζ;逐件可查、可改,见 <see cref="Services.HydraulicLocalLossTable"/>)。</summary>
        public List<HydraulicLocalLossItem> LocalLossItems { get; set; } = new List<HydraulicLocalLossItem>();

        /// <summary>按介质取默认系数(保证 LocalLossItems 非空)。</summary>
        public static HydraulicCoefficients CreateDefault()
        {
            return new HydraulicCoefficients { LocalLossItems = Services.HydraulicLocalLossTable.CreateDefaults() };
        }
    }

    /// <summary>常用局部阻力系数表的一行(管件 → ζ)。</summary>
    public class HydraulicLocalLossItem
    {
        /// <summary>管件名称(如「90°弯头 R/D=1.0」「渐缩管」「闸阀(全开)」)。</summary>
        public string Name { get; set; } = "";

        /// <summary>适用介质(风管 / 水管 / 通用)。</summary>
        public string AppliesTo { get; set; } = "通用";

        /// <summary>局部阻力系数 ζ。</summary>
        public double Zeta { get; set; }

        /// <summary>取值来源说明(必须写明,便于评审替换为项目样本值)。</summary>
        public string Source { get; set; } = "";
    }

    /// <summary>
    /// 水力计算模块的落盘容器:**(全局系数) + (全站多套水力系统)**。
    /// <para>
    /// 一个车站通常有多套风系统(送风 / 排风 / 排烟…)与多套水系统(冷冻水 / 冷却水…),故容器按
    /// **「介质 + 系统编号」**存多套(<see cref="Systems"/>),<see cref="Upsert"/> 同键覆盖。
    /// 旧版单系统文件里的 <c>Air</c> / <c>Water</c> 两个元素会被**自动迁移**进 <see cref="Systems"/>
    /// (见 <see cref="MigrateLegacy"/>),迁移后置空、不再写回。
    /// </para>
    /// <para>
    /// 结果不落盘 —— 与其它模块一致,打开「计算结果」窗时按输入**现算**(打开即算)。
    /// </para>
    /// </summary>
    public class HydraulicProject
    {
        /// <summary>系数集(全站共用一份)。</summary>
        public HydraulicCoefficients Coefficients { get; set; } = HydraulicCoefficients.CreateDefault();

        /// <summary>全站水力系统(风 + 水,按「介质 + 系统编号」区分)。</summary>
        public List<HydraulicInput> Systems { get; set; } = new List<HydraulicInput>();

        /// <summary>旧版单系统字段(风):**只用于读取旧文件**,迁移后置空。</summary>
        [XmlElement("Air")]
        public HydraulicInput LegacyAir { get; set; }

        /// <summary>旧版单系统字段(水):**只用于读取旧文件**,迁移后置空。</summary>
        [XmlElement("Water")]
        public HydraulicInput LegacyWater { get; set; }

        /// <summary>系统数。</summary>
        [XmlIgnore]
        public int SystemCount => Systems == null ? 0 : Systems.Count;

        /// <summary>按「介质 + 系统编号」找一套系统(找不到返回 null)。</summary>
        public HydraulicInput Find(HydraulicKind kind, string systemCode)
        {
            if (Systems == null) return null;
            string code = systemCode ?? "";
            foreach (var system in Systems)
            {
                if (system == null) continue;
                if (system.Kind != kind) continue;
                if (string.Equals(system.SystemCode ?? "", code, StringComparison.Ordinal)) return system;
            }
            return null;
        }

        /// <summary>取某介质的**第一套**系统(兼容入口:打开窗时先看已有数据)。</summary>
        public HydraulicInput FirstOf(HydraulicKind kind)
        {
            if (Systems == null) return null;
            foreach (var system in Systems)
            {
                if (system != null && system.Kind == kind) return system;
            }
            return null;
        }

        /// <summary>某介质的系统数。</summary>
        public int CountOf(HydraulicKind kind)
        {
            if (Systems == null) return 0;
            int n = 0;
            foreach (var system in Systems)
            {
                if (system != null && system.Kind == kind) n++;
            }
            return n;
        }

        /// <summary>按「介质 + 系统编号」新增或覆盖(同类型其它编号的系统不受影响)。</summary>
        public void Upsert(HydraulicInput input)
        {
            if (input == null) return;
            if (Systems == null) Systems = new List<HydraulicInput>();
            input.SystemCode = input.SystemCode ?? "";

            for (int i = 0; i < Systems.Count; i++)
            {
                var existing = Systems[i];
                if (existing == null) continue;
                if (existing.Kind != input.Kind) continue;
                if (!string.Equals(existing.SystemCode ?? "", input.SystemCode, StringComparison.Ordinal)) continue;
                Systems[i] = input;
                return;
            }
            Systems.Add(input);
        }

        /// <summary>按「介质 + 系统编号」删除,返回是否删掉了。</summary>
        public bool Remove(HydraulicKind kind, string systemCode)
        {
            if (Systems == null) return false;
            string code = systemCode ?? "";
            for (int i = 0; i < Systems.Count; i++)
            {
                var existing = Systems[i];
                if (existing == null) continue;
                if (existing.Kind != kind) continue;
                if (!string.Equals(existing.SystemCode ?? "", code, StringComparison.Ordinal)) continue;
                Systems.RemoveAt(i);
                return true;
            }
            return false;
        }

        /// <summary>清空某介质的全部系统,返回清掉的套数。</summary>
        public int RemoveAllOf(HydraulicKind kind)
        {
            if (Systems == null) return 0;
            int removed = 0;
            for (int i = Systems.Count - 1; i >= 0; i--)
            {
                var existing = Systems[i];
                if (existing == null || existing.Kind != kind) continue;
                Systems.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        /// <summary>
        /// 把旧版单系统字段(Air / Water)搬进 <see cref="Systems"/>(只在读到旧文件时发生一次)。
        /// </summary>
        /// <returns>是否发生了迁移(调用方据此决定要不要立即落盘)。</returns>
        public bool MigrateLegacy()
        {
            bool changed = false;
            if (LegacyAir != null)
            {
                Upsert(LegacyAir);
                LegacyAir = null;
                changed = true;
            }
            if (LegacyWater != null)
            {
                Upsert(LegacyWater);
                LegacyWater = null;
                changed = true;
            }
            return changed;
        }
    }
}
