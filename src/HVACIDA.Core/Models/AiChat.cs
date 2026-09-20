using System;
using System.Collections.Generic;

namespace HVACIDA.Core.Models
{
    /// <summary>
    /// **AI 问答(DeepSeek)接入设置**。
    /// <para>
    /// 接口形态**照官方文档实现**(DeepSeek API 文档「首次调用 API」):
    /// base_url <c>https://api.deepseek.com</c>、<c>POST /chat/completions</c>、
    /// 请求头 <c>Authorization: Bearer &lt;API key&gt;</c>、body <c>{ model, messages, stream:false, … }</c>;
    /// 与 OpenAI 兼容。错误以 **HTTP 状态码**区分(400 格式 / 401 认证 / 402 余额 / 422 参数 / 429 限速 / 5xx 服务端)。
    /// </para>
    /// <para>
    /// 三条硬约束(与本仓库口径一致):
    /// ① **不把模型输出当结论** —— 回答必须挂在检索到的**依据**上,并列出依据清单;
    /// ② **只发该发的** —— 只把「你的问题 + 检索到的依据文本」发往云端,**不发送 Revit 模型数据与工程输入**;
    /// ③ **失败不编** —— 未启用 / 没填 Key / 网络不通 / 接口报错,都照实说,并保留本地检索结果。
    /// </para>
    /// </summary>
    public class AiChatSettings
    {
        /// <summary>是否启用 AI 问答(默认关:没填 Key 就不发任何请求)。</summary>
        public bool Enabled { get; set; }

        /// <summary>DeepSeek API key(**本机明文保存,勿外发**;在 platform.deepseek.com 申请)。</summary>
        public string ApiKey { get; set; } = "";

        /// <summary>接口地址(留空即官方 <c>https://api.deepseek.com</c>;自建/代理时可改)。</summary>
        public string Endpoint { get; set; } = "";

        /// <summary>模型名(留空即官方文档给的默认模型,见 <c>DeepSeekClient.DefaultModel</c>)。</summary>
        public string Model { get; set; } = "";

        /// <summary>服务预设键(见 <c>AiProviderPresets</c>;自定义时为 <c>custom</c>)。</summary>
        public string Provider { get; set; } = "";

        /// <summary>采样温度(工程问答要稳,默认 0.2;允许 0~2)。</summary>
        public double Temperature { get; set; } = 0.2;

        /// <summary>单次回答的最大生成 token(默认 1024,避免长答跑偏与费用失控)。</summary>
        public int MaxTokens { get; set; } = 1024;

        /// <summary>单次请求超时(秒,默认 60)。</summary>
        public int TimeoutSeconds { get; set; } = 60;

        /// <summary>是否用流式(SSE)接收:AI 助手默认 true(打字机效果);本地知识库问答用 false 也可以。</summary>
        public bool Stream { get; set; } = true;

        /// <summary>送入模型的**依据条数上限**(默认 5)。</summary>
        public int ContextEntryLimit { get; set; } = 5;

        /// <summary>送入模型的**依据文本总字数上限**(默认 6000;超了截断并在提示里写明)。</summary>
        public int ContextMaxChars { get; set; } = 6000;

        /// <summary>是否具备发请求的必要条件(Enabled 由调用方另外判断)。</summary>
        public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

        /// <summary>缺什么(界面直接显示,不猜)。</summary>
        public string MissingCredentialText()
        {
            return IsConfigured ? "凭证齐全(有 API key)。" : "还缺:DeepSeek API key。";
        }

        /// <summary>给用户看的凭证/隐私说明(界面与说明文件共用同一段话)。</summary>
        public static string CredentialHelp =>
            "AI 问答走 DeepSeek 官方接口(OpenAI 兼容):需要 **API key**(在 platform.deepseek.com 申请),默认接口 https://api.deepseek.com、默认模型见官方文档。" +
            "调用会联网,只发送**你的问题 + 插件检索到的依据文本**,**不发送 Revit 模型数据与工程输入**;" +
            "请勿在提问里粘贴涉密内容。模型回答是**生成文本**,必须对着列出的依据与标准原文核对后才能用 —— 插件不把模型输出当结论。";

