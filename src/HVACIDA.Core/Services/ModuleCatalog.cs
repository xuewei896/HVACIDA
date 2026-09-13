using System.Collections.Generic;
using System.Linq;

namespace HVACIDA.Core.Services
{
    /// <summary>模块实现状态(用于 Ribbon 提示与"待实现说明"窗,不伪装可用)。</summary>
    public enum ModuleStatus
    {
        /// <summary>已实现:按钮直达功能窗口。</summary>
        Implemented,

        /// <summary>骨架:只有布局/占位,算法未实现。</summary>
        Skeleton,

        /// <summary>待实现:给出已定口径与待补项。</summary>
        NotImplemented
    }

    /// <summary>一个 Ribbon 模块(对应一个 PushButton)。</summary>
    public sealed class ModuleInfo
    {
        public ModuleInfo(
            string key,
            string panel,
            string title,
            ModuleStatus status,
            string summary,
            params string[] notes)
        {
            Key = key;
            Panel = panel;
            Title = title;
            Status = status;
            Summary = summary;
            Notes = notes ?? new string[0];
        }

        /// <summary>命令/查找用键(与 Ribbon 按钮名一致)。</summary>
        public string Key { get; }

        /// <summary>所属 Ribbon 面板名称。</summary>
        public string Panel { get; }

        /// <summary>按钮文字(可含 \n 控制两行,Revit 大按钮按此换行)。</summary>
        public string Title { get; }

        /// <summary>实现状态。</summary>
        public ModuleStatus Status { get; }

        /// <summary>一句话说明(ToolTip / LongDescription)。</summary>
        public string Summary { get; }

        /// <summary>计算要点 / 已定口径 / 待补项。</summary>
        public IList<string> Notes { get; }

        /// <summary>单行标题(窗口标题栏用)。</summary>
        public string TitleOneLine => Title.Replace("\n", "");

