using System;
using System.Collections.Generic;
using System.Text;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// **规范 / 口径知识库**(需求 2.7):条目化 + 可检索 + 每条带出处。
    /// <para>
    /// 为什么是"知识库"而不是"在线大模型":本项目所有结论都必须**可追溯到出处**(需求文档 / 公式文档 /
    /// GB 50736 等标准 / 已定口径),问答也不能例外。所以这里把已定口径整理成**条目**:
    /// 每条 = 典型问法 + 答复 + **出处** + 关键词;检索按关键词加权打分,给出命中条目;
    /// **答不出时明确说"知识范围之外"并列出覆盖范围**,不编一个像真的答案。
    /// </para>
    /// <para>
    /// 与在线 AI 的关系:接口稳定(<see cref="Search"/> / <see cref="Answer"/>),将来接大模型时,
    /// 本知识库可以作为**检索到的上下文(依据)**一起喂给模型 —— 模型答什么都得挂这些出处。
    /// </para>
    /// </summary>
    public static class KnowledgeBase
    {
        /// <summary>命中条目最多返回几条(避免一次抛一大堆)。</summary>
        public const int MaxMatches = 5;

        /// <summary>
        /// 算「命中」的最低得分:标题命中(3)或关键词命中(2+2)才算答得上。
        /// 只有正文里巧合出现一个词(1 分)**不算命中** —— 宁可说"知识范围外",也不给一个看起来像答案的弱相关条目。
        /// </summary>
        public const int MinimumScore = 3;

        private static readonly List<KnowledgeEntry> Entries = BuildEntries();

        private static readonly List<KnowledgeEntry> AllEntries = BuildAll();

        /// <summary>
        /// 知识库 = **本项目已定口径** + **规范条文检索**(<see cref="StandardClauseLibrary"/>)+
        /// **Revit 操作指南**(<see cref="RevitOperationGuide"/>)—— 三者在同一个窗里可检索、可按分类筛选。
        /// </summary>
        private static List<KnowledgeEntry> BuildAll()
        {
            var all = new List<KnowledgeEntry>(BuildEntries());

            // 规范条文:把「标准 + 章节线索 + 要点概述 + 边界」转成条目(分类「规范条文」)
            foreach (var clause in StandardClauseLibrary.All)
            {
                var entry = Entry(clause.Id, KnowledgeCategory.Clause, clause.Title, clause.Question,
                    clause.Summary + "\n\n章节线索:" + clause.ClauseHint + "\n" + clause.BoundaryNote,
                    clause.SourceText, clause.Keywords.ToArray());
                entry.Question = clause.Question;
                all.Add(entry);
            }

            // Revit 操作指南:按分组转成条目(分类「操作步骤」)
            foreach (var section in RevitOperationGuide.All)
            {
                var text = new System.Text.StringBuilder();
                text.AppendLine(section.Summary);
                text.AppendLine();
                for (int i = 0; i < section.Steps.Count; i++)
                {
                    text.AppendLine((i + 1) + ". " + section.Steps[i]);
                }
                if (!string.IsNullOrEmpty(section.Note))
                {
                    text.AppendLine();
                    text.AppendLine("注意:" + section.Note);
                }
                var entry = Entry("revit-" + section.Id, KnowledgeCategory.Operation,
                    "Revit 操作 · " + section.Title, section.Title + " 在 Revit 里怎么操作?",
                    text.ToString(), "Autodesk Revit 2020 官方帮助 + 本项目实践(操作类,不含设计取值)",
                    section.Keywords.ToArray());
                entry.Question = section.Title + " 在 Revit 里怎么操作?";
                all.Add(entry);
            }
            return all;
        }

        /// <summary>全部条目(按分类 + 标题排序,界面列表直接绑它)。</summary>
        public static IList<KnowledgeEntry> All => AllEntries;

        /// <summary>分类中文名。</summary>
        public static string CategoryName(KnowledgeCategory category)
        {
            switch (category)
            {
                case KnowledgeCategory.Caliber: return "已定口径";
                case KnowledgeCategory.Standard: return "规范依据";
                case KnowledgeCategory.Operation: return "操作步骤";
                case KnowledgeCategory.Data: return "数据与存储";
                case KnowledgeCategory.Pending: return "待补与局限";
                case KnowledgeCategory.Clause: return "规范条文";
                default: return "其它";
            }
        }

        /// <summary>按分类取条目。</summary>
        public static IList<KnowledgeEntry> ByCategory(KnowledgeCategory category)
        {
            var result = new List<KnowledgeEntry>();
            foreach (var entry in AllEntries)
            {
                if (entry.Category == category) result.Add(entry);
            }
            return result;
        }

        /// <summary>按 Id 取条目(找不到返回 null)。</summary>
        public static KnowledgeEntry Find(string id)
        {
            foreach (var entry in Entries)
            {
                if (string.Equals(entry.Id, id, StringComparison.Ordinal)) return entry;
            }
            return null;
        }

        /// <summary>
        /// 检索:标题命中 3 分、关键词命中 2 分、正文 / 出处命中 1 分(关键词按包含匹配;
        /// 中文按 2 字以上片段切分,避免"的/是"这类噪声)。
        /// </summary>
        public static IList<KnowledgeMatch> Search(string query)
        {
            var matches = new List<KnowledgeMatch>();
            if (string.IsNullOrEmpty(query)) return matches;

            string text = query.Trim();
            var tokens = Tokenize(text);

            foreach (var entry in AllEntries)
            {
                // 打分口径(按"种类"计分,不按 token 个数累积 —— 否则关键词多的条目会凭数量压过真正对口的条目)
                int score = 0;
                var hits = new List<string>();

                bool titleHit = Contains(entry.Title, text);
                bool keywordHit = false;
                bool bodyHit = false;
                foreach (var token in tokens)
                {
                    if (string.IsNullOrEmpty(token)) continue;
                    if (!titleHit && Contains(entry.Title, token)) titleHit = true;
                    if (!keywordHit)
                    {
                        foreach (var keyword in entry.Keywords)
                        {
                            if (Contains(keyword, token) || Contains(token, keyword)) { keywordHit = true; break; }
                        }
                    }
                    if (!bodyHit && (Contains(entry.Answer, token) || Contains(entry.Question, token))) bodyHit = true;
                }
                if (titleHit) { score += 3; hits.Add("标题"); }
                if (keywordHit) { score += 2; hits.Add("关键词"); }
                if (bodyHit) { score += 1; hits.Add("正文"); }

                if (score <= 0) continue;
                matches.Add(new KnowledgeMatch
                {
                    Entry = entry,
                    Score = score,
                    HitText = string.Join("+", hits.ToArray())
                });
            }

            matches.Sort(delegate (KnowledgeMatch a, KnowledgeMatch b)
            {
                int byScore = b.Score.CompareTo(a.Score);
                if (byScore != 0) return byScore;
                // 同分时:**本项目已定口径 → 规范条文 → 规范依据 → 操作步骤 → 数据 → 待补**(工程口径优先于软件操作)
                int byCategory = Rank(a.Entry.Category).CompareTo(Rank(b.Entry.Category));
                if (byCategory != 0) return byCategory;
                return string.Compare(a.Entry.Title, b.Entry.Title, StringComparison.Ordinal);
            });
            if (matches.Count > MaxMatches) matches.RemoveRange(MaxMatches, matches.Count - MaxMatches);
            return matches;
        }

        /// <summary>问答:命中给答复 + 出处;答不出给"知识范围说明"并附最接近的条目。</summary>
        public static KnowledgeAnswer Answer(string query)
        {
            var answer = new KnowledgeAnswer { Query = query ?? "" };
            var matches = Search(query);
            answer.Matches.AddRange(matches);

            bool strong = matches.Count > 0 && matches[0].Score >= MinimumScore;
            if (!strong)
            {
                answer.HasAnswer = false;
                answer.AnswerText = "";
                var scope = new StringBuilder();
                scope.Append("这个问题不在当前知识库范围内 —— 本知识库只覆盖**本项目已定口径与已实装模块**:");
                scope.Append("大系统负荷 / 排烟、小系统六类、水力计算(风与水)、气象参数与省市气象库、材料表统计、图纸与批量出图、");
                scope.Append("计算书与 Excel 导出、数据存储与口径纪律。");
                scope.Append("换几个关键词试试(例如「排烟 选型」「水力 扬程」「气象 湿球」「材料表 单位」),或在上方按分类浏览条目。");
                scope.Append("**本知识库不会为范围外的问题编答案**;需要规范原文时请查 GB 50736 等标准或项目设计文件。");
                if (matches.Count > 0)
                {
                    scope.Append("\n\n以下是**弱相关**条目(仅供参考,不作为答复):");
                    foreach (var match in matches)
                    {
                        if (match.Score >= MinimumScore) continue;
                        scope.Append("\n  · " + match.Entry.Title + "(" + match.Entry.CategoryName + ",得分 " + match.Score + ")");
                    }
                }
                answer.ScopeNote = scope.ToString();
                return answer;
            }

            // 只有达到阈值的才算"真命中";弱相关的列在相关条目里
            var strongMatches = new List<KnowledgeMatch>();
            foreach (var match in matches)
            {
                if (match.Score >= MinimumScore) strongMatches.Add(match);
            }
            answer.Matches.Clear();
            answer.Matches.AddRange(strongMatches);

            answer.HasAnswer = true;
            var top = strongMatches[0].Entry;
            var sb = new StringBuilder();
            sb.AppendLine("【" + top.CategoryName + "】" + top.Title);
            sb.AppendLine();
            sb.AppendLine(top.Answer);
            sb.AppendLine();
            sb.AppendLine("出处:" + top.Source);
            if (strongMatches.Count > 1)
            {
                sb.AppendLine();
                sb.AppendLine("相关条目:");
                for (int i = 1; i < strongMatches.Count; i++)
                {
                    sb.AppendLine("  · " + strongMatches[i].Entry.Title + "(" + strongMatches[i].Entry.CategoryName + ")");
                }
            }
            answer.AnswerText = sb.ToString();
            return answer;
        }

        /// <summary>示例问题(界面上的快捷提问按钮)。</summary>
        public static IList<string> SampleQuestions()
        {
            var questions = new List<string>();
            foreach (var entry in Entries)
            {
                if (!string.IsNullOrEmpty(entry.Question)) questions.Add(entry.Question);
            }
            return questions;
        }

        // ================================================================== 内部

        /// <summary>供其它检索入口(如规范条文库)复用的分词方法。</summary>
        public static List<string> TokenizeForSearch(string text)
        {
            return Tokenize(text);
        }
        /// <summary>同分时的类别优先序:工程口径优先于软件操作(避免"操作指南"压过"算法口径")。</summary>
        private static int Rank(KnowledgeCategory category)
        {
            switch (category)
            {
                case KnowledgeCategory.Caliber: return 0;
                case KnowledgeCategory.Clause: return 1;
                case KnowledgeCategory.Standard: return 2;
                case KnowledgeCategory.Operation: return 3;
                case KnowledgeCategory.Data: return 4;
                default: return 5;
            }
        }
        /// <summary>把查询切成检索片段:先按标点/空白切,再对较长的中文片段取 2~4 字滑窗。</summary>
        private static List<string> Tokenize(string text)
        {
            var tokens = new List<string>();
            var separators = new[] { ' ', ',', ',', '。', '?', '?', '!', '!', ';', ';', ':', '、', '(', ')', '(', ')', '的', '了', '吗', '呢', '请', '问' };
            foreach (var part in text.Split(separators, StringSplitOptions.RemoveEmptyEntries))
            {
                string piece = part.Trim();
                if (piece.Length == 0) continue;
                if (piece.Length <= 4) { tokens.Add(piece); continue; }

                // 长片段:滑动取 3 字片段(中文按字,英文数字按整段)
                for (int i = 0; i + 3 <= piece.Length; i++) tokens.Add(piece.Substring(i, 3));
            }
            return tokens;
        }

        private static bool Contains(string text, string token)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(token)) return false;
            return text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static KnowledgeEntry Entry(string id, KnowledgeCategory category, string title, string question,
            string answer, string source, params string[] keywords)
        {
            var entry = new KnowledgeEntry
            {
                Id = id,
                Category = category,
                Title = title,
                Question = question,
                Answer = answer,
                Source = source
            };
            entry.Keywords.AddRange(keywords);
            return entry;
        }

        private static List<KnowledgeEntry> BuildEntries()
        {
            var list = new List<KnowledgeEntry>
            {
                Entry("large-load", KnowledgeCategory.Caliber, "大系统负荷计算的口径",
                    "大系统冷负荷怎么算?",
                    "按《大系统负荷计算公式》逐格实现:客流量(含集群系数)→ 站厅/站台冷负荷 → 湿负荷 → 焓湿过程 →\n" +
                    "风量与制冷量 → 设备选型。**公式与系数一律以公式文档为准**,已与《大系统负荷计算公式-示例.xls》\n" +
                    "逐格核对(30 项断言);气象参数 C5(室外湿球)/F4(站厅干球)/F6(站台干球)默认从「项目信息 → 气象参数」联动。",
                    "《大系统负荷计算公式.docx》+ 示例 xls(逐格核对,见 HVACIDA.Smoke 场景3)",
                    "大系统", "冷负荷", "客流", "焓湿", "选型"),

                Entry("large-smoke", KnowledgeCategory.Caliber, "大系统排烟量与风机选型",
                    "排烟风机怎么选?",
                    "计算排烟量 = 公共区面积 × 60 m³/(h·m²);**选型排烟量 = 计算排烟量 × 1.2**;\n" +
                    "风机 2 台、取站厅/站台计算的较大者,单台 = 基准区选型量 ÷ 2。\n" +
                    "口径存疑处:防烟分区几何(分区面积、挡烟垂壁、储烟仓)尚未接入模型,现按公共区整体作为一个分区、量偏大,\n" +
                    "接入后应逐分区取量、按最大分区选型。",
                    "需求 2.2.3.1 + 公式文档示例(2026-09-04 定口径:×60 / ×1.2 / 2 台)",
                    "排烟", "风机", "1.2", "60", "选型", "防烟分区"),

                Entry("small-allair", KnowledgeCategory.Caliber, "小系统空调负荷(全空气一次回风)",
                    "小系统全空气系统怎么算?",
                    "逐房间算:照明/人员/结构湿负荷 → 露点与送风点焓 → 室内焓 → 消除余热通风量与换气通风量**取大** →\n" +
                    "新回风混合焓 → 空调器冷量。**一律按公式文档的文字公式计算**(评审 2026-09-15 明确:示例仅用于理解公式) ——\n" +
                    "例如空调器冷量用**实际通风量**(不用示例里个别房间对应的消除余热通风量,与示例合计差约 3% 属预期)。",
                    "《小系统空调负荷、送排风、排烟计算公式.docx》(口径确认 2026-09-15)",
                    "小系统", "全空气", "空调器冷量", "通风量"),

                Entry("small-others", KnowledgeCategory.Caliber, "小系统其余五类的算法",
                    "排风/排烟/送排风/加压送风怎么算?",
                    "· 排风:面积 × 层高 × 换气次数(换气次数按房间类型取默认:卫生间 20、淋浴 10、环控机房 6、其它 4);\n" +
                    "· 排烟:面积 × 60,补风 = 排烟 × 0.6;\n" +
                    "· 送风排风排烟:排风(换气)→ 送风 = 排风 × 0.9 → 排烟 = 面积 × 60 → 补风 = 排烟 × 0.6,补风机取送风与补风的大者;\n" +
                    "· 多联机+新风:过渡季温差算消除余热通风量 + 新风冷负荷;\n" +
                    "· 加压送风:门开启风量 + 门缝漏风 + 余压阀漏风,选型 × 1.2。",
                    "《小系统空调负荷、送排风、排烟计算公式.docx》",
                    "小系统", "排风", "排烟", "加压送风", "多联机", "换气次数"),

                Entry("hydraulic-formula", KnowledgeCategory.Caliber, "水力计算的口径(风与水)",
                    "水力计算怎么算风压和扬程?",
                    "① 流速 v = Q ÷ 3600 ÷ A;② 水力直径 = 4A ÷ 湿周(矩形风管即流速当量直径);\n" +
                    "③ 雷诺数 Re = v·d/ν(物性按介质 + 温度查常用物性表插值);\n" +
                    "④ 摩擦系数 λ = 0.11×(K/d + 68/Re)^0.25(**阿尔特舒利**显式式;Re 小于 2320 按 64/Re);\n" +
                    "⑤ 比摩阻 R = λ/d × ρv²/2,沿程 = R × 段长;⑥ 局部 = Σζ × ρv²/2;\n" +
                    "⑦ **需求全压 =(最不利环路沿程 + 局部 + 环路末端/设备 + 出口动压)× 富余系数**;\n" +
                    "⑧ **水泵扬程 H =(环路总阻力 + 静压)× 富余系数 ÷(ρg)**,静压按高差计(闭式取 0)。",
                    "评审 2026-09-15「没有具体计算公式,由你实现」→ 采用流体力学/暖通通用公式,系数全部可见可改",
                    "水力", "风压", "扬程", "比摩阻", "局部阻力", "雷诺数"),

                Entry("hydraulic-balance", KnowledgeCategory.Caliber, "并联环路平衡与工况点",
                    "并联环路不平衡怎么办?工况点怎么定?",
                    "**不平衡率 =(最不利环路 − 该支路)÷ 最不利环路 × 100%**,允许值默认 15%(可改);超限支路给出平衡装置参数:\n" +
                    "· 水系统 平衡阀 **Kv = Q ÷ √(ΔP[bar])**(20 ℃ 水,Kv 定义式)与**阀权度 = 需吸收压差 ÷ 最不利环路总阻力**;\n" +
                    "· 风系统 **需增加 ζ = 需吸收压差 ÷ (ρv²/2)**(按该支路末端管段动压折算)。\n" +
                    "系统阻力特性曲线 ΔP(Q) = 静压 +(总阻力 − 静压)×(Q ÷ Q设计)²,给设计流量 50%~130%。\n" +
                    "⚠ **工况点必须与厂家风机/水泵性能曲线求交** —— 插件不内置设备曲线,不假装算了工况点。",
                    "本项目已定口径(不平衡率 15% 为工程通行口径)+ Kv 定义式",
                    "水力", "平衡", "不平衡率", "Kv", "阀权度", "工况点", "特性曲线"),

                Entry("weather", KnowledgeCategory.Standard, "气象参数的来源与回填范围",
                    "室外参数从哪来?哪些会被气象库覆盖?",
                    "省/市下拉取自**内嵌 GB 50736-2012 附录A 全国 294 个台站 / 31 个省级行政区**数据库;\n" +
                    "选定城市后**自动回填室外参数**(大/小系统室外 8 项 + 大气压力取夏季值 + 室外相对湿度取夏季通风值)。\n" +
                    "**室内设计参数(站厅/站台/用房温湿度)属设计取值,气象库不动它。**\n" +
                    "标准未记录夏季空调湿球温度的 6 个台站(咸阳/黔南州/新疆塔城等)取用时**不覆盖原值并告警**,不猜值。",
                    "GB 50736-2012 附录A + 条文说明;数据文件与生成器在 tools/weatherdb",
                    "气象", "湿球", "干球", "省市", "回填", "大气压力"),

                Entry("result-timing", KnowledgeCategory.Caliber, "结果什么时候出现(打开即算)",
                    "为什么打开结果窗就有数?计算会不会自动存盘?",
                    "**打开即算**:以「计算结果」为名的窗、以及带结果区的录入窗,打开时就自动算一次 —— 不用进来再点【计 算】。\n" +
                    "**计算即保存**:录入窗点【计 算】= **先把本次输入落盘再算**(大系统 large-system.xml、排烟 large-smoke.xml、\n" +
                    "小系统按「类型 + 编号」upsert、水力按「介质 + 系统编号」upsert),所以结果窗读到的必然是刚算的那一份;\n" +
                    "**只是打开窗 / 拾取回填 / 恢复默认不写盘**;**没有数据时不落盘**(避免在汇总里留空系统)。",
                    "评审 2026-09-15 口径 + UI设计规范 §4.9",
                    "计算", "保存", "打开即算", "结果窗"),

                Entry("excel", KnowledgeCategory.Operation, "计算书导出 Excel",
                    "怎么把计算书导成 Excel?",
                    "每个模块的录入窗 / 结果窗都有【导出 Excel】,落盘到 `%AppData%\\HVACIDA\\Reports`:\n" +
                    "· 大系统负荷:负荷汇总(7 分区 66 行)+ 口径;排烟:分区宽表 + 选型 + 口径;\n" +
                    "· 小系统:系统结果 + 房间明细 + 设备选型 + 口径;全站汇总:逐系统 + 全站合计 + 每套明细页;\n" +
                    "· 水力:汇总 / 管段明细 / 环路阻力项 / 并联平衡 / 特性曲线 / 取值与口径;全站汇总同理;\n" +
                    "· 材料表:类别小计 / 逐类型明细 / 口径;图纸:概况与图框 / 逐张图纸 / 出图记录 / 口径。\n" +
                    "**汇总页与界面、文本计算书同源**(同一份结果表渲染),Excel 里的数字不会另算。",
                    "本项目已实装(XlsxWriter 自写最小 XLSX,零外部依赖)",
                    "Excel", "计算书", "导出", "报表"),

                Entry("material-takeoff", KnowledgeCategory.Caliber, "材料表统计的计量口径",
                    "材料表怎么统计?为什么长度和个数不合并?",
                    "归并键 = **类别 + 族 + 类型 + 单位** —— 单位进归并键,所以**长度(m)与件数(个)绝不相加**;\n" +
                    "计量:风管 / 水管取长度(定位线曲线长度,退化到包围盒长边)、保温取长度(面积需按展开面另算)、\n" +
                    "管件 / 附件 / 末端 / 设备取件数;类别小计若出现混合单位,只给件数与类型数并注明;\n" +
                    "读不到长度曲线的构件**按 1 件计并在窗口报数**;不含现场损耗与接头/翻边等附加量。",
                    "需求 2.5 + 本项目已定口径(2026-09-16)",
                    "材料表", "统计", "单位", "损耗", "件数", "长度"),

                Entry("sheet-export", KnowledgeCategory.Operation, "图纸清单与批量出图",
                    "怎么批量出图?PDF 导不出来怎么办?",
                    "【出图 → 图框】先读图纸清单(编号 / 名称 / 图框族与类型 / 图幅 mm / 视图数),**空图框单独计数并在出图时跳过**。\n" +
                    "· **DWG / DXF**:走 Revit 导出接口逐张导出(文件名「图纸编号_图纸名称」)—— 最可靠;\n" +
                    "· **PDF**:走系统打印机(PrintManager),**依赖本机装了 PDF 打印机驱动**(如「Microsoft Print to PDF」);\n" +
                    "  没装就会**逐张写明失败原因**,不会谎报成功 —— 装一个 PDF 虚拟打印机即可。\n" +
                    "输出目录在窗口里可改(默认 `%AppData%\\HVACIDA\\Export`)。",
                    "需求 2.6 + 本项目已实装(2026-09-16)",
                    "出图", "图纸", "DWG", "DXF", "PDF", "打印机", "批量"),

                Entry("storage", KnowledgeCategory.Data, "数据存在哪里",
                    "插件的数据存在哪?换电脑怎么办?",
                    "全部落在 `%AppData%\\HVACIDA\\`:`project.xml`(工程信息 + 气象)、`large-system.xml`(大系统输入)、\n" +
                    "`large-smoke.xml`(排烟参数)、`small-systems.xml`(小系统多系统容器)、`hydraulic.xml`(水力系数 + 多系统)。\n" +
                    "计算书与 Excel 在 `Reports\\`,批量出图在 `Export\\`。\n" +
                    "旧版单系统文件(`small-system.xml`、水力单系统)首次读取会**自动迁移**并落盘。\n" +
                    "结果**不落盘**:打开结果窗按输入现算,所以不会出现「结果文件与输入不一致」。",
                    "本项目已实装(IDataRepository → XmlProjectRepository;SQLite 待换)",
                    "存储", "文件", "路径", "迁移", "XML", "SQLite"),

                Entry("discipline", KnowledgeCategory.Caliber, "本项目的口径纪律(不猜、不静默)",
                    "插件遇到算不出来的情况怎么办?",
                    "四条纪律:① **不编工程值** —— 系数 / 额定参数 / 物性都要有出处,读不到就写「未读到」并提示;\n" +
                    "② **不静默按 0** —— 匹配不到的管件、缺断面的管段、没有阻力参数的末端,逐条列进待补提示;\n" +
                    "③ **没算过就不摆结果** —— 空工程只给「去录入」的指引,不拿默认参数算一版假结果;\n" +
                    "④ **口径全部可见** —— 界面表格、计算书、Excel 由同一份结果模型渲染,取值随计算书一起输出。",
                    "本项目开发纪律(见 docs/开发流程.md 与 README)",
                    "口径", "纪律", "待补", "不猜", "假结果"),

                Entry("pending-list", KnowledgeCategory.Pending, "还没做的部分(别当成已做)",
                    "哪些功能还没做?",
                    "· **防烟分区几何**(分区面积 / 挡烟垂壁 / 储烟仓)未接入 —— 排烟现按公共区整体一个分区,量偏大;\n" +
                    "· **图例表与自动标注规则库**未做(图纸清单与批量出图已可用);\n" +
                    "· **水力**:局部阻力系数取的是手册常用值(需按项目样本替换)、工况点需厂家曲线、未做环路平衡的自动调节阀选型;\n" +
                    "· **材料表**:不含损耗率与接头/翻边附加量;\n" +
                    "· **存储**:仍是 XML,SQLite 未做;**计算书**:PDF 未做;\n" +
                    "· **规范知识库**:本地知识库,**未接在线大模型**(接入时本知识库作为依据上下文)。",
                    "README 第 6 节「关键 TODO」+ 各模块待补清单",
                    "待补", "未实现", "TODO", "局限", "防烟分区", "SQLite"),

                Entry("report-ref", KnowledgeCategory.Standard, "公式与需求文档在哪",
                    "公式文档和需求文档在哪里?",
                    "仓库根目录:`大系统负荷计算公式.docx` + `大系统负荷计算公式-示例.xls`(大系统 30 项逐格核对)、\n" +
                    "`小系统空调负荷、送排风、排烟计算公式.docx`(六类小系统)、`HVACIDA_需求分析文档.md`(需求 2.x 章节)。\n" +
                    "Word 文档在 `docs/` 下有**正文副本**(`.md`),并有 SHA256 一致性门禁 —— 文档改了副本必须同步,\n" +
                    "所以「代码里引用的公式」与「文档里的公式」不会悄悄分叉。",
                    "仓库文件 + tools/docx2md/check-docx-sync.ps1(同步门禁)",
                    "文档", "公式", "需求", "在哪里", "同步")
            };
            return list;
        }
    }
}
