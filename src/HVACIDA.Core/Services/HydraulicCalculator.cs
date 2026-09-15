using System;
using System.Collections.Generic;
using HVACIDA.Core.Models;
using HVACIDA.Core.Utils;

namespace HVACIDA.Core.Services
{
    /// <summary>水力计算器接口(风系统与水系统共用;便于自检替换与后续扩展)。</summary>
    public interface IHydraulicCalculator
    {
        /// <summary>按输入 + 系数集算最不利环路阻力,给出需求风压(Pa)或需求扬程(m)。</summary>
        HydraulicResult Calculate(HydraulicInput input, HydraulicCoefficients coefficients);
    }

    /// <summary>
    /// 水力计算器(需求 2.3 风系统 / 2.4 水系统)。
    /// <para>
    /// **口径**(用户 2026-09-15:「没有具体计算公式,由你实现该功能」——
    /// 故采用流体力学/暖通通用公式,并把每个取值都摆到明面上):
    /// </para>
    /// <list type="number">
    /// <item>断面:圆形 A=πd²/4、湿周 U=πd;矩形 A=宽×高、U=2(宽+高);**水力直径 d_h = 4A/U**
    /// (矩形即"流速当量直径" 2ab/(a+b),与手册比摩阻线算图口径一致)。</item>
    /// <item>流速:v = Q ÷ 3600 ÷ A(m/s;Q 为 m³/h)。</item>
    /// <item>雷诺数:Re = v·d_h/ν(ν 按介质 + 温度查物性表)。</item>
    /// <item>摩擦系数:湍流用**阿尔特舒利(Altshul)显式式** λ = 0.11 × (K/d_h + 68/Re)^0.25;
    /// Re 小于 2320 时按层流 λ = 64/Re。</item>
    /// <item>比摩阻:R = λ/d_h × ρv²/2(Pa/m);沿程阻力 ΔP沿 = R × 段长。</item>
    /// <item>局部阻力:ΔP局 = Σζ × ρv²/2(Σζ 由管件表逐件累加,见
    /// <see cref="HydraulicLocalLossTable"/>)。</item>
    /// <item>最不利环路:**沿程 + 局部 + 环路上末端/设备阻力 + 出口动压(+ 水系统静压)**;
    /// 环路连接关系由模型读取器按连接件拓扑给出。</item>
    /// <item>需求值:风系统 **需求全压 = 计算总阻力 × 富余系数(默认 1.1)**;
    /// 水系统 **需求扬程 H = 计算总阻力 × 富余系数 ÷ (ρ·g)**。</item>
    /// <item>校核:模型里读到风机额定全压 / 水泵额定扬程时,给出余量百分比与结论(满足 / 偏紧 / 不足);
    /// 读不到就明说"未校核",不编一个额定值。</item>
    /// </list>
    /// <para>
    /// 物性表:空气与水的常用物性(标准大气压、干空气),按温度**线性插值**;
    /// 本次实际用到的 ρ 与 ν 会写进结果与计算书,便于复核。
    /// </para>
    /// </summary>
    public class HydraulicCalculator : IHydraulicCalculator
    {
        // ---- 介质的常用物性表(标准大气压下的干空气 / 常压水) ----

        private static readonly double[] AirTempC = { 0, 10, 20, 30, 40, 50, 60 };
        private static readonly double[] AirDensity = { 1.293, 1.247, 1.205, 1.165, 1.128, 1.093, 1.060 };
        private static readonly double[] AirViscosity = { 1.33e-5, 1.42e-5, 1.51e-5, 1.60e-5, 1.70e-5, 1.80e-5, 1.90e-5 };

        private static readonly double[] WaterTempC = { 5, 10, 15, 20, 25, 30, 40, 50, 60, 70, 80 };
        private static readonly double[] WaterDensity = { 999.97, 999.70, 999.10, 998.20, 997.05, 995.65, 992.22, 988.03, 983.20, 977.76, 971.80 };
        private static readonly double[] WaterViscosity = { 1.519e-6, 1.306e-6, 1.139e-6, 1.004e-6, 0.893e-6, 0.801e-6, 0.658e-6, 0.553e-6, 0.474e-6, 0.413e-6, 0.365e-6 };

