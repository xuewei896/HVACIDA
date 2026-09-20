using System;
using System.Collections.Generic;

namespace HVACIDA.Core.Models
{
    /// <summary>
    /// **大模型服务预设**(照《如何将AI大模型(DeepSeek)接入Revit中》的做法:内置若干 OpenAI 兼容预设 + 自定义)。
    /// <para>
    /// 预设只提供「名称 + 接口地址 + 模型名」;**不含任何密钥**(密钥单独加密保存)。
    /// 各家的模型名与价格会变,以各家官方文档为准 —— 填错会照实报 422/400。
    /// </para>
    /// </summary>
    public class AiProviderPreset
    {
        /// <summary>预设键(设置里存这个)。</summary>
        public string Key { get; set; } = "";

        /// <summary>显示名。</summary>
        public string DisplayName { get; set; } = "";

        /// <summary>OpenAI 兼容的 base 地址(不含 /chat/completions)。</summary>
        public string Endpoint { get; set; } = "";

        /// <summary>默认模型名。</summary>
        public string Model { get; set; } = "";

        /// <summary>获取密钥的说明(界面上给用户指路)。</summary>
        public string KeyPage { get; set; } = "";

        /// <summary>界面显示用一行。</summary>
        public string DisplayText => DisplayName + "(" + Model + ")";
    }

    /// <summary>
    /// **一条可被模型调用的工具**(OpenAI function calling 的 function 定义)。
    /// <para>
    /// 只把**当前进程里真正注册成功**的命令发给模型(见 <c>AiToolCatalog</c>)——
    /// 老版本 Revit 不会看到它不支持的命令,模型也就不会去调它。
    /// </para>
    /// </summary>
    public class AiToolDefinition
    {
        /// <summary>命令名(与 <c>AiCommandBus</c> 注册名一致,模型调它)。</summary>
        public string Name { get; set; } = "";

        /// <summary>给模型看的说明(写清楚"什么时候用、返回什么")。</summary>
        public string Description { get; set; } = "";

        /// <summary>参数 schema(JSON Schema 文本;**必须是合法 JSON**,否则接口会 400)。</summary>
        public string ParametersJson { get; set; } = "{\"type\":\"object\",\"properties\":{}}";

        /// <summary>是否属于"会改模型"的操作(界面/日志要标出来)。</summary>
        public bool ModifiesModel { get; set; }
    }

    /// <summary>模型要求执行的一次工具调用(流式返回时参数是分片拼出来的)。</summary>
    public class AiToolCall
    {
        /// <summary>调用序号(流式分片按它累加)。</summary>
        public int Index { get; set; }

        /// <summary>调用 id(把结果回给模型时要带)。</summary>
        public string Id { get; set; } = "";

        /// <summary>命令名。</summary>
        public string Name { get; set; } = "";

        /// <summary>参数(拼完整的 JSON 文本)。</summary>
        public string ArgumentsJson { get; set; } = "";

        /// <summary>本次调用是否已执行。</summary>
        public bool Executed { get; set; }

        /// <summary>执行结果(JSON 或错误说明)。</summary>
        public string ResultJson { get; set; } = "";

        /// <summary>执行耗时(毫秒)。</summary>
        public long ElapsedMs { get; set; }

        /// <summary>界面显示用一行。</summary>
        public string DisplayText
        {
            get
            {
                string state = !Executed ? "未执行" : (ResultJson != null && ResultJson.StartsWith("{\"error", StringComparison.Ordinal) ? "失败" : "完成");
                return Name + "(" + state + "," + ElapsedMs + " ms)";
            }
        }
    }

    /// <summary>一次 AI 助手调用的阶段(界面据此显示状态)。</summary>
    public enum AiTurnStage
    {
        /// <summary>正在请求模型。</summary>
        Requesting = 0,

        /// <summary>正在接收流式回答。</summary>
        Streaming = 1,

        /// <summary>正在执行工具调用。</summary>
        ExecutingTool = 2,

        /// <summary>已给出最终回答。</summary>
        Answered = 3,

        /// <summary>失败。</summary>
        Failed = 4
    }

    /// <summary>**AI 助手的一次问答结果**(含工具调用轨迹与本地检索依据)。</summary>
    public class AiAssistantResult
    {
        /// <summary>是否拿到最终回答。</summary>
        public bool Success { get; set; }

        /// <summary>问题。</summary>
        public string Query { get; set; } = "";

        /// <summary>最终回答(Markdown/纯文本)。</summary>
        public string Content { get; set; } = "";

        /// <summary>失败原因(照实透传)。</summary>
        public string ErrorMessage { get; set; } = "";

        /// <summary>失败分类(复用 <see cref="AiErrorKind"/>)。</summary>
        public AiErrorKind ErrorKind { get; set; } = AiErrorKind.None;

        /// <summary>HTTP 状态码(没发出去为 0)。</summary>
        public int HttpStatus { get; set; }

