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
                "几何:站厅公共区面积、站台公共区面积、站厅公共区层高、站厅公共区长度(可由模型空间取值)。",
                "客流:上/下行线上客量与下客量、换乘上/下客量、停站时间、集群系数与超高峰小时系数。",
                "保存后「大系统 → 负荷计算」读取同一份数据,不需重复录入。"),

            new ModuleInfo("large-load", "大系统", "负荷计算", ModuleStatus.Implemented,
                "地铁站厅/站台空调负荷、风量与制冷量、设备选型(需求 2.2.3.1)",
                "七节参数:空气计算参数及标准 / 车站几何与出入口 / 高峰客流(必填)/ 人员散热散湿 / 照明广告设备 / 屏蔽门 / 其他湿负荷。",
                "公式口径与《大系统负荷计算公式.docx》一致,已通过北京站算例《-示例.xls》30 项逐格校验。"),

            new ModuleInfo("large-smoke", "大系统", "排烟计算", ModuleStatus.Implemented,
                "站厅/站台排烟量与排烟风机选型(需求 2.2.3.1)",
                "计算排烟量 = 公共区面积 × 60 m³/(h·m²);选型排烟量 = 计算 × 1.2(2026-09-04 决策);" +
                "排烟风机 2 台,按站厅、站台大者选型,单台 = 基准区选型风量 ÷ 2。",
                "结果以表格给出:区域 / 面积 / 计算排烟量 / 选型排烟量 / 单台风机风量,并标注选型基准区;可导出计算书。",
                "过渡口径(界面已注明):防烟分区几何(分区面积、挡烟垂壁、储烟仓)未接入模型,当前按公共区整体作为一个分区," +
                "量偏大;接入后应逐防烟分区取量、风机按最大分区选型。"),

            new ModuleInfo("large-result", "大系统", "计算结果", ModuleStatus.Implemented,
                "按当前已保存参数计算结果并导出计算书",
                "加载「公共区参数 / 负荷计算」保存的输入 → 执行公式链 → 输出客流、冷负荷、风量与制冷量、设备选型。",
                "未保存过输入时按默认参数计算,并在窗口内提示。"),

            // ---------- 3. 小系统(2026-09-15:六类全部实装,公式源《小系统空调负荷、送排风、排烟计算公式.docx》) ----------
            new ModuleInfo("small-allair", "小系统", "全空气一次回风\n系统", ModuleStatus.Implemented,
                "多个弱电/强电房间共用一台柜式空调机组的空调冷负荷与风量(需求 2.2.3.2)",
                "逐房间:照明=照明指标×面积、人员=134×人数、结构湿负荷=(层高×外墙长+屋顶面积)×壁面产湿量;" +
                "送风温度=室内−送风温差,露点温度=送风温度−管道温升;消除余热通风量与换气次数通风量取大得实际通风量。",
                "输出:柜式空调机组送风量与制冷量、回排风机回风量;结果按「系统结果 + 房间明细 + 设备选型」三张表呈现。",
                "口径(已确认):一律按公式文档的文字公式计算 —— 空调器冷量用实际通风量;示例仅用于理解公式,个别房间的示例值与公式值有约 3% 差异属正常。"),

            new ModuleInfo("small-vrf", "小系统", "多联机+新风\n系统", ModuleStatus.Implemented,
                "多个人员房间的多联机冷负荷与新风机组选型(需求 2.2.3.2)",
                "逐房间:照明+人员+设备=房间冷负荷;消除余热通风量按过渡季温差算,与换气次数通风量取大;" +
                "室内焓与新风焓算新风冷负荷。",
                "输出:新风机组送风量与制冷量、送风机、排风机、多联机室外机制冷量。"),

            new ModuleInfo("small-exhaust", "小系统", "排风系统", ModuleStatus.Implemented,
                "卫生间、泵房等房间的排风量与排风机选型(需求 2.2.3.2)",
                "计算排风量 = 空间面积 × 高度 × 换气次数;换气次数按房间类型取默认值(卫生间 20、淋浴间 10、" +
                "环控机房 6、气瓶间/电缆引入间/泵房/其他 4),可逐房间修改。",
                "选型排风量 = 计算排风量 × 1.1(按公式文档文字;示例用的 1.3 属示例取值,不作为基准)。"),

            new ModuleInfo("small-sesmoke", "小系统", "送风排风排烟\n系统", ModuleStatus.Implemented,
                "环控机房的排风/排烟/补风 + 气瓶间的排风(需求 2.2.3.2)",
                "计算排风量 = 面积×层高×换气次数;计算送风量 = 排风量×0.9;计算排烟量 = 面积×60;" +
                "计算补风量 = 排烟量×0.6;补风机取「计算送风量与计算补风量的最大值」。",
                "输出:排风机、排烟风机、补风机/送风机选型(系数 1.1 / 1.2 / 1.1)。"),

            new ModuleInfo("small-press", "小系统", "加压送风\n系统", ModuleStatus.Implemented,
                "楼梯间加压送风量(需求 2.2.3.2)",
                "门开启风量 + 门缝漏风量 + 余压阀漏风量 = 楼梯间加压送风量;选型 = ×1.2。",
                "口径:加压送风量 = (门开启风量 + 门缝漏风量 + 余压阀漏风量) × 3600,量纲为 m³/h(文档单位标注与公式自相矛盾,按公式)。"),

            new ModuleInfo("small-smoke", "小系统", "排烟系统", ModuleStatus.Implemented,
                "多个走道/防烟分区的排烟量与补风量(需求 2.2.3.2)",
                "计算排烟量 = 空间面积 × 60;选型排烟量 = 计算排烟量 × 1.2;" +
                "计算补风量 = 计算排烟量 × 0.6;选型补风量 = 计算补风量 × 1.1。",
                "输出:排烟风机与补风机选型;已按公式文档示例(2 个防烟分区)逐值核对。"),

            new ModuleInfo("small-result", "小系统", "计算结果", ModuleStatus.Implemented,
                "小系统计算结果汇总与计算书导出",
                "读取已保存的系统参数与房间列表 → 按系统类型执行公式链 → 输出系统结果、房间明细、设备选型三张表。",
                "六类系统均已实装;计算结果窗展示最近一次保存的系统。"),

            // ---------- 4. 水力计算 ----------
            new ModuleInfo("hyd-air", "水力计算", "风系统", ModuleStatus.Implemented,
                "风系统水力计算(需求 2.3):在模型里选取风系统 → 自动读风管/管件/末端 → 算最不利环路 → 需求风机全压",
                "口径(通用流体力学公式,系数全部可见可改):① 断面→流速 v=Q÷3600÷A;② 水力直径 d=4A÷湿周(矩形风管即流速当量直径);" +
                "③ 雷诺数 Re=v·d/ν,摩擦系数 λ=0.11×(K/d+68/Re)^0.25(Re 小于 2320 按 64/Re);④ 比摩阻 R=λ/d×ρv²/2,沿程=R×段长;" +
                "⑤ 局部=Σζ×ρv²/2;⑥ 需求全压=(环路上沿程+局部+末端/设备阻力+出口动压)×富余系数。",
                "数据来自模型:风管长度/断面/风量取模型几何与连接件流量,管件按族匹配局部阻力系数表;模型没建模管件时逐类列入待补提示。" +
                "模型里读到风机额定全压时给出余量与结论(满足/偏紧/不足)。"),

            new ModuleInfo("hyd-water", "水力计算", "水系统", ModuleStatus.Implemented,
                "水系统水力计算(需求 2.4):在模型里选取水系统 → 自动读水管/管件/设备 → 算最不利环路 → 需求水泵扬程",
                "口径同风系统(沿程达西-魏斯巴赫 + 局部 Σζ·ρv²/2);水泵**扬程 H =(环路总阻力 + 静压)× 富余系数 ÷(ρ·g)**," +
                "静压按最不利环路高差计(闭式冷冻水环路取 0),水物性按温度查表。",
                "数据来自模型:水管内径/长度/流量取模型与连接件;机组/盘管水阻取模型参数或样本值;模型里读到水泵额定扬程时给出余量与结论。"),

            new ModuleInfo("hyd-result", "水力计算", "计算结果", ModuleStatus.Implemented,
                "水力计算结果汇总(需求 2.3 / 2.4)",
                "读取已保存的风/水系统管网输入 → 现算最不利环路 → 给出需求风机全压(Pa)或需求水泵扬程(m)、逐段明细与设备校核。",
                "打开即有结果(与「大系统/小系统 计算结果」同规矩);没拾取过系统时只给指引,不摆假结果。"),

            // ---------- 5. 出图 ----------
            new ModuleInfo("schedule", "出图", "明细表", ModuleStatus.Implemented,
                "材料表统计(需求 2.5):按类别统计风管 / 水管 / 管件 / 附件 / 末端 / 设备 / 保温,归并成材料表并可导出 Excel",
                "计量口径:风管与水管取**长度 m**、保温取长度(面积需按展开面另算)、其余按**件数**;" +
                "归并键 = 类别 + 族 + 类型 + **单位**(单位进键,长度与件数不互相求和);" +
                "读不到长度曲线的构件按 1 件计并在界面报数,不静默丢弃。",
                "输出:类别小计 + 逐类型明细 + Excel(.xlsx)三个工作表;后续可加「损耗率」与「接头/翻边」等附加量。"),

            new ModuleInfo("titleblock", "出图", "图框", ModuleStatus.Implemented,
                "图纸清单与批量出图(需求 2.6):读图纸(编号/名称/图框/图幅/视图数)→ 批量导出 DWG/DXF/PDF → 图纸清单 Excel",
                "读模型:图纸编号 / 名称 / 图框族与类型(按图纸范围收集图框实例)/ 图幅(图纸外框换算 mm)/ 图框上的视图;" +
                "**空图框单独计数并在批量出图时跳过(记原因)**。",
                "导出口径:**DWG/DXF 走 Revit 导出接口**(逐张、文件名取「图纸编号_图纸名称」);" +
                "**PDF 走系统打印机,依赖本机 PDF 打印机驱动** —— 没有装就逐张写明失败原因,不谎报成功;" +
                "图纸清单 Excel 4 页(概况与图框 / 逐张图纸 / 批量出图记录 / 口径与待补)。" +
                "后续:自动标注规则库、图例表(可复用材料表)与批量打印排版。"),

            // ---------- 6. AI问答 ----------
            new ModuleInfo("guide", "AI问答", "操作指南", ModuleStatus.Implemented,
                "推荐操作顺序 ①→⑥ 与每步要点",
                "① 工程信息 → ② 气象参数 → ③ 公共区参数 → ④ 负荷计算 / 计算结果 → ⑤ 小系统六类 → ⑥ 水力 / 出图 / AI / 支持。"),

            new ModuleInfo("knowledge", "AI问答", "规范知识库", ModuleStatus.Implemented,
                "规范 / 口径知识库(需求 2.7):条目化、可检索、**每条都带出处**;答不出明说知识范围,不编答案",
                "内容 = 本项目已实装模块的**已定口径**(大/小系统、排烟、水力风与水、气象与省市库、材料表、图纸与批量出图、" +
                "计算书与 Excel、数据存储),每条含典型问法 + 答复 + **出处**(需求文档 / 公式文档 / 示例 xls / GB 50736 / 已定口径含日期)。",
                "检索为关键词加权(标题 3 / 关键词 2 / 正文 1);范围外的问题给「知识范围说明 + 提问建议」并列出最接近条目;" +
                "**未接在线大模型** —— 接入时本知识库作为「检索到的依据上下文」一并提交,答复仍须挂出处。可按分类浏览并导出 Excel。"),

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