        /// <summary>介质密度 kg/m³(按温度插值;超范围按端点值)。</summary>
        public static double Density(HydraulicKind kind, double tempC)
        {
            return kind == HydraulicKind.WaterPipe
                ? Interpolate(WaterTempC, WaterDensity, tempC)
                : Interpolate(AirTempC, AirDensity, tempC);
        }

        /// <summary>介质运动粘度 m²/s(按温度插值;超范围按端点值)。</summary>
        public static double KinematicViscosity(HydraulicKind kind, double tempC)
        {
            return kind == HydraulicKind.WaterPipe
                ? Interpolate(WaterTempC, WaterViscosity, tempC)
                : Interpolate(AirTempC, AirViscosity, tempC);
        }

        /// <summary>介质在给定温度下的动压 ρv²/2 Pa(供读取器算风口等 ζ·动压 用,保证与计算器同源)。</summary>
        public static double DynamicPressure(HydraulicKind kind, double tempC, double velocityMs)
        {
            double rho = Density(kind, tempC);
            return rho * velocityMs * velocityMs / 2.0;
        }

        /// <summary>
        /// 摩擦系数 λ。
        /// <para>湍流(Re ≥ 2320):λ = 0.11 × (相对粗糙度 + 68/Re)^0.25 —— 阿尔特舒利显式式;
        /// 层流(Re 小于 2320):λ = 64/Re。相对粗糙度 = K ÷ 水力直径(无量纲)。</para>
        /// </summary>
        public static double FrictionFactor(double reynolds, double relativeRoughness)
        {
            if (reynolds <= 0) return 0.0;
            if (reynolds < HvacConstants.LaminarReynoldsLimit) return 64.0 / reynolds;

            double k = relativeRoughness > 0 ? relativeRoughness : 0.0;
            double term = k + 68.0 / reynolds;
            if (term <= 0) return 0.0;
            return 0.11 * Math.Pow(term, 0.25);
        }

        /// <summary>
        /// 算一个管段的水力参数(比摩阻 / 沿程 / 局部 / 动压)。
        /// 界面"手改一个管段立刻看阻力"与 Revit 读取器逐件累加后复核都走这里,保证口径唯一。
        /// </summary>
        public static HydraulicSegmentResult CalculateSegment(HydraulicSegment segment, HydraulicKind kind,
            double tempC, double defaultRoughnessMm)
        {
            if (segment == null) throw new ArgumentNullException(nameof(segment));

            double rho = Density(kind, tempC);
            double nu = KinematicViscosity(kind, tempC);
            double k = segment.RoughnessMm > 0 ? segment.RoughnessMm : defaultRoughnessMm;

            var row = new HydraulicSegmentResult
            {
                Name = segment.Name,
                ElementId = segment.ElementId,
                SectionText = segment.SectionText,
                FlowM3H = segment.FlowM3H,
                LengthM = segment.LengthM,
                LocalZetaSum = segment.LocalZetaSum,
                LocalNote = segment.LocalNote,
                OnCriticalPath = segment.OnCriticalPath,
                AreaM2 = segment.AreaM2,
                HydraulicDiameterM = segment.HydraulicDiameterM,
                RoughnessMm = k,
                Source = segment.ElementId > 0 ? "模型" : "手工"
            };

            if (row.AreaM2 <= 1e-9 || row.HydraulicDiameterM <= 1e-9 || segment.FlowM3H <= 0)
            {
                row.Source = row.Source + ";数据不完整(缺流量或断面),未参与计算";
                return row;
            }

            double v = segment.FlowM3H / 3600.0 / row.AreaM2;
            double re = v * row.HydraulicDiameterM / nu;
            double lambda = FrictionFactor(re, k / 1000.0 / row.HydraulicDiameterM);
            double pd = rho * v * v / 2.0;
            double r = lambda / row.HydraulicDiameterM * pd;

            row.VelocityMs = v;
            row.Reynolds = re;
            row.FrictionFactor = lambda;
            row.DynamicPressurePa = pd;
            row.SpecificFrictionPaPerM = r;
            row.FrictionLossPa = r * segment.LengthM;
            row.LocalLossPa = segment.LocalZetaSum * pd;
            row.TotalLossPa = row.FrictionLossPa + row.LocalLossPa;
            return row;
        }

