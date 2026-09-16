using System;
using System.Collections.Generic;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// **知识库条目**的 Excel(.xlsx)导出(需求 2.7):把条目清单(含出处)导出,便于评审与交底。
    /// </summary>
    public static class KnowledgeBaseExcelExporter
    {
        /// <summary>知识库工作簿(2 页:条目清单 / 说明)。</summary>
        public static XlsxWorkbook Build(IList<KnowledgeEntry> entries)
        {
            var workbook = new XlsxWorkbook();
            var list = entries ?? KnowledgeBase.All;

            var sheet = workbook.AddSheet("条目清单");
            var rows = new List<object[]>();
            foreach (var entry in list)
            {
                rows.Add(new object[]
                {
                    entry.Id, entry.CategoryName, entry.Title, entry.Question,
                    entry.Answer == null ? "" : entry.Answer.Replace("\n", " "),
                    entry.Source,
                    string.Join("、", entry.Keywords.ToArray())
                });
            }
            ExcelReportBuilder.AddTable(sheet,
                new List<string> { "条目编号", "分类", "标题", "典型问法", "答复", "出处", "关键词" }, rows);

            var note = workbook.AddSheet("说明");
            note.AddRow("知识库", "HVACIDA 规范 / 口径知识库(需求 2.7)");
            note.AddRow("条目数", list.Count);
            note.AddRow("口径",
                "每条都带出处:项目权威文档(需求文档 / 公式文档 / 示例 xls)、公开标准(GB 50736 等)或本项目已定口径(含日期)。" +
                "检索为关键词加权(标题 3 / 关键词 2 / 正文 1),答不出时明确说明知识范围,不编答案。");
            note.AddRow("与在线 AI 的关系",
                "接口稳定(Search / Answer);将来接大模型时,本知识库作为**检索到的依据上下文**一起提交,答复仍须挂这些出处。");
            return workbook;
        }
    }
}
