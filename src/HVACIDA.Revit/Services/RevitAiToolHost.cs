using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.Revit.Services
{
    /// <summary>
    /// **Revit 侧的 AI 命令宿主**(实现 Core 的 <see cref="IAiToolHost"/>)。
    /// <para>
    /// 本阶段**只实现只读命令**(照评审口径:先让 AI 能"看",不许它"改"):
    /// 工程信息 / 构件统计 / 空间清单 / 材料表 / 图纸清单 / 已保存的水力汇总 / 知识库检索。
    /// 返回到模型的数据**限长**(列表与说明都截断),避免一次把几万行塞进对话。
    /// </para>
    /// <para>
    /// 每条命令都通过 <see cref="AiExternalEventBridge"/> 回到 Revit 主线程执行;
    /// 汇总口径**复用插件自己的服务**(材料表用 <see cref="MaterialTakeoffService"/>、
    /// 水力用 <see cref="HydraulicSummaryService"/>),保证"AI 说的数"和界面里看到的是同一份。
    /// </para>
    /// </summary>
    internal sealed class RevitAiToolHost : IAiToolHost
    {
        private const int ToolTimeoutMs = 60000;
        private const int MaxRows = 60;

        private readonly UIApplication _uiApp;
        private readonly AiExternalEventBridge _bridge = new AiExternalEventBridge();

        public RevitAiToolHost(UIApplication uiApp)
        {
            _uiApp = uiApp;
        }

        /// <summary>本阶段实现的命令名(与 <see cref="AiToolCatalog.Known"/> 里的定义对齐)。</summary>
        private static readonly HashSet<string> Implemented = new HashSet<string>(StringComparer.Ordinal)
        {
            "get_project_info", "analyze_model_statistics", "list_spaces", "get_material_takeoff",
            "list_sheets", "get_hydraulic_summary", "search_knowledge",
            "set_parameter_value"
        };

        /// <summary>一次修改类命令最多动多少个构件(超过就拒绝,要求先缩小选择 —— 防"一句话改全楼")。</summary>
        private const int MaxModifyElements = 200;

        public string Description => "HVACIDA 命令集(" + Implemented.Count + " 条:只读 7 条 + 修改类 1 条,后者需额外开关且逐条确认)";

        public IEnumerable<AiToolDefinition> Tools
        {
            get
            {
                var list = new List<AiToolDefinition>();
                foreach (var tool in AiToolCatalog.Known)
                {
                    if (Implemented.Contains(tool.Name)) list.Add(tool);
                }
                return list;
            }
        }

        public string Execute(string name, string argumentsJson)
        {
            switch (name)
            {
                case "get_project_info":
                    return _bridge.Run(_uiApp, GetProjectInfo, ToolTimeoutMs);
                case "analyze_model_statistics":
                    return _bridge.Run(_uiApp, AnalyzeModelStatistics, ToolTimeoutMs);
                case "list_spaces":
                    string keyword = Argument(argumentsJson, "keyword");
                    return _bridge.Run(_uiApp, app => ListSpaces(app, keyword), ToolTimeoutMs);
                case "get_material_takeoff":
                    return _bridge.Run(_uiApp, MaterialTakeoff, ToolTimeoutMs);
                case "list_sheets":
                    return _bridge.Run(_uiApp, ListSheets, ToolTimeoutMs);
                case "get_hydraulic_summary":
                    return _bridge.Run(_uiApp, HydraulicSummary, ToolTimeoutMs);
                case "search_knowledge":
                    return SearchKnowledge(argumentsJson);      // 纯本地检索,不需要 Revit 线程
                case "set_parameter_value":
                    return _bridge.Run(_uiApp, app => SetParameterValue(app, argumentsJson), ToolTimeoutMs);
                default:
                    return Obj("error", S("找不到命令:" + name));
            }
        }

        // ------------------------------------------------------------------ 各条命令

        /// <summary>工程信息(读插件自己存的 project.xml;气象参数在"气象参数"窗,这里只给工程基本信息)。</summary>
        private string GetProjectInfo(UIApplication app)
        {
            ProjectInfoModel info = new XmlProjectRepository().LoadProject();
            var rows = new List<string>
            {
                Obj("项目", "工程名称", S(info.Basic == null ? "" : info.Basic.ProjectName)),
                Obj("项目", "省", S(info.Basic == null ? "" : info.Basic.LocationProvince)),
                Obj("项目", "市", S(info.Basic == null ? "" : info.Basic.LocationCity)),
                Obj("项目", "区县", S(info.Basic == null ? "" : info.Basic.LocationDistrict)),
                Obj("项目", "车站", S(info.Basic == null ? "" : info.Basic.StationName)),
                Obj("项目", "设计阶段", S(info.Basic == null ? "" : info.Basic.DesignStage)),
                Obj("项目", "备注", S(info.Basic == null ? "" : info.Basic.Remark))
            };
            return Obj(
                "source", S("插件存储的工程信息(%AppData%\\HVACIDA\\project.xml)"),
                "fields", "[" + string.Join(",", rows.ToArray()) + "]",
                "note", S("室外气象参数请在「项目信息 → 气象参数」窗查看;本命令不返回模型数据。"));
        }

        /// <summary>构件统计:按类别计数(取前 25 类),另给空间数量。</summary>
        private string AnalyzeModelStatistics(UIApplication app)
        {
            Document doc = DocumentOf(app);
            if (doc == null) return NoDocument();

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            int total = 0;
            foreach (Element element in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                total++;
                string category = null;
                try
                {
                    if (element.Category != null) category = element.Category.Name;
                }
                catch
                {
                    category = null;
                }
                if (string.IsNullOrEmpty(category)) category = "(无类别)";
                int value;
                counts[category] = counts.TryGetValue(category, out value) ? value + 1 : 1;
            }

            var pairs = new List<KeyValuePair<string, int>>(counts);
            pairs.Sort(delegate (KeyValuePair<string, int> a, KeyValuePair<string, int> b)
            {
                int byCount = b.Value.CompareTo(a.Value);
                return byCount != 0 ? byCount : string.Compare(a.Key, b.Key, StringComparison.Ordinal);
            });

            var rows = new List<string>();
            for (int i = 0; i < pairs.Count && i < 25; i++)
            {
                rows.Add(Obj("category", S(pairs[i].Key), "count", N(pairs[i].Value)));
            }

            int spaces = 0;
            foreach (Element element in new FilteredElementCollector(doc).OfClass(typeof(SpatialElement)))
            {
                if (element as Space != null) spaces++;
            }

            return Obj(
                "totalElements", N(total),
                "categoryCount", N(pairs.Count),
                "spaces", N(spaces),
                "topCategories", "[" + string.Join(",", rows.ToArray()) + "]",
                "note", S(pairs.Count > 25 ? "类别较多,只列了构件数最多的 25 类。" : "已列出全部类别。"));
        }

        /// <summary>空间清单(复用 RevitSpaceReader,含已加载链接模型里的空间)。</summary>
        private string ListSpaces(UIApplication app, string keyword)
        {
            Document doc = DocumentOf(app);
            if (doc == null) return NoDocument();

            string note;
            IList<SpaceSnapshot> spaces = RevitSpaceReader.ReadAll(doc, out note);
            var rows = new List<string>();
            double area = 0;
            int matched = 0;
            foreach (var space in spaces)
            {
                bool hit = string.IsNullOrEmpty(keyword)
                    || (space.Name ?? "").IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
                    || (space.Number ?? "").IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!hit) continue;
                matched++;
                area += space.AreaM2;
                if (rows.Count < MaxRows)
                {
                    rows.Add(Obj("number", S(space.Number), "name", S(space.Name), "level", S(space.LevelName),
                        "areaM2", N(space.AreaM2), "heightM", N(space.HeightM)));
                }
            }

            return Obj(
                "keyword", S(keyword),
                "matched", N(matched),
                "areaM2Sum", N(area),
                "spaces", "[" + string.Join(",", rows.ToArray()) + "]",
                "note", S((string.IsNullOrEmpty(keyword) ? "全部空间。" : "按关键词筛选。") +
                          (matched > rows.Count ? " 只列出前 " + rows.Count + " 个。" : "") +
                          (string.IsNullOrEmpty(note) ? "" : " " + note)));
        }

        /// <summary>材料表(复用材料表窗同一套归并口径:类别 + 族 + 类型 + 单位)。</summary>
        private string MaterialTakeoff(UIApplication app)
        {
            Document doc = DocumentOf(app);
            if (doc == null) return NoDocument();

            string readNote;
            IList<MaterialItem> items = RevitMaterialTakeoffReader.Read(doc, out readNote);
            if (items.Count == 0)
            {
                return Obj("itemCount", N(0), "note", S("模型里没有读到可统计的构件。" + readNote));
            }

            MaterialTakeoffResult result = new MaterialTakeoffService().Summarize(items);
            var rows = new List<string>();
            foreach (var total in result.CategoryTotals)
            {
                rows.Add(Obj("category", S(total.CategoryName), "unit", S(total.Unit),
                    "quantity", N(total.TotalQuantity), "count", N(total.TotalCount)));
            }

            return Obj(
                "itemCount", N(result.ItemCount),
                "totalCount", N(result.TotalCount),
                "rows", N(result.Rows.Count),
                "categories", "[" + string.Join(",", rows.ToArray()) + "]",
                "note", S("口径与「出图 → 明细表」一致:长度与件数按单位分开合计,不混加。" +
                          (string.IsNullOrEmpty(readNote) ? "" : " " + readNote)));
        }

        /// <summary>图纸清单(编号/名称/视图数/图幅,并统计空图框)。</summary>
        private string ListSheets(UIApplication app)
        {
            Document doc = DocumentOf(app);
            if (doc == null) return NoDocument();

            string note;
            IList<SheetItem> sheets = RevitSheetReader.Read(doc, out note);
            var rows = new List<string>();
            int empty = 0;
            foreach (var sheet in sheets)
            {
                int viewCount = sheet.Views == null ? 0 : sheet.Views.Count;
                if (viewCount == 0) empty++;
                if (rows.Count < MaxRows)
                {
                    rows.Add(Obj("number", S(sheet.SheetNumber), "name", S(sheet.SheetName),
                        "titleBlock", S(sheet.TitleBlockType), "sizeMm", S(Format(sheet.WidthMm) + "×" + Format(sheet.HeightMm)),
                        "views", N(viewCount), "empty", viewCount == 0 ? "true" : "false"));
                }
            }

            return Obj(
                "sheetCount", N(sheets.Count),
                "emptySheetCount", N(empty),
                "sheets", "[" + string.Join(",", rows.ToArray()) + "]",
                "note", S("空图框(没有放置视图)仍会列出并标 empty=true;批量出图时会被跳过。" +
                          (sheets.Count > rows.Count ? " 只列出前 " + rows.Count + " 张。" : "") +
                          (string.IsNullOrEmpty(note) ? "" : " " + note)));
        }

        /// <summary>已保存的水力计算结果汇总(读 hydraulic.xml,不重新读模型)。</summary>
        private string HydraulicSummary(UIApplication app)
        {
            HydraulicProject project = new XmlProjectRepository().LoadHydraulic();
            if (project == null || project.Systems == null || project.Systems.Count == 0)
            {
                return Obj("systemCount", N(0),
                    "note", S("还没有保存过水力计算。请先在「水力计算 → 风系统 / 水系统」里录入并点【计算并保存】。"));
            }

            HydraulicSummary summary = new HydraulicSummaryService().Summarize(project);
            return Obj(
                "systemCount", N(summary.AirCount + summary.WaterCount),
                "airCount", N(summary.AirCount),
                "waterCount", N(summary.WaterCount),
                "segmentCount", N(summary.SegmentCount),
                "totalLengthM", N(summary.TotalLengthM),
                "maxRequiredPressurePa", N(summary.MaxRequiredPressurePa),
                "maxRequiredHeadM", N(summary.MaxRequiredHeadM),
                "unbalancedSystemCount", N(summary.UnbalancedSystemCount),
                "uncheckedSystemCount", N(summary.UncheckedSystemCount),
                "note", S("数据来自已保存的水力计算(不重算);风机全压与水泵扬程是**各系统最大值**,不可相加。" +
                          (string.IsNullOrEmpty(summary.Note) ? "" : " " + summary.Note)));
        }

        /// <summary>知识库检索(导入的条文原文 / 本项目口径 / Revit 操作指南)。</summary>
        private string SearchKnowledge(string argumentsJson)
        {
            string query = Argument(argumentsJson, "query");
            if (string.IsNullOrEmpty(query)) return Obj("error", S("缺少参数 query"));

            var matches = KnowledgeBase.Search(query);
            var rows = new List<string>();
            foreach (var match in matches)
            {
                string text = match.Entry.Answer ?? "";
                if (text.Length > 900) text = text.Substring(0, 900) + "…(已截断,完整正文请在知识库窗查看)";
                rows.Add(Obj("title", S(match.Entry.Title), "category", S(match.Entry.CategoryName),
                    "source", S(match.Entry.Source), "score", N(match.Score), "text", S(text)));
            }
            if (rows.Count == 0)
            {
                return Obj("matches", "[]", "note", S("知识库里没有匹配「" + query + "」的条目。" +
                    "可以在「AI问答 → 规范知识库」里导入标准条文电子版,或换个说法再试。"));
            }
            return Obj("matches", "[" + string.Join(",", rows.ToArray()) + "]",
                "note", S("依据来自本插件知识库;引用条文请以标准原文为准。"));
        }

        // ------------------------------------------------------------------ 修改类命令(需额外开关 + 逐条确认)

        /// <summary>
        /// **修改模型:设置参数值**(本仓库第一条"会改模型"的命令)。
        /// <para>
        /// 四道闸门,缺一不可:① Core 的「操作 Revit」开关;② Core 的「允许修改模型」开关;
        /// ③ **Revit 原生确认对话框**(默认按钮是「否」);④ 单次最多 <see cref="MaxModifyElements"/> 个构件。
        /// 只写文本 / 整数 / 构件 ID 三类参数;**数值型(带单位实数)一律拒写** ——
        /// Revit 内部单位是英尺,直接写数字会把几何/风量改错,宁可让用户手工改。
        /// </para>
        /// </summary>
        private string SetParameterValue(UIApplication app, string argumentsJson)
        {
            Document doc = DocumentOf(app);
            if (doc == null) return NoDocument();

            string parameterName = Argument(argumentsJson, "parameter");
            string value = Argument(argumentsJson, "value");
            if (string.IsNullOrEmpty(parameterName))
                return Obj("error", S("缺少参数 parameter(参数名)"));

            // 目标构件:显式给 Id 就用它,否则用当前选择
            var targets = new List<Element>();
            var explicitIds = ArgumentInts(argumentsJson, "elementIds");
            if (explicitIds.Count > 0)
            {
                foreach (int id in explicitIds)
                {
                    Element element = doc.GetElement(new ElementId(id));
                    if (element != null) targets.Add(element);
                }
            }
            else
            {
                UIDocument uidoc = app.ActiveUIDocument;
                if (uidoc == null) return Obj("error", S("没有活动文档/选择集"));
                foreach (ElementId id in uidoc.Selection.GetElementIds())
                {
                    Element element = doc.GetElement(id);
                    if (element != null) targets.Add(element);
                }
                if (targets.Count == 0)
                    return Obj("error", S("当前没有选中任何构件 —— 请先在 Revit 里选中要改的构件(或让 AI 显式给出 elementIds)"));
            }

            if (targets.Count > MaxModifyElements)
            {
                return Obj("error", S("一次要改 " + targets.Count + " 个构件,超过安全上限 " + MaxModifyElements +
                                      " —— 请先缩小选择范围再让我改(避免一句话改掉整栋楼)"));
            }

            // 先做一遍"能不能改"的体检(不写模型),把不能改的原因逐条报出来
            var writable = new List<Element>();
            var skipped = new List<string>();
            StorageType storage = StorageType.String;
            int stringCount = 0, integerCount = 0, elementIdCount = 0, doubleCount = 0;
            foreach (var element in targets)
            {
                Parameter parameter = element.LookupParameter(parameterName);
                if (parameter == null) { skipped.Add(IdOf(element) + ":没有参数「" + parameterName + "」"); continue; }
                if (parameter.IsReadOnly) { skipped.Add(IdOf(element) + ":参数只读"); continue; }
                StorageType type = parameter.StorageType;
                if (type == StorageType.Double) doubleCount++;
                else if (type == StorageType.Integer) integerCount++;
                else if (type == StorageType.ElementId) elementIdCount++;
                else stringCount++;
                storage = type;
                writable.Add(element);
            }

            if (doubleCount > 0)
            {
                return Obj("error", S("参数「" + parameterName + "」是**数值型**(带单位的实数)参数," + doubleCount +
                                      " 个构件命中。Revit 内部单位是英尺,直接写数字会把几何/风量改错,本命令不写数值型参数 —— " +
                                      "请在 Revit 里手工改,或改用带单位的算式参数。") +
                    (skipped.Count > 0 ? ", \"skipped\":" + Strings(skipped) : ""));
            }
            if (writable.Count == 0)
            {
                return Obj("error", S("没有一个构件的参数「" + parameterName + "」可以写(参数不存在或只读)"),
                    "skipped", Strings(skipped));
            }

            // 用户确认(Revit 原生对话框;默认按钮放在「否」上)
            string kind = stringCount >= integerCount && stringCount >= elementIdCount ? "文本"
                : (integerCount >= elementIdCount ? "整数" : "构件 ID");
            var dialog = new TaskDialog("HVACIDA AI 助手 — 确认修改模型");
            dialog.MainInstruction = "AI 请求修改模型,是否执行?";
            dialog.MainContent =
                "参数:" + parameterName + "(" + kind + "型)\n" +
                "设为:" + (value ?? "") + "\n" +
                "构件数:" + writable.Count + (skipped.Count > 0 ? "(另有 " + skipped.Count + " 个构件不能写,会跳过)" : "") + "\n" +
                "构件示例:" + SampleIds(writable);
            dialog.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
            dialog.DefaultButton = TaskDialogResult.No;
            TaskDialogResult answer = dialog.Show();
            if (answer != TaskDialogResult.Yes)
            {
                return Obj("ok", "false", "cancelled", "true",
                    "message", S("用户取消了本次修改,模型未改动。"));
            }

            int changed = 0;
            var failed = new List<string>();
            using (var transaction = new Transaction(doc, "HVACIDA AI:设置参数 " + parameterName))
            {
                transaction.Start();
                foreach (var element in writable)
                {
                    try
                    {
                        Parameter parameter = element.LookupParameter(parameterName);
                        if (parameter == null) continue;
                        bool ok;
                        switch (parameter.StorageType)
                        {
                            case StorageType.Integer:
                                int integerValue;
                                ok = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out integerValue)
                                     && parameter.Set(integerValue);
                                if (!ok) failed.Add(IdOf(element) + ":整数解析失败或写入被拒");
                                break;
                            case StorageType.ElementId:
                                int idValue;
                                ok = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out idValue)
                                     && parameter.Set(new ElementId(idValue));
                                if (!ok) failed.Add(IdOf(element) + ":构件 ID 解析失败或写入被拒");
                                break;
                            default:
                                ok = parameter.Set(value ?? "");
                                if (!ok) failed.Add(IdOf(element) + ":写入被拒");
                                break;
                        }
                        if (ok) changed++;
                    }
                    catch (Exception ex)
                    {
                        failed.Add(IdOf(element) + ":" + ex.Message);
                    }
                }
                if (changed > 0) transaction.Commit();
                else transaction.RollBack();
            }

            return Obj(
                "changed", N(changed),
                "skipped", N(skipped.Count + failed.Count),
                "parameter", S(parameterName),
                "value", S(value),
                "details", Strings(failed),
                "note", S("已在事务内完成(" + changed + " 个构件)。撤销可用 Revit 的 Ctrl+Z(本命令是单个事务)。" +
                          (skipped.Count > 0 ? " 另有 " + skipped.Count + " 个构件因参数不存在/只读被跳过。" : "")));
        }

        private static string IdOf(Element element)
        {
            try { return "#" + element.Id.IntegerValue; }
            catch { return "#?"; }
        }

        private static string SampleIds(IList<Element> elements)
        {
            var parts = new List<string>();
            for (int i = 0; i < elements.Count && i < 8; i++) parts.Add(IdOf(elements[i]));
            return string.Join(",", parts.ToArray()) + (elements.Count > 8 ? " …" : "");
        }

        private static string Strings(IList<string> items)
        {
            var parts = new List<string>();
            foreach (var item in items) parts.Add(S(item));
            return "[" + string.Join(",", parts.ToArray()) + "]";
        }

        /// <summary>取整数数组参数(缺省返回空表)。</summary>
        private static List<int> ArgumentInts(string argumentsJson, string name)
        {
            var values = new List<int>();
            try
            {
                var node = JsonValue.Parse(argumentsJson ?? "{}").Get(name);
                if (!node.IsArray) return values;
                foreach (var item in node.Items) values.Add(item.AsInt(0));
            }
            catch
            {
                // 参数坏了就当没给:走"用当前选择"的路径,并在上面给出提示
            }
            return values;
        }

        // ------------------------------------------------------------------ 工具

        private static Document DocumentOf(UIApplication app)
        {
            try
            {
                UIDocument uidoc = app == null ? null : app.ActiveUIDocument;
                return uidoc == null ? null : uidoc.Document;
            }
            catch
            {
                return null;
            }
        }

        private static string NoDocument()
        {
            return Obj("error", S("当前没有打开的文档(请先在 Revit 里打开一个项目)"));
        }

        private static string Argument(string argumentsJson, string name)
        {
            try
            {
                return JsonValue.Parse(argumentsJson ?? "{}").Get(name).AsString("");
            }
            catch
            {
                return "";
            }
        }

        private static string Format(double value)
        {
            return Math.Round(value, 0).ToString("0", CultureInfo.InvariantCulture);
        }

        private static string S(string text)
        {
            return "\"" + JsonValue.Escape(text ?? "") + "\"";
        }

        private static string N(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "null";
            return Math.Round(value, 3).ToString(CultureInfo.InvariantCulture);
        }

        private static string Obj(params string[] pairs)
        {
            var sb = new StringBuilder("{");
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                if (i > 0) sb.Append(',');
                sb.Append(S(pairs[i])).Append(':').Append(pairs[i + 1]);
            }
            sb.Append('}');
            return sb.ToString();
        }
    }
}