        /// <summary>算最不利环路(见类注释的口径)。</summary>
        public HydraulicResult Calculate(HydraulicInput input, HydraulicCoefficients coefficients)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            var c = coefficients ?? HydraulicCoefficients.CreateDefault();
            bool water = input.Kind == HydraulicKind.WaterPipe;
            double rho = Density(input.Kind, input.MediumTempC);
            double nu = KinematicViscosity(input.Kind, input.MediumTempC);
            double defaultK = water ? c.PipeRoughnessMm : c.DuctRoughnessMm;
            if (defaultK <= 0) defaultK = water ? HvacConstants.PipeRoughnessMm : HvacConstants.DuctRoughnessMm;

            var result = new HydraulicResult
            {
                Kind = input.Kind,
                SystemName = input.SystemName,
                DensityKgM3 = rho,
                KinematicViscosityM2S = nu,
                RatedPressurePa = input.RatedPressurePa,
                RatedHeadM = input.RatedHeadM,
                ExtraFactor = input.ExtraFactor > 0 ? input.ExtraFactor : (water ? c.WaterExtraFactor : c.AirExtraFactor)
            };

            // ---- 1. 逐段计算 ----
            var segments = input.Segments ?? new List<HydraulicSegment>();
            var incomplete = new List<string>();
            foreach (var segment in segments)
            {
                if (segment == null) continue;
                var row = CalculateSegment(segment, input.Kind, input.MediumTempC, defaultK);
                result.Segments.Add(row);
                if (row.VelocityMs <= 0 && row.TotalLossPa <= 0)
                    incomplete.Add(string.IsNullOrEmpty(row.Name) ? ("#" + row.ElementId) : row.Name);
            }

            // ---- 2. 环路阻力项(末端 / 设备);出口动压由本计算器补 ----
            var items = input.Terminals ?? new List<HydraulicTerminal>();
            foreach (var terminal in items)
            {
                if (terminal == null) continue;
                result.Items.Add(new HydraulicItemResult
                {
                    Name = terminal.Name,
                    Kind = terminal.Kind,
                    KindText = KindText(terminal.Kind),
                    ResistancePa = terminal.ResistancePa,
                    Source = terminal.Source,
                    OnCriticalPath = terminal.OnCriticalPath
                });
            }

            // ---- 3. 环路标记:一个都没标 → 说明没有拓扑信息,按"全部之和"保守计入并写清 ----
            var pending = new List<string>();
            if (!string.IsNullOrEmpty(input.PendingNote)) pending.Add(input.PendingNote);

            bool anyCritical = false;
            foreach (var row in result.Segments) if (row.OnCriticalPath) { anyCritical = true; break; }
            if (!anyCritical)
                foreach (var item in result.Items) if (item.OnCriticalPath) { anyCritical = true; break; }

            if (!anyCritical && (result.Segments.Count > 0 || result.Items.Count > 0))
            {
                foreach (var row in result.Segments) row.OnCriticalPath = true;
                foreach (var item in result.Items) item.OnCriticalPath = true;
                pending.Add("本次输入没有最不利环路的连接关系(手工录入,或模型管网未连通),已按**全部管段与阻力项之和**保守计入;" +
                            "从模型拾取系统时会按连接件拓扑自动判定最不利环路。");
            }

            // ---- 4. 汇总 ----
            double friction = 0.0, local = 0.0, terminalPa = 0.0, equipmentPa = 0.0;
            int criticalSegments = 0;
            foreach (var row in result.Segments)
            {
                if (!row.OnCriticalPath) continue;
                friction += row.FrictionLossPa;
                local += row.LocalLossPa;
                criticalSegments++;
            }
            foreach (var item in result.Items)
            {
                if (!item.OnCriticalPath) continue;
                if (item.Kind == HydraulicItemKind.Equipment) equipmentPa += item.ResistancePa;
                else if (item.Kind == HydraulicItemKind.Terminal) terminalPa += item.ResistancePa;
            }

            // 出口动压损失:风系统开式出口按出口管段的动压计(水系统闭式环路无此项)
            double outletPa = 0.0;
            bool hasOutletItem = false;
            foreach (var item in result.Items) if (item.Kind == HydraulicItemKind.OutletDynamic) hasOutletItem = true;

