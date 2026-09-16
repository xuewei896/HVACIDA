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
    /// **ima 在线知识库客户端**(腾讯 ima 开放接口:HTTP POST + JSON)。
    /// <para>
    /// 接口形态(社区 skill 包整理、本机未联网比对官方文档,字段以实际返回为准):
    /// <c>POST https://ima.qq.com/openapi/wiki/v1/search_knowledge</c>,
    /// 请求头 <c>ima-openapi-clientid</c> / <c>ima-openapi-apikey</c>,
    /// body <c>{ query, cursor, knowledge_base_id }</c>,
    /// 应答统一为 <c>{ retcode, errmsg, data }</c>,命中在 <c>data.info_list[]</c>
    /// (<c>media_id / title / parent_folder_id / highlight_content</c>)。
    /// </para>
    /// <para>
    /// 三条硬约束(与本仓库口径一致):
    /// ① **shareId 不是凭证** —— 只用 Client ID / API Key / 知识库 ID 三样;
    /// ② **网络失败不抛异常** —— 一律返回 <see cref="ImaKnowledgeSearchResult"/>,
    ///    由界面照实显示"没查到 + 原因",不拿本地条目冒充在线结果;
    /// ③ **返回的是片段不是全文** —— <c>highlight_content</c> 是命中高亮片段,
    ///    界面与报告都标注"以 ima 原文为准"。
    /// </para>
    /// <para>
    /// 全部实现只用 .NET Framework 自带类型(<see cref="HttpWebRequest"/> + 自写 <see cref="JsonValue"/>),
    /// **不引任何 NuGet**;只在用户显式启用并填好凭证后才会发网络请求。
    /// </para>
    /// </summary>
    public static class ImaOpenApiClient
    {
        /// <summary>官方接口根地址(如需私有化部署/代理,可在设置里改 Endpoint)。</summary>
        public const string DefaultEndpoint = "https://ima.qq.com/openapi/wiki/v1/";

        /// <summary>默认超时(毫秒)。Revit 是交互进程,超时给短一点,失败了照实说,别卡住界面。</summary>
        public const int DefaultTimeoutMs = 12000;

        /// <summary>检索接口名。</summary>
        public const string SearchApi = "search_knowledge";

        /// <summary>知识库信息接口名(用于"测试连接")。</summary>
        public const string KnowledgeBaseApi = "get_knowledge_base";

        private static bool _tlsReady;

        /// <summary>接口地址(设置里没填就用官方地址;统一补上结尾的「/」)。</summary>
        public static string ResolveEndpoint(ImaKnowledgeSettings settings)
        {
            string endpoint = settings == null ? null : settings.Endpoint;
            if (string.IsNullOrWhiteSpace(endpoint)) endpoint = DefaultEndpoint;
            endpoint = endpoint.Trim();
            if (!endpoint.EndsWith("/", StringComparison.Ordinal)) endpoint += "/";
            return endpoint;
        }

        /// <summary>
        /// 在知识库里检索(接口 <see cref="SearchApi"/>)。
        /// <para>
        /// <see cref="ImaKnowledgeSettings.Limit"/> **不是接口参数**(接口未提供条数参数),只在本地截取返回条数。
        /// </para>
        /// </summary>
        public static ImaKnowledgeSearchResult Search(ImaKnowledgeSettings settings, string query)
        {
            return Search(settings, query, DefaultTimeoutMs);
        }

        /// <summary>在知识库里检索(可指定超时,便于自检/断网演练)。</summary>
        public static ImaKnowledgeSearchResult Search(ImaKnowledgeSettings settings, string query, int timeoutMs)
        {
            string text = (query ?? "").Trim();
            if (text.Length == 0)
                return ImaKnowledgeSearchResult.Fail(query, -1, "问题为空", "请先输入要检索的问题。");

            var guard = CheckCredentials(settings, text, true);
            if (guard != null) return guard;

            // 请求体:接口要求 cursor 首次传空串
            string body = "{\"query\":\"" + JsonValue.Escape(text) + "\",\"cursor\":\"\"," +
                          "\"knowledge_base_id\":\"" + JsonValue.Escape(settings.KnowledgeBaseId.Trim()) + "\"}";
            return Post(settings, SearchApi, body, text, false, timeoutMs);
        }

        /// <summary>
        /// **连通性测试**:拉一次知识库信息(接口 <see cref="KnowledgeBaseApi"/>)。
        /// 用来在装配凭证时立刻分辨"凭证/权限不对"还是"网络不通"。
        /// </summary>
        public static ImaKnowledgeSearchResult TestConnection(ImaKnowledgeSettings settings)
        {
            return TestConnection(settings, DefaultTimeoutMs);
        }

        /// <summary>连通性测试(可指定超时)。</summary>
        public static ImaKnowledgeSearchResult TestConnection(ImaKnowledgeSettings settings, int timeoutMs)
        {
            var guard = CheckCredentials(settings, "", false);
            if (guard != null) return guard;

            string body = "{\"ids\":[\"" + JsonValue.Escape(settings.KnowledgeBaseId.Trim()) + "\"]}";
            return Post(settings, KnowledgeBaseApi, body, "", true, timeoutMs);
        }

        /// <summary>把检索应答文本解读成结果(纯函数,不发网络;自检直接喂样例应答)。</summary>
        public static ImaKnowledgeSearchResult InterpretSearch(string responseJson, string query)
        {
            JsonValue root;
            try
            {
                root = JsonValue.Parse(responseJson);
            }
            catch (Exception ex)
            {
                return ImaKnowledgeSearchResult.Fail(query, -1, "应答不是合法 JSON:" + ex.Message,
                    "ima 接口返回的内容无法解析,请把接口地址与凭证核对一遍(也可能是代理/网关插入了页面)。");
            }

            int retcode = root.Get("retcode").AsInt(-1);
            string errmsg = root.Get("errmsg").AsString("");
            if (retcode != 0)
            {
                return ImaKnowledgeSearchResult.Fail(query, retcode, errmsg,
                    "ima 接口返回失败:retcode=" + retcode + "," + DescribeRetCode(retcode, errmsg));
            }

            var result = new ImaKnowledgeSearchResult
            {
                Success = true,
                Query = query ?? "",
                RetCode = 0
            };

            var data = root.Get("data");
            var list = data.Get("info_list");
            if (list.IsArray)
            {
                foreach (var item in list.Items)
                {
                    result.Hits.Add(new ImaKnowledgeHit
                    {
                        MediaId = item.Get("media_id").AsString(""),
                        Title = item.Get("title").AsString(""),
                        ParentFolderId = item.Get("parent_folder_id").AsString(""),
                        Highlight = item.Get("highlight_content").AsString("")
                    });
                }
            }
            result.HasMore = !data.Get("is_end").AsBool(true);

            result.Note = result.Count > 0
                ? "ima 知识库命中 " + result.Count + " 条。" +
                  (result.HasMore ? "接口显示还有更多结果(本次只取第一页)。" : "") +
                  "返回的是**标题与命中片段**,不是条文全文 —— 引用请回 ima 打开原文核对。"
                : "ima 知识库返回成功但没有匹配「" + (query ?? "") + "」的内容(0 条)。" +
                  "可以换个说法再试,或确认这条内容确实在这个知识库里。";
            return result;
        }

        /// <summary>把知识库信息应答解读成"连接是否正常"(纯函数,不发网络)。</summary>
        public static ImaKnowledgeSearchResult InterpretConnection(string responseJson, string knowledgeBaseId)
        {
            JsonValue root;
            try
            {
                root = JsonValue.Parse(responseJson);
            }
            catch (Exception ex)
            {
                return ImaKnowledgeSearchResult.Fail("", -1, "应答不是合法 JSON:" + ex.Message,
                    "ima 接口返回的内容无法解析,请核对接口地址与凭证。");
            }

            int retcode = root.Get("retcode").AsInt(-1);
            string errmsg = root.Get("errmsg").AsString("");
            if (retcode != 0)
            {
                return ImaKnowledgeSearchResult.Fail("", retcode, errmsg,
                    "ima 接口返回失败:retcode=" + retcode + "," + DescribeRetCode(retcode, errmsg));
            }

            string id = (knowledgeBaseId ?? "").Trim();
            var result = new ImaKnowledgeSearchResult
            {
                Success = true,
                RetCode = 0,
                IsConnectionTest = true
            };

            var infos = root.Get("data").Get("infos");
            var info = infos.Get(id);
            if (info.IsNull && infos.IsObject)
            {
                // 只传一个 ID 却查不到,通常说明这个 ID 不属于当前凭证;逐个找一遍,能找着就用真实的
                foreach (string name in infos.Names)
                {
                    info = infos.Get(name);
                    id = name;
                    break;
                }
            }

            if (!info.IsNull)
            {
                string kbName = info.Get("name").AsString("");
                string description = info.Get("description").AsString("");
                result.Note = "ima 连接正常:知识库「" + (string.IsNullOrEmpty(kbName) ? id : kbName) + "」" +
                              (string.IsNullOrEmpty(description) ? "" : "(" + description + ")") +
                              "。检索时返回的是命中片段,引用请回 ima 打开原文核对。";
            }
            else
            {
                result.Note = "ima 接口连通、凭证可用,但返回里没有知识库「" + id + "」的信息 —— " +
                              "请确认「知识库 ID」填的是知识库 ID(不是 shareId、也不是知识库名称)。";
            }
            return result;
        }

        /// <summary>ima 错误码 → 一句人话(未知码只回 errmsg,不自造含义)。</summary>
        public static string DescribeRetCode(int retcode, string errmsg)
        {
            string reason;
            switch (retcode)
            {
                case 110001: reason = "参数非法"; break;
                case 110002: reason = "服务配置非法"; break;
                case 110010: reason = "下游网络错误(可重试)"; break;
                case 110011: reason = "下游逻辑错误(重试无用)"; break;
                case 110012: reason = "接口不存在"; break;
                case 110013: reason = "客户端取消(多为请求超时)"; break;
                case 110020: reason = "内容被安全策略拦截"; break;
                case 110021: reason = "请求过于频繁(降低频率后重试)"; break;
                case 110030: reason = "无权限(检查该 Client 是否被授权访问这个知识库)"; break;
                default: reason = "未收录的错误码"; break;
            }
            string message = string.IsNullOrEmpty(errmsg) ? "(ima 未给出 errmsg)" : errmsg;
            return reason + ";ima 原文:" + message;
        }

        // ------------------------------------------------------------------ 网络实现

        /// <summary>
        /// 凭证/启用状态检查:不满足就不发请求,直接给出"缺什么"。
        /// <paramref name="requireEnabled"/> = false 用于「测试连接」—— 用户就是来试凭证的,
        /// 这时候不该因为还没勾"启用"而拒绝测试(测试本身也是显式动作)。
        /// </summary>
        private static ImaKnowledgeSearchResult CheckCredentials(ImaKnowledgeSettings settings, string query,
            bool requireEnabled)
        {
            if (settings == null)
                return ImaKnowledgeSearchResult.Fail(query, -1, "没有 ima 设置",
                    "未提供 ima 在线知识库设置,本次只用本地知识库检索。");

            if (requireEnabled && !settings.Enabled)
                return ImaKnowledgeSearchResult.Fail(query, -1, "未启用",
                    "ima 在线知识库未启用(界面勾选「启用 ima 在线知识库」并保存后才发请求)。");

            if (!settings.IsConfigured)
                return ImaKnowledgeSearchResult.Fail(query, -1, "凭证不全",
                    "ima 在线知识库凭证不全 —— " + settings.MissingCredentialText() +
                    ImaKnowledgeSettings.CredentialHelp);

            return null;
        }

        private static ImaKnowledgeSearchResult Post(ImaKnowledgeSettings settings, string api, string body,
            string query, bool isConnectionTest, int timeoutMs)
        {
            if (timeoutMs <= 0) timeoutMs = DefaultTimeoutMs;
            string url = ResolveEndpoint(settings) + api;
            try
            {
                EnsureTls();
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "application/json; charset=utf-8";
                request.Accept = "application/json";
                request.Headers["ima-openapi-clientid"] = settings.ClientId.Trim();
                request.Headers["ima-openapi-apikey"] = settings.ApiKey.Trim();
                request.Timeout = timeoutMs;
                request.ReadWriteTimeout = timeoutMs;
                request.KeepAlive = false;      // 长驻进程里别留半死的 Keep-Alive 连接
                request.UserAgent = "HVACIDA/0.1 (Revit add-in)";

                byte[] payload = Encoding.UTF8.GetBytes(body ?? "{}");
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

                var result = isConnectionTest
                    ? InterpretConnection(text, settings.KnowledgeBaseId)
                    : InterpretSearch(text, query);
                if (!isConnectionTest && result.Success && settings.Limit > 0 && result.Hits.Count > settings.Limit)
                {
                    result.Hits = result.Hits.GetRange(0, settings.Limit);
                    result.Note = "ima 知识库命中 " + result.Count + " 条(按设置只显示前 " + settings.Limit + " 条)。" +
                                  (result.HasMore ? "接口显示还有更多结果。" : "") +
                                  "返回的是**标题与命中片段**,不是条文全文 —— 引用请回 ima 打开原文核对。";
                }
                return result;
            }
            catch (WebException ex)
            {
                // 接口层报错(4xx/5xx)时服务器往往仍带 JSON body,先读出来,能拿到 retcode 就用它
                string responseText = TryReadResponse(ex);
                if (!string.IsNullOrEmpty(responseText))
                {
                    var parsed = isConnectionTest
                        ? InterpretConnection(responseText, settings.KnowledgeBaseId)
                        : InterpretSearch(responseText, query);
                    if (!parsed.Success && parsed.RetCode >= 0) return parsed;
                }
                return ImaKnowledgeSearchResult.Fail(query, -1, ex.Message, DescribeNetworkError(ex, url, timeoutMs));
            }
            catch (Exception ex)
            {
                return ImaKnowledgeSearchResult.Fail(query, -1, ex.Message,
                    "调用 ima 接口失败:" + ex.Message + "(接口地址:" + url + ")");
            }
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
                    hint = "TLS 证书校验失败(" + url + ";企业代理替换证书时请让信息部门把 ima 域名加白)";
                    break;
                case WebExceptionStatus.ProtocolError:
                    hint = "接口返回了 HTTP 错误状态(" + url + ";" + ex.Message + ")";
                    break;
                default:
                    hint = "网络请求失败(" + url + ";" + ex.Status + ";" + ex.Message + ")";
                    break;
            }
            return "ima 在线检索没有成功 —— " + hint +
                   "。本条问答**只用本地知识库**的结果,没有编造在线内容。";
        }

        /// <summary>Revit 2020 进程默认可能只开 TLS 1.0/1.1,ima 走 HTTPS,这里显式打开 TLS 1.2。</summary>
        private static void EnsureTls()
        {
            if (_tlsReady) return;
            try
            {
                ServicePointManager.SecurityProtocol =
                    ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12;
            }
            catch
            {
                // 老系统上没有 Tls12 枚举值也不影响:后面连不上会照实报错
            }
            _tlsReady = true;
        }

        /// <summary>把命中片段拼成给人看的答复块(界面与报告共用;空片段不编内容)。</summary>
        public static string FormatHits(IList<ImaKnowledgeHit> hits)
        {
            if (hits == null || hits.Count == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < hits.Count; i++)
            {
                var hit = hits[i];
                sb.Append("[").Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append("] ");
                sb.Append(string.IsNullOrEmpty(hit.Title) ? "(无标题)" : hit.Title);
                sb.AppendLine();
                string snippet = string.IsNullOrEmpty(hit.Highlight)
                    ? "(接口未返回命中片段,请到 ima 里打开该条目查看正文)"
                    : hit.Highlight;
                sb.Append("    ").AppendLine(snippet.Replace("\r\n", " ").Replace('\n', ' ').Trim());
                sb.AppendLine("    " + hit.SourceText);
            }
            return sb.ToString();
        }
    }
}
