using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI.Selection;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.Revit.Services
{
    /// <summary>
    /// 从 Revit 模型读取**风系统 / 水系统**管网(需求 2.3 / 2.4)。
    /// <para>
    /// 用户点「水力计算 → 风系统 / 水系统」后在模型里选**该系统任意构件**(风管/水管/管件/风口/风机/水泵):
    /// 读取器由该构件反查所属 <see cref="MEPSystem"/>,再把整个系统的构件读成 Core 的
    /// <see cref="HydraulicInput"/>:管段(长度/断面/流量)、管件(按族名匹配局部阻力系数表)、
    /// 末端/设备(阻力取模型参数或 ζ·动压)、风机/水泵(额定全压/扬程,用于校核)。
    /// </para>
    /// <para>
    /// **最不利环路**用连接件拓扑求:把系统的构件当节点、连接关系当边,从风机/水泵出发深度优先走到各末端,
    /// 取累计阻力**最大**的那条路,标记到管段与末端上(<see cref="HydraulicSegment.OnCriticalPath"/>) ——
    /// 拓扑算不出来时(未成系统、找不到风机、环路发散)不猜:不标任何标记,由 Core 计算器按"全部之和"保守计入并在待补说明里写明。
    /// </para>
    /// <para>
    /// 只读操作,不开事务;必须在**没有模态窗**的 IExternalCommand 上下文里调用(见 <see cref="RevitElementPicker"/> 注释)。
    /// </para>
    /// </summary>
    internal static class RevitHydraulicReader
    {
        private const double RoundTolerance = 1e-9;

        /// <summary>深度优先搜索的展开次数上限(环形管网会指数发散;超限即放弃标记,退回保守口径)。</summary>
        private const int MaxSearchSteps = 200000;

        // ================================================================== 拾取

        /// <summary>
        /// 让用户选择系统构件(可多选,回车结束)。返回值仅用于反查系统,不做几何计算。
        /// </summary>
        /// <returns>选中的元素 Id;用户 Esc 取消时返回 null。</returns>
        public static IList<ElementId> PickSystemMembers(Autodesk.Revit.UI.UIDocument uidoc, HydraulicKind kind, out string note)
        {
            note = "";
            if (uidoc == null)
            {
                note = "没有活动文档,无法拾取。";
                return null;
            }

            string what = kind == HydraulicKind.WaterPipe ? "水管 / 管件 / 水泵 / 末端" : "风管 / 管件 / 风口 / 风机";
            try
            {
                var references = uidoc.Selection.PickObjects(ObjectType.Element, new SystemMemberSelectionFilter(kind),
                    "请选择该" + (kind == HydraulicKind.WaterPipe ? "水" : "风") + "系统上的任意构件(" + what +
                    ",可多选,回车结束;Esc 取消)");

                var ids = new List<ElementId>();
                foreach (var reference in references)
                {
                    if (reference == null || reference.ElementId == null) continue;
                    if (!ids.Contains(reference.ElementId)) ids.Add(reference.ElementId);
                }
                note = "已选 " + ids.Count + " 个构件";
                return ids;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                note = "已取消";
                return null;
            }
        }

        // ================================================================== 读取

        /// <summary>
        /// 读取选中构件所属系统,生成水力计算输入。
        /// </summary>
        /// <param name="doc">当前文档。</param>
        /// <param name="pickedIds">用户选中的构件(用于反查系统)。</param>
        /// <param name="kind">风系统 / 水系统。</param>
        /// <param name="coefficients">系数集(局部阻力系数表来自这里)。</param>
        /// <param name="note">读取说明(选中了什么、读到多少、哪些没读到)。</param>
        public static HydraulicInput ReadSystem(Document doc, IList<ElementId> pickedIds, HydraulicKind kind,
            HydraulicCoefficients coefficients, out string note)
        {
            note = "";
            if (doc == null || pickedIds == null || pickedIds.Count == 0) return null;

            var coefficientsUsed = coefficients ?? HydraulicCoefficients.CreateDefault();
            var picked = new List<Element>();
            foreach (var id in pickedIds)
            {
                var element = doc.GetElement(id);
                if (element != null) picked.Add(element);
            }
            if (picked.Count == 0)
            {
                note = "选中的构件已不存在,请重新拾取。";
                return null;
            }

            // ---- 反查系统:选中构件 → MEPSystem ----
            MEPSystem system = null;
            foreach (var element in picked)
            {
                system = ResolveSystem(element, kind);
                if (system != null) break;
            }

            var input = new HydraulicInput
            {
                Kind = kind,
                MediumTempC = kind == HydraulicKind.WaterPipe ? 10.0 : 20.0,
                FromModel = true,
                ExtraFactor = kind == HydraulicKind.WaterPipe
                    ? coefficientsUsed.WaterExtraFactor
                    : coefficientsUsed.AirExtraFactor
            };

            var members = new List<Element>();
            if (system != null)
            {
                foreach (Element element in system.Elements) members.Add(element);
                input.SystemName = SafeName(system);
                input.SystemTypeName = SystemTypeName(doc, system);
            }
            else
            {
                // 没成系统(风管/水管未指定系统):只按所选构件算,不猜管网
                members.AddRange(picked);
                input.SystemName = "未成系统的所选构件";
                input.SystemTypeName = "";
                input.PendingNote = "所选构件没有归属到 " + (kind == HydraulicKind.WaterPipe ? "管道" : "风管") +
                                    "系统(Revit 里未指定系统),本次只按**所选构件**计算、无法判定最不利环路;" +
                                    "建议在模型里把这些管段归入同一个系统后再拾取。";
            }

            // ---- 分类:管段 / 管件 / 末端 / 设备 ----
            var curves = new List<MEPCurve>();
            var fittings = new List<Element>();
            var terminals = new List<Element>();
            var equipments = new List<Element>();
            int unknownCategories = 0;

            foreach (var element in members)
            {
                if (element == null) continue;
                var curve = element as MEPCurve;
                if (curve != null) { curves.Add(curve); continue; }
                if (element is Duct || element is Pipe) { curves.Add(element as MEPCurve); continue; }

                var category = CategoryId(element);
                if (IsFittingCategory(category, kind)) { fittings.Add(element); continue; }
                if (IsEquipmentCategory(category)) { equipments.Add(element); continue; }
                if (IsTerminalCategory(category, kind)) { terminals.Add(element); continue; }
                unknownCategories++;
            }

            // ---- 管段 ----
            var segmentByElementId = new Dictionary<int, HydraulicSegment>();
            var curveByElementId = new Dictionary<int, MEPCurve>();
            var sizeProblems = new List<string>();
            foreach (var curve in curves)
            {
                var segment = ReadSegment(curve, kind, out string segmentProblem);
                if (segment == null) continue;
                if (!string.IsNullOrEmpty(segmentProblem) && sizeProblems.Count < 6) sizeProblems.Add(segmentProblem);
                segment.Name = SafeName(curve);
                segment.ElementId = curve.Id.IntegerValue;
                input.Segments.Add(segment);
                segmentByElementId[curve.Id.IntegerValue] = segment;
                curveByElementId[curve.Id.IntegerValue] = curve;
            }

            // ---- 管件:按族名匹配局部阻力系数表,ζ 累加到所连接的管段上 ----
            var unmatchedFittings = new List<string>();
            var orphanFittings = new List<string>();
            double orphanZeta = 0.0;
            foreach (var fitting in fittings)
            {
                string tableName = MapFittingToTable(fitting, kind);
                double zeta = HydraulicLocalLossTable.ZetaOf(coefficientsUsed.LocalLossItems, tableName);
                if (double.IsNaN(zeta)) zeta = HydraulicLocalLossTable.ZetaOf(HydraulicLocalLossTable.CreateDefaults(kind), tableName);
                if (double.IsNaN(zeta) || zeta <= 0)
                {
                    unmatchedFittings.Add(SafeName(fitting));
                    continue;
                }

                var host = HostCurveOf(fitting, curveByElementId);
                if (host == null)
                {
                    // 归属不到管段:不静默丢弃 —— 记下来在界面提示,用户可手工填到相应管段
                    orphanZeta += zeta;
                    if (orphanFittings.Count < 8) orphanFittings.Add(SafeName(fitting) + "(ζ=" + zeta.ToString("0.##") + ")");
                    continue;
                }

                var segment = segmentByElementId[host.Id.IntegerValue];
                segment.LocalZetaSum += zeta;
                string label = Trimmed(tableName) + "(ζ=" + zeta.ToString("0.##") + ")";
                segment.LocalNote = string.IsNullOrEmpty(segment.LocalNote) ? label : segment.LocalNote + "、" + label;
            }

            // ---- 末端 / 设备(阻力取模型参数;取不到用 ζ·动压或留 0 并提示) ----
            var terminalProblems = new List<string>();
            foreach (var terminal in terminals)
            {
                var item = ReadTerminal(terminal, kind, coefficientsUsed, curveByElementId, input.MediumTempC, out string problem);
                if (item != null) input.Terminals.Add(item);
                if (!string.IsNullOrEmpty(problem) && terminalProblems.Count < 6) terminalProblems.Add(problem);
            }

            var equipmentProblems = new List<string>();
            foreach (var equipment in equipments)
            {
                // 风机 / 水泵是"压力源",不是阻力项:它们的额定值另读(见下),不作为环路阻力
                if (IsPressureSource(equipment, kind)) continue;

                var item = ReadEquipment(equipment, kind, curveByElementId, out string problem);
                if (item != null) input.Terminals.Add(item);
                if (!string.IsNullOrEmpty(problem) && equipmentProblems.Count < 6) equipmentProblems.Add(problem);
            }

            // ---- 额定参数(风机全压 / 水泵扬程):只读模型,读不到就明说 ----
            string ratedNote = "";
            var source = BaseEquipmentOf(system) ?? FindEquipmentElement(equipments, kind);
            if (source != null)
            {
                if (kind == HydraulicKind.WaterPipe)
                {
                    double head;
                    if (TryReadLength(source, PumpHeadNames, out head)) input.RatedHeadM = head;
                    else ratedNote = "模型里的水泵没有「扬程」参数,未做扬程校核(可手工填额定扬程后重算)。";
                }
                else
                {
                    double pressure;
                    if (TryReadPressure(source, FanPressureNames, out pressure)) input.RatedPressurePa = pressure;
                    else ratedNote = "模型里的风机没有「全压/机外静压」参数,未做全压校核(可手工填额定全压后重算)。";
                }
            }
            else
            {
                ratedNote = kind == HydraulicKind.WaterPipe
                    ? "系统里没找到水泵,未做扬程校核。"
                    : "系统里没找到风机,未做全压校核,也无法判定最不利环路的起点。";
            }

            // ---- 最不利环路 ----
            string pathNote = MarkCriticalPath(input, members, source, curveByElementId, segmentByElementId, coefficientsUsed);

            // ---- 出口动压(风系统):取系统里风口所在管段;找不到就交给 Core 按最大动压段兜底 ----
            if (kind == HydraulicKind.AirDuct)
            {
                var supplyTerminal = terminals.Count > 0 ? terminals[0] : null;
                var outletCurve = supplyTerminal == null ? null : HostCurveOf(supplyTerminal, curveByElementId);
                if (outletCurve != null) input.OutletSegmentElementId = outletCurve.Id.IntegerValue;
            }

            // ---- 说明 ----
            int critical = input.CriticalSegmentCount;
            input.SourceNote = "数据来自模型:" + input.SystemName +
                               (string.IsNullOrEmpty(input.SystemTypeName) ? "" : "(" + input.SystemTypeName + ")") +
                               ",共读入 " + input.Segments.Count + " 段管段(其中 " + critical + " 段在最不利环路上)、" +
                               fittings.Count + " 个管件、" + terminals.Count + " 个末端、" + equipments.Count + " 台设备。";

            var pending = new List<string>();
            if (!string.IsNullOrEmpty(input.PendingNote)) pending.Add(input.PendingNote);
            if (sizeProblems.Count > 0)
                pending.Add("以下管道读不到断面/长度/流量,已列入管段表并按「数据不完整」处理(请核对或手工补):" +
                            string.Join("、", sizeProblems.ToArray()));
            if (unmatchedFittings.Count > 0)
                pending.Add("有 " + unmatchedFittings.Count + " 个管件在局部阻力系数表里匹配不到(如「" +
                            string.Join("、", unmatchedFittings.Take(4).ToArray()) + "」),**未计其局部阻力**;" +
                            "可在界面按管件实际形式手工加到相应管段的 Σζ。");
            if (orphanFittings.Count > 0)
                pending.Add("有 " + orphanFittings.Count + " 个管件(ζ 合计约 " + orphanZeta.ToString("0.##") +
                            ")找不到所连接的管段,未计入:" + string.Join("、", orphanFittings.ToArray()));
            if (unknownCategories > 0)
                pending.Add("系统里还有 " + unknownCategories + " 个构件不属于管段/管件/末端/设备,已忽略。");
            pending.AddRange(terminalProblems);
            pending.AddRange(equipmentProblems);
            if (!string.IsNullOrEmpty(ratedNote)) pending.Add(ratedNote);
            if (!string.IsNullOrEmpty(pathNote)) pending.Add(pathNote);

            var smoke = string.Join(" ", pending.ToArray());
            input.PendingNote = smoke;
            note = input.SourceNote + (string.IsNullOrEmpty(smoke) ? "" : " ⚠ " + smoke);
            return input;
        }

        // ================================================================== 管段

        /// <summary>读一个管段:断面(圆内径 / 矩形宽高)、长度、流量(取两端连接件的较大者)。</summary>
        private static HydraulicSegment ReadSegment(MEPCurve curve, HydraulicKind kind, out string problem)
        {
            problem = "";
            var segment = new HydraulicSegment { Shape = HydraulicShape.Round };

            // 长度
            try
            {
                var location = curve.Location as LocationCurve;
                if (location != null && location.Curve != null)
                    segment.LengthM = UnitUtils.ConvertFromInternalUnits(location.Curve.Length, DisplayUnitType.DUT_METERS);
            }
            catch { /* 取不到长度按 0,由调用方计入"读不到" */ }

            // 断面
            bool sizeOk = false;
            var duct = curve as Duct;
            if (duct != null)
            {
                double width = 0, height = 0, diameter = 0;
                try { width = duct.Width; } catch { }
                try { height = duct.Height; } catch { }
                try { diameter = duct.Diameter; } catch { }

                bool rectangular = width > RoundTolerance && height > RoundTolerance &&
                                   Math.Abs(width - height) > RoundTolerance;
                var ductShape = DuctShape(duct);
                if (ductShape == ConnectorProfileType.Rectangular || (rectangular && ductShape != ConnectorProfileType.Round))
                {
                    segment.Shape = HydraulicShape.Rectangular;
                    segment.WidthM = UnitUtils.ConvertFromInternalUnits(width, DisplayUnitType.DUT_METERS);
                    segment.HeightM = UnitUtils.ConvertFromInternalUnits(height, DisplayUnitType.DUT_METERS);
                    sizeOk = segment.WidthM > 0 && segment.HeightM > 0;
                }
                else if (diameter > RoundTolerance)
                {
                    segment.Shape = HydraulicShape.Round;
                    segment.DiameterM = UnitUtils.ConvertFromInternalUnits(diameter, DisplayUnitType.DUT_METERS);
                    sizeOk = segment.DiameterM > 0;
                }
                else if (width > RoundTolerance)
                {
                    segment.Shape = HydraulicShape.Round;
                    segment.DiameterM = UnitUtils.ConvertFromInternalUnits(width, DisplayUnitType.DUT_METERS);
                    sizeOk = segment.DiameterM > 0;
                }
            }

            var pipe = curve as Pipe;
            if (pipe != null)
            {
                // 内径:Revit 把管道内径放在内置参数里(族/管段表给);读不到退到公称直径并写明
                double inner = ReadDoubleParameter(pipe, BuiltInParameter.RBS_PIPE_INNER_DIAM_PARAM);
                if (inner <= RoundTolerance)
                {
                    inner = ReadDoubleParameter(pipe, BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                    if (inner <= RoundTolerance)
                    {
                        try { inner = pipe.Diameter; } catch { }
                    }
                    problem = "管道「" + SafeName(pipe) + "」取不到内径,已按公称直径代入;";
                }
                segment.Shape = HydraulicShape.Round;
                segment.DiameterM = UnitUtils.ConvertFromInternalUnits(inner, DisplayUnitType.DUT_METERS);
                sizeOk = segment.DiameterM > 0;
            }

            // 流量:两端连接件取较大者(管段按最大流量校核最保守)
            double flowM3H = 0;
            try
            {
                var connectors = curve.ConnectorManager == null ? null : curve.ConnectorManager.Connectors;
                if (connectors != null)
                {
                    foreach (Connector connector in connectors)
                    {
                        double flow = Math.Abs(connector.Flow);
                        double converted = UnitUtils.ConvertFromInternalUnits(flow, DisplayUnitType.DUT_CUBIC_METERS_PER_HOUR);
                        if (converted > flowM3H) flowM3H = converted;
                    }
                }
            }
            catch { /* 取不到流量按 0 */ }
            segment.FlowM3H = flowM3H;

            if (!sizeOk || segment.LengthM <= 0 || segment.FlowM3H <= 0)
            {
                problem = (problem + "「" + SafeName(curve) + "」缺" +
                           (!sizeOk ? "断面" : "") + (segment.LengthM <= 0 ? "长度" : "") + (segment.FlowM3H <= 0 ? "流量" : "") +
                           ";").TrimStart(';');
                return segment;      // 仍然返回:界面上能看到这一段并手工补齐,不静默丢弃
            }
            return segment;
        }

        private static ConnectorProfileType DuctShape(Duct duct)
        {
            try
            {
                var type = duct.DuctType;
                if (type != null) return type.Shape;
            }
            catch { }
            return ConnectorProfileType.Invalid;
        }

        // ================================================================== 管件 / 末端 / 设备

        /// <summary>按族/类型名把管件匹配到局部阻力系数表的一条。</summary>
        private static string MapFittingToTable(Element fitting, HydraulicKind kind)
        {
            string text = (SafeName(fitting) + " " + TypeName(fitting)).ToLowerInvariant();
            bool water = kind == HydraulicKind.WaterPipe;

            if (Contains(text, "软接", "软管", "flexible", "flex")) return "软接头";
            if (Contains(text, "过滤器", "filter", "除污")) return water ? "Y 型过滤器" : null;
            if (Contains(text, "平衡阀", "balancing")) return water ? "平衡阀(全开)" : null;
            if (Contains(text, "止回", "check")) return water ? "止回阀" : null;
            if (Contains(text, "截止", "globe")) return water ? "截止阀(全开)" : null;
            if (Contains(text, "闸阀", "gate")) return water ? "闸阀(全开)" : null;
            if (Contains(text, "蝶阀", "butterfly")) return water ? "闸阀(全开)" : "蝶阀(全开)";
            if (Contains(text, "防火阀", "fire damper")) return "防火阀(全开)";
            if (Contains(text, "多叶", "调节阀", "damper", "风阀")) return "多叶调节阀(全开)";

            if (Contains(text, "变径", "渐缩", "缩径", "reducer", "transition"))
                return Contains(text, "扩", "expansion") ? (water ? "渐扩管" : "渐扩管") : (water ? "渐缩管" : "渐缩管");
            if (Contains(text, "渐扩", "扩径")) return water ? "渐扩管" : "渐扩管";
            if (Contains(text, "三通", "tee", "四通", "cross")) return water ? "三通(直通)" : "三通(直通)";
            if (Contains(text, "弯头", "elbow", "弯管"))
            {
                bool fortyFive = Contains(text, "45");
                if (water)
                {
                    if (fortyFive) return "45°弯头";
                    return Contains(text, "螺纹", "丝扣", "threaded") ? "90°弯头(螺纹,DN≤50)" : "90°弯头(焊接)";
                }
                if (fortyFive) return "45°弯头";
                return "90°弯头(圆形 R/D=1.0)";     // 圆形与矩形 90°弯头 ζ 同取 0.25,取值一致
            }
            return null;
        }

        /// <summary>末端(风口/盘管/散热器等):阻力优先取模型参数,取不到按 ζ·动压(风口)估算。</summary>
        private static HydraulicTerminal ReadTerminal(Element element, HydraulicKind kind,
            HydraulicCoefficients coefficients, Dictionary<int, MEPCurve> curveByElementId, double mediumTempC,
            out string problem)
        {
            problem = "";
            var item = new HydraulicTerminal
            {
                Name = SafeName(element),
                ElementId = element.Id.IntegerValue,
                Kind = HydraulicItemKind.Terminal
            };

            double resistance;
            if (TryReadPressure(element, ResistanceNames, out resistance) && resistance > 0)
            {
                item.ResistancePa = resistance;
                item.Source = "模型参数(" + MatchedParameterName(element, ResistanceNames) + " = " + resistance.ToString("0.#") + " Pa)";
                return item;
            }

            if (kind == HydraulicKind.AirDuct)
            {
                // 风口的局部阻力 = ζ × 该段动压(ζ 取表里"送风口/回风口"常用值)
                string tableName = Contains(SafeName(element) + TypeName(element), "回风", "return") ? "回风口(格栅)" : "送风口(散流器)";
                double zeta = HydraulicLocalLossTable.ZetaOf(coefficients.LocalLossItems, tableName);
                if (double.IsNaN(zeta)) zeta = HydraulicLocalLossTable.ZetaOf(HydraulicLocalLossTable.CreateDefaults(kind), tableName);

                var host = HostCurveOf(element, curveByElementId);
                double velocity = 0;
                if (host != null)
                {
                    double area = 0;
                    try
                    {
                        var duct = host as Duct;
                        if (duct != null)
                        {
                            double w = duct.Width, h = duct.Height, d = duct.Diameter;
                            if (w > RoundTolerance && h > RoundTolerance && Math.Abs(w - h) > RoundTolerance)
                                area = UnitUtils.ConvertFromInternalUnits(w, DisplayUnitType.DUT_METERS) *
                                       UnitUtils.ConvertFromInternalUnits(h, DisplayUnitType.DUT_METERS);
                            else if (d > RoundTolerance)
                            {
                                double dm = UnitUtils.ConvertFromInternalUnits(d, DisplayUnitType.DUT_METERS);
                                area = Math.PI * dm * dm / 4.0;
                            }
                        }
                    }
                    catch { }

                    if (area > 0)
                    {
                        double flow = FlowM3H(host);
                        if (flow > 0) velocity = flow / 3600.0 / area;
                    }
                }

                if (velocity > 0 && !double.IsNaN(zeta))
                {
                    double dynamic = HydraulicCalculator.DynamicPressure(kind, mediumTempC, velocity);
                    item.ResistancePa = zeta * dynamic;
                    item.Source = "ζ=" + zeta.ToString("0.##") + "(" + tableName + "常用取值)× 动压 " +
                                  dynamic.ToString("0.#") + " Pa";
                    return item;
                }

                problem = "末端「" + item.Name + "」既没有阻力参数、也算不出所在管段的动压,阻力按 0 计(请按风口样本补)。";
                item.Source = "未读到阻力(按 0 计,需补)";
                return item;
            }

            // 水系统末端:盘管水阻一般写在族参数里;读不到就只能留 0 让人填
            problem = "水系统末端「" + item.Name + "」没有读到「水阻/阻力/压降」参数,阻力按 0 计(请按样本填)。";
            item.Source = "未读到水阻(按 0 计,需补)";
            return item;
        }

        /// <summary>设备(机组/水箱/冷机等):阻力取模型参数,取不到按 0 并提示。</summary>
        private static HydraulicTerminal ReadEquipment(Element element, HydraulicKind kind,
            Dictionary<int, MEPCurve> curveByElementId, out string problem)
        {
            problem = "";
            var item = new HydraulicTerminal
            {
                Name = SafeName(element),
                ElementId = element.Id.IntegerValue,
                Kind = HydraulicItemKind.Equipment
            };

            double resistance;
            if (TryReadPressure(element, ResistanceNames, out resistance) && resistance > 0)
            {
                item.ResistancePa = resistance;
                item.Source = "模型参数(" + MatchedParameterName(element, ResistanceNames) + " = " + resistance.ToString("0.#") + " Pa)";
                return item;
            }

            problem = "设备「" + item.Name + "」没有读到「阻力/压降/水阻」参数,阻力按 0 计(请按设备样本补;" +
                      (kind == HydraulicKind.WaterPipe ? "机组水阻通常需查样本)" : "机组余压/阻力通常需查样本)");
            item.Source = "未读到阻力(按 0 计,需补)";
            return item;
        }

        // ================================================================== 最不利环路

        /// <summary>
        /// 用连接件拓扑求最不利环路:节点 = 构件,边 = 连接关系,从风机/水泵出发走到各末端,取累计阻力最大的路径。
        /// </summary>
        /// <returns>说明(标了什么 / 为什么没标)。</returns>
        private static string MarkCriticalPath(HydraulicInput input, List<Element> members, Element source,
            Dictionary<int, MEPCurve> curveByElementId, Dictionary<int, HydraulicSegment> segmentByElementId,
            HydraulicCoefficients coefficients)
        {
            var adjacency = BuildAdjacency(members);

            int sourceId = source == null ? 0 : source.Id.IntegerValue;
            if (sourceId == 0 || !adjacency.ContainsKey(sourceId))
            {
                return "没有找到可供起点判定的风机/水泵(或它不在管网连接关系里),**未判定最不利环路** —— " +
                       "已按全部管段之和保守计入;若模型里有风机/水泵,请确认它已连入该管网。";
            }

            // 节点阻力:管段取段阻力,末端取末端阻力,其余(管件/设备)为 0
            double defaultRoughness = input.Kind == HydraulicKind.WaterPipe
                ? coefficients.PipeRoughnessMm
                : coefficients.DuctRoughnessMm;
            var lossById = new Dictionary<int, double>();
            foreach (var pair in segmentByElementId)
            {
                double loss = 0;
                var segment = pair.Value;
                if (segment != null)
                {
                    var row = HydraulicCalculator.CalculateSegment(segment, input.Kind, input.MediumTempC, defaultRoughness);
                    loss = row.TotalLossPa;
                }
                lossById[pair.Key] = loss;
            }
            var terminalById = new Dictionary<int, HydraulicTerminal>();
            foreach (var terminal in input.Terminals)
            {
                if (terminal == null || terminal.ElementId == 0) continue;
                terminalById[terminal.ElementId] = terminal;
                if (!lossById.ContainsKey(terminal.ElementId)) lossById[terminal.ElementId] = terminal.ResistancePa;
            }

            var visited = new HashSet<int> { sourceId };
            var path = new List<int> { sourceId };
            double bestLoss = -1.0;
            List<int> bestPath = null;
            int steps = 0;
            bool overflow = false;

            Search(sourceId, adjacency, lossById, terminalById, visited, path, 0.0,
                ref bestLoss, ref bestPath, ref steps, ref overflow);

            if (overflow || bestPath == null)
            {
                return overflow
                    ? "管网连接关系过于复杂(环形/分支发散,搜索超过 " + MaxSearchSteps + " 步),**未判定最不利环路** —— 已按全部管段之和保守计入。"
                    : "从风机/水泵出发没有走到任何末端,**未判定最不利环路** —— 已按全部管段之和保守计入。";
            }

            int onPathSegments = 0;
            foreach (int id in bestPath)
            {
                HydraulicSegment segment;
                if (segmentByElementId.TryGetValue(id, out segment) && segment != null)
                {
                    segment.OnCriticalPath = true;
                    onPathSegments++;
                }
            }
            var endTerminal = terminalById.ContainsKey(bestPath[bestPath.Count - 1])
                ? terminalById[bestPath[bestPath.Count - 1]]
                : null;
            if (endTerminal != null) endTerminal.OnCriticalPath = true;

            // 环路中间若有末端(支管末端不在最不利环路上),一律不计入
            var onPath = new HashSet<int>(bestPath);
            foreach (var terminal in input.Terminals)
            {
                if (terminal == null || terminal.ElementId == 0) continue;
                if (!onPath.Contains(terminal.ElementId)) terminal.OnCriticalPath = false;
            }

            input.CriticalPathName = endTerminal != null ? endTerminal.Name : "未命名末端";
            return "最不利环路:" + input.CriticalPathName + ",经 " + onPathSegments +
                   " 段管段,段阻力合计约 " + bestLoss.ToString("0.#") + " Pa(按本题参数估算)。";
        }

        private static void Search(int current, Dictionary<int, List<int>> adjacency, Dictionary<int, double> lossById,
            Dictionary<int, HydraulicTerminal> terminalById, HashSet<int> visited, List<int> path, double loss,
            ref double bestLoss, ref List<int> bestPath, ref int steps, ref bool overflow)
        {
            if (overflow) return;
            if (++steps > MaxSearchSteps) { overflow = true; return; }

            bool isEnd = terminalById.ContainsKey(current);
            if (isEnd && loss > bestLoss)
            {
                bestLoss = loss;
                bestPath = new List<int>(path);
            }

            List<int> nexts;
            if (!adjacency.TryGetValue(current, out nexts)) return;
            foreach (int next in nexts)
            {
                if (visited.Contains(next)) continue;
                visited.Add(next);
                path.Add(next);
                Search(next, adjacency, lossById, terminalById, visited, path, loss + LossOf(lossById, next),
                    ref bestLoss, ref bestPath, ref steps, ref overflow);
                path.RemoveAt(path.Count - 1);
                visited.Remove(next);
            }
        }

        private static double LossOf(Dictionary<int, double> lossById, int id)
        {
            double value;
            return lossById.TryGetValue(id, out value) ? value : 0.0;
        }

        /// <summary>构件级邻接表:两个构件只要有连接件互相引用,就连一条边。</summary>
        private static Dictionary<int, List<int>> BuildAdjacency(List<Element> members)
        {
            var adjacency = new Dictionary<int, List<int>>();
            var memberIds = new HashSet<int>();
            foreach (var element in members)
            {
                if (element == null) continue;
                memberIds.Add(element.Id.IntegerValue);
                if (!adjacency.ContainsKey(element.Id.IntegerValue))
                    adjacency[element.Id.IntegerValue] = new List<int>();
            }

            foreach (var element in members)
            {
                if (element == null) continue;
                int id = element.Id.IntegerValue;
                var manager = ConnectorManagerOf(element);
                if (manager == null) continue;

                try
                {
                    foreach (Connector connector in manager.Connectors)
                    {
                        if (connector == null) continue;
                        foreach (Connector other in connector.AllRefs)
                        {
                            if (other == null || other.Owner == null) continue;
                            // 只连实体(忽略"系统"这类非几何引用)
                            if (other.Owner is MEPSystem) continue;
                            int otherId = other.Owner.Id.IntegerValue;
                            if (otherId == id || !memberIds.Contains(otherId)) continue;
                            if (!adjacency[id].Contains(otherId)) adjacency[id].Add(otherId);
                            if (!adjacency[otherId].Contains(id)) adjacency[otherId].Add(id);
                        }
                    }
                }
                catch { /* 单个构件连接关系读不到就算了,不影响其它 */ }
            }
            return adjacency;
        }

        // ================================================================== 通用读取工具

        private static MEPSystem ResolveSystem(Element element, HydraulicKind kind)
        {
            try
            {
                var curve = element as MEPCurve;
                if (curve != null)
                {
                    var system = curve.MEPSystem;
                    if (Matches(system, kind)) return system;
                }
            }
            catch { }

            try
            {
                var manager = ConnectorManagerOf(element);
                if (manager != null)
                {
                    foreach (Connector connector in manager.Connectors)
                    {
                        var system = connector == null ? null : connector.MEPSystem;
                        if (Matches(system, kind)) return system;
                    }
                }
            }
            catch { }
            return null;
        }

        private static bool Matches(MEPSystem system, HydraulicKind kind)
        {
            if (system == null) return false;
            return kind == HydraulicKind.WaterPipe ? system is PipingSystem : system is MechanicalSystem;
        }

        /// <summary>系统的基础设备(Revit 里的"系统设备",通常就是风机 / 水泵):作为最不利环路的起点。</summary>
        private static Element BaseEquipmentOf(MEPSystem system)
        {
            if (system == null) return null;
            try { return system.BaseEquipment; } catch { return null; }
        }

        /// <summary>是不是"压力源"(风机 / 水泵):它们提供压力,不作为环路阻力项。</summary>
        private static bool IsPressureSource(Element element, HydraulicKind kind)
        {
            string text = (SafeName(element) + " " + TypeName(element)).ToLowerInvariant();
            if (kind == HydraulicKind.WaterPipe) return Contains(text, "泵", "pump");
            return Contains(text, "风机", "fan ");
        }

        private static Element FindEquipmentElement(List<Element> equipments, HydraulicKind kind)
        {
            foreach (var element in equipments)
            {
                string text = (SafeName(element) + TypeName(element)).ToLowerInvariant();
                if (kind == HydraulicKind.WaterPipe)
                {
                    if (Contains(text, "泵", "pump")) return element;
                }
                else if (Contains(text, "风机", "fan", "机组", "ahu")) return element;
            }
            return null;
        }

        /// <summary>管件的宿主管段:取与之相连的管段中流量最大的一段。</summary>
        private static MEPCurve HostCurveOf(Element element, Dictionary<int, MEPCurve> curveByElementId)
        {
            MEPCurve best = null;
            double bestFlow = -1;
            try
            {
                var manager = ConnectorManagerOf(element);
                if (manager == null) return null;
                foreach (Connector connector in manager.Connectors)
                {
                    if (connector == null) continue;
                    foreach (Connector other in connector.AllRefs)
                    {
                        if (other == null || other.Owner == null) continue;
                        MEPCurve curve;
                        if (!curveByElementId.TryGetValue(other.Owner.Id.IntegerValue, out curve) || curve == null) continue;
                        double flow = FlowM3H(curve);
                        if (flow > bestFlow) { bestFlow = flow; best = curve; }
                    }
                }
            }
            catch { }
            return best;
        }

        private static ConnectorManager ConnectorManagerOf(Element element)
        {
            try
            {
                var curve = element as MEPCurve;
                if (curve != null) return curve.ConnectorManager;
                var instance = element as FamilyInstance;
                if (instance != null && instance.MEPModel != null) return instance.MEPModel.ConnectorManager;
            }
            catch { }
            return null;
        }

        private static double FlowM3H(MEPCurve curve)
        {
            double flowM3H = 0;
            try
            {
                var manager = curve.ConnectorManager;
                if (manager == null) return 0;
                foreach (Connector connector in manager.Connectors)
                {
                    if (connector == null) continue;
                    double converted = UnitUtils.ConvertFromInternalUnits(Math.Abs(connector.Flow),
                        DisplayUnitType.DUT_CUBIC_METERS_PER_HOUR);
                    if (converted > flowM3H) flowM3H = converted;
                }
            }
            catch { }
            return flowM3H;
        }

        private static readonly string[] ResistanceNames = { "水阻", "阻力", "压降", "压力损失", "阻力损失", "水阻力", "pressure drop" };
        private static readonly string[] FanPressureNames = { "全压", "风机全压", "机外静压", "静压", "余压", "total pressure" };
        private static readonly string[] PumpHeadNames = { "扬程", "水泵扬程", "设计扬程", "额定扬程", "head" };

        /// <summary>按参数名关键词读压力参数并换算成 Pa(读不到返回 false,不编值)。</summary>
        private static bool TryReadPressure(Element element, string[] keywords, out double pascals)
        {
            pascals = 0;
            var parameter = FindParameter(element, keywords);
            if (parameter == null || parameter.StorageType != StorageType.Double) return false;

            try
            {
                double raw = parameter.AsDouble();
                double value;
                try { value = UnitUtils.ConvertFromInternalUnits(raw, DisplayUnitType.DUT_PASCALS); }
                catch { value = raw; }
                if (value <= 0) return false;
                pascals = value;
                return true;
            }
            catch { return false; }
        }

        /// <summary>按参数名关键词读长度(米)参数(扬程用)。</summary>
        private static bool TryReadLength(Element element, string[] keywords, out double meters)
        {
            meters = 0;
            var parameter = FindParameter(element, keywords);
            if (parameter == null || parameter.StorageType != StorageType.Double) return false;

            try
            {
                double raw = parameter.AsDouble();
                double value;
                try { value = UnitUtils.ConvertFromInternalUnits(raw, DisplayUnitType.DUT_METERS); }
                catch { value = raw; }
                if (value <= 0) return false;
                meters = value;
                return true;
            }
            catch { return false; }
        }

        /// <summary>读内置参数的双精度值(取不到返回 0)。</summary>
        private static double ReadDoubleParameter(Element element, BuiltInParameter builtInParameter)
        {
            try
            {
                var parameter = element == null ? null : element.get_Parameter(builtInParameter);
                if (parameter == null || parameter.StorageType != StorageType.Double) return 0;
                return parameter.AsDouble();
            }
            catch { return 0; }
        }

        private static Parameter FindParameter(Element element, string[] keywords)        {
            try
            {
                foreach (Parameter parameter in element.Parameters)
                {
                    if (parameter == null || parameter.Definition == null) continue;
                    string name = (parameter.Definition.Name ?? "").ToLowerInvariant();
                    foreach (string keyword in keywords)
                    {
                        if (name.Contains(keyword.ToLowerInvariant())) return parameter;
                    }
                }
            }
            catch { }
            return null;
        }

        private static string MatchedParameterName(Element element, string[] keywords)
        {
            var parameter = FindParameter(element, keywords);
            return parameter == null || parameter.Definition == null ? "?" : parameter.Definition.Name;
        }

        private static BuiltInCategory CategoryId(Element element)
        {
            try
            {
                var category = element.Category;
                return category == null ? BuiltInCategory.INVALID : (BuiltInCategory)category.Id.IntegerValue;
            }
            catch { return BuiltInCategory.INVALID; }
        }

        private static bool IsFittingCategory(BuiltInCategory category, HydraulicKind kind)
        {
            if (kind == HydraulicKind.WaterPipe)
            {
                return category == BuiltInCategory.OST_PipeFitting ||
                       category == BuiltInCategory.OST_PipeAccessory ||
                       category == BuiltInCategory.OST_PipeInsulations;
            }
            return category == BuiltInCategory.OST_DuctFitting ||
                   category == BuiltInCategory.OST_DuctAccessory ||
                   category == BuiltInCategory.OST_DuctInsulations;
        }

        private static bool IsTerminalCategory(BuiltInCategory category, HydraulicKind kind)
        {
            if (kind == HydraulicKind.WaterPipe)
            {
                // 水侧末端:卫生器具 / 喷头(风机盘管、机组等属"机械设备",走设备分支读水阻)
                return category == BuiltInCategory.OST_PlumbingFixtures ||
                       category == BuiltInCategory.OST_Sprinklers;
            }
            return category == BuiltInCategory.OST_DuctTerminal;
        }

        private static bool IsEquipmentCategory(BuiltInCategory category)
        {
            return category == BuiltInCategory.OST_MechanicalEquipment;
        }

        private static string SafeName(Element element)
        {
            try
            {
                if (element == null) return "";
                string name = element.Name;
                if (string.IsNullOrEmpty(name))
                {
                    var parameter = element.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_NAME);
                    if (parameter != null) name = parameter.AsString();
                }
                return string.IsNullOrEmpty(name) ? (element.GetType().Name + " #" + element.Id.IntegerValue) : name;
            }
            catch { return ""; }
        }

        private static string TypeName(Element element)
        {
            try
            {
                var type = element == null ? null : element.Document.GetElement(element.GetTypeId());
                return type == null ? "" : (type.Name ?? "");
            }
            catch { return ""; }
        }

        private static string SystemTypeName(Document doc, MEPSystem system)
        {
            try
            {
                var typeId = system.GetTypeId();
                if (typeId != null && typeId != ElementId.InvalidElementId)
                {
                    var type = doc.GetElement(typeId);
                    if (type != null && !string.IsNullOrEmpty(type.Name)) return type.Name;
                }
            }
            catch { }
            try { return system.Name ?? ""; } catch { return ""; }
        }

        private static string Trimmed(string tableName)
        {
            if (string.IsNullOrEmpty(tableName)) return "";
            int index = tableName.IndexOf('(');
            return index > 0 ? tableName.Substring(0, index) : tableName;
        }

        private static bool Contains(string text, params string[] keywords)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (string keyword in keywords)
            {
                if (text.Contains(keyword.ToLowerInvariant())) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 选择过滤器:只允许"属于风系统 / 水系统"的构件(风管、水管、管件、末端、风机、水泵)。
    /// 判据不是类别名,而是它能否反查到对应域的系统 —— 这样各类管件/附件/自定义族都能选。
    /// </summary>
    internal sealed class SystemMemberSelectionFilter : ISelectionFilter
    {
        private readonly HydraulicKind _kind;

        public SystemMemberSelectionFilter(HydraulicKind kind)
        {
            _kind = kind;
        }

        public bool AllowElement(Element elem)
        {
            if (elem == null) return false;
            if (elem is MEPSystem) return false;

            var curve = elem as MEPCurve;
            if (curve != null) return MatchesSystem(curve.MEPSystem);

            try
            {
                var instance = elem as FamilyInstance;
                if (instance != null && instance.MEPModel != null && instance.MEPModel.ConnectorManager != null)
                {
                    foreach (Connector connector in instance.MEPModel.ConnectorManager.Connectors)
                    {
                        if (connector != null && MatchesSystem(connector.MEPSystem)) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }

        private bool MatchesSystem(MEPSystem system)
        {
            if (system == null) return false;
            return _kind == HydraulicKind.WaterPipe ? system is PipingSystem : system is MechanicalSystem;
        }
    }
}