        /// <summary>要发给模型的依据文本(供界面/报告显示"这次发了什么",不隐藏)。</summary>
        public string PrivacyNote => "本次发送:你的问题 + 插件检索到的依据文本(见下方依据清单)。未发送 Revit 模型数据与工程输入。";

        /// <summary>复制一份(避免界面直接改到已保存的对象)。</summary>
        public AiChatSettings Clone()
        {
            return new AiChatSettings
            {
                Enabled = Enabled,
                ApiKey = ApiKey ?? "",
                Endpoint = Endpoint ?? "",
                Model = Model ?? "",
                Provider = Provider ?? "",
                Temperature = Temperature,
                MaxTokens = MaxTokens,
                TimeoutSeconds = TimeoutSeconds,
                ContextEntryLimit = ContextEntryLimit,
                ContextMaxChars = ContextMaxChars,
                Stream = Stream
            };
        }
    }

    /// <summary>一条对话消息(role: system / user / assistant)。</summary>
    public class AiChatMessage
    {
        public AiChatMessage()
        {
        }

        public AiChatMessage(string role, string content)
        {
            Role = role;
            Content = content;
        }

        /// <summary>角色:system / user / assistant。</summary>
        public string Role { get; set; } = "user";

        /// <summary>正文。</summary>
        public string Content { get; set; } = "";

        /// <summary>
        /// 助手消息里带的**工具调用**(原始 JSON 数组文本;OpenAI 协议要求:
        /// 回传工具结果前必须先把带 <c>tool_calls</c> 的助手消息放进历史,否则接口会 400)。
        /// </summary>
        public string ToolCallsJson { get; set; } = "";

        /// <summary>工具结果消息对应的调用 id(role=tool 时必填)。</summary>
        public string ToolCallId { get; set; } = "";

        /// <summary>系统提示。</summary>
        public static AiChatMessage System(string content) => new AiChatMessage("system", content);

        /// <summary>用户消息。</summary>
        public static AiChatMessage User(string content) => new AiChatMessage("user", content);

        /// <summary>助手消息(可带工具调用)。</summary>
        public static AiChatMessage Assistant(string content, string toolCallsJson = "")
        {
            return new AiChatMessage("assistant", content) { ToolCallsJson = toolCallsJson ?? "" };
        }

        /// <summary>工具结果消息。</summary>
        public static AiChatMessage Tool(string toolCallId, string content)
        {
            return new AiChatMessage("tool", content) { ToolCallId = toolCallId ?? "" };
        }
    }

    /// <summary>**本次回答用到的依据**(来自本地知识库检索;界面与报告都列出来,便于核对)。</summary>
    public class AiCitation
    {
        /// <summary>依据编号(与提示词里的 [1] [2] 对应)。</summary>
        public int Index { get; set; }

        /// <summary>条目标题。</summary>
        public string Title { get; set; } = "";

        /// <summary>分类中文名(已定口径 / 规范条文 / 操作步骤 …)。</summary>
        public string Category { get; set; } = "";

        /// <summary>出处(必填;没有出处的条目不会进依据)。</summary>
        public string Source { get; set; } = "";

        /// <summary>依据正文(**按字数上限截断**;截断了会置 <see cref="Truncated"/>)。</summary>
        public string Text { get; set; } = "";

        /// <summary>是否被截断(超长条文/条目只送前若干字)。</summary>
        public bool Truncated { get; set; }

        /// <summary>检索得分(命中强度;低于 <c>KnowledgeBase.MinimumScore</c> 的算弱相关,界面要标注)。</summary>
        public int Score { get; set; }

