using System;
using System.Collections.Generic;
using System.Text;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>组装好的提示词(系统提示 + 用户消息 + 依据清单)。</summary>
    public class AiPrompt
    {
        /// <summary>要发给模型的完整消息列表(界面可直接显示"发了什么")。</summary>
        public List<AiChatMessage> Messages { get; set; } = new List<AiChatMessage>();

        /// <summary>本次送入模型的依据(来自本地知识库检索)。</summary>
        public List<AiCitation> Citations { get; set; } = new List<AiCitation>();

        /// <summary>本次检索是否有"够格"的依据(得分达到 <c>KnowledgeBase.MinimumScore</c>)。</summary>
        public bool HasStrongCitation { get; set; }

        /// <summary>本地检索的范围说明(没检索到时用得上)。</summary>
        public string ScopeNote { get; set; } = "";

        /// <summary>组装说明(截断了几条、有没有依据)。</summary>
        public string Note { get; set; } = "";
    }

    /// <summary>
    /// **AI 问答(RAG)组装 + 调用**(需求 2.7 的"接在线大模型")。
    /// <para>
    /// 做法是**检索增强**,不是让模型凭记忆答工程问题:
    /// ① 先用本地知识库(<see cref="KnowledgeBase"/>)检索出**依据**(本项目口径 / 规范条文原文 / Revit 操作指南);
    /// ② 把依据编号 [1][2]…连同问题一起发给 DeepSeek;
    /// ③ 系统提示里写死纪律:**只能依据给定资料回答、不得编造条文号与数值、资料不足要明说并给建议、结尾列依据**;
    /// ④ 回答连同**依据清单**一起回给界面 —— 用户核对的是依据,不是模型的自述。
    /// </para>
    /// <para>
    /// 隐私:只发送**问题 + 依据文本**;不发送 Revit 模型数据与工程输入(见
    /// <see cref="DeepSeekClient.PrivacyNote"/>)。
    /// </para>
    /// </summary>
    public static class AiAnswerService
    {
        /// <summary>
        /// 系统提示:把"不猜、不编、给依据"这条项目纪律翻译成模型能执行的约束。
        /// 自检会断言这段话里必须含关键约束词,防止后来被改软。
        /// </summary>
        public const string SystemPrompt =
            "你是地铁通风空调与给排水专业的规范检索助手,服务于一个 Revit 插件(HVACIDA)。严格执行以下规则:\n" +
            "1. 只能依据我提供的【可用依据】回答;不要使用你自己的记忆补充规范条文号、数值、系数、表格号。\n" +
            "2. 依据里没有的内容,直接说「提供的依据里没有」,并指出该去哪里查(哪本标准、插件哪个窗口)或该补什么资料;严禁猜测或编造。\n" +
            "3. 引用条文时,必须逐字引用依据里给出的原文,并在句末标注依据编号,例如 [1]。\n" +
            "4. 涉及数值计算时,写清公式与每个系数的出处(来自哪条依据);依据里没有的系数一律标注「需项目确认」,不要给一个看起来合理的数。\n" +
            "5. 回答末尾必须单列一行「依据:」,列出你用到的依据编号与出处。\n" +
            "6. 用简体中文、条理化、直接回答;不要寒暄,不要复述问题,不要输出与问题无关的内容。\n" +
            "7. 你的回答是**草稿**,用户会对着依据与标准原文核对;不要声称已经" +
            "「按规范计算完成」或「已满足规范要求」。";

        /// <summary>单条依据正文的截断长度(字)。</summary>
        public const int CitationMaxChars = 1200;

        /// <summary>按检索结果组装提示词(纯函数,不发网络;自检直接核对)。</summary>
        public static AiPrompt BuildPrompt(string query, int entryLimit, int maxCharsTotal)
        {
            var prompt = new AiPrompt();
            string text = (query ?? "").Trim();

            if (entryLimit <= 0) entryLimit = 5;
            if (maxCharsTotal <= 0) maxCharsTotal = 6000;

            var answer = KnowledgeBase.Answer(text);
            prompt.ScopeNote = answer.ScopeNote;

            // 取**有得分**的条目作依据(强命中优先,弱相关也带上但会在提示里标明"弱相关")
            var matches = KnowledgeBase.Search(text);
            int used = 0;
            int chars = 0;
            var body = new StringBuilder();
            foreach (var match in matches)
            {
                if (used >= entryLimit) break;
                var entry = match.Entry;
                if (entry == null || string.IsNullOrEmpty(entry.Source)) continue;   // 依据必须有出处

                string entryText = entry.Answer ?? "";
                bool truncated = false;
                if (entryText.Length > CitationMaxChars)
                {
                    entryText = entryText.Substring(0, CitationMaxChars);
                    truncated = true;
                }
                if (chars + entryText.Length > maxCharsTotal)
                {
                    int remain = maxCharsTotal - chars;
                    if (remain < 200) break;                       // 剩得太少就不硬塞半条
                    entryText = entryText.Substring(0, remain);
                    truncated = true;
                }

                used++;
                chars += entryText.Length;
                var citation = new AiCitation
                {
                    Index = used,
                    Title = entry.Title,
                    Category = entry.CategoryName,
                    Source = entry.Source,
                    Text = entryText,
                    Truncated = truncated,
                    Score = match.Score
                };
                prompt.Citations.Add(citation);

                body.Append('[').Append(citation.Index).Append("] ").Append(citation.Category)
                    .Append(" · ").Append(citation.Title).AppendLine();
                body.Append("出处:").AppendLine(citation.Source);
                body.AppendLine(citation.Text);
                if (truncated) body.AppendLine("(本条依据过长,已按字数上限截断;需要全文请在插件里按分类查看完整条目)");
                body.AppendLine();
            }

            prompt.HasStrongCitation = prompt.Citations.Count > 0 && prompt.Citations[0].Score >= KnowledgeBase.MinimumScore;

            var user = new StringBuilder();
            user.AppendLine("【问题】");
            user.AppendLine(text);
            user.AppendLine();
            if (prompt.Citations.Count == 0)
            {
                user.AppendLine("【可用依据】");
                user.AppendLine("(本次没有检索到任何依据 —— 请直接说明资料不足,并告诉用户该去查哪本标准或插件哪个窗口;不要凭记忆作答。)");
            }
            else
            {
                user.AppendLine("【可用依据】(以下内容来自本插件的本地知识库,出处已标明)");
                user.Append(body.ToString());
                if (!prompt.HasStrongCitation)
                {
                    user.AppendLine("(注意:以上依据与问题的相关度不高,若不足请明说,不要勉强作答。)");
                }
            }
            user.AppendLine();
            user.AppendLine("【要求】按系统规则作答,结尾列出「依据:」并用编号引用。");

            prompt.Messages.Add(AiChatMessage.System(SystemPrompt));
            prompt.Messages.Add(AiChatMessage.User(user.ToString()));

            prompt.Note = prompt.Citations.Count == 0
                ? "本地知识库没有检索到依据 —— 模型只能给「该查哪里」的建议,不会给结论。"
                : "已送入 " + prompt.Citations.Count + " 条依据" +
                  (prompt.HasStrongCitation ? "(相关度达标)。" : "(相关度偏低,回答仅供参考)。") +
                  (prompt.Citations.Count > 0 && prompt.Citations[prompt.Citations.Count - 1].Truncated ? "部分依据超长已截断。" : "");
            return prompt;
        }

        /// <summary>问一次 DeepSeek(先本地检索依据,再连同问题发出去)。失败不抛异常。</summary>
        public static AiChatResult Ask(AiChatSettings settings, string query)
        {
            settings = settings ?? new AiChatSettings();
            string text = (query ?? "").Trim();
            if (text.Length == 0)
            {
                return AiChatResult.Fail(query, AiErrorKind.BadRequest, 0, "问题为空", "请先输入问题。");
            }

            // 未启用/没 Key 时不发请求:照样把本地检索结果给出来(依据清单对用户仍有价值)
            if (!settings.Enabled || !settings.IsConfigured)
            {
                var promptOnly = BuildPrompt(text, settings.ContextEntryLimit, settings.ContextMaxChars);
                var blocked = DeepSeekClient.Ask(settings, promptOnly.Messages, text);
                blocked.Citations = promptOnly.Citations;
                return blocked;
            }

            var prompt = BuildPrompt(text, settings.ContextEntryLimit, settings.ContextMaxChars);
            var result = DeepSeekClient.Ask(settings, prompt.Messages, text);
            result.Citations = prompt.Citations;

            if (result.Success)
            {
                result.Note = result.Note + " " + prompt.Note + " " + DeepSeekClient.PrivacyNote;
            }
            else if (result.ErrorKind == AiErrorKind.Network || result.ErrorKind == AiErrorKind.ServerError)
            {
                result.Note = result.Note + " 本地检索到的依据仍在下方清单里。";
            }
            return result;
        }
    }
}
