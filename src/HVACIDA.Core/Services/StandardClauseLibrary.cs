using System;
using System.Collections.Generic;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// **规范条文检索库**(需求 2.7):覆盖**通风空调**与**给水排水**(含防烟排烟、地铁专项、制图验收)
    /// 相关标准的条文检索。
    /// <para>
    /// ⚠ **纪律(必须遵守)**:本库**不编条文号、不编数值** —— 每条只给
    /// 「标准编号与名称 + 章节线索 + 要点概述 + 关键词」,作用是**快速定位该查哪本标准、哪一章**;
    /// 章节线索只到章一级并注明以标准目录为准,条文号与数值一律**以标准原文为准**
    /// (见 <see cref="ScopeNote"/>)。需要引用或计算时请查标准原文/项目设计文件。
    /// </para>
    /// <para>
    /// 与「本项目已定口径」的关系:<see cref="KnowledgeBase"/> 里那些条目讲的是**本项目怎么算的**;
    /// 本库讲的是**规范怎么要求的**。两者在同一个知识库窗里可分别筛选(分类「规范条文」/「已定口径」)。
    /// </para>
    /// </summary>
    public static class StandardClauseLibrary
    {
        /// <summary>全局边界说明(界面与导出都必须显示)。</summary>
        public const string ScopeNote =
            "本库是**条文检索线索**,不是标准原文:「章节线索」只到章一级(以标准目录为准)," +
            "**不含具体条文号,也不含任何数值**;要点概述用于帮助你判断该查哪一本、哪一章。" +            "引用、计算与施工依据一律**以标准原文(现行版本)为准** —— " +
            "标准会修订,请核对现行版本与地方标准/行业标准中更严的规定。";

        private static readonly List<StandardClauseEntry> Entries = BuildEntries();

        /// <summary>全部条文条目。</summary>
        public static IList<StandardClauseEntry> All => Entries;

        /// <summary>专业中文名。</summary>
        public static string DisciplineName(CodeDiscipline discipline)
        {
            switch (discipline)
            {
                case CodeDiscipline.Hvac: return "通风空调";
                case CodeDiscipline.Plumbing: return "给水排水";
                case CodeDiscipline.Fire: return "防烟排烟 / 防火";
                case CodeDiscipline.Metro: return "轨道交通(地铁)";
                case CodeDiscipline.Drawing: return "制图 / 施工验收";
                default: return "其它";
            }
        }

        /// <summary>按专业取条文条目。</summary>
        public static IList<StandardClauseEntry> ByDiscipline(CodeDiscipline discipline)
        {
            var result = new List<StandardClauseEntry>();
            foreach (var entry in Entries)
            {
                if (entry.Discipline == discipline) result.Add(entry);
            }
            return result;
        }

        /// <summary>收录的标准清单(按专业、编号排序;含每本标准收录了几条)。</summary>
        public static IList<StandardInfo> Standards()
        {
            var index = new Dictionary<string, StandardInfo>();
            var order = new List<string>();
            foreach (var entry in Entries)
            {
                string key = entry.StandardCode + "|" + entry.StandardName;
                StandardInfo info;
                if (!index.TryGetValue(key, out info))
                {
                    info = new StandardInfo
                    {
                        Code = entry.StandardCode,
                        Name = entry.StandardName,
                        Discipline = entry.Discipline
                    };
                    index[key] = info;
                    order.Add(key);
                }
                info.EntryCount++;
            }

            var list = new List<StandardInfo>();
            foreach (var key in order) list.Add(index[key]);
            list.Sort(delegate (StandardInfo a, StandardInfo b)
            {
                int byDiscipline = a.Discipline.CompareTo(b.Discipline);
                if (byDiscipline != 0) return byDiscipline;
                return string.Compare(a.Code, b.Code, StringComparison.Ordinal);
            });
            return list;
        }

        /// <summary>按主题 / 关键词 / 标准编号检索(标准编号如「GB 50736」也能命中)。</summary>
        public static IList<StandardClauseEntry> Search(string query)
        {
            var matches = new List<StandardClauseEntry>();
            if (string.IsNullOrEmpty(query)) return matches;

            string text = query.Trim();
            var tokens = KnowledgeBase.TokenizeForSearch(text);
            foreach (var entry in Entries)
            {
                if (Hit(entry, text)) { matches.Insert(0, entry); continue; }
                foreach (var token in tokens)
                {
                    if (string.IsNullOrEmpty(token)) continue;
                    if (Hit(entry, token)) { matches.Add(entry); break; }
                }
            }
            return matches;
        }

        private static bool Hit(StandardClauseEntry entry, string token)
        {
            if (Contains(entry.Title, token) || Contains(entry.StandardCode, token) ||
                Contains(entry.StandardName, token) || Contains(entry.Summary, token) ||
                Contains(entry.ClauseHint, token)) return true;
            foreach (var keyword in entry.Keywords)
            {
                if (Contains(keyword, token) || Contains(token, keyword)) return true;
            }
            return false;
        }

        private static bool Contains(string text, string token)
        {
            return !string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(token) &&
                   text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static StandardClauseEntry Entry(string id, string code, string name, CodeDiscipline discipline,
            string title, string clauseHint, string summary, string question, params string[] keywords)
        {
            var entry = new StandardClauseEntry
            {
                Id = id,
                StandardCode = code,
                StandardName = name,
                Discipline = discipline,
                Title = title,
                ClauseHint = clauseHint,
                Summary = summary,
                Question = question,
                BoundaryNote = "条文号与数值以《" + name + "》原文为准;章节线索仅到章一级,以标准目录为准。"
            };
            entry.Keywords.AddRange(keywords);
            return entry;
        }

        private static List<StandardClauseEntry> BuildEntries()
        {
            const string Gb50736 = "GB 50736-2012";
            const string Gb50736Name = "民用建筑供暖通风与空气调节设计规范";
            const string Gb50019 = "GB 50019-2015";
            const string Gb50019Name = "工业建筑供暖通风与空气调节设计规范";
            const string Gb51251 = "GB 51251-2017";
            const string Gb51251Name = "建筑防烟排烟系统技术标准";
            const string Gb50016 = "GB 50016-2014(2018 年版)";
            const string Gb50016Name = "建筑设计防火规范";
            const string Gb50015 = "GB 50015-2019";
            const string Gb50015Name = "建筑给水排水设计标准";
            const string Gb50013 = "GB 50013-2018";
            const string Gb50013Name = "室外给水设计标准";
            const string Gb50014 = "GB 50014-2021";
            const string Gb50014Name = "室外排水设计标准";
            const string Gb50974 = "GB 50974-2014";
            const string Gb50974Name = "消防给水及消火栓系统技术规范";
            const string Gb50157 = "GB 50157-2013";
            const string Gb50157Name = "地铁设计规范";
            const string Gb51298 = "GB 51298-2018";
            const string Gb51298Name = "地铁设计防火标准";
            const string Gb50243 = "GB 50243-2016";
            const string Gb50243Name = "通风与空调工程施工质量验收规范";
            const string Gb50242 = "GB 50242-2002";
            const string Gb50242Name = "建筑给水排水及采暖工程施工质量验收规范";
            const string GbT50114 = "GB/T 50114-2010";
            const string GbT50114Name = "暖通空调制图标准";

            return new List<StandardClauseEntry>
            {
                // ---------------- 通风空调 ----------------
                Entry("hvac-indoor-param", Gb50736, Gb50736Name, CodeDiscipline.Hvac,
                    "室内空气设计参数(温度 / 相对湿度 / 风速)",
                    "室内空气设计参数章(以标准目录为准)",
                    "规定民用建筑舒适性空调与供暖的室内设计温度、相对湿度、风速等参数的取值区间与选取原则;" +
                    "设计说明里的室内设计参数应能对到该章的参数表。",
                    "室内设计参数按哪本标准取?",
                    "室内温度", "相对湿度", "风速", "设计参数", "舒适性"),

                Entry("hvac-outdoor-param", Gb50736, Gb50736Name, CodeDiscipline.Hvac,
                    "室外空气计算参数(干球 / 湿球 / 通风 / 冬季)",
                    "室外空气计算参数章 + 附录(气象参数表,以标准目录为准)",
                    "规定室外计算干球、湿球、夏季通风、冬季空调与供暖等室外参数的取值口径,并给出全国主要城市的参数表" +
                    "(本插件内嵌的 294 台站气象库即转自该附录)。",
                    "室外湿球温度从哪来?",
                    "室外", "干球", "湿球", "气象", "台站", "附录"),

                Entry("hvac-load-calc", Gb50736, Gb50736Name, CodeDiscipline.Hvac,
                    "冷负荷 / 热负荷计算原则与新风量",
                    "空气调节章(负荷计算与新风的计入原则,以标准目录为准)",
                    "规定空调冷热负荷的计算口径(逐时计算、新风负荷单列、围护结构传热与太阳辐射计入方式)," +
                    "以及民用建筑人员新风量下限的取值依据。",
                    "新风量下限按什么取?",
                    "冷负荷", "热负荷", "新风量", "新风", "负荷计算"),

                Entry("hvac-ventilation-rate", Gb50736, Gb50736Name, CodeDiscipline.Hvac,
                    "通风与事故通风(换气次数 / 有害物控制)",
                    "通风章(以标准目录为准)",
                    "规定房间通风量的确定方式(按换气次数或按有害物散发量计算)、事故通风的设置条件与换气要求;" +
                    "卫生间、泵房、气瓶间等房间的换气次数属于本类要求,工程上常取项目通行值。",
                    "房间换气次数在哪本规范?",
                    "换气次数", "通风量", "事故通风", "卫生间", "泵房"),

                Entry("hvac-duct-design", Gb50736, Gb50736Name, CodeDiscipline.Hvac,
                    "风管风速与风系统设计",
                    "空气调节章 / 通风章(风管风速与系统划分,以标准目录为准)",
                    "给出主风管、支管、风口等部位的风速建议区间,以及风系统划分与水力平衡的原则 ——" +
                    "本插件的水力计算模块就是按这些原则做最不利环路与并联平衡的。",
                    "风管风速限制在哪?",
                    "风速", "风管", "风系统", "水力平衡", "阻力"),

                Entry("hvac-insulation", Gb50736, Gb50736Name, CodeDiscipline.Hvac,
                    "保温与防结露(风管 / 水管)",
                    "绝热与防腐章(以标准目录为准)",
                    "规定风管与冷热水管的绝热层设置条件、防结露与防冻要求;" +
                    "材料表统计里「保温」按长度计,面积需按展开面另算,正是为了对得上这里的做法。",
                    "风管保温厚度按什么选?",
                    "保温", "绝热", "防结露", "防冻"),

                Entry("hvac-noise", Gb50736, Gb50736Name, CodeDiscipline.Hvac,
                    "消声与隔振",
                    "消声与隔振章(以标准目录为准)",
                    "规定空调通风系统的噪声控制要求、消声器/消声段设置与设备隔振做法;" +
                    "地铁车站的设备房、风亭噪声往往另有环保与地方标准叠加要求。",
                    "空调系统噪声怎么控制?",
                    "噪声", "消声", "隔振", "风亭"),

                Entry("hvac-industrial", Gb50019, Gb50019Name, CodeDiscipline.Hvac,
                    "工业建筑供暖通风(与民用规范的分工)",
                    "全书(工业建筑与民用建筑的分工以标准适用范围为准)",
                    "工业建筑的供暖、通风、除尘与空调设计要求;厂房、库房类建筑常按本标准," +
                    "而民用/公共建筑(含地铁车站公共区)按 GB 50736。",
                    "厂房通风按哪本标准?",
                    "工业建筑", "厂房", "除尘", "适用范围"),

                // ---------------- 防烟排烟 / 防火 ----------------
                Entry("fire-smoke-control", Gb51251, Gb51251Name, CodeDiscipline.Fire,
                    "防烟分区与排烟量计算",
                    "防烟分区与排烟量章(以标准目录为准)",
                    "规定防烟分区的划分(面积、长边、储烟仓)、排烟量的计算方法(按面积或按热释放速率)," +
                    "以及排烟风机与排烟口的风量、单台风机要求 —— 本插件排烟模块的 ×60 口径属于项目自定,规范取值请查本标准。",
                    "防烟分区怎么划分?排烟量怎么算?",
                    "防烟分区", "排烟量", "储烟仓", "挡烟垂壁", "排烟风机"),

                Entry("fire-pressurization", Gb51251, Gb51251Name, CodeDiscipline.Fire,
                    "防烟楼梯间 / 前室加压送风",
                    "防烟系统设计章(加压送风量,以标准目录为准)",
                    "规定需设加压送风的部位(防烟楼梯间、前室、合用前室、避难层等)、加压送风量的确定方式" +
                    "(查表法/查表与计算结合)与余压控制;" +
                    "本插件加压送风模块按「门开启 + 门缝漏风 + 余压阀漏风」计算,属于计算法的一种,规范原文与表格请查本标准。",
                    "楼梯间加压送风量怎么确定?",
                    "加压送风", "楼梯间", "前室", "余压", "门缝漏风"),

                Entry("fire-compartment", Gb50016, Gb50016Name, CodeDiscipline.Fire,
                    "防火分区 / 防火分隔 / 建筑构造",
                    "防火分区与平面布置章(以标准目录为准)",
                    "规定各类建筑的防火分区面积、防火分隔措施与竖井/管道穿墙封堵要求 ——" +
                    "风管穿墙、穿楼板处的防火阀设置与耐火极限要求在此查。",
                    "风管穿墙要设防火阀吗?",
                    "防火分区", "防火分隔", "防火阀", "封堵", "耐火极限"),

                Entry("fire-air-duct", Gb50016, Gb50016Name, CodeDiscipline.Fire,
                    "通风空调系统的防火要求",
                    "通风和空气调节章(以标准目录为准)",
                    "规定风管材质与耐火极限、风管穿越防火分隔处防火阀的设置、排烟与通风系统合用时的要求,以及" +
                    "空调机房、燃油燃气房间的通风与防爆要求。",
                    "空调风管的防火要求在哪?",
                    "风管", "防火阀", "耐火极限", "机房", "防爆"),

                // ---------------- 给水排水 ----------------
                Entry("plumb-water-supply", Gb50015, Gb50015Name, CodeDiscipline.Plumbing,
                    "建筑给水(用水定额 / 水压 / 管材)",
                    "给水章(以标准目录为准)",
                    "规定生活给水用水定额与小时变化系数、给水系统分区与水压要求、水质与管材选用;" +
                    "给水系统的水力计算(流量、管径、水头损失)按本标准的计算原则做。",
                    "生活给水定额怎么取?",
                    "给水", "用水定额", "水压", "管材", "生活给水"),

                Entry("plumb-drainage", Gb50015, Gb50015Name, CodeDiscipline.Plumbing,
                    "建筑排水(排水量 / 通气管 / 坡度)",
                    "排水章(以标准目录为准)",
                    "规定生活排水系统设计秒流量、横支管与横干管的坡度、通气系统设置、污水泵与集水坑要求 ——" +
                    "地铁车站的废水泵房、卫生间排水按本标准并结合地方要求。",
                    "排水管道坡度怎么定?",
                    "排水", "坡度", "通气", "污水泵", "集水坑", "废水"),

                Entry("plumb-hot-water", Gb50015, Gb50015Name, CodeDiscipline.Plumbing,
                    "热水与循环系统",
                    "热水章(以标准目录为准)",
                    "规定热水用水定额与水温、热水供应系统与循环方式、膨胀与安全设施;" +
                    "集中热水系统的循环流量计算与管网水力计算在此查。",
                    "热水循环怎么做?",
                    "热水", "循环", "水温", "膨胀管"),

                Entry("plumb-rain", Gb50015, Gb50015Name, CodeDiscipline.Plumbing,
                    "建筑屋面雨水排水",
                    "雨水章(以标准目录为准)",
                    "规定屋面雨水排水系统的选型(重力流/压力流)、设计重现期与暴雨强度口径、溢流设施;" +
                    "地铁出入口、风亭与下沉广场的排水常需与地方暴雨强度公式结合。",
                    "屋面雨水怎么算?",
                    "雨水", "重现期", "暴雨强度", "溢流", "下沉广场"),

                Entry("plumb-outdoor-supply", Gb50013, Gb50013Name, CodeDiscipline.Plumbing,
                    "室外给水(水源 / 管网 / 消防给水)",
                    "全书(以标准目录为准)",
                    "规定室外给水系统的水源、水量、水压与管网计算(含消防用水量与水压要求),是室外管网水力计算的依据。",
                    "室外给水管网怎么算?",
                    "室外给水", "水源", "管网", "水压"),

                Entry("plumb-outdoor-drain", Gb50014, Gb50014Name, CodeDiscipline.Plumbing,
                    "室外排水(排水体制 / 管道 / 泵站)",
                    "全书(以标准目录为准)",
                    "规定室外排水体制、设计流量与管道水力计算(满流/非满流)、检查井与泵站设计要求。",
                    "室外排水管道怎么算?",
                    "室外排水", "排水体制", "满流", "检查井", "泵站"),

                Entry("plumb-fire-water", Gb50974, Gb50974Name, CodeDiscipline.Plumbing,
                    "消防给水与消火栓系统",
                    "全书(以标准目录为准)",
                    "规定消防水源、消防水池与水箱容积、消防水泵与稳压设施、消火栓系统与管网计算" +
                    "(消防用水量、栓口压力、水力计算)等要求。",
                    "消防水池容积怎么确定?",
                    "消防给水", "消火栓", "消防水池", "消防泵", "稳压"),

                // ---------------- 地铁专项 ----------------
                Entry("metro-general", Gb50157, Gb50157Name, CodeDiscipline.Metro,
                    "地铁设计总体要求(通风空调 / 给排水章节)",
                    "通风空调与给排水相关章(以标准目录为准)",
                    "地铁工程各专业的总体设计规定,其中通风空调、给排水与防灾章节给出车站与区间的环控、" +
                    "给排水与消防设计原则;车站公共区温湿度、区间隧道通风与阻塞工况等要求在此查。",
                    "地铁车站环控按哪本标准?",
                    "地铁", "车站", "区间", "环控", "通风空调", "阻塞"),

                Entry("metro-fire", Gb51298, Gb51298Name, CodeDiscipline.Metro,
                    "地铁防火与防烟排烟(专项)",
                    "全书(以标准目录为准)",
                    "地铁工程的防火分隔、安全疏散、防烟排烟与消防设施专项要求 ——" +
                    "地铁排烟量、防烟分区、区间隧道排烟与出入口排烟的要求以本标准为准(常严于通用规范)。",
                    "地铁排烟按哪本标准?",
                    "地铁", "防火", "排烟", "疏散", "区间隧道"),

                // ---------------- 制图 / 施工验收 ----------------
                Entry("drawing-standard", GbT50114, GbT50114Name, CodeDiscipline.Drawing,
                    "暖通空调制图(图例 / 标注 / 图样画法)",
                    "全书(以标准目录为准)",
                    "规定暖通空调专业图的图例符号、设备与管道标注方式、系统图与平面图画法;" +
                    "出图模块的图例表、图纸清单与标注做法应与之相符。",
                    "暖通图例按哪本标准?",
                    "制图", "图例", "标注", "系统图", "图样"),

                Entry("accept-hvac", Gb50243, Gb50243Name, CodeDiscipline.Drawing,
                    "通风空调工程施工质量验收",
                    "全书(以标准目录为准)",
                    "规定风管制作与安装、设备安装、系统调试与检验批的验收要求;" +
                    "材料表的统计口径(长度、件数、保温)在采购与验收阶段会用到。",
                    "风管安装验收要求在哪?",
                    "验收", "施工", "风管", "安装", "调试"),

                Entry("accept-plumb", Gb50242, Gb50242Name, CodeDiscipline.Drawing,
                    "建筑给水排水及采暖工程施工质量验收",
                    "全书(以标准目录为准)",
                    "规定室内给水、排水、热水、采暖系统的安装与试验验收要求(水压试验、灌水试验等)。",
                    "给水管道试压要求在哪?",
                    "验收", "给水", "排水", "试压", "灌水试验")
            };
        }
    }
}