        /// <summary>提示语(依据几条、发往哪里、工具执行了几次)。</summary>
        public string Note { get; set; } = "";

        /// <summary>实际使用的模型与地址。</summary>
        public string Model { get; set; } = "";

        /// <summary>本轮用到的本地依据(检索增强;可为空)。</summary>
        public List<AiCitation> Citations { get; set; } = new List<AiCitation>();

        /// <summary>工具调用轨迹(按发生顺序)。</summary>
        public List<AiToolCall> ToolCalls { get; set; } = new List<AiToolCall>();

        /// <summary>实际用掉的"模型轮次"(工具调用循环计数;上限见 <c>AiChatClient.MaxToolRounds</c>)。</summary>
        public int Rounds { get; set; }

        /// <summary>是否因为达到轮次上限而停(此时回答可能不完整,界面必须说明)。</summary>
        public bool HitRoundLimit { get; set; }

        /// <summary>累计 token(各轮相加;接口没给就是 0)。</summary>
        public int TotalTokens { get; set; }

        /// <summary>总耗时(毫秒)。</summary>
        public long ElapsedMs { get; set; }

        /// <summary>工具调用次数。</summary>
        public int ToolCallCount => ToolCalls.Count;

        /// <summary>token 用量一句话(没有 usage 时不编数字)。</summary>
        public string UsageText => TotalTokens > 0
            ? "token 合计 " + TotalTokens + "(含 " + Rounds + " 轮)"
            : "token:接口未返回用量";

        /// <summary>构造失败结果。</summary>
        public static AiAssistantResult Fail(string query, AiErrorKind kind, int httpStatus, string message, string note)
        {
            return new AiAssistantResult
            {
                Success = false,
                Query = query ?? "",
                ErrorKind = kind,
                HttpStatus = httpStatus,
                ErrorMessage = message ?? "",
                Note = note ?? ""
            };
        }
    }

    /// <summary>聊天记录的一条(界面左栏/主区显示)。</summary>
    public class AiChatTurn
    {
        public AiChatTurn(bool isUser, string text)
        {
            IsUser = isUser;
            Text = text ?? "";
        }

        /// <summary>是否用户说的话。</summary>
        public bool IsUser { get; }

        /// <summary>正文。</summary>
        public string Text { get; set; }
    }

    /// <summary>
    /// **工作区标识**:聊天记录与密钥按「Revit 版本 + Windows 用户 + 项目路径哈希」隔离
    /// (照参考文档 2.6 的做法:换一个 .rvt,记录与密钥不跟着跑)。
    /// </summary>
    public class AiWorkspaceScope
    {
        /// <summary>Revit 版本(如 2020)。</summary>
        public string RevitVersion { get; set; } = "";

        /// <summary>Windows 用户名。</summary>
        public string UserName { get; set; } = "";

        /// <summary>项目文件路径(未保存的文档用其标题)。</summary>
        public string ProjectPath { get; set; } = "";

        /// <summary>文档是否尚未保存过。</summary>
        public bool IsUnsaved { get; set; }

        /// <summary>隔离键(可读前缀 + 路径哈希,既能人读也便于比对)。</summary>
        public string Id
        {
            get
            {
                string project = string.IsNullOrWhiteSpace(ProjectPath) ? "(无项目)" : ProjectPath.Trim();
                string tag = (IsUnsaved ? "unsaved-" : "file-") + ShortHash(project);
                return Safe(RevitVersion) + "_" + Safe(UserName) + "_" + tag;
            }
        }

        /// <summary>未保存文档的临时工作区(另存为后应迁移,见 <see cref="Migrate"/>)。</summary>
        public static AiWorkspaceScope ForUnsaved(string revitVersion, string userName, string documentTitle)
        {
            return new AiWorkspaceScope
            {
                RevitVersion = revitVersion,
                UserName = userName,
                ProjectPath = documentTitle ?? "",
                IsUnsaved = true
            };
        }

        /// <summary>文档另存为之后,把"未保存"工作区迁移到正式路径(返回新作用域)。</summary>
        public AiWorkspaceScope Migrate(string newPath)
        {
            return new AiWorkspaceScope
            {
                RevitVersion = RevitVersion,
                UserName = UserName,
                ProjectPath = newPath ?? "",
                IsUnsaved = false
            };
        }

        /// <summary>用于 DPAPI 的附加熵(不同工作区的密钥互相解不开)。</summary>
        public byte[] Entropy
        {
            get { return System.Text.Encoding.UTF8.GetBytes("HVACIDA-AI-" + Id); }
        }

        private static string Safe(string text)
        {
            if (string.IsNullOrEmpty(text)) return "unknown";
            var sb = new System.Text.StringBuilder(text.Length);
            foreach (char c in text)
            {
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            }
            return sb.ToString();
        }

        private static string ShortHash(string text)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text ?? ""));
                var sb = new System.Text.StringBuilder(16);
                for (int i = 0; i < 8; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
