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
            "list_sheets", "get_hydraulic_summary", "search_knowledge"
        };

        public string Description => "HVACIDA 只读命令集(" + Implemented.Count + " 条:工程信息/构件统计/空间/材料表/图纸/水力汇总/知识库)";

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