        /// <summary>界面显示用一行。</summary>
        public string DisplayText =>
            "[" + Index + "] " + Category + " · " + Title + (Truncated ? "(已截断)" : "") + " — " + Source;
    }

    /// <summary>AI 问答失败的原因分类(界面照实说,便于用户知道该改什么)。</summary>
    public enum AiErrorKind
    {
        /// <summary>没失败。</summary>
        None = 0,

        /// <summary>没有设置 / 未启用。</summary>
        NotEnabled = 1,

        /// <summary>缺 API key。</summary>
        NoCredential = 2,

        /// <summary>网络/连不通/超时。</summary>
        Network = 3,

        /// <summary>认证失败(401,key 不对)。</summary>
        Unauthorized = 4,

        /// <summary>余额不足(402)。</summary>
        InsufficientBalance = 5,

        /// <summary>请求体/参数错误(400 / 422)。</summary>
        BadRequest = 6,

        /// <summary>限速(429)。</summary>
        RateLimited = 7,

        /// <summary>服务端故障(5xx)。</summary>
        ServerError = 8,

        /// <summary>应答无法解析。</summary>
        BadResponse = 9
    }

    /// <summary>
    /// **一次 AI 问答的结果**。失败**不抛异常**给界面,把原因写进
    /// <see cref="ErrorMessage"/> / <see cref="Note"/>,界面照实显示。
    /// </summary>
    public class AiChatResult
    {
        /// <summary>是否成功拿到模型回答。</summary>
        public bool Success { get; set; }

        /// <summary>模型回答正文(成功时)。</summary>
        public string Content { get; set; } = "";

        /// <summary>失败原因(接口的 message 原样透传 + 插件补的处置建议)。</summary>
        public string ErrorMessage { get; set; } = "";

        /// <summary>失败分类。</summary>
        public AiErrorKind ErrorKind { get; set; } = AiErrorKind.None;

        /// <summary>HTTP 状态码(没发出去时为 0)。</summary>
        public int HttpStatus { get; set; }

        /// <summary>提示语(成功与失败都有:依据几条、发往哪里、要不要联网核对)。</summary>
        public string Note { get; set; } = "";

        /// <summary>实际使用的模型名。</summary>
        public string Model { get; set; } = "";

        /// <summary>本次请求的问题。</summary>
        public string Query { get; set; } = "";

        /// <summary>送入模型的依据清单。</summary>
        public List<AiCitation> Citations { get; set; } = new List<AiCitation>();

        /// <summary>提示 token 数(接口 usage;没拿到为 0)。</summary>
        public int PromptTokens { get; set; }

        /// <summary>生成 token 数(接口 usage;没拿到为 0)。</summary>
        public int CompletionTokens { get; set; }

        /// <summary>总 token 数(接口 usage;没拿到为 0)。</summary>
        public int TotalTokens { get; set; }

        /// <summary>本次耗时(毫秒)。</summary>
        public long ElapsedMs { get; set; }

        /// <summary>依据条数。</summary>
        public int CitationCount => Citations.Count;

        /// <summary>是不是"没有检索到依据"(此时模型只能给通用建议,界面要标注)。</summary>
        public bool HasNoCitation => Citations.Count == 0;

        /// <summary>token 用量一句话(没有 usage 时不编数字)。</summary>
        public string UsageText => TotalTokens > 0
            ? "token:提示 " + PromptTokens + " / 生成 " + CompletionTokens + " / 合计 " + TotalTokens
            : "token:接口未返回用量";

        /// <summary>构造一条失败结果(不抛异常)。</summary>
        public static AiChatResult Fail(string query, AiErrorKind kind, int httpStatus, string errorMessage, string note)
        {
            return new AiChatResult
            {
                Success = false,
                Query = query ?? "",
                ErrorKind = kind,
                HttpStatus = httpStatus,
                ErrorMessage = errorMessage ?? "",
                Note = note ?? ""
            };
        }
    }
}
