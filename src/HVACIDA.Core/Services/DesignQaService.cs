using System;
using System.Collections.Generic;
using System.Linq;

namespace HVACIDA.Core.Services
{
    /// <summary>一条知识库问答(关键词命中 → 答复)。</summary>
    public sealed class QaEntry
    {
        public QaEntry(string question, string answer, params string[] keywords)
        {
            Question = question;
            Answer = answer;
            Keywords = keywords ?? new string[0];
        }

        /// <summary>示例问法(界面里作为可点的常用问题)。</summary>
        public string Question { get; }

        /// <summary>答复正文。</summary>
        public string Answer { get; }

        /// <summary>命中关键词(任一命中即算匹配)。</summary>
        public IList<string> Keywords { get; }
    }

    /// <summary>
    /// 规范知识库(原型为本地规则应答;正式版接 AI 服务,需求 2.7 / 4.1)。
    /// 只覆盖本项目已定口径,答不出时明确给出知识范围,不编造。
    /// </summary>
    public class DesignQaService
    {
        private static readonly QaEntry[] Entries =
        {
            new QaEntry(
                "地铁站厅夏季送风温差一般取多少?",
                "地铁公共区常用送风温差 8~10 ℃(站厅取 10 ℃)。注意:站厅与站台送风温度必须一致 —— " +
                "本项目由站厅送风温度统一确定,站台不单独计算送风温度。",
                "送风温差", "温差", "送风温度"),

            new QaEntry(
                "排烟风量怎么算?风机怎么选?",
                "排烟计算:计算风量 = 公共区(防烟分区)面积 × 60 m³/(h·m²)。" +
                "单台排烟风机风量取站厅、站台最大值的一半,共 2 台;" +
                "选型风量 = 计算风量 × 1.2(等效于按防烟分区面积 × 72)。",
                "排烟", "风机选型", "选型"),

            new QaEntry(
                "新风量怎么确定?",
                "空调季新风指标按 20 m³/(h·人) 取值,并满足不小于总送风量的 10%;实际新风量取两者较大值。",
                "新风", "新风量", "新风比"),

            new QaEntry(
                "焓湿计算的口径是什么?",
                "露点相对湿度取 95%;饱和含湿量按公式文档的 7 次多项式;焓值 h = 1.01t + (2500 + 1.84t)·d/1000 + 0.4。" +
                "送风量按焓差反算,焓差小于 1.0 kJ/kg 时按退化保护处理。",
                "焓湿", "焓值", "露点", "含湿量", "饱和"),

            new QaEntry(
                "高峰客流怎么算?",
                "高峰客流 = (上客 + 下客) × 集群系数(0.89)× 超高峰小时系数;停站时间默认上车 2 min / 下车 1.5 min。" +
                "客流必须由用户输入(A27~F27),不能由模型推断。",
                "客流", "集群", "停站", "超高峰"),

            new QaEntry(
                "屏蔽门负荷怎么考虑?",
                "屏蔽门:传热量 = K·h·L·ΔT·安全系数(默认 3.2 W/(m²·℃) / 3 m / 292 m / 8 ℃ / 1.5);" +
                "漏风按站厅 30 kW、站台 45 kW 计,系统发热 4 kW。",
                "屏蔽门", "漏风", "传热"),

            new QaEntry(
                "大系统和小系统的分工是什么?",
                "大系统负责站厅/站台公共区空调(客流负荷、风量、制冷量与组合式空调机组/回排风机/排烟风机选型);" +
                "小系统负责设备管理用房等房间(全空气一次回风、多联机+新风、排风、排烟、加压送风等)。",
                "大系统", "小系统", "分工", "区别"),

            new QaEntry(
                "单台设备的选型风量怎么取?",
                "组合式空调机组单台送风量 = 总送风量的一半,单台制冷量 = 总制冷量的一半;" +
                "回排风机单台回风量 = 总回风量的一半;排烟风机单台风量取站厅/站台最大值的一半," +
                "另有 1.2 的选型系数。",
                "单台", "设备选型", "机组", "回排风机")
        };

        /// <summary>常用问题(界面按钮/提示用)。</summary>
        public IList<string> SampleQuestions => Entries.Select(e => e.Question).ToList();

        /// <summary>
        /// 规则应答:按关键词命中数取最佳匹配(同分取靠前条目)。
        /// 未命中时返回知识范围说明,不编造答案。
        /// </summary>
        public string Answer(string question)
        {
            if (string.IsNullOrWhiteSpace(question)) return NotCovered(question);

            QaEntry best = null;
            int bestScore = 0;
            foreach (var entry in Entries)
            {
                int score = entry.Keywords.Count(k => question.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = entry;
                }
            }

            return best != null ? best.Answer : NotCovered(question);
        }

        /// <summary>命中的问答条目(未命中返回 null),供界面展示"关联问题"。</summary>
        public QaEntry Match(string question)
        {
            if (string.IsNullOrWhiteSpace(question)) return null;
            QaEntry best = null;
            int bestScore = 0;
            foreach (var entry in Entries)
            {
                int score = entry.Keywords.Count(k => question.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = entry;
                }
            }
            return best;
        }

        /// <summary>知识范围说明(给未命中问题的答复)。</summary>
        public string NotCovered(string question)
        {
            return "这个问题暂不在本地知识库范围内(当前仅覆盖本项目已定口径)。" + Environment.NewLine +
                   "可以试试:" + Environment.NewLine +
                   string.Join(Environment.NewLine, SampleQuestions.Select(q => "· " + q)) + Environment.NewLine +
                   "正式版将接入 AI 服务与规范全文检索(需求 2.7 / 4.1),届时可回答更广的问题。";
        }
    }
}
