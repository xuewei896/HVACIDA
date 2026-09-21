# HVACIDA — Revit 2020 地铁暖通智能设计辅助插件(骨架版)

依据 `HVACIDA_需求分析文档.md`(310 行)从零搭建的可编译骨架。当前里程碑:**能编译、能出 Ribbon 页、能打开三个 WPF 功能窗;大系统负荷主计算链已按《大系统负荷计算公式.docx》移植,并通过北京站算例《大系统负荷计算公式-示例.xls》30 项逐格一致校验(tools/HVACIDA.Smoke)**。

> ⚠️ 数值口径说明:大系统空调主链已核对可用;小系统、排烟"防烟分区"选型等仍为演示/过渡口径(见各文件注释与第 6 节),投入使用前按 TODO 完成。定稿依据 = 需求文档 + 两份公式文档(均在本仓库)。

> 🧭 开发总流程、**模块状态看板**、**每轮验证清单模板**、领域口径决策记录 → 见 [docs/开发流程.md](docs/开发流程.md)。

## 1. 环境(本机已验证)

- Revit 2020:`C:\Program Files\Autodesk\Revit 2020\RevitAPI.dll` / `RevitAPIUI.dll`
- Visual Studio 2022 Community(或 VS2019);.NET Framework 4.8 目标包已装
- .NET SDK 9.0.102(dotnet CLI 亦可编译,纯离线、无 NuGet 依赖)

## 2. 结构

```
D:\DSH
├─ HVACIDA.sln
├─ docs
│  ├─ 开发流程.md      开发流程/模块状态看板/每轮验证清单/口径决策记录
│  ├─ UI设计规范.md    Revit 原生风格 UI 规范(窗口框架/线框/交互/文案/落地映射)
│  ├─ 需求源文档-通风空调智能设计助手.md  交付 docx 的正文 Markdown 副本(diff 友好,由 tools/docx2md 生成)
│  └─ ui-prototype/    HTML 可点击原型(双击 index.html;含流程/逻辑视图与深链接)
├─ src
│  ├─ HVACIDA.Core     领域模型/计算/焓湿图/仓库/报告/模块目录/气象库(无 Revit 依赖,可单测)
│  │  └─ Resources/weather-db.csv  全国省市气象参数库(294 台站,由 tools/weatherdb 生成、内嵌进 DLL)
│  ├─ HVACIDA.UI       WPF 窗口+MVVM(不引用 Revit API);结果视图统一走可复用 ResultTableView(分组表格)
│  └─ HVACIDA.Revit    ExternalApplication/Ribbon/Command + 内嵌图标(引用前两者+Revit 2020 API)
│     └─ Resources/Icons/  23 个按钮图标 ×(16/32)px,由 tools/HVACIDA.IconGen 生成、内嵌进 DLL
├─ tools
│  ├─ HVACIDA.Smoke    数值+结构自检(北京算例 30 项、目录/仓库/知识库/小系统/空间聚合/气象联动/省市气象库/排烟计算)
│  │                   + 窗口 / Ribbon / 文档同步 / 气象库同步四项脚本
│  ├─ HVACIDA.IconGen  Ribbon 图标生成器(矢量几何 → PNG,无需设计素材)
│  ├─ docx2md          交付 docx → Markdown 正文副本提取器与同步门禁
│  └─ weatherdb        各省市室外空气参数.md → weather-db.csv 生成器与同步门禁(含数据校验报告)
├─ 各省市室外空气参数.md   ★ 气象库源数据(GB 50736-2012 附录A 表A,294 台站;生成物的唯一上游)
├─ deploy
│  ├─ HVACIDA.addin     清单模板(占位路径)
│  └─ install.ps1       编译 + 安装 .addin 到 C:\ProgramData\Autodesk\Revit\Addins\2020
└─ .dsh\skills\revit-hvac-2020\SKILL.md   开发技能(DSH 会话自动加载)
```

## 3. 编译

```powershell
cd D:\DSH
# 方式一(自动选 MSBuild/dotnet,并安装 addin):
powershell -ExecutionPolicy Bypass -File .\deploy\install.ps1
# 方式二(仅编译):
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" .\HVACIDA.sln /restore
# 或:
dotnet build .\HVACIDA.sln
```

