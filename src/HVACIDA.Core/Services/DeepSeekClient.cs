using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// **DeepSeek 客户端**(对话补全接口,OpenAI 兼容)。
    /// <para>
    /// 形态**照 DeepSeek 官方文档实现**,不是猜的:
    /// base_url <c>https://api.deepseek.com</c>、<c>POST /chat/completions</c>、
    /// 请求头 <c>Content-Type: application/json</c> + <c>Authorization: Bearer &lt;API key&gt;</c>、
    /// body <c>{ "model": "...", "messages": [ {role, content} … ], "stream": false }</c>;
    /// 成功时回答在 <c>choices[0].message.content</c>,用量在 <c>usage</c>;
    /// 失败以 **HTTP 状态码**区分(400 格式错误 / 401 认证失败 / 402 余额不足 / 422 参数错误 / 429 限速 / 5xx 服务端故障)。
    /// </para>
    /// <para>
    /// 与 ima 客户端同一套取舍:**只用 .NET 自带类型**(<see cref="HttpWebRequest"/> + 自写
    /// <see cref="JsonValue"/>),**不引任何 NuGet**;失败**不抛异常**,一律返回
    /// <see cref="AiChatResult"/>,由界面照实显示 —— 绝不拿本地规则答案冒充模型回答。
    /// </para>
    /// </summary>
    public static class DeepSeekClient
    {
        /// <summary>官方接口根地址(私有化/代理可在设置里改)。</summary>
        public const string DefaultEndpoint = "https://api.deepseek.com";

        /// <summary>官方文档给的默认模型名(留空时用它;型号与价格以官方文档为准)。</summary>
        public const string DefaultModel = "deepseek-flash";

        /// <summary>对话补全路径(OpenAI 兼容)。</summary>
        public const string ChatCompletionsPath = "/chat/completions";

        /// <summary>默认超时(毫秒)。</summary>
        public const int DefaultTimeoutMs = 60000;

        /// <summary>隐私口径(界面与文档共用同一句话,避免"到底发了什么"说不清)。</summary>
        public const string PrivacyNote =
            "调用 DeepSeek 会联网:插件只发送**你的问题 + 检索到的依据文本**(界面下方的依据清单就是全部内容);" +
            "**不发送** Revit 模型数据、工程输入、计算结果文件。请勿在提问中粘贴涉密内容。";

        /// <summary>本次实际请求的完整地址(<paramref name="settings"/> 没填就按官方地址)。</summary>
        public static string ResolveUrl(AiChatSettings settings)
        {
            string endpoint = settings == null ? null : settings.Endpoint;
            if (string.IsNullOrWhiteSpace(endpoint)) endpoint = DefaultEndpoint;
            endpoint = endpoint.Trim().TrimEnd('/');
            if (endpoint.EndsWith(ChatCompletionsPath, StringComparison.OrdinalIgnoreCase)) return endpoint;
            return endpoint + ChatCompletionsPath;
        }

        /// <summary>本次实际使用的模型名(留空用官方默认)。</summary>
        public static string ResolveModel(AiChatSettings settings)
        {
            string model = settings == null ? null : settings.Model;
            return string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
        }

        /// <summary>
        /// 组装请求体(纯函数,便于自检逐字段核对)。
        /// 字段与官方样例一致:model / messages / stream=false,另加工程问答需要的 temperature 与 max_tokens。
        /// </summary>
        public static string BuildRequestBody(AiChatSettings settings, IList<AiChatMessage> messages)
        {
            settings = settings ?? new AiChatSettings();
            double temperature = settings.Temperature;
            if (temperature < 0) temperature = 0;
            if (temperature > 2) temperature = 2;
            int maxTokens = settings.MaxTokens > 0 ? settings.MaxTokens : 1024;

            var sb = new StringBuilder();
            sb.Append("{\"model\":\"").Append(JsonValue.Escape(ResolveModel(settings))).Append("\",");
            sb.Append("\"messages\":[");
            if (messages != null)
            {
                for (int i = 0; i < messages.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"role\":\"").Append(JsonValue.Escape(messages[i].Role))
                      .Append("\",\"content\":\"").Append(JsonValue.Escape(messages[i].Content)).Append("\"}");
                }
            }
            sb.Append("],");
            sb.Append("\"stream\":false,");
            sb.Append("\"temperature\":").Append(temperature.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"max_tokens\":").Append(maxTokens);
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>发一次对话补全请求(失败不抛异常)。</summary>
        public static AiChatResult Ask(AiChatSettings settings, IList<AiChatMessage> messages, string query)
        {
            int timeoutMs = settings != null && settings.TimeoutSeconds > 0
                ? settings.TimeoutSeconds * 1000
                : DefaultTimeoutMs;
            return Ask(settings, messages, query, timeoutMs);
        }

        /// <summary>发一次对话补全请求(可指定超时,便于自检/断网演练)。</summary>
        public static AiChatResult Ask(AiChatSettings settings, IList<AiChatMessage> messages, string query, int timeoutMs)
        {
            var guard = CheckCredentials(settings, query);
            if (guard != null) return guard;

            if (timeoutMs <= 0) timeoutMs = DefaultTimeoutMs;
            string url = ResolveUrl(settings);
            string model = ResolveModel(settings);
            string body = BuildRequestBody(settings, messages);
            var watch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                HttpTls.EnsureTls12();
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Accept = "application/json";
                request.Headers["Authorization"] = "Bearer " + settings.ApiKey.Trim();
                request.Timeout = timeoutMs;
                request.ReadWriteTimeout = timeoutMs;
                request.KeepAlive = false;
                request.UserAgent = "HVACIDA/0.1 (Revit add-in)";

                byte[] payload = Encoding.UTF8.GetBytes(body);
                request.ContentLength = payload.Length;
                using (var stream = request.GetRequestStream())
                {
                    stream.Write(payload, 0, payload.Length);
                }

                string text;
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    text = reader.ReadToEnd();
                }

                var result = Interpret(text, model, query);
                result.ElapsedMs = watch.ElapsedMilliseconds;
                return result;
            }
            catch (WebException ex)
            {
                string responseText = TryReadResponse(ex);
                var httpResponse = ex.Response as HttpWebResponse;
                int status = httpResponse == null ? 0 : (int)httpResponse.StatusCode;
                if (!string.IsNullOrEmpty(responseText))
                {
                    var parsed = InterpretError(responseText, status, model, query);
                    if (!string.IsNullOrEmpty(parsed.ErrorMessage)) { parsed.ElapsedMs = watch.ElapsedMilliseconds; return parsed; }
                }
                var failed = AiChatResult.Fail(query, AiErrorKind.Network, status, ex.Message,
                    DescribeNetworkError(ex, url, timeoutMs));
                failed.ElapsedMs = watch.ElapsedMilliseconds;
                return failed;
            }
            catch (Exception ex)
            {
                var failed = AiChatResult.Fail(query, AiErrorKind.Network, 0, ex.Message,
                    "调用 DeepSeek 接口失败:" + ex.Message + "(接口地址:" + url +
                    ")。本次**没有模型回答**;本地检索结果不受影响。");
                failed.ElapsedMs = watch.ElapsedMilliseconds;
                return failed;
            }
        }

        /// <summary>把成功应答解读成结果(纯函数,不发网络;自检直接喂样例应答)。</summary>
        public static AiChatResult Interpret(string responseJson, string model, string query)
        {
            JsonValue root;
            try
            {
                root = JsonValue.Parse(responseJson);
            }
            catch (Exception ex)
            {
                return AiChatResult.Fail(query, AiErrorKind.BadResponse, 0, "应答不是合法 JSON:" + ex.Message,
                    "DeepSeek 接口返回的内容无法解析(也可能是代理/网关插入了页面)。本次没有模型回答。");
            }

            var error = root.Get("error");
            if (error.IsObject)
            {
                var failed = InterpretError(responseJson, 0, model, query);
                failed.ErrorKind = AiErrorKind.BadRequest;
                return failed;
            }

            var choices = root.Get("choices");
            if (!choices.IsArray || choices.Count == 0)
            {
                return AiChatResult.Fail(query, AiErrorKind.BadResponse, 0,
                    "应答里没有 choices(字段可能变了,或返回了非对话补全的内容)",
                    "DeepSeek 返回的成功应答里找不到回答字段,本次没有可用回答。");
            }

            string content = choices.Get(0).Get("message").Get("content").AsString("");
            if (string.IsNullOrWhiteSpace(content))
            {
                return AiChatResult.Fail(query, AiErrorKind.BadResponse, 0, "choices[0].message.content 为空",
                    "DeepSeek 返回了空回答(可能被内容策略拦下或 max_tokens 太小)。");
            }

            var usage = root.Get("usage");
            var result = new AiChatResult
            {
                Success = true,
                Content = content.Trim(),
                Query = query ?? "",
                Model = string.IsNullOrEmpty(root.Get("model").AsString("")) ? model : root.Get("model").AsString(""),
                PromptTokens = usage.Get("prompt_tokens").AsInt(0),
                CompletionTokens = usage.Get("completion_tokens").AsInt(0),
                TotalTokens = usage.Get("total_tokens").AsInt(0)
            };
            result.Note = "模型回答由 DeepSeek 生成(" + result.Model + ")," +
                          "**必须对着下面的依据与标准原文核对后才能用**;" + result.UsageText + "。";
            return result;
        }

        /// <summary>把错误应答 / HTTP 状态码解读成失败结果(纯函数)。</summary>
        public static AiChatResult InterpretError(string responseJson, int httpStatus, string model, string query)
        {
            string message = "";
            string code = "";
            try
            {
                var root = JsonValue.Parse(responseJson);
                var error = root.Get("error");
                if (error.IsObject)
                {
                    message = error.Get("message").AsString("");
                    code = error.Get("code").AsString("");
                }
                else if (error.Kind == JsonKind.String)
                {
                    message = error.AsString("");
                }
                if (string.IsNullOrEmpty(message)) message = root.Get("message").AsString("");
                if (string.IsNullOrEmpty(code)) code = root.Get("code").AsString("");
            }
            catch
            {
                // 不是 JSON(网关 HTML 之类):把原文截一段给用户,便于判断是网络还是接口
                string raw = (responseJson ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
                if (raw.Length > 200) raw = raw.Substring(0, 200) + "…";
                message = raw;
            }

            var kind = KindOf(httpStatus);
            string reason = DescribeHttpStatus(httpStatus);
            string detail = string.IsNullOrEmpty(message) ? "(接口未给出 message)" : message;
            string errorMessage = "HTTP " + httpStatus + " " + reason + ";" +
                                  (string.IsNullOrEmpty(code) ? "" : "code=" + code + ";") + "message:" + detail;
            return AiChatResult.Fail(query, kind, httpStatus, errorMessage,
                "DeepSeek 接口返回失败:" + reason + " —— " + detail +
                "。本次**没有模型回答**;本地检索到的依据仍在下面的清单里。");
        }

        /// <summary>HTTP 状态码 → 官方文档给出的原因(未知码只报码,不自造含义)。</summary>
        public static string DescribeHttpStatus(int httpStatus)
        {
            switch (httpStatus)
            {
                case 400: return "格式错误(请求体格式不对)";
                case 401: return "认证失败(API key 不对或没带)";
                case 402: return "余额不足(请到 platform.deepseek.com 充值)";
                case 422: return "参数错误(检查模型名与参数取值)";
                case 429: return "请求速率达到上限(稍后重试,或降低频率)";
                case 500: return "服务器故障(稍后重试)";
                case 503: return "服务器繁忙(稍后重试)";
                case 0: return "未能取得 HTTP 状态(多为网络/代理问题)";
                default: return "接口返回 HTTP " + httpStatus;
            }
        }

        /// <summary>HTTP 状态码 → 失败分类。</summary>
        public static AiErrorKind KindOf(int httpStatus)
        {
            switch (httpStatus)
            {
                case 400:
                case 422: return AiErrorKind.BadRequest;
                case 401: return AiErrorKind.Unauthorized;
                case 402: return AiErrorKind.InsufficientBalance;
                case 429: return AiErrorKind.RateLimited;
                case 500:
                case 502:
                case 503:
                case 504: return AiErrorKind.ServerError;
                case 0: return AiErrorKind.Network;
                default: return AiErrorKind.BadRequest;
            }
        }

        // ------------------------------------------------------------------ 内部

        private static AiChatResult CheckCredentials(AiChatSettings settings, string query)
        {
            if (settings == null)
                return AiChatResult.Fail(query, AiErrorKind.NotEnabled, 0, "没有 AI 设置",
                    "未提供 AI 问答设置,本次只用本地知识库检索。");

            if (!settings.Enabled)
                return AiChatResult.Fail(query, AiErrorKind.NotEnabled, 0, "未启用",
                    "AI 问答未启用(在知识库窗勾选「启用 AI 问答(DeepSeek)」并保存后才发请求)。");

            if (!settings.IsConfigured)
                return AiChatResult.Fail(query, AiErrorKind.NoCredential, 0, "缺 API key",
                    "AI 问答缺 DeepSeek API key —— " + settings.MissingCredentialText() +
                    AiChatSettings.CredentialHelp);

            return null;
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

        private static string DescribeNetworkError(WebException ex, string url, int timeoutMs)
        {
            string hint;
            switch (ex.Status)
            {
                case WebExceptionStatus.Timeout:
                    hint = "请求超时(" + url + "," + timeoutMs + " ms 内没有应答)";
                    break;
                case WebExceptionStatus.NameResolutionFailure:
                case WebExceptionStatus.ProxyNameResolutionFailure:
                    hint = "域名解析失败(连不上 " + url + ";检查网络/代理/是否允许出网)";
                    break;
                case WebExceptionStatus.ConnectFailure:
                    hint = "连接被拒绝或不通(" + url + ";检查网络/代理/防火墙是否放通 443)";
                    break;
                case WebExceptionStatus.TrustFailure:
                case WebExceptionStatus.SecureChannelFailure:
                    hint = "TLS 证书校验失败(" + url + ";企业代理替换证书时请让信息部门把 api.deepseek.com 加白)";
                    break;
                default:
                    hint = "网络请求失败(" + url + ";" + ex.Status + ";" + ex.Message + ")";
                    break;
            }
            return "调用 DeepSeek 没有成功 —— " + hint + "。本次**没有模型回答**,本地检索到的依据不受影响。";
        }
    }
}