            if (!water && input.IncludeOutletDynamic && !hasOutletItem && criticalSegments > 0)
            {
                var outlet = FindOutletSegment(result, input.OutletSegmentElementId);
                if (outlet != null)
                {
                    outletPa = outlet.DynamicPressurePa;
                    result.Items.Add(new HydraulicItemResult
                    {
                        Name = "出口动压损失(按管段「" + outlet.Name + "」动压)",
                        Kind = HydraulicItemKind.OutletDynamic,
                        KindText = KindText(HydraulicItemKind.OutletDynamic),
                        ResistancePa = outletPa,
                        Source = "ρv²/2,按出口管段动压计",
                        OnCriticalPath = true
                    });
                    if (input.OutletSegmentElementId <= 0)
                        pending.Add("未指定出口管段,出口动压按最不利环路上动压最大的管段「" + outlet.Name + "」取值。");
                }
            }
            else if (hasOutletItem)
            {
                foreach (var item in result.Items)
                    if (item.Kind == HydraulicItemKind.OutletDynamic && item.OnCriticalPath) outletPa += item.ResistancePa;
            }

            // 静压:水系统按高差折算(给高差优先),风系统为 0
            double staticPa = 0.0;
            if (water)
            {
                staticPa = input.StaticHeightM > 0
                    ? rho * c.GravityM2S * input.StaticHeightM
                    : input.StaticPressurePa;
            }

            double total = friction + local + terminalPa + equipmentPa + outletPa + staticPa;
            result.CriticalSegmentCount = criticalSegments;
            result.FrictionTotalPa = friction;
            result.LocalTotalPa = local;
            result.TerminalTotalPa = terminalPa;
            result.EquipmentTotalPa = equipmentPa;
            result.OutletDynamicPa = outletPa;
            result.StaticPa = staticPa;
            result.TotalResistancePa = total;
            result.RequiredPressurePa = total * result.ExtraFactor;
            result.RequiredHeadM = water ? result.RequiredPressurePa / (rho * c.GravityM2S) : 0.0;

            // ---- 5. 最不利环路名称(读取器判定的末端名;手工录入时退到环路上第一个末端) ----
            result.CriticalPathName = input.CriticalPathName ?? "";
            if (string.IsNullOrEmpty(result.CriticalPathName))
            {
                foreach (var item in result.Items)
                {
                    if (!item.OnCriticalPath) continue;
                    if (item.Kind == HydraulicItemKind.OutletDynamic) continue;
                    result.CriticalPathName = item.Name;
                    break;
                }
            }

            // ---- 7. 并联环路平衡 + 系统阻力特性曲线 ----
            result.ImbalanceLimitPct = c.ImbalanceLimitPct > 0
                ? c.ImbalanceLimitPct
                : HvacConstants.HydraulicImbalanceLimitPct;
            BuildBranches(input, result, water);
            BuildCurve(input, result);

            // ---- 8. 与模型里的风机 / 水泵额定值校核 ----
            ApplyCheck(input, result, water);

            // ---- 7. 口径与待补 ----
            result.Note = BuildNote(input, result, criticalSegments, water);
            if (incomplete.Count > 0)
                pending.Add("有 " + incomplete.Count + " 个管段缺流量或断面,未参与计算(界面里逐段标了「数据不完整」):" +
                            string.Join("、", incomplete.ToArray()));
            if (result.Segments.Count == 0 && result.Items.Count == 0)
                pending.Add("没有读到任何管段或末端/设备:请确认选中的是风系统/水系统的构件(风管、水管、风机、水泵、末端)," +
                            "或确认该系统的管道已在模型中建模。");
            result.PendingNote = string.Join(" ", pending.ToArray());
            return result;
        }