        /// <summary>状态中文名。</summary>
        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case ModuleStatus.Implemented: return "已实现";
                    case ModuleStatus.Skeleton: return "骨架(算法未实现)";
                    default: return "待实现";
                }
            }
        }
    }

    /// <summary>
    /// 模块目录(单一数据源):Ribbon 的 7 面板 / 22 按钮、操作指南、帮助信息都取自本类,
    /// 避免按钮文字与窗口说明两头维护(2026-09-11 评审定稿的 Ribbon 结构)。
    /// </summary>
    public static class ModuleCatalog
    {
        /// <summary>面板顺序(即 Ribbon 上的排列顺序)。</summary>
        public static readonly IList<string> PanelOrder = new[]
        {
            "项目信息", "大系统", "小系统", "水力计算", "出图", "AI问答", "产品支持"
        };

        /// <summary>全部 22 个模块(顺序 = Ribbon 按钮顺序)。</summary>
        public static readonly IList<ModuleInfo> All = new[]
        {
            // ---------- 1. 项目信息 ----------
            new ModuleInfo("eng-info", "项目信息", "工程信息", ModuleStatus.Implemented,
                "工程基本信息(需求 2.1.1);保存后写入 %AppData%\\HVACIDA\\project.xml,后续按同一接口换 SQLite",
                "填写工程名称、设计阶段、项目地点、车站名称、备注。",
                "确定/保存后作为模型元数据使用(正式版写入 .rvt 全局参数,见需求 2.1.3)。"),

            new ModuleInfo("weather", "项目信息", "气象参数", ModuleStatus.Implemented,
                "室外/室内计算参数(需求 2.1.2);大系统与小系统分列,支持从气象数据库获取默认值",
                "大系统室外:夏季空调干球/湿球、夏季通风、冬季通风、冬季空调。",
                "小系统室外:夏季空调干球/湿球、夏季通风;室内:站厅/站台、管理/弱电/强电用房、室内湿球。",
                "其他:室外相对湿度、大气压力(正式版接气象数据库自动获取)。"),

            // ---------- 2. 大系统 ----------
            new ModuleInfo("public-area", "大系统", "公共区参数", ModuleStatus.Implemented,
                "站厅/站台公共区几何与高峰客流(需求 2.2.3.1 用户输入项);与大系统负荷计算共用同一份输入",
                "几何:站厅公共区面积 D55、站台公共区面积 D56、站厅公共区层高 C13、站厅公共区长度 C14。",
                "客流:上/下行线上客量与下客量、换乘上/下客量、停站时间、集群系数与超高峰小时系数。",
                "保存后「大系统 → 负荷计算」读取同一份数据,不需重复录入。"),

            new ModuleInfo("large-load", "大系统", "负荷计算", ModuleStatus.Implemented,
                "地铁站厅/站台空调负荷、风量与制冷量、设备选型(需求 2.2.3.1)",
                "七节参数:空气计算参数及标准 / 车站几何与出入口 / 高峰客流(必填)/ 人员散热散湿 / 照明广告设备 / 屏蔽门 / 其他湿负荷。",
                "公式口径与《大系统负荷计算公式.docx》一致,已通过北京站算例《-示例.xls》30 项逐格校验。"),

            new ModuleInfo("large-smoke", "大系统", "排烟计算", ModuleStatus.NotImplemented,
                "站厅/站台排烟量与排烟风机选型(需求 2.2.3.1)",
                "已定口径:计算风量 = 公共区面积 × 60 m³/(h·m²);单台排烟风机风量取站厅、站台最大值的一半;风机 2 台;选型风量 = 计算风量 × 1.2。",
                "待补:防烟分区几何(分区面积、挡烟垂壁、储烟仓)的模型读取 —— 目前只有公共区总面积,尚不能按防烟分区出量。"),

            new ModuleInfo("large-result", "大系统", "计算结果", ModuleStatus.Implemented,
                "按当前已保存参数计算结果并导出计算书",
                "加载「公共区参数 / 负荷计算」保存的输入 → 执行公式链 → 输出客流、冷负荷、风量与制冷量、设备选型。",
                "未保存过输入时按默认参数计算,并在窗口内提示。"),

            // ---------- 3. 小系统 ----------
            new ModuleInfo("small-allair", "小系统", "全空气一次回风\n系统", ModuleStatus.Implemented,
                "按空间计算照明/人员/设备负荷、除热通风量、换气次数通风量、新风量(需求 2.2.3.2)",
                "物理参数由模型获取(面积、层高、与土壤接触外墙长度、屋顶面积)。",
                "负荷与通风参数可编辑:设备冷负荷、照明指标、预测人数、换气次数、室内温度、送风温差。",
                "输出:柜式空调机组与回排风机选型(选型规则库待接入)。"),

            new ModuleInfo("small-vrf", "小系统", "多联机+新风\n系统", ModuleStatus.NotImplemented,
                "多联机 + 新风系统(需求 2.2.3.2)",
                "计算要点:夏季/过渡季温度、空间物理参数、负荷指标、人数、换气次数 → 照明/人员/设备/新风冷负荷。",
                "后续:消除余热通风量与换气次数通风量 → 新风机组、送风机、排风机、多联机外机选型。"),

            new ModuleInfo("small-exhaust", "小系统", "排风系统", ModuleStatus.NotImplemented,
                "环控机房通风 / 卫生间排风(需求 2.2.3.2)",
                "计算要点:按换气次数确定通风量;子类型分别取换气次数指标(环控机房通风、卫生间排风)。",
                "后续:风量汇总 → 排风机选型与风管断面校核。"),

            new ModuleInfo("small-sesmoke", "小系统", "送风排风排烟\n系统", ModuleStatus.NotImplemented,
                "送风 / 排风 / 排烟共用系统(需求 2.2.3.2)",
                "计算要点:送风、排风、排烟风量叠加与工况切换(空调季 / 通风季 / 火灾工况)。",
                "后续:阀门联锁逻辑与共用风管的风速校核。"),

            new ModuleInfo("small-press", "小系统", "加压送风\n系统", ModuleStatus.NotImplemented,
                "楼梯间 / 前室加压送风(需求 2.2.3.2)",
                "计算要点:按规范查表确定加压送风量,并进行门洞风速与余压校核。",
                "后续:与消防联动状态、前室/楼梯间门开启数量组合。"),

            new ModuleInfo("small-smoke", "小系统", "排烟系统", ModuleStatus.NotImplemented,
                "防烟分区排烟量与风机选型(需求 2.2.3.2)",
                "计算要点:计算风量 = 防烟分区面积 × 60 倍/h;选型风量 = 计算风量 × 1.2。",
                "待补:防烟分区几何(分区面积、清晰高度)与排烟口布置。"),

            new ModuleInfo("small-result", "小系统", "计算结果", ModuleStatus.Implemented,
                "小系统各空间计算结果汇总与计算书导出",
                "按空间汇总总冷负荷、通风量、新风量。",
                "当前仅「全空气一次回风系统」完成计算链路;其余五类实现后自动纳入汇总。"),

            // ---------- 4. 水力计算 ----------
            new ModuleInfo("hyd-air", "水力计算", "风系统", ModuleStatus.NotImplemented,
                "风系统水力计算(需求 2.3)",
                "计划布局:左=系统树(风管/末端),中=管段参数(风量/风速/阻力),右=结果与平衡。",
                "待实现:沿程与局部阻力算法、环路平衡、风机选型与可视化。"),

            new ModuleInfo("hyd-water", "水力计算", "水系统", ModuleStatus.NotImplemented,
                "水系统水力计算(需求 2.4)",
                "计划布局:左=环路树,中=参数(流量/管径/比摩阻),右=阻力与选型。",
                "待实现:水管阻力计算、水泵选型、管路平衡、调节阀选型。"),

            new ModuleInfo("hyd-result", "水力计算", "计算结果", ModuleStatus.NotImplemented,
                "水力计算结果汇总(需求 2.3 / 2.4)",
                "待风系统 / 水系统算法完成后,汇总阻力、平衡与设备选型结果。",
                "当前无可用结果,不提供假数据。"),

            // ---------- 5. 出图 ----------
            new ModuleInfo("schedule", "出图", "明细表", ModuleStatus.NotImplemented,
                "材料表统计(需求 2.5):设备 / 管道 / 配件统计 → 材料清单 → 导出 Excel",
                "待实现:材料数据库接口(需求 4.2)与按系统、楼层的归类统计。",
                "后续:与 Revit 明细表(明细表视图)联动或直接生成 xlsx。"),

            new ModuleInfo("titleblock", "出图", "图框", ModuleStatus.NotImplemented,
                "图纸模板、自动标注、图例与批量出图(需求 2.6)",
                "计划:图纸模板管理 → 自动标注 → 图例生成 → 批量出图与排版。",
                "待实现:标注规则库、图框族参数读写与批量打印。"),

            // ---------- 6. AI问答 ----------
            new ModuleInfo("guide", "AI问答", "操作指南", ModuleStatus.Implemented,
                "推荐操作顺序 ①→⑥ 与每步要点",
                "① 工程信息 → ② 气象参数 → ③ 公共区参数 → ④ 负荷计算 / 计算结果 → ⑤ 小系统六类 → ⑥ 水力 / 出图 / AI / 支持。"),

            new ModuleInfo("knowledge", "AI问答", "规范知识库", ModuleStatus.Skeleton,
                "设计口径问答(当前为本地规则应答,正式版接 AI 服务,需求 2.7 / 4.1)",
                "可问:送风温差、排烟风量与选型、新风量、焓湿计算、客流与集群系数、屏蔽门负荷等。",
                "已知边界:仅覆盖本项目已定口径,不替代规范原文;答不出时给出知识范围说明。"),

            // ---------- 7. 产品支持 ----------
            new ModuleInfo("feedback", "产品支持", "问题反馈", ModuleStatus.NotImplemented,
                "问题反馈入口(待接入反馈服务或邮件通道)",
                "当前可手动提供:Revit 日志目录、HVACIDA.addin 加载清单、插件存储目录。",
                "待实现:一键打包日志与截图、填写联系方式后提交。"),

            new ModuleInfo("help", "产品支持", "帮助", ModuleStatus.Implemented,
                "版本、运行环境、加载清单与日志路径、常用操作与快捷键",
                "版本 0.1.0(原型评审版);Revit 2020 / .NET Framework 4.8 / x64。",
                "排错信息:加载清单与 Revit 日志目录(见窗口内路径)。")
        };

        /// <summary>已知模块键集合(供自检/测试)。</summary>
        public static IEnumerable<string> Keys => All.Select(m => m.Key);

        /// <summary>按面板取模块(Ribbon 建面板时用)。</summary>
        public static IList<ModuleInfo> ByPanel(string panel)
        {
            return All.Where(m => m.Panel == panel).ToList();
        }

        /// <summary>按键取模块;找不到返回 null。</summary>
        public static ModuleInfo Get(string key)
        {
            return All.FirstOrDefault(m => m.Key == key);
        }

        /// <summary>操作指南步骤。</summary>
        public static readonly IList<string> GuideSteps = new[]
        {
            "① 工程信息:填工程名称、设计阶段、项目地点、车站名称;保存后可被后续计算引用。",
            "② 气象参数:核对大/小系统室外计算参数与室内设计参数;不确定时用「从气象数据库获取」取默认值。",
            "③ 大系统 → 公共区参数:从模型拾取站厅/站台公共区空间,并填写高峰客流(必填)。",
            "④ 大系统 → 负荷计算:核对七节参数(可用「恢复默认」回到公式文档值)→ 点【计 算】。",
            "⑤ 大系统 → 计算结果:查看客流、冷负荷、风量与制冷量、设备选型,可导出计算书。",
            "⑥ 小系统 → 选具体系统类型:在全空气一次回风窗内选空间、改参数、计算并导出计算书。",
            "⑦ 水力计算 / 出图 / 规范知识库 / 产品支持:查看待实现口径、提问或反馈问题。"
        };

        /// <summary>帮助窗内容(A 版本信息 / B 排错信息 / C 常用操作)。</summary>
        public static readonly IList<string> HelpVersionLines = new[]
        {
            "插件名称:HVACIDA — 地铁通风空调智能设计助手",
            "插件版本:0.1.0(原型评审版)",
            "运行环境:Revit 2020 · .NET Framework 4.8 · x64",
            "AddInId:D257A0A5-CF45-416B-8B08-A1B3CB8A40AB",
            "加载清单:FullClassName = HVACIDA.Revit.App"
        };

        /// <summary>排错信息路径。</summary>
        public static readonly IList<string> HelpDiagnosticLines = new[]
        {
            "加载清单:C:\\ProgramData\\Autodesk\\Revit\\Addins\\2020\\HVACIDA.addin",
            "Revit 日志:%LOCALAPPDATA%\\Autodesk\\Revit\\Autodesk Revit 2020\\Journals\\",
            "插件数据:%AppData%\\HVACIDA\\(project.xml / large-system.xml / small-system.xml)"
        };

        /// <summary>常用操作与快捷键。</summary>
        public static readonly IList<string> HelpShortcutLines = new[]
        {
            "Enter = 默认按钮(确定/计算);Esc = 取消/关闭",
            "Tab 顺序 = 视觉顺序;数值框右对齐,失焦即校验",
            "计算书:各计算窗底栏【导出计算书】→ %AppData%\\HVACIDA\\reports\\",
            "恢复默认:大系统 / 气象参数 / 公共区参数窗提供,均需二次确认"
        };
    }
}