输出:`src\HVACIDA.Revit\bin\Debug\`(HVACIDA.Revit.dll + HVACIDA.UI.dll + HVACIDA.Core.dll)。

## 4. 部署到 Revit 2020

1. 运行 `deploy\install.ps1`(需要管理员权限写 ProgramData)。
2. 重启 Revit 2020 → Ribbon 出现 **HVACIDA** 页:**7 个面板 / 23 个 PushButton**(已按评审定稿实现)——
   项目信息(工程信息·气象参数)/ 大系统(公共区参数·负荷计算·排烟计算·计算结果)/ 小系统(**7 键**:六类系统 + 计算结果)/
   水力计算(风系统·水系统·计算结果)/ 出图(明细表·图框)/ AI问答(操作指南·规范知识库)/ 产品支持(问题反馈·帮助)。
   面板名与按钮文字来自 `HVACIDA.Core.Services.ModuleCatalog`(单一数据源),详见 `docs/UI设计规范.md` §4.0。
3. 卸载:删除 `C:\ProgramData\Autodesk\Revit\Addins\2020\HVACIDA.addin`。

> ⚠️ 未实现的模块(排烟计算、水力计算 3 键、明细表、图框、问题反馈、小系统 5 类)点击后打开**待实现说明窗**,
> 写清"已定口径 + 待补项",**不给假数据、不伪装可用**。

> 若本机 2020 之外还要支持 2017/2018:改 `deploy\install.ps1 -RevitYear 2018`,并把各 csproj 的
> RevitAPI HintPath 指向对应年份目录(API 差异需另行适配,本项目按需求仅锁 2020)。

## 5. 模块-需求映射与完成度

| 需求章节 | 模块 | 现状 |
|---|---|---|
| 2.1 项目信息 | 工程信息窗 + 气象参数窗(ProjectInfoModel/DesignConditionParams → project.xml) | ✅ 可用(Excel 模板导入、.rvt 全局参数待实现) |
| 2.1.2 气象数据库 | 省/市下拉 + 选定城市自动回填室外参数(`WeatherDatabase`,内嵌 294 台站 / 31 省级行政区) | ✅ 可用(源 GB 50736-2012 附录A) |
| 2.2.3.1 大系统负荷 | 公共区参数窗 + 负荷计算窗 + 计算结果窗(LargeSystemLoadCalculator) | ✅ 已按公式文档移植;北京站算例 30 项逐格一致;**结果按分区分组以表格呈现**(§4.6) |
| 界面文案纪律 | — | **不体现公式文档单元格编号**(D55/C39/E159…):只在悬停提示里可见,计算书正文亦不含;window-smoke 有防回归扫描 |
| 2.1.2→2.2.3.1 气象联动 | `LargeSystemInputService` + `ProjectDesignSync`(C5/F4/F6 ← 项目信息) | ✅ 默认自动联动、可取消转手工;未填不覆盖 |
| 2.2.1/2.2.3.1 模型取值 | `SpaceSnapshot` + `PublicAreaAggregator` + `RevitSpaceReader`(D55/D56/C13/C14) | 🟡 Core+UI+命令层就绪,Revit 实机待验 |
| 2.2.3.2 小系统负荷 | 六类系统计算窗 + **全站多系统汇总窗**(SmallSystemLoadCalculator / SmallSystemSummaryService) | ✅ **六类全部实装**(全空气一次回风 / 多联机+新风 / 排风 / 送风排风排烟 / 加压送风 / 排烟);公式源《小系统空调负荷、送排风、排烟计算公式.docx》(**口径:按公式计算,示例仅用于理解公式**);结果按「系统结果 + 房间明细 + 设备选型」三张表呈现;**多系统按类型+编号 upsert 汇总**(§4.6);模型拾取:拾取空间建房间列表 / 拾取墙体求外墙总长 |
| 排烟计算(2.2.3.1) | 排烟计算窗(`LargeSmokeCalculator`)+ 计算结果表格 | ✅ 已实现(计算 ×60 / 选型 ×1.2 / 2 台取大者;防烟分区口径待接入) |
| 结果获取时机 | **打开即算 + 计算即保存**(UI设计规范 §4.9) | ✅ 「计算结果」窗**打开就有结果**,不需要再点一次【计 算】;录入窗点【计 算】= 先落盘再算,故结果窗读到的必然是刚算的那一份;只是打开窗不写盘、空系统不落盘 |
| 焓湿图 | PsychrometricHelper(饱和分压/含湿量/焓/露点/热湿比/除热风量) | ✅ 标准公式 |
| 2.2.4 结果管理 | 文本计算书(TextReportGenerator)+ **Excel 计算书**(`XlsxWriter` 自写最小 XLSX,零依赖) | ✅ 文本 + Excel:**大系统负荷 / 排烟 / 小系统 / 水力**各模块均可导出(`%AppData%\HVACIDA\Reports`) |
| 存储 | IDataRepository → XmlProjectRepository(project.xml / large-system.xml / large-smoke.xml / **small-systems.xml** / **hydraulic.xml** 多系统容器) | 🟡 待换 SQLite(旧 small-system.xml 与水力单系统文件首次读取自动迁移) |
| 2.7 规范知识库 | `KnowledgeBase`(本项目口径 + **规范条文检索** + **Revit 操作指南**,三类同窗可检索/筛选)+ **标准条文电子版导入(主路径)** + **ima 在线知识库(辅助,可选)** + 知识库窗口 | ✅ 条目化、每条带出处;规范条文覆盖 GB 50736 / GB 50015 / GB 51251 / GB 50016 / GB 50013 / GB 50014 / GB 50974 / GB 50157 / GB 51298 / GB 50243 / GB 50242 / GB/T 50114 等;**只给检索线索与要点,不编条文号与数值**(以标准原文为准);Revit 操作指南 15 节覆盖建模/空间/MEP/标注/出图/协同/排错;✅ 用户手上的条文电子版(txt/md/csv/docx)放进 `%AppData%\HVACIDA\规范条文` 即成为**可检索的条文原文** —— **打开知识库窗即自动载入**(不必每次手动点,可重复导入不累加)、**条文正文中段的词也能搜到**(关键词按全文均匀取样)、命中时答复里**单列整条原文**、按条文号提问原文进首位;✅ **ima 在线知识库**(辅助)按腾讯 ima 开放接口检索(需 Client ID + API Key + 知识库 ID,见 §6e),只给标题与片段;✅ **AI 问答(DeepSeek,检索增强,见 §6f)**:先本地检索**依据**,再把「问题 + 依据文本」发给模型,回答以**草稿** + **依据清单**呈现,系统提示写死不编条文号与数值、资料不足要明说;✅ **AI 助手停靠面板**(§6g):在 Revit 右侧聊天,需要工程数据时由模型调用**进程内只读命令**去取(不起 MCP、不配端口) |
| 2.7b AI 助手(Revit 停靠面板) | `AiChatPanel` + `AiAssistantViewModel` + `AiChatClient` + `AiCommandBus` + `RevitAiToolHost` | ✅ 见 §6g:「操作 Revit」**默认关**(关掉时模型看不到命令、命令不执行、工程数据不出网);「**允许模型修改模型**」第二级开关 + **Revit 原生确认框**;API key 走 **DPAPI** 按工作区加密;**只读命令 7 条 + 修改类 1 条**;命令活动日志可审计;⬜ create/delete 类命令、会话记忆、MCP |
| 2.3/2.4 水力计算 | 风系统 / 水系统录入窗 + **全站汇总窗**(`HydraulicCalculator` / `RevitHydraulicReader` / `HydraulicSummaryService`) | ✅ 已实装:模型里选系统 → 读管网 → 连接件拓扑求**最不利环路** → 需求全压(Pa)/ 扬程(m)+ 设备校核;**并联环路平衡**(Kv / 阀权度 / 需增加 ζ)、**系统阻力特性曲线**、**全站多系统汇总**(按「介质 + 系统编号」upsert)与 **Excel 导出**;系数全部可见可改(§4.10) |
| 2.5 材料表统计(出图→明细表) | 材料表窗(`MaterialTakeoffService` / `RevitMaterialTakeoffReader`) | ✅ 读模型 11 类构件(风管/水管/管件/附件/末端/设备/保温)→ 归并键含**单位**(长度与件数不相加)→ 类别小计 + 逐类型明细 + Excel 3 页(§4.11) |
| 2.6 图纸与批量出图(出图→图框) | 图框窗(`SheetCatalogService` / `RevitSheetReader` / `RevitSheetExporter` / `RevitAutoTagger`) | ✅ 图纸清单(编号/名称/图框/图幅 mm/视图数)+ **空图框计数** + **批量导出 DWG/DXF**(Revit 导出接口)+ **PDF**(系统打印机,**依赖本机 PDF 驱动,没有就逐张报失败**)+ 清单 Excel 4 页(§4.12);✅ **空间自动标注**(名称+编号,已有标注跳过)、✅ **图例表**(复用材料表);⬜ 风管/水管尺寸与设备编号标注、图例自动排版 |

## 6. 关键 TODO(按技能规范)

1. **大系统公式核对**:主计算链完成(算例 30 项逐格一致,2026-09-04);**排烟计算已按已定口径实装**(计算=面积×60,选型=×1.2,风机 2 台取站厅/站台大者,结果以表格给出);
   待补:**防烟分区几何**(分区面积、挡烟垂壁、储烟仓)未接入模型 —— 现按公共区整体作为一个分区,量偏大,接入后应逐分区取量、风机按最大分区选型。
2. ~~大系统输入 F4/F6/C5 接项目信息~~ 已实现(`ProjectDesignSync`,默认联动+可手工覆盖);~~接气象数据库~~ 已实现(**内嵌 GB 50736-2012 附录A 全国 294 台站**,按省市选取)。
   剩余:① **附录A 表19**(标准对咸阳/黔南州/新疆塔城等 6 个台站未记录夏季湿球温度,需按表19 或当地资料补);
   ② 源文件里 2 处海拔疑似笔误(西藏山南地区 9280→约 4280 m、青海黄南州 8500 m,由气压列反证),待人工核对。
3. SQLite 化:实现 `IDataRepository` 的 SQLite 版(需求:数据库 SQLite)。
4. Revit 读取:空间面积/体积/高度→D55/D56/C13/C14 **已实现**(含链接模型、自动识别+手动拾取,实机待验);
   剩余:**墙长**(小系统"与土壤接触外墙长度")、**与土壤接触屋顶面积**、参数回写、批量空间分区。
5. ~~计算书升级 Excel~~ **已实现并推广到全部模块**(大系统负荷/排烟、小系统单系统与全站汇总、水力单系统与全站汇总:
   Core 内自写最小 XLSX 写入器,**不引 EPPlus/OpenXML**);剩余:PDF 导出、出图/标注/图例,以及 Excel 里的图表(把阻力特性曲线画出来)。
6. ~~按钮图标~~ 已实装(23 个图标 ×16/32px,`tools/HVACIDA.IconGen` 生成并内嵌 DLL,见 `docs/UI设计规范.md` §4.0.1);
   剩余:中英文界面、操作日志与撤销。
7. 数值回归:Smoke 工程已含北京算例 30 项断言 + 结构自检(7 面板/23 按钮、仓库往返、知识库、**条文导入(含打开即载入/幂等/中段可搜)、ima 在线知识库接入、AI 问答(DeepSeek 检索增强)**、小系统、**空间聚合、气象联动**)、**水力计算(风/水,手算复算)**——
   `tools\HVACIDA.Smoke\bin\Debug\net48\HVACIDA.Smoke.exe`。
8. AI 问答待补(需求 2.7 的"接在线大模型"已完成最小可用版):
   ① **流式输出**(现为非流式:一次返回,长回答要等;流式需按 SSE 逐块解析);
   ② **多轮会话**(现在每次只带本轮依据的问答,不带历史);
   ③ **把工程数据作为上下文**(现口径是**不发送**模型数据与工程输入;若要用模型分析本工程算例,需先加显式开关与脱敏,并按保密要求评审);
   ④ **MCP / 工具调用**(让模型直接读模型或回写参数)未做 —— 会让"模型说的话"变成"改过的模型",需另行设计与门禁。
9. 水力计算待补(需求 2.3 / 2.4 已能算,下面几条要按项目补):
   ① **局部阻力系数取的是手册常用值**(不是唯一值),项目应按手册图表或**设备样本**替换 `HydraulicLocalLossTable` 的取值;
   ② 模型里若没给风机「全压」/ 水泵「扬程」参数,需手工填额定值才能校核;
   ③ 三通未区分直通/分流,管件族名**匹配不到的管件不计局部阻力**(界面逐条提示,可手工加到管段 Σζ);
   ④ **风机/水泵工况点**:插件只给**系统侧**阻力特性曲线(设计流量 50%~130%),**工况点要用厂家性能曲线与本表求交** —— 不内置设备曲线,也不假装算了工况点;
   ⑤ 允许不平衡率默认 15%(可改);平衡阀 **Kv 是按"需吸收压差 + 支路流量"反算**的,选型时还应核对厂家阀门的 Kv 档位与可调范围。

## 6b. 四项自检(不需要打开 Revit)
```powershell
# 1) 数值 + 结构断言(30 项北京算例 + 7 面板/23 按钮 + 仓库往返 + 知识库 + 小系统 + 空间聚合 + 气象联动
#    + 水力计算 / 多系统汇总 / Excel 导出(解压 xlsx 校验工作表与单元格)
#    + 条文电子版导入(含打开即载入·中段可搜)/ ima 在线知识库接入(JSON 读取器·凭证口径·应答解读·断网分支)
#    + AI 问答(DeepSeek 检索增强:请求体·应答·官方错误码·RAG 提示词·隐私口径·断网分支,离线可跑))
& ".\tools\HVACIDA.Smoke\bin\Debug\net48\HVACIDA.Smoke.exe"

