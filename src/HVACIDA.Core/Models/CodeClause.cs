using System;
using System.Collections.Generic;

namespace HVACIDA.Core.Models
{
    /// <summary>规范所属专业(检索筛选用)。</summary>
    public enum CodeDiscipline
    {
        /// <summary>通风空调 / 供暖。</summary>
        Hvac = 0,

        /// <summary>给水排水。</summary>
        Plumbing = 1,

        /// <summary>建筑防火 / 防烟排烟。</summary>
        Fire = 2,

        /// <summary>轨道交通(地铁)专项。</summary>
        Metro = 3,

        /// <summary>制图与施工验收。</summary>
        Drawing = 4
    }

    /// <summary>
    /// **一条规范条文检索条目**(需求 2.7:通风空调 + 给排水相关规范条文检索)。
    /// <para>
    /// ⚠ **不编条文号与数值**:每条给的是「标准编号与名称 + 章节线索 + 要点概述 + 关键词」,
    /// 用于**快速定位该查哪本标准、哪一章**;<see cref="ClauseHint"/> 只到章节级别并注明以标准目录为准,
    /// <see cref="BoundaryNote"/> 写明"条文号与数值以标准原文为准"。需要引用时请查标准原文。
    /// </para>
    /// </summary>
    public class StandardClauseEntry
    {
        /// <summary>稳定标识。</summary>
        public string Id { get; set; } = "";

        /// <summary>标准编号(如 GB 50736-2012)。</summary>
        public string StandardCode { get; set; } = "";

        /// <summary>标准名称(如 民用建筑供暖通风与空气调节设计规范)。</summary>
        public string StandardName { get; set; } = "";

        /// <summary>专业。</summary>
        public CodeDiscipline Discipline { get; set; } = CodeDiscipline.Hvac;

        /// <summary>专业中文名。</summary>
        public string DisciplineName => Services.StandardClauseLibrary.DisciplineName(Discipline);

        /// <summary>条文主题(检索结果标题)。</summary>
        public string Title { get; set; } = "";

        /// <summary>章节线索(章节级,注明以标准目录为准;**不写具体条文号**)。</summary>
        public string ClauseHint { get; set; } = "";

        /// <summary>要点概述(用自己的话说明这一条管什么;不替代原文)。</summary>
        public string Summary { get; set; } = "";

        /// <summary>相关关键词。</summary>
        public List<string> Keywords { get; set; } = new List<string>();

        /// <summary>典型问法(界面快捷提问用)。</summary>
        public string Question { get; set; } = "";

        /// <summary>边界说明(条文号与数值以标准原文为准)。</summary>
        public string BoundaryNote { get; set; } = "";

        /// <summary>出处文字(标准编号 + 名称 + 章节线索)。</summary>
        public string SourceText => StandardCode + " " + StandardName +
                                    (string.IsNullOrEmpty(ClauseHint) ? "" : "(" + ClauseHint + ")");
    }

    /// <summary>一份被收录的标准(编号 + 名称 + 专业 + 收录条文条目数)。</summary>
    public class StandardInfo
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public CodeDiscipline Discipline { get; set; } = CodeDiscipline.Hvac;
        public string DisciplineName => Services.StandardClauseLibrary.DisciplineName(Discipline);
        public int EntryCount { get; set; }
        public string Display => Code + " " + Name;
    }

    /// <summary>Revit 操作指南的一节(需求 2.7 / 操作指南 = **Revit 软件操作**指南,不只本插件)。</summary>
    public class RevitGuideSection
    {
        /// <summary>稳定标识。</summary>
        public string Id { get; set; } = "";

        /// <summary>分组(项目与视图 / 建模 / MEP / 出图 / 协同 …)。</summary>
        public string Group { get; set; } = "";

        /// <summary>标题(检索结果列表显示)。</summary>
        public string Title { get; set; } = "";

        /// <summary>一句话说明这一节解决什么问题。</summary>
        public string Summary { get; set; } = "";

        /// <summary>操作步骤(逐条;按功能名描述,不写死某版本的菜单路径)。</summary>
        public List<string> Steps { get; set; } = new List<string>();

        /// <summary>关键词。</summary>
        public List<string> Keywords { get; set; } = new List<string>();

        /// <summary>注意 / 与暖通专业相关的一点提醒。</summary>
        public string Note { get; set; } = "";
    }
}