        /// <summary>
        /// 并联环路平衡:逐支路累计阻力 → 与最不利环路比较 → 超限支路给出平衡装置参数
        /// (水:平衡阀 Kv 与阀权度;风:需增加的局部阻力系数 ζ)。没有支路数据就不做(不猜)。
        /// </summary>
        private static void BuildBranches(HydraulicInput input, HydraulicResult result, bool water)
        {
            var branches = input.Branches;
            if (branches == null || branches.Count == 0)
            {
                result.BalanceNote =
                    "本次没有并联支路数据(管网未形成可分辨的支路拓扑),**未做并联环路平衡分析**。" +
                    "从模型拾取系统时会按连接件拓扑逐末端给出支路;" +
                    "⚠ 风机 / 水泵的工况点需与**厂家性能曲线求交**,插件不内置设备曲线。";
                return;
            }

            // 段阻力按元素 Id 索引(支路只带管段 Id,数值一律来自同一份逐段计算结果)
            var lossById = new Dictionary<int, double>();
            var dynamicById = new Dictionary<int, double>();
            var flowById = new Dictionary<int, double>();
            foreach (var row in result.Segments)
            {
                if (row.ElementId == 0) continue;
                lossById[row.ElementId] = row.TotalLossPa;
                dynamicById[row.ElementId] = row.DynamicPressurePa;
                flowById[row.ElementId] = row.FlowM3H;
            }

            // 末端阻力按"支路末端元素 Id"取;取不到按 0 并在结论里说明
            var terminalByElementId = new Dictionary<int, double>();
            foreach (var terminal in input.Terminals)
            {
                if (terminal == null || terminal.ElementId == 0) continue;
                terminalByElementId[terminal.ElementId] = terminal.ResistancePa;
            }

            // 逐支路累计
            var computed = new List<HydraulicBranchResult>();
            var branchFlows = new List<double>();
            foreach (var branch in branches)
            {
                if (branch == null) continue;
                var row = new HydraulicBranchResult
                {
                    Name = branch.Name,
                    TerminalElementId = branch.TerminalElementId,
                    Path = branch.SegmentSummary
                };

                double segmentLoss = 0;
                int count = 0;
                double lastFlow = 0;
                double lastDynamic = 0;
                var ids = branch.SegmentElementIds ?? new List<int>();
                foreach (int id in ids)
                {
                    double loss;
                    if (lossById.TryGetValue(id, out loss))
                    {
                        segmentLoss += loss;
                        count++;
                    }
                    double flow;
                    if (flowById.TryGetValue(id, out flow)) { lastFlow = flow; }
                    double dynamic;
                    if (dynamicById.TryGetValue(id, out dynamic)) { lastDynamic = dynamic; }
                }

                double terminalPa;
                if (!terminalByElementId.TryGetValue(branch.TerminalElementId, out terminalPa)) terminalPa = 0;

                row.SegmentCount = count;
                row.SegmentLossPa = segmentLoss;
                row.TerminalPa = terminalPa;
                row.TotalLossPa = segmentLoss + terminalPa;
                row.ReferenceDynamicPa = lastDynamic;
                computed.Add(row);
                branchFlows.Add(lastFlow);
            }

            if (computed.Count == 0)
            {
                result.BalanceNote = "支路数据里没有可用的路径,未做并联环路平衡分析。";
                return;
            }

            double criticalPa = 0;
            foreach (var row in computed) if (row.TotalLossPa > criticalPa) criticalPa = row.TotalLossPa;

            double limit = result.ImbalanceLimitPct;
            double maxImbalance = 0;
            int unbalanced = 0;
            for (int i = 0; i < computed.Count; i++)
            {
                var row = computed[i];
                double branchFlow = branchFlows[i];
                row.IsCritical = criticalPa > 0 && Math.Abs(row.TotalLossPa - criticalPa) < 1e-9;
                row.ImbalancePa = criticalPa - row.TotalLossPa;
                row.ImbalancePct = criticalPa > 0 ? row.ImbalancePa / criticalPa * 100.0 : 0.0;
                if (Math.Abs(row.ImbalancePct) > maxImbalance) maxImbalance = Math.Abs(row.ImbalancePct);
                row.WithinLimit = Math.Abs(row.ImbalancePct) <= limit;
                row.RequiredAbsorbPa = row.WithinLimit || row.IsCritical ? 0.0 : row.ImbalancePa;

                if (row.IsCritical)
                {
                    row.Conclusion = "最不利环路(平衡基准,不设平衡装置)";
                }
                else if (row.WithinLimit)
                {
                    row.Conclusion = "不平衡率 " + row.ImbalancePct.ToString("0.#") + "% ≤ 允许 " + limit.ToString("0.#") +
                                     "%:与最不利环路差 " + row.ImbalancePa.ToString("0.#") + " Pa,在允许范围内,可不设平衡装置";
                }
                else
                {
                    unbalanced++;
                    if (water)
                    {
                        // 平衡阀 Kv = Q ÷ √(ΔP[bar]);阀权度 S = 需吸收压差 ÷ 最不利环路总阻力
                        if (row.RequiredAbsorbPa > 0)
                            row.ValveKv = branchFlow > 0
                                ? branchFlow / Math.Sqrt(row.RequiredAbsorbPa / HvacConstants.PascalPerBar)
                                : 0.0;
                        row.ValveAuthority = criticalPa > 0 ? row.RequiredAbsorbPa / criticalPa : 0.0;
                        row.Conclusion = "不平衡率 " + row.ImbalancePct.ToString("0.#") + "% 超允许 " + limit.ToString("0.#") +
                                         "%:需吸收 " + row.RequiredAbsorbPa.ToString("0.#") + " Pa → 配平衡阀 Kv ≈ " +
                                         row.ValveKv.ToString("0.##") + " m³/h(支路流量 " + branchFlow.ToString("N0") +
                                         " m³/h)、阀权度 " + row.ValveAuthority.ToString("0.00");
                    }
                    else
                    {
                        row.ZetaToAdd = row.ReferenceDynamicPa > 0
                            ? row.RequiredAbsorbPa / row.ReferenceDynamicPa
                            : 0.0;
                        row.Conclusion = "不平衡率 " + row.ImbalancePct.ToString("0.#") + "% 超允许 " + limit.ToString("0.#") +
                                         "%:需吸收 " + row.RequiredAbsorbPa.ToString("0.#") + " Pa → 需增加局部阻力系数 ζ ≈ " +
                                         row.ZetaToAdd.ToString("0.##") + "(按末端管段动压 " +
                                         row.ReferenceDynamicPa.ToString("0.#") + " Pa 折算)";
                    }
                }
                result.Branches.Add(row);
            }

            // 最不利环路以计算结果为准重标(读取器标的是同一件事,这里再兜一层)
            result.UnbalancedBranchCount = unbalanced;
            result.MaxImbalancePct = maxImbalance;
            result.BalanceNote =
                "并联环路平衡口径:逐支路累计阻力(管段沿程+局部+该支路末端/设备)与**最不利环路**比较," +
                "不平衡率 = (最不利环路 − 该支路) ÷ 最不利环路 × 100%,允许不平衡率取 " + limit.ToString("0.#") +
                "%(工程通行口径,界面上可改)。超限支路的平衡装置参数:" +
                (water
                    ? "平衡阀 Kv = Q ÷ √(ΔP[bar])(20 ℃ 水,Kv 定义式),阀权度 S = 需吸收压差 ÷ 最不利环路总阻力;"
                    : "需增加的局部阻力系数 ζ = 需吸收压差 ÷ (ρv²/2,按该支路末端管段动压折算);") +
                "允许范围内不设平衡装置。⚠ 阻力特性曲线按 ΔP(Q) = 静压 + (总阻力 − 静压)×(Q ÷ Q设计)² 给出," +
                "**与厂家风机/水泵性能曲线的交点才是工况点** —— 插件不内置设备曲线,请用样本曲线核对。";
        }