# 2) 窗口装载自检:15 个 WPF 窗口真构造 + Show + Close(抓 XAML/绑定致命错误)
#    + 静态绑定一致性(§5.2:12 个窗口约 470 条 {Binding} 路径逐条反射校验)
#    + 气象联动/计算结果窗同源 + 公共区自动识别与手动拾取回填
#    + 打开即算 / 计算即保存(打开就出结果、只是打开不写盘、点计算即落盘、空系统不落盘)
#    + 水力计算窗(风/水两介质、管段表/系数表、全站多系统汇总 20 列、「—」、并联环路平衡与阻力特性曲线表、Excel 落盘)
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\tools\HVACIDA.Smoke\window-smoke.ps1 `
  -UiDir .\src\HVACIDA.UI\bin\Release\net48

# 3) Ribbon 结构自检:23 个命令注册 + [Transaction] 标注 + 23×2 内嵌图标(齐全/尺寸/可解码/覆盖率)
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\tools\HVACIDA.Smoke\ribbon-smoke.ps1 `
  -BinDir .\src\HVACIDA.Revit\bin\Release\net48

# 4) 文档同步自检:交付 docx 与 Markdown 副本的 SHA256 是否一致(0 = 副本不过期)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\docx2md\check-docx-sync.ps1

# 5) 气象库同步自检:源文件 各省市室外空气参数.md 与内嵌 weather-db.csv 是否一致 + 解析校验
#    (过期时 -Regenerate 自动重生成)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\weatherdb\check-weather-db-sync.ps1
```

## 6c. docx → Markdown 正文副本(便于 diff)

交付类 `.docx` 是二进制,Git 只能整体覆盖、无法合并。仓库把它们的正文抽成 Markdown 副本:

```powershell
# 重新生成(装了 python-docx 即可: pip install python-docx)
python tools\docx2md\docx_to_markdown.py "通风空调智能设计助手.docx" `
  "docs\需求源文档-通风空调智能设计助手.md" --auto-headings --toc
```

