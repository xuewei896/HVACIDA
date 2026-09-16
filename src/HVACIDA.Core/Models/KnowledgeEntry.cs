using System;
using System.Collections.Generic;

namespace HVACIDA.Core.Models
{
    /// <summary>知识库条目分类。</summary>
    public enum KnowledgeCategory
    {
        /// <summary>已定口径(算怎么算、取什么值)。</summary>
        Caliber = 0,

        /// <summary>规范 / 标准依据。</summary>
        Standard = 1,

        /// <summary>操作步骤(插件怎么用)。</summary>
        Operation = 2,

        /// <summary>数据与存储(文件、字段、迁移)。</summary>
        Data = 3,

        /// <summary>待补 / 局限(明确"还没做、别当成做了")。</summary>
        Pending = 4
    }

    /// <summary>
    /// **知识库条目**(需求 2.7):一条"问题 → 答复 + 出处"的可检索记录。
    /// <para>
    /// 每条**必须带出处**(<see cref="Source"/>)—— 出处要么是项目里的权威文档(需求文档 / 公式文档 / 示例 xls),
    /// 要么是公开标准(GB 50736 等),要么是"本项目已定口径(日期)"。**没有出处的话不许写条目**,
    /// 这是"不猜、不编"在问答模块里的落地方式。
    /// </para>
    /// </summary>
    public class KnowledgeEntry
    {
        /// <summary>稳定标识(自检与交叉引用用)。</summary>
        public string Id { get; set; } = "";

        /// <summary>分类。</summary>
        public KnowledgeCategory Category { get; set; } = KnowledgeCategory.Caliber;

        /// <summary>分类中文名。</summary>
        public string CategoryName => Services.KnowledgeBase.CategoryName(Category);

        /// <summary>条目标题(检索结果列表显示)。</summary>
        public string Title { get; set; } = "";

        /// <summary>典型问法(界面上的"示例问题"按钮直接用它)。</summary>
        public string Question { get; set; } = "";

        /// <summary>答复正文(可以分段,用 \n 换行)。</summary>
        public string Answer { get; set; } = "";

        /// <summary>出处(权威来源;必填)。</summary>
        public string Source { get; set; } = "";

        /// <summary>关键词(检索加权用)。</summary>
        public List<string> Keywords { get; set; } = new List<string>();
    }

    /// <summary>一次检索命中的条目与得分。</summary>
    public class KnowledgeMatch
    {
        public KnowledgeEntry Entry { get; set; }
        public int Score { get; set; }

        /// <summary>命中说明(为什么这条被选中:标题 / 关键词 / 正文)。</summary>
        public string HitText { get; set; } = "";
    }

    /// <summary>
    /// **一次问答的结果**:命中的条目(按得分排序)+ 拼好的答复文本 + **答不出时的范围说明**。
    /// <para>
    /// 答不出**不编**:<see cref="ScopeNote"/> 会说明"知识库覆盖哪些范围",并给出最接近的几条供参考。
    /// </para>
    /// </summary>
    public class KnowledgeAnswer
    {
        /// <summary>原问题。</summary>
        public string Query { get; set; } = "";

        /// <summary>是否命中(至少一条关键词 / 标题命中)。</summary>
        public bool HasAnswer { get; set; }

        /// <summary>命中条目(按得分从高到低,最多若干条)。</summary>
        public List<KnowledgeMatch> Matches { get; set; } = new List<KnowledgeMatch>();

        /// <summary>答复正文(命中时:首条正文 + 出处;多条时附"相关条目"列表)。</summary>
        public string AnswerText { get; set; } = "";

        /// <summary>知识范围说明(答不出时给出,说明覆盖范围与如何提问)。</summary>
        public string ScopeNote { get; set; } = "";

        /// <summary>最相关条目(界面可用来直接跳转)。</summary>
        public KnowledgeEntry Top => Matches.Count > 0 ? Matches[0].Entry : null;
    }
}