        /// <summary>
        /// 系统阻力特性曲线(50%~130% 设计流量,每 10% 一点)。
        /// 口径:阻力与流量平方成正比,但**静压不随流量变化**,故 ΔP(Q) = 静压 + (总阻力 − 静压)×(Q/Q设计)²。
        /// </summary>
        private static void BuildCurve(HydraulicInput input, HydraulicResult result)
        {
            double designFlow = 0;
            foreach (var row in result.Segments)
            {
                if (!row.OnCriticalPath) continue;
                if (row.FlowM3H > designFlow) designFlow = row.FlowM3H;
            }
            if (designFlow <= 0)
            {
                foreach (var row in result.Segments) if (row.FlowM3H > designFlow) designFlow = row.FlowM3H;
            }
            if (designFlow <= 0) return;

            double variable = result.TotalResistancePa - result.StaticPa;   // 随流量平方变化的那部分
            for (int pct = 50; pct <= 130; pct += 10)
            {
                double ratio = pct / 100.0;
                double resistance = result.StaticPa + variable * ratio * ratio;
                result.Curve.Add(new HydraulicCurvePoint
                {
                    FlowRatioPct = pct,
                    FlowM3H = designFlow * ratio,
                    ResistancePa = resistance,
                    RequiredPa = resistance * result.ExtraFactor
                });
            }
        }

        /// <summary>出口管段:优先按元素 Id 找;没指定则取环路上动压最大的一段(并在待补说明里写明)。</summary>
        private static HydraulicSegmentResult FindOutletSegment(HydraulicResult result, int outletSegmentElementId)
        {
            HydraulicSegmentResult best = null;
            foreach (var row in result.Segments)
            {
                if (!row.OnCriticalPath) continue;
                if (outletSegmentElementId > 0 && row.ElementId != outletSegmentElementId) continue;
                if (outletSegmentElementId > 0) return row;
                if (best == null || row.DynamicPressurePa > best.DynamicPressurePa) best = row;
            }
            return best;
        }