| 项 | 说明 |
|---|---|
| 生成物 | `docs/需求源文档-通风空调智能设计助手.md`(头部写入源文件 SHA256 与重生成命令) |
| 提取规则 | 标题样式→`#`;项目符号→`- `;加粗/斜体→`**`/`*`;表格→管道表格(首行作表头);不提取图片/页眉页脚/批注 |
| `--auto-headings` | 源文档没有标题样式时,把**标签式行**(短、以冒号结尾、不含分号句号)提升为 `###`,**正文一字不改** |
| `--toc` | 开头生成目录 |
| 约定 | **docx 是交付件、Markdown 是派生件**:改正文改 docx,然后重跑上面的命令;两边不要各改各的 |
| 门禁 | `tools\docx2md\check-docx-sync.ps1` 校验副本头部记录的 SHA256 与当前 docx 是否一致;**过期则退出码 1**(加 `-Regenerate` 可自动重生成)——已挂到 `docs/开发流程.md` §5.4 与 §6 交付清单 |

## 6d. 全国省市气象参数库(内嵌,GB 50736-2012 附录A)

「项目信息 → 工程信息」的项目地点是**省 / 市级联下拉**;选定城市后**自动把该市室外气象参数写进本工程**。

| 项 | 说明 |
|---|---|
| 源数据 | 仓库根目录 `各省市室外空气参数.md` —— GB 50736-2012 **附录A 表A「室外空气计算参数」**的 HTML 表格转录(294 个台站 / 31 个省级行政区 = 22 省 + 5 自治区 + 4 直辖市) |
| 生成物 | `src/HVACIDA.Core/Resources/weather-db.csv`(内嵌进 `HVACIDA.Core.dll`,插件不依赖外部文件、不联网) |
| 重新生成 | `python tools\weatherdb\build_weather_db.py` |
| 校验报告 | `tools/weatherdb/weather-db-report.txt`(每个省的台站数核对、错误与提示清单) |
| 门禁 | `tools\weatherdb\check-weather-db-sync.ps1`:源文件 SHA256 ↔ CSV 头部记录、台站数、重跑解析校验;**过期退出码 1**(`-Regenerate` 自动重生成) |
| 回填范围 | **只回填室外参数**:大系统室外 5 项、小系统室外 3 项、大气压力(取**夏季**值,1000.2 hPa → 100.02 kPa)、室外相对湿度(取夏季通风相对湿度)。**室内设计参数(站厅/站台/用房温湿度)属设计取值,气象库不动它** |
| 缺记录处理 | 标准未记录的格(附录A 条文说明点名:咸阳、黔南州、新疆塔城等共 **6 个台站**的夏季空调湿球温度)在 CSV 里留**空**,取用时**不覆盖原值** —— **不猜值、不用邻近台站顶替**。缺失项记在 `WeatherApplyResult.MissingText`(自检/报告用);**2026-09-20 起界面不再提示告警**(用户口径「删掉所有告警」) |

