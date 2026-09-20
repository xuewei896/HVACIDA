using System;
using System.Collections.Generic;
using System.Text;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// **Revit 侧要实现的命令宿主**(Core 不认识 Revit,只认识这个接口)。
    /// <para>
    /// 实现方(Revit 层)负责:① 提供**当前进程真正注册成功**的命令清单;
    /// ② 把命令丢到 Revit API 线程执行(ExternalEvent)并把结果 JSON 返回。
    /// </para>
    /// </summary>
    public interface IAiToolHost
    {
        /// <summary>当前注册成功的工具(只读命令 + 已启用的修改类命令)。</summary>
        IEnumerable<AiToolDefinition> Tools { get; }

        /// <summary>执行一条命令,返回 JSON 文本(失败也要返回 JSON,不要抛出去)。</summary>
        string Execute(string name, string argumentsJson);

        /// <summary>实现方自述(界面显示"命令集从哪来")。</summary>
        string Description { get; }
    }

    /// <summary>
    /// **进程内命令总线**(照参考文档 2.3:用进程内 CommandBus 取代外部 MCP Server —— 不起端口、不做协议握手)。
    /// <para>
    /// 三条纪律:
    /// ① **总开关**:<see cref="OperateRevitEnabled"/> 关掉时**既拒绝执行**,也**不把工具清单发给模型**
    ///    (用户可以安心聊天,不怕 AI 乱改图);
    /// ② **串行**:Revit API 不是线程安全的,用带超时的互斥锁让命令排队执行(默认 3 分钟);
    /// ③ **可审计**:每次执行(含被拒绝的)都留下 <see cref="Log"/> 记录,界面显示给用户。
    /// </para>
    /// </summary>
    public static class AiCommandBus
    {
        /// <summary>执行命令的等待上限(超过就报"命令排队超时",不无限等)。</summary>
        public static readonly TimeSpan ExecuteTimeout = TimeSpan.FromMinutes(3);

        private static readonly object ExecuteLock = new object();
        private static readonly List<string> LogEntries = new List<string>();
        private static IAiToolHost _host;

        /// <summary>「操作 Revit」总开关(**默认关**:只聊天、不动模型)。</summary>
        public static bool OperateRevitEnabled { get; set; }

        /// <summary>命令集是否已就绪(Revit 完全初始化后由 ApplicationInitialized 装载)。</summary>
        public static bool IsReady => _host != null;

        /// <summary>命令集来源说明。</summary>
        public static string HostDescription => _host == null ? "(命令集未加载)" : (_host.Description ?? "");

        /// <summary>最近一次被拒绝的原因(界面提示用)。</summary>
        public static string LastBlockedReason { get; private set; } = "";

        /// <summary>执行日志(最近若干条,界面显示)。</summary>
        public static IList<string> Log => LogEntries;

        /// <summary>注册命令宿主(Revit 层在 Revit 完全初始化之后调用;幂等,可重复注册覆盖)。</summary>
        public static void Register(IAiToolHost host)
        {
            _host = host;
            Append("命令集已加载:" + (host == null ? "(空)" : host.Description));
        }

        /// <summary>卸载(Revit 关闭/文档关闭时用)。</summary>
        public static void Clear()
        {
            _host = null;
            Append("命令集已卸载");
        }

        /// <summary>已注册的命令名(没就绪时为空)。</summary>
        public static IList<string> RegisteredNames()
        {
            var names = new List<string>();
            if (_host == null) return names;
            foreach (var tool in _host.Tools)
            {
                if (tool != null && !string.IsNullOrEmpty(tool.Name)) names.Add(tool.Name);
            }
            return names;
        }

        /// <summary>
        /// 交给模型的工具清单(总开关关掉 / 未就绪时返回**空** —— 模型看不到工具,自然不会去调)。
        /// </summary>
        public static IList<AiToolDefinition> ToolsForModel()
        {
            var tools = new List<AiToolDefinition>();
            if (!OperateRevitEnabled || _host == null) return tools;
            foreach (var tool in _host.Tools)
            {
                if (tool == null || string.IsNullOrEmpty(tool.Name)) continue;
                tools.Add(tool);
            }
            return tools;
        }

        /// <summary>
        /// 执行一条命令(总开关 + 串行锁 + 超时)。**返回 JSON 文本**,失败也不抛异常。
        /// </summary>
        public static string Execute(string name, string argumentsJson)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            if (!OperateRevitEnabled)
            {
                LastBlockedReason = "已关闭「操作 Revit」,命令未执行";
                Append(name + ":被拒绝(" + LastBlockedReason + ")");
                return "{\"error\":\"已关闭操作 Revit,命令未执行\",\"command\":\"" + JsonValue.Escape(name ?? "") + "\"}";
            }
            if (_host == null)
            {
                LastBlockedReason = "命令集尚未加载(Revit 还没完全初始化)";
                Append(name + ":被拒绝(" + LastBlockedReason + ")");
                return "{\"error\":\"命令集尚未加载\",\"command\":\"" + JsonValue.Escape(name ?? "") + "\"}";
            }
            if (!IsRegistered(name))
            {
                LastBlockedReason = "找不到命令:" + name;
                Append(name + ":找不到命令");
                return "{\"error\":\"找不到命令:" + JsonValue.Escape(name ?? "") + "\",\"command\":\"" +
                       JsonValue.Escape(name ?? "") + "\"}";
            }

            bool entered = false;
            try
            {
                System.Threading.Monitor.TryEnter(ExecuteLock, ExecuteTimeout, ref entered);
                if (!entered)
                {
                    LastBlockedReason = "命令排队超时(另一条命令还在执行)";
                    Append(name + ":排队超时");
                    return "{\"error\":\"命令排队超时,请稍后重试\",\"command\":\"" + JsonValue.Escape(name ?? "") + "\"}";
                }

                string result = _host.Execute(name, argumentsJson ?? "{}");
                watch.Stop();
                Append(name + ":完成(" + watch.ElapsedMilliseconds + " ms)");
                return string.IsNullOrEmpty(result) ? "{\"ok\":true}" : result;
            }
            catch (Exception ex)
            {
                LastBlockedReason = ex.Message;
                Append(name + ":异常(" + ex.Message + ")");
                return "{\"error\":\"" + JsonValue.Escape(ex.Message) + "\",\"command\":\"" + JsonValue.Escape(name ?? "") + "\"}";
            }
            finally
            {
                if (entered) System.Threading.Monitor.Exit(ExecuteLock);
            }
        }

        /// <summary>清空执行日志(界面按钮)。</summary>
        public static void ClearLog()
        {
            LogEntries.Clear();
        }

        /// <summary>该命令是否在当前命令集里(模型可能记错名字,靠这个挡住)。</summary>
        public static bool IsRegistered(string name)
        {
            if (_host == null || string.IsNullOrEmpty(name)) return false;
            foreach (var tool in _host.Tools)
            {
                if (tool != null && string.Equals(tool.Name, name, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static void Append(string entry)
        {
            LogEntries.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + entry);
            while (LogEntries.Count > 200) LogEntries.RemoveAt(0);
        }
    }

    /// <summary>
    /// **工具目录**:把命令定义转成 OpenAI function calling 的 <c>tools</c> JSON,并做**schema 清洗**。
    /// <para>
    /// 为什么要清洗:参考文档踩过的坑 —— 参数 schema 里若混进 PowerShell 风格的
    /// <c>@{type=object;...}</c> 或单引号,大模型接口会直接返回 **HTTP 400**。
    /// 这里在发送前把不合法的 schema 统一替换成合法的 <c>{"type":"object",...}</c> 骨架,
    /// 并把每次替换记进 <see cref="LastSanitizeNote"/> 供界面与自检核对(不静默)。
    /// </para>
    /// </summary>
    public static class AiToolCatalog
    {
        /// <summary>最近一次清洗的说明(没有清洗时为空)。</summary>
        public static string LastSanitizeNote { get; private set; } = "";

        /// <summary>把工具定义拼成请求体里的 <c>tools</c> 数组 JSON。</summary>
        public static string BuildToolsJson(IEnumerable<AiToolDefinition> tools)
        {
            var sb = new StringBuilder();
            var notes = new List<string>();
            sb.Append('[');
            bool first = true;
            if (tools != null)
            {
                foreach (var tool in tools)
                {
                    if (tool == null || string.IsNullOrEmpty(tool.Name)) continue;
                    string schema = SanitizeParameters(tool.ParametersJson, tool.Name, notes);
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"type\":\"function\",\"function\":{");
                    sb.Append("\"name\":\"").Append(JsonValue.Escape(tool.Name)).Append("\",");
                    sb.Append("\"description\":\"").Append(JsonValue.Escape(tool.Description ?? "")).Append("\",");
                    sb.Append("\"parameters\":").Append(schema);
                    sb.Append("}}");
                }
            }
            sb.Append(']');
            LastSanitizeNote = notes.Count == 0 ? "" : string.Join(";", notes.ToArray());
            return sb.ToString();
        }

        /// <summary>schema 清洗 + 合法性校验(不合法就换骨架并记录原因)。</summary>
        public static string SanitizeParameters(string schemaJson, string toolName, IList<string> notes)
        {
            const string skeleton = "{\"type\":\"object\",\"properties\":{}}";
            string text = (schemaJson ?? "").Trim();
            if (text.Length == 0) return skeleton;

            if (LooksLikePowerShellHashtable(text))
            {
                if (notes != null) notes.Add(toolName + ":参数 schema 是 PowerShell 风格散列表,已换成空对象骨架(避免 HTTP 400)");
                return skeleton;
            }

            try
            {
                var parsed = JsonValue.Parse(text);
                if (!parsed.IsObject)
                {
                    if (notes != null) notes.Add(toolName + ":参数 schema 不是对象,已换成空对象骨架");
                    return skeleton;
                }
                // 补上 type=object(有些 schema 只写了 properties)
                if (parsed.Get("type").AsString("") != "object")
                {
                    if (notes != null) notes.Add(toolName + ":参数 schema 缺 type=object,已补齐");
                    return "{\"type\":\"object\"," + text.TrimStart('{');
                }
                return text;
            }
            catch (Exception ex)
            {
                if (notes != null) notes.Add(toolName + ":参数 schema 不是合法 JSON(" + ex.Message + "),已换成空对象骨架");
                return skeleton;
            }
        }

        /// <summary>是否像 PowerShell 散列表(<c>@{...}</c> 或 <c>key=value</c> 分号串)。</summary>
        public static bool LooksLikePowerShellHashtable(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string trimmed = text.Trim();
            if (trimmed.StartsWith("@{")) return true;
            if (trimmed.Contains("@{") && trimmed.Contains("=")) return true;
            // 合法 JSON 里不该出现裸的 "名字=" (键必须带引号)
            if (trimmed.Contains("=") && !trimmed.Contains("\":")) return true;
            return false;
        }

        /// <summary>
        /// **本插件已知的命令定义**(单一数据源:Revit 层按当前版本挑能注册的,自检按名字核对)。
        /// 全部为**只读**命令 —— 修改类命令需另行设计确认流程后再加,且必须置 <c>ModifiesModel=true</c>。
        /// </summary>
        public static IList<AiToolDefinition> Known { get; } = new List<AiToolDefinition>
        {
            new AiToolDefinition
            {
                Name = "get_project_info",
                Description = "读取当前工程的名称、编号、项目地点与室外气象参数(夏季/冬季干湿球、大气压力等)。问「我这个工程的气象参数是多少」时用。",
                ParametersJson = "{\"type\":\"object\",\"properties\":{}}"
            },
            new AiToolDefinition
            {
                Name = "analyze_model_statistics",
                Description = "统计当前模型的构件数量与类型分布(墙/门/窗/风管/水管/设备/空间等),返回 Markdown 表格。问「模型里有多少构件」时用。",
                ParametersJson = "{\"type\":\"object\",\"properties\":{}}"
            },
            new AiToolDefinition
            {
                Name = "list_spaces",
                Description = "列出当前模型的空间(名称/编号/标高/面积/体积),可筛选站厅、站台等。问「本项目有哪些空间/公共区面积多少」时用。",
                ParametersJson = "{\"type\":\"object\",\"properties\":{\"keyword\":{\"type\":\"string\",\"description\":\"可选:名称/编号关键词,如 站厅\"}}}"
            },
            new AiToolDefinition
            {
                Name = "get_material_takeoff",
                Description = "按类别统计材料表(风管/水管/管件/附件/末端/设备/保温),长度按 m、件数按个,给出类别小计。问「材料表/工程量」时用。",
                ParametersJson = "{\"type\":\"object\",\"properties\":{}}"
            },
            new AiToolDefinition
            {
                Name = "list_sheets",
                Description = "列出当前模型的图纸(编号/名称/图框/图幅/视图数),并标出空图框。问「出图情况/图纸清单」时用。",
                ParametersJson = "{\"type\":\"object\",\"properties\":{}}"
            },
            new AiToolDefinition
            {
                Name = "get_hydraulic_summary",
                Description = "读取已保存的水力计算结果汇总(风系统/水系统的需求全压、扬程、管段数、最大不平衡率)。问「我算过的水力结果」时用。",
                ParametersJson = "{\"type\":\"object\",\"properties\":{}}"
            },
            new AiToolDefinition
            {
                Name = "search_knowledge",
                Description = "在本插件的知识库里检索(本项目已定口径 / 用户导入的标准条文原文 / Revit 操作指南),返回条目标题、正文与出处。问规范条文或软件操作时优先用这个。",
                ParametersJson = "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\",\"description\":\"检索关键词\"}},\"required\":[\"query\"]}"
            }
        };
    }
}