        /// <summary>额定值与需求值对比(模型读到额定参数才校核;读不到就明说,不编)。</summary>
        private static void ApplyCheck(HydraulicInput input, HydraulicResult result, bool water)
        {
            if (water)
            {
                if (input.RatedHeadM <= 0)
                {
                    result.CheckVerdict = "未校核:模型里的水泵没有读到额定扬程(可在泵族参数里补,或手工填额定值后重算)。";
                    return;
                }
                double margin = (input.RatedHeadM - result.RequiredHeadM) / result.RequiredHeadM * 100.0;
                result.MarginPct = margin;
                result.CheckVerdict = Verdict("水泵额定扬程 " + input.RatedHeadM.ToString("0.##") + " m",
                    result.RequiredHeadM.ToString("0.##") + " m", margin);
            }
            else
            {
                if (input.RatedPressurePa <= 0)
                {
                    result.CheckVerdict = "未校核:模型里的风机没有读到额定全压(可在风机族参数里补,或手工填额定值后重算)。";
                    return;
                }
                double margin = (input.RatedPressurePa - result.RequiredPressurePa) / result.RequiredPressurePa * 100.0;
                result.MarginPct = margin;
                result.CheckVerdict = Verdict("风机额定全压 " + input.RatedPressurePa.ToString("0.#") + " Pa",
                    result.RequiredPressurePa.ToString("0.#") + " Pa", margin);
            }
        }

        private static string Verdict(string ratedText, string requiredText, double margin)
        {
            string state = margin < 0 ? "不足" : (margin < 10.0 ? "偏紧" : "满足");
            return "校核:" + ratedText + " 对需求 " + requiredText + ",余量 " + margin.ToString("0.#") + "% → " + state + "。";
        }

        private static string BuildNote(HydraulicInput input, HydraulicResult result, int criticalSegments, bool water)
        {
            string medium = water
                ? "水(ρ=" + result.DensityKgM3.ToString("0.##") + " kg/m³、ν=" + result.KinematicViscosityM2S.ToString("0.000e+0") + " m²/s,按 " + input.MediumTempC.ToString("0.#") + " ℃ 查常用物性表)"
                : "空气(ρ=" + result.DensityKgM3.ToString("0.###") + " kg/m³、ν=" + result.KinematicViscosityM2S.ToString("0.000e+0") + " m²/s,按 " + input.MediumTempC.ToString("0.#") + " ℃ 查常用物性表)";

            return "口径:① 沿程阻力用达西-魏斯巴赫式,摩擦系数 λ 取阿尔特舒利显式式 0.11×(K/d+68/Re)^0.25" +
                   "(Re 小于 2320 时按 64/Re);② 局部阻力 = Σζ×ρv²/2;③ 水力直径 = 4×断面积÷湿周(矩形风管即流速当量直径);" +
                   "④ 最不利环路 = " + criticalSegments + " 段管段的沿程与局部 + 环路上末端/设备阻力" +
                   (water ? " + 静压,再乘富余系数后除以 ρg 得扬程" : " + 出口动压,再乘富余系数得需求全压") +
                   ";⑤ 介质与物性:" + medium + "。";
        }

        private static string KindText(HydraulicItemKind kind)
        {
            switch (kind)
            {
                case HydraulicItemKind.Equipment: return "设备";
                case HydraulicItemKind.OutletDynamic: return "出口动压";
                default: return "末端";
            }
        }

        /// <summary>按温度在物性表里线性插值(超出表范围取端点值)。</summary>
        private static double Interpolate(double[] temps, double[] values, double tempC)
        {
            if (temps == null || values == null || temps.Length == 0) return 0.0;
            if (tempC <= temps[0]) return values[0];
            int last = temps.Length - 1;
            if (tempC >= temps[last]) return values[last];

            for (int i = 1; i <= last; i++)
            {
                if (tempC > temps[i]) continue;
                double span = temps[i] - temps[i - 1];
                if (span <= 0) return values[i];
                double ratio = (tempC - temps[i - 1]) / span;
                return values[i - 1] + (values[i] - values[i - 1]) * ratio;
            }
            return values[last];
        }
    }
}