生成器为什么要存在(而不是手抄 CSV):源文件是分块 HTML 表格,块与块**共享省名上下文**,且有两类缺省形状 ——
① 省份格只剩计数(如 `(2)` = 天津续块、`(10)` = 河北续块);② 少数块整行没有省行(39 行布局)。
生成器按列位展开 `rowspan/colspan`、左起逐格推断"当前省",并用**每个省声明的台站数做 checksum**
(30 个省全部相符),另有"湿球 ≤ 干球""台站号唯一且 5 位""大气压力与海拔物理自洽"等断言。

> 已知问题(已记录,不影响计算):源文件里 **2 处海拔疑似笔误** —— 西藏山南地区标 9280 m、
> 青海黄南州标 8500 m;由**气压列反证**其真值约为 4280 m / 3400 m(相应高度的大气压才对得上)。
> `HVACIDA.Smoke` 场景10 断言"气压/海拔不一致的**只有**这 2 个已知台站",防止列位串行被放过。

## 6e. 条文原文(推荐主路径)与 ima 在线知识库(辅助,可选)

「AI问答 → 规范知识库」里有**两条**扩展知识的路,优先级不同,界面上也分开标了 ①②。

### ① 条文原文 —— 推荐主路径(`%AppData%\HVACIDA\规范条文`)

