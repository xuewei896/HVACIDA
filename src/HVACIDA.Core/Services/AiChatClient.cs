using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// **AI 助手对话客户端**(照参考文档 2.2/2.3 的架构):
    /// OpenAI 兼容 <c>/chat/completions</c> + **SSE 流式** + **function calling**,最多 <see cref="MaxToolRounds"/> 轮。
    /// <para>
    /// 一轮的流程:请求模型 → 收到 <c>tool_calls</c> 就在**进程内**通过 <see cref="AiCommandBus"/> 执行 →
    /// 把结果作为 <c>role=tool</c> 消息塞回历史 → 再请求模型 → 直到模型给出最终回答(没有工具调用为止)。
    /// </para>
    /// <para>
    /// 三个协议细节(参考文档里踩过的坑,这里都处理了):
    /// ① 流式 <c>tool_calls</c> 的 <c>function.arguments</c> 是**分片**到达的,按 <c>index</c> 累加拼完整;
    /// ② 回传工具结果前**必须**先把带 <c>tool_calls</c> 的助手消息放进历史,否则接口 400;
    /// ③ 参数 schema 发出去前做**清洗**(见 <see cref="AiToolCatalog.SanitizeParameters"/>),避免 PowerShell 风格 schema 触发 400。
    /// </para>
    /// <para>
    /// Core 不引用 Revit:命令执行通过 <see cref="IAiToolHost"/> 交给 Revit 层;<c>"操作 Revit"</c> 总开关关掉时
    /// **既不执行命令、也不把工具清单发给模型**(照参考文档的做法)。
    /// </para>
    /// </summary>
    public static class AiChatClient
    {
        /// <summary>工具调用最多几轮(参考文档取 12 轮:模型拿到结果后可能还要再调下一个工具)。</summary>
        public const int MaxToolRounds = 12;

        /// <summary>
        /// **开启「操作 Revit」后的隐私口径**(必须与关闭时分开写清楚 —— 工具返回的是**工程数据**,它会随对话发到云端)。
        /// </summary>
        public const string PrivacyNoteWithTools =
            "⚠ 已开启「操作 Revit」:模型会调用命令,命令**返回的工程数据**(构件统计、空间清单、材料表、图纸清单、已保存的水力结果等)" +
            "会随对话一起发给大模型服务方。若项目不允许工程数据出网,请**关闭该开关**(关闭后模型看不到工具,也不会执行任何命令)。";

        /// <summary>系统提示:AI 助手版的纪律(在参考文档"动态 system prompt"基础上加本项目的口径约束)。</summary>
        public const string DefaultSystemPrompt =
            "你是嵌入 Revit 的地铁通风空调与给排水设计助手(HVACIDA)。工作方式:\n" +
            "1. 需要工程事实(构件数量、空间、材料、图纸、已算结果)时,**先调用工具去取真实数据**,不要凭印象回答。\n" +
            "2. 需要规范条文或本插件口径时,调用 search_knowledge;回答里引用条文必须逐字引用工具返回的原文。\n" +
            "3. 工具返回什么就说什么;工具没返回的、或工具报错的,如实说明(例如「命令集未加载」「已关闭操作 Revit」),不要编数值。\n" +
            "4. 不要编造规范条文号、表格号、系数;依据不足时明说该查哪本标准或插件哪个窗口。\n" +
            "5. 涉及修改模型的操作,先说明将要改什么、改多少,等用户确认;一次只做用户要求的那件事。\n" +
            "6. 用简体中文、条理化、直接回答;结尾列出你用到的数据来源(工具名或知识库条目标题)。\n" +
            "7. 你的输出是给设计人员看的**草稿**,不声称「已满足规范要求」。";

        /// <summary>发一次问答(含工具调用循环)。失败不抛异常。</summary>
        public static AiAssistantResult Send(string query, AiChatSettings settings, string systemPrompt,
            string contextSnapshot, bool allowTools, Action<string> onPartial, Action<string> onStatus,
            Action<AiToolCall> onToolExecuted, IAiToolHost host)
        {
            int timeoutMs = settings != null && settings.TimeoutSeconds > 0
                ? settings.TimeoutSeconds * 1000
                : DeepSeekClient.DefaultTimeoutMs;
            return Send(query, settings, systemPrompt, contextSnapshot, allowTools, onPartial, onStatus,
                onToolExecuted, host, timeoutMs, MaxToolRounds);
        }

        /// <summary>发一次问答(可指定超时与轮次上限,便于自检与断网演练)。</summary>
        public static AiAssistantResult Send(string query, AiChatSettings settings, string systemPrompt,
            string contextSnapshot, bool allowTools, Action<string> onPartial, Action<string> onStatus,
            Action<AiToolCall> onToolExecuted, IAiToolHost host, int timeoutMs, int maxRounds)
        {
            string text = (query ?? "").Trim();
            if (text.Length == 0)
                return AiAssistantResult.Fail(query, AiErrorKind.BadRequest, 0, "问题为空", "请先输入问题。");

            if (settings == null || !settings.Enabled)
                return AiAssistantResult.Fail(query, AiErrorKind.NotEnabled, 0, "未启用",
                    "AI 助手未启用(在 AI 助手面板里勾选启用并保存后才发请求)。");
            if (!settings.IsConfigured)
                return AiAssistantResult.Fail(query, AiErrorKind.NoCredential, 0, "缺 API key",
                    "AI 助手缺 API key —— " + settings.MissingCredentialText() + AiChatSettings.CredentialHelp);

            if (maxRounds <= 0) maxRounds = MaxToolRounds;
            string model = DeepSeekClient.ResolveModel(settings);

            // 工具清单:总开关关掉 / 未就绪就是空 → 模型看不到工具(参考文档的做法)
            bool toolsOn = allowTools && AiCommandBus.OperateRevitEnabled && AiCommandBus.IsReady;
            string toolsJson = toolsOn ? AiToolCatalog.BuildToolsJson(AiCommandBus.ToolsForModel()) : "[]";
            int toolCount = toolsOn ? AiCommandBus.ToolsForModel().Count : 0;

            var messages = new List<AiChatMessage>();
            string system = string.IsNullOrWhiteSpace(systemPrompt) ? DefaultSystemPrompt : systemPrompt;
            if (!string.IsNullOrWhiteSpace(contextSnapshot))
            {
                system = system + "\n\n【当前 Revit 上下文】\n" + contextSnapshot.Trim();
            }
            messages.Add(AiChatMessage.System(system));
            messages.Add(AiChatMessage.User(text));

            var result = new AiAssistantResult { Query = text, Model = model };
            var visible = new StringBuilder();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            string url = DeepSeekClient.ResolveUrl(settings);
            bool stream = settings.Stream;

            for (int round = 1; round <= maxRounds; round++)
            {
                result.Rounds = round;
                if (onStatus != null)
                {
                    onStatus(toolCount > 0
                        ? "请求 " + model + "(第 " + round + " 轮,可用命令 " + toolCount + " 条)…"
                        : "请求 " + model + "(第 " + round + " 轮)…");
                }

                var roundResult = Request(settings, messages, toolsJson, stream, timeoutMs, url, model, text, onPartial, visible);
                result.TotalTokens += roundResult.Tokens;
                if (!roundResult.Ok)
                {
                    result.Success = false;
                    result.ErrorKind = roundResult.Kind;
                    result.HttpStatus = roundResult.HttpStatus;
                    result.ErrorMessage = roundResult.ErrorMessage;
                    result.Note = roundResult.Note;
                    result.Content = visible.ToString().Trim();
                    result.ElapsedMs = watch.ElapsedMilliseconds;
                    return result;
                }

                if (roundResult.ToolCalls.Count == 0)
                {
                    result.Success = true;
                    result.Content = visible.ToString().Trim();
                    result.Note = "模型回答由 " + model + " 生成(共 " + round + " 轮" +
                                  (result.ToolCallCount > 0 ? "、执行命令 " + result.ToolCallCount + " 条" : "") + ")," +
                                  "**必须对着工具返回的数据与知识库依据核对后才能用**;" + result.UsageText + "。" +
                                  (toolsOn ? "" : " 本次未把工具清单发给模型(未开启「操作 Revit」或命令集未加载)。");
                    result.ElapsedMs = watch.ElapsedMilliseconds;
                    return result;
                }

                // 有工具调用:先把"带 tool_calls 的助手消息"放进历史(协议要求),再逐条执行
                messages.Add(AiChatMessage.Assistant(roundResult.Content ?? "", BuildToolCallsJson(roundResult.ToolCalls)));
                foreach (var call in roundResult.ToolCalls)
                {
                    result.ToolCalls.Add(call);
                    if (onStatus != null) onStatus("正在执行命令 " + call.Name + "…");
                    var toolWatch = System.Diagnostics.Stopwatch.StartNew();
                    string toolResult;
                    if (!toolWatch.IsRunning) toolWatch.Start();
                    if (!allowTools)
                    {
                        toolResult = "{\"error\":\"本次未允许操作 Revit,命令未执行\"}";
                    }
                    else
                    {
                        toolResult = AiCommandBus.Execute(call.Name, call.ArgumentsJson);
                    }
                    toolWatch.Stop();
                    call.Executed = true;
                    call.ResultJson = toolResult;
                    call.ElapsedMs = toolWatch.ElapsedMilliseconds;
                    if (onToolExecuted != null) onToolExecuted(call);
                    messages.Add(AiChatMessage.Tool(call.Id, toolResult));
                }
            }

            result.Success = true;
            result.HitRoundLimit = true;
            result.Content = visible.ToString().Trim();
            result.Note = "已达到工具调用轮次上限(" + maxRounds + " 轮)而停止 —— 回答可能不完整,请把问题拆小再问一次。" +
                          result.UsageText + "。";
            result.ElapsedMs = watch.ElapsedMilliseconds;
            return result;
        }

        /// <summary>组装请求体(含 tools 与工具消息;纯函数,便于自检逐字段核对)。</summary>
        public static string BuildRequestBody(AiChatSettings settings, IList<AiChatMessage> messages, string toolsJson, bool stream)
        {
            settings = settings ?? new AiChatSettings();
            double temperature = settings.Temperature;
            if (temperature < 0) temperature = 0;
            if (temperature > 2) temperature = 2;
            int maxTokens = settings.MaxTokens > 0 ? settings.MaxTokens : 1024;

            var sb = new StringBuilder();
            sb.Append("{\"model\":\"").Append(JsonValue.Escape(DeepSeekClient.ResolveModel(settings))).Append("\",");
            sb.Append("\"messages\":[");
            if (messages != null)
            {
                for (int i = 0; i < messages.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var message = messages[i];
                    sb.Append("{\"role\":\"").Append(JsonValue.Escape(message.Role)).Append('"');
                    if (!string.IsNullOrEmpty(message.Content))
                    {
                        sb.Append(",\"content\":\"").Append(JsonValue.Escape(message.Content)).Append('"');
                    }
                    else
                    {
                        sb.Append(",\"content\":\"\"");
                    }
                    if (!string.IsNullOrEmpty(message.ToolCallsJson))
                    {
                        sb.Append(",\"tool_calls\":").Append(message.ToolCallsJson);
                    }
                    if (!string.IsNullOrEmpty(message.ToolCallId))
                    {
                        sb.Append(",\"tool_call_id\":\"").Append(JsonValue.Escape(message.ToolCallId)).Append('"');
                    }
                    sb.Append('}');
                }
            }
            sb.Append("],");
            sb.Append("\"stream\":").Append(stream ? "true" : "false").Append(',');
            sb.Append("\"temperature\":").Append(temperature.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"max_tokens\":").Append(maxTokens);
            string tools = (toolsJson ?? "").Trim();
            if (tools.Length > 2 && tools != "[]")
            {
                sb.Append(",\"tools\":").Append(tools);
                sb.Append(",\"tool_choice\":\"auto\"");
            }
            sb.Append('}');
            return sb.ToString();
        }

        // ------------------------------------------------------------------ SSE 解析(纯函数,自检直接喂样例)

        /// <summary>是不是 SSE 的 data 行(<c>data: {...}</c> 或 <c>data:[DONE]</c>)。</summary>
        public static bool IsSseDataLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return false;
            return line.TrimStart().StartsWith("data:", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 取 SSE 行里的负载:非 data 行返回 null;<c>[DONE]</c> 返回空串;其余返回 JSON 文本。
        /// </summary>
        public static string ExtractSsePayload(string line)
        {
            if (!IsSseDataLine(line)) return null;
            string payload = line.TrimStart().Substring(5).Trim();
            if (payload.Length == 0) return "";
            if (string.Equals(payload, "[DONE]", StringComparison.OrdinalIgnoreCase)) return "";
            return payload;
        }

        /// <summary>
        /// 把一片流式 delta 累加进正文与工具调用。
        /// <c>tool_calls</c> 的 <c>function.arguments</c> 是按 <c>index</c> 分片到达的,这里按 index 拼接。
        /// </summary>
        public static void ApplyStreamDelta(string payloadJson, StringBuilder content, IDictionary<int, AiToolCall> calls,
            out bool done, out int tokens, Action<string> onPartial)
        {
            done = false;
            tokens = 0;
            JsonValue root;
            try { root = JsonValue.Parse(payloadJson); }
            catch { return; }                              // 单行坏数据不该毁掉整轮

            tokens = root.Get("usage").Get("total_tokens").AsInt(0);

            var choices = root.Get("choices");
            if (!choices.IsArray || choices.Count == 0) return;
            var choice = choices.Get(0);
            string finish = choice.Get("finish_reason").AsString("");
            if (!string.IsNullOrEmpty(finish)) done = true;

            var delta = choice.Get("delta");
            string piece = delta.Get("content").AsString("");
            if (!string.IsNullOrEmpty(piece))
            {
                content.Append(piece);
                if (onPartial != null) onPartial(piece);
            }

            var toolDeltas = delta.Get("tool_calls");
            if (toolDeltas.IsArray)
            {
                foreach (var item in toolDeltas.Items)
                {
                    int index = item.Get("index").AsInt(0);
                    AiToolCall call;
                    if (calls == null) continue;
                    if (!calls.TryGetValue(index, out call))
                    {
                        call = new AiToolCall { Index = index };
                        calls[index] = call;
                    }
                    string id = item.Get("id").AsString("");
                    if (!string.IsNullOrEmpty(id)) call.Id = id;
                    var function = item.Get("function");
                    string name = function.Get("name").AsString("");
                    if (!string.IsNullOrEmpty(name)) call.Name = call.Name + name;
                    string args = function.Get("arguments").AsString("");
                    if (!string.IsNullOrEmpty(args)) call.ArgumentsJson = call.ArgumentsJson + args;
                }
            }
        }

        /// <summary>把工具调用列表拼成协议要求的 <c>tool_calls</c> JSON 数组。</summary>
        public static string BuildToolCallsJson(IList<AiToolCall> calls)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            for (int i = 0; i < calls.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":\"").Append(JsonValue.Escape(calls[i].Id ?? ("call_" + i))).Append("\",");
                sb.Append("\"type\":\"function\",\"function\":{\"name\":\"")
                  .Append(JsonValue.Escape(calls[i].Name ?? "")).Append("\",");
                sb.Append("\"arguments\":\"").Append(JsonValue.Escape(calls[i].ArgumentsJson ?? "{}")).Append("\"}}");
            }
            sb.Append(']');
            return sb.ToString();
        }

        // ------------------------------------------------------------------ 传输

        private sealed class RoundResult
        {
            public bool Ok;
            public string Content = "";
            public List<AiToolCall> ToolCalls = new List<AiToolCall>();
            public int Tokens;
            public AiErrorKind Kind = AiErrorKind.None;
            public int HttpStatus;
            public string ErrorMessage = "";
            public string Note = "";
        }

        private static RoundResult Request(AiChatSettings settings, IList<AiChatMessage> messages, string toolsJson,
            bool stream, int timeoutMs, string url, string model, string query, Action<string> onPartial, StringBuilder visible)
        {
            var result = new RoundResult();
            try
            {
                HttpTls.EnsureTls12();
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Accept = stream ? "text/event-stream" : "application/json";
                request.Headers["Authorization"] = "Bearer " + settings.ApiKey.Trim();
                request.Timeout = timeoutMs;
                request.ReadWriteTimeout = Math.Max(timeoutMs, 300000);   // 流式回答可能很长,读超时给宽
                request.KeepAlive = false;
                request.UserAgent = "HVACIDA/0.1 (Revit add-in)";

                byte[] payload = Encoding.UTF8.GetBytes(BuildRequestBody(settings, messages, toolsJson, stream));
                request.ContentLength = payload.Length;
                using (var streamOut = request.GetRequestStream())
                {
                    streamOut.Write(payload, 0, payload.Length);
                }

                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    if (!stream)
                    {
                        string text = reader.ReadToEnd();
                        var assistant = DeepSeekClient.Interpret(text, model, query);
                        if (!assistant.Success)
                        {
                            result.Ok = false;
                            result.Kind = assistant.ErrorKind;
                            result.ErrorMessage = assistant.ErrorMessage;
                            result.Note = assistant.Note;
                            return result;
                        }
                        result.Ok = true;
                        result.Content = assistant.Content;
                        result.Tokens = assistant.TotalTokens;
                        if (visible != null && !string.IsNullOrEmpty(assistant.Content))
                        {
                            visible.Append(assistant.Content);
                            if (onPartial != null) onPartial(assistant.Content);
                        }
                        result.ToolCalls = ExtractMessageToolCalls(text);
                        return result;
                    }

                    var content = new StringBuilder();
                    var calls = new Dictionary<int, AiToolCall>();
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        string ssePayload = ExtractSsePayload(line);
                        if (ssePayload == null) continue;      // 注释行/事件名行
                        if (ssePayload.Length == 0) continue;  // [DONE] 或空 data
                        bool done;
                        int tokens;
                        ApplyStreamDelta(ssePayload, content, calls, out done, out tokens, onPartial);
                        if (tokens > 0) result.Tokens = tokens;
                    }

                    result.Ok = true;
                    result.Content = content.ToString();
                    var list = new List<AiToolCall>(calls.Values);
                    list.Sort(delegate (AiToolCall a, AiToolCall b) { return a.Index.CompareTo(b.Index); });
                    result.ToolCalls = list;
                    return result;
                }
            }
            catch (WebException ex)
            {
                string responseText = TryReadResponse(ex);
                var httpResponse = ex.Response as HttpWebResponse;
                int status = httpResponse == null ? 0 : (int)httpResponse.StatusCode;
                if (!string.IsNullOrEmpty(responseText))
                {
                    var parsed = DeepSeekClient.InterpretError(responseText, status, model, query);
                    result.Ok = false;
                    result.Kind = parsed.ErrorKind;
                    result.HttpStatus = status;
                    result.ErrorMessage = parsed.ErrorMessage;
                    result.Note = parsed.Note;
                    return result;
                }
                result.Ok = false;
                result.Kind = AiErrorKind.Network;
                result.HttpStatus = status;
                result.ErrorMessage = ex.Message;
                result.Note = "调用大模型接口失败(" + url + "):" + ex.Status + ";" + ex.Message +
                              "。本次**没有模型回答**;可以先用本地知识库与插件窗口看数据。";
                return result;
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.Kind = AiErrorKind.Network;
                result.ErrorMessage = ex.Message;
                result.Note = "调用大模型接口失败:" + ex.Message + "(接口地址:" + url + ")";
                return result;
            }
        }

        /// <summary>从**非流式**应答里取 <c>choices[0].message.tool_calls</c>。</summary>
        public static List<AiToolCall> ExtractMessageToolCalls(string responseJson)
        {
            var calls = new List<AiToolCall>();
            try
            {
                var root = JsonValue.Parse(responseJson);
                var toolCalls = root.Get("choices").Get(0).Get("message").Get("tool_calls");
                if (!toolCalls.IsArray) return calls;
                int index = 0;
                foreach (var item in toolCalls.Items)
                {
                    calls.Add(new AiToolCall
                    {
                        Index = item.Get("index").AsInt(index),
                        Id = item.Get("id").AsString(""),
                        Name = item.Get("function").Get("name").AsString(""),
                        ArgumentsJson = item.Get("function").Get("arguments").AsString("{}")
                    });
                    index++;
                }
            }
            catch
            {
                // 解析不了就当没有工具调用:模型会直接给回答,不会静默执行任何命令
            }
            return calls;
        }

        private static string TryReadResponse(WebException ex)
        {
            try
            {
                var response = ex.Response as HttpWebResponse;
                if (response == null) return null;
                using (response)
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