| 项 | 说明 |
|---|---|
| 做法 | 把标准条文电子版(`.txt` / `.md` / `.csv` / `.docx`)放进条文目录(窗内【打开条文目录】一键打开,首次自动创建并写入 `说明.md`),点【重新导入条文】 |
| 为什么推荐 | 插件手里是**整条原文**:可离线检索、可按条文号提问、可随知识库一起导出 Excel 交底。**ima 在线只给标题 + 片段**,引用时还得回 ima 看原文 |
| 打开即载入 | 打开知识库窗时**自动读一次**该目录(2026-09-16 起),不必每次手动点;手动【重新导入条文】保留,用于刚放进去的文件立刻生效;重复导入按 Id 替换、**不会重复累加** |
| 全文可搜 | 关键词按**全文均匀取样**(不是只取开头 40 字),所以条文**中段/末段**才出现的词也搜得到;命中时答复里**单列一段"命中的规范条文原文"(整条)**,按条文号提问则原文直接进首位 |
| 不支持 | `.pdf` / `.xls` / `.xlsx`:PDF 解析容易读错条文,插件不冒险 —— 请用 **Word 打开 PDF → 另存为 .docx**,或复制成 `.txt`;跳过原因会在界面逐条写明 |
| 出错处理 | 目录不存在时自动创建;目录不可用时**不抛异常**,只把原因写进状态行,内置条目照常可用 |
| 自检 | Smoke 场景21(解析与落库)+ **场景23(打开即载入 / 幂等 / 中段可搜 / 答复单列原文 / 说明文件真换行 / 目录不可用不抛)** |

### ② ima 在线知识库 —— 辅助(可选)

「AI问答 → 规范知识库」窗里可以再挂一个 ima 知识库:**勾选启用并填好凭证后**,同一个提问会**同时**查本地知识库与 ima。

| 项 | 说明 |
|---|---|
| 接口 | `POST https://ima.qq.com/openapi/wiki/v1/search_knowledge`(检索)、`.../get_knowledge_base`(连通性测试);请求头 `ima-openapi-clientid` / `ima-openapi-apikey`;应答统一 `{ retcode, errmsg, data }` |
| 凭证 | **Client ID + API Key + 知识库 ID 三样**(前两样在 ima 开放平台申请,知识库 ID 取自知识库本身) |
| ⚠ shareId | **分享链接里的 shareId 不能当接口凭证** —— 插件**不拿它鉴权**,只用它(需整条 `https://` 链接)在浏览器里打开分享页;只填一个 shareId 时插件会直说"缺 Client ID / API Key / 知识库 ID",**不发请求、不假装查到** |
| 落盘 | `%AppData%\HVACIDA\ima.xml`(凭证属本机个人信息,与工程数据分开存;**API Key 明文**,文件与界面都写明"勿外发/勿提交") |
| 返回内容 | 命中条目的**标题 + `highlight_content` 命中片段**,**不是**条文全文 —— 界面、答复区、说明都标注"引用请回 ima 打开原文核对" |
| 失败口径 | 未启用 / 凭证不全 / 网络不通 / `retcode≠0`:一律**照实显示原因**(含 errmsg 原样透传与错误码释义),**不抛异常、不拿本地条目冒充在线结果、不编内容** |
| 依赖 | 全部用 .NET 自带类型(`HttpWebRequest` + **自写最小 JSON 读取器** `Json.cs`),**不引任何 NuGet**;只在用户显式启用且凭证齐全时才发网络请求 |
| 自检 | `HVACIDA.Smoke` 场景22:JSON 读取器(含转义/缺失字段/语法错误)、retcode 与 errmsg 解读、shareId 不当凭证的口径门禁、设置落盘往返、断网分支(连本机空端口)**全部离线可跑** |

> 接口形态来自社区把 ima 开放接口整理成 skill 包的公开资料([openakita kb-api.md](https://github.com/openakita/openakita/blob/main/skills/tencent-ima/references/kb-api.md)、
> [ruiyongwang/dlh api.md](https://github.com/ruiyongwang/dlh/blob/main/skills/ima-notes/knowledge-base/references/api.md)),
> **本机未联网比对腾讯官方文档**:字段名或权限模型若有出入,插件表现为"接口返回失败 + 照实显示 errmsg"。
> 若你手上只有 ima 的**分享知识库**而没有开放平台凭证,**就走 ①**:在 ima 里打开条文、全选复制 → 按上面的命名与条文号规则整理成 `.txt` / `.docx` → 丢进条文目录 → 点【重新导入条文】。这样拿到的是**原文**、离线可检索、还不用配任何凭证。

### ③ AI 问答(DeepSeek)—— 检索增强(可选)

点【AI 回答】时:插件**先**用本地知识库把**依据**检索出来,**再**把「你的问题 + 依据文本」发给 DeepSeek。
接口形态**照官方文档实现**([首次调用 API](https://api-docs.deepseek.com/zh-cn/)、[错误码](https://api-docs.deepseek.com/zh-cn/quick_start/error_codes)):

| 项 | 说明 |
|---|---|
| 接口 | `POST https://api.deepseek.com/chat/completions`,请求头 `Authorization: Bearer <API key>`,body `{ model, messages, stream:false, temperature, max_tokens }`(OpenAI 兼容);回答在 `choices[0].message.content`,用量在 `usage` |
| 凭证 | **DeepSeek API key**(在 [platform.deepseek.com](https://platform.deepseek.com/api_keys) 申请);落 `%AppData%\HVACIDA\ai.xml`(**明文**,界面与文件都写明"勿外发/勿提交") |
| 模型 | 默认取官方文档给的默认模型(`deepseek-flash`),界面可改;模型名/价格以官方文档为准,填错会照实报 **422 参数错误** |
| 为什么不会乱编 | **检索增强 + 写死的系统提示**:只能依据给定资料回答、**不得**用记忆补条文号/数值/表格号、依据里没有就说没有并指出该查哪里、引用要标注依据编号、**结尾必须列「依据:」**;依据不足时提示词里还会明说"资料不足,别勉强作答" |
| 界面怎么呈现 | 回答放**草稿框**(黄底),旁边就是**依据清单**(序号/分类/标题/出处/相关度/是否截断,点一条看它的正文)—— 核对的是依据;状态行给模型名、耗时与 token 用量 |
| 隐私 | **只发送「你的问题 + 检索到的依据文本」**,不发送 Revit 模型数据、工程输入、计算结果文件;请在提问里避免涉密内容(界面与 `ai.xml` 注释都写明) |
| 失败口径 | 未启用 / 没填 key / 网络不通 / 接口报错(**400 格式、401 认证、402 余额、422 参数、429 限速、5xx 服务端**)一律照实显示(接口 message 原样透传),**不抛异常、不编回答**,并保留本地检索结果 |
| 成本 | 每次回答消耗 token(状态行显示用量);温度默认 0.2、单次生成上限默认 1024,可在面板改 |
| 依赖 | 复用 `HttpWebRequest` + **自写最小 JSON 读取器**,**不引任何 NuGet**(无 OpenAI SDK、无 Newtonsoft) |
| 自检 | `HVACIDA.Smoke` 场景24(约 70 项:请求体字段与转义 / 应答解读 / 官方错误码与分类 / 守卫分支 / **RAG 提示词纪律与依据清单** / 隐私口径 / 设置往返 / 断网分支),**离线可跑** |

> 与 ① ② 的关系:**① 给原文(引用靠它)、② 给在线线索(片段)、③ 把两者汇总成人话(草稿)**。
> ③ 永远不替代 ①:回答是模型生成的,**必须对着依据与标准原文核对后才能用** —— 插件不把模型输出当结论。

## 6g. AI 助手(进程内 function calling,照《如何将AI大模型(DeepSeek)接入Revit中》的架构)

> 参考资料是网络下载的第三方文章(《如何将AI大模型(DeepSeek)接入Revit中》),**未入库、版权归原作者**;
> 本项目只借鉴其架构思路,实现、口径与安全约束均按本仓库纪律重写(见下方安全口径)。

参考文档的核心思路:**不起 MCP Server、不配端口**,把大模型的 function calling 与 Revit 的 ExternalEvent 缝在一起,
用**进程内 CommandBus** 替代外部 MCP。本条按该架构分阶段落地,**当前进度见下表**。

| 阶段 | 内容 | 状态 |
|---|---|---|
| ① Core 引擎 | `AiChatClient`:OpenAI 兼容 `/chat/completions` + **SSE 流式** + **function calling** 最多 **12 轮**;`AiCommandBus`(进程内、串行锁 3 分钟、可审计日志)+ **「操作 Revit」总开关**;`AiToolCatalog`(**工具 schema 清洗**,避免 PowerShell 风格 schema 触发 HTTP 400);`ApiKeyVault`(**Windows DPAPI 加密**保存 API key,按工作区隔离);`AiWorkspaceScope`(Revit 版本 + 用户 + 项目路径哈希);`AiProviderPresets`(DeepSeek / 通义 / 智谱 / Kimi / OpenAI + 自定义) | ✅ 本阶段完成(Smoke 场景25 约 70 项,离线可跑) |
| ② Revit 侧 | 停靠面板「AI 助手」(`RegisterDockablePane` + **静态字段防 GC**)+ `CommandBus.Initialize` 放在 **ApplicationInitialized**(不在 OnStartup)+ `Idling` 兜底 + `AiExternalEventBridge`(ExternalEvent 命令模式:后台线程派活 → Revit 主线程执行 → `ManualResetEvent` 通知完成;每次派活先 `Reset()`)+ **只读命令集 7 条**(工程信息 / 构件统计 / 空间清单 / 材料表 / 图纸清单 / 水力汇总 / 知识库检索) | ✅ 本阶段完成(`RevitAiToolHost`;汇总口径**复用插件自己的服务**,AI 说的数与窗口里看到的是同一份) |
| ③ WPF 聊天面板 | `AiChatPanel`(停靠面板内容控件)+ `AiAssistantViewModel`:流式打字机渲染(逐段追加)、**命令活动日志**(调了哪条、成功/失败、耗时)、**服务预设选择**(DeepSeek / 通义 / 智谱 / Kimi / OpenAI / 自定义)、**「操作 Revit」开关**、按工作区隔离的设置与密钥、失败红字照实显示 | ✅ 本阶段完成(面板不是模态窗:聊天时仍可操作模型) |
| ④ 修改类命令 | **已实现 1 条**:`set_parameter_value`(改文本 / 整数 / 构件 ID 参数;默认作用于**当前选择**,也可显式给 elementIds)。**四道闸门**:①「操作 Revit」②「**允许模型修改模型**」(第二级开关,默认关)③ **Revit 原生确认对话框**(默认按钮是「否」)④ 单次 ≤ 200 个构件。**数值型(带单位实数)参数一律拒写** —— Revit 内部单位是英尺,直接写数字会把几何 / 风量改错,宁可让用户手工改 | ✅ 本阶段完成(create / delete 类按同一模式后续可加) |

**安全口径(照参考文档 2.6,并按本仓库纪律加严)**:
① **「操作 Revit」默认关**:关掉时**既不执行命令,也不把工具清单发给模型**(用户可以放心聊天,不怕 AI 乱改图);
② **API key 走 DPAPI 加密**(`%AppData%\HVACIDA\ai-key.bin`,按本机本用户 + 工作区隔离),**不落明文**;DPAPI 不可用时**明确降级提示**,不静默;
③ **改模型的命令要过两道开关 + 逐条确认**:「**允许模型修改模型**」**默认关**(关掉时修改类命令不下发给模型,硬调也被拒绝);开启后每条修改类命令执行前弹 **Revit 原生对话框**(默认按钮「否」),单次最多改 200 个构件,**数值型(带单位)参数一律不写**;
④ **工程数据出网要写明**:开启「操作 Revit」后,命令返回的工程数据(构件统计/空间/材料/图纸/已保存水力结果)**会**发给模型服务方 —— 界面与 `AiChatClient.PrivacyNoteWithTools` 都写明,项目不允许出网时请关掉开关;
⑤ **不做**参考文档里的 `send_code_to_revit`(让 AI 直接执行 C# 代码)—— 能力太强、prompt 约束不是硬约束,本项目不实现。

## 7. 与 AI 协作(DSH 技能)

`.dsh\skills\revit-hvac-2020\SKILL.md` 已被 DSH 自动发现;任何会话写本项目代码前都会加载它
(命名空间/框架/API 兼容/公式核对纪律)。修改技能文件即改规则,立即对新会话生效。
