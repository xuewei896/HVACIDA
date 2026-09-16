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
│     └─ Resources/Icons/  22 个按钮图标 ×(16/32)px,由 tools/HVACIDA.IconGen 生成、内嵌进 DLL
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
2. 重启 Revit 2020 → Ribbon 出现 **HVACIDA** 页:**7 个面板 / 22 个 PushButton**(已按评审定稿实现)——
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
| 2.2.4 结果管理 | 文本计算书(TextReportGenerator)+ **Excel 计算书**(`XlsxWriter` 自写最小 XLSX,零依赖) | ✅ 文本 + Excel(`%AppData%\HVACIDA\Reports`) |
| 存储 | IDataRepository → XmlProjectRepository(project.xml / large-system.xml / large-smoke.xml / **small-systems.xml** / **hydraulic.xml** 多系统容器) | 🟡 待换 SQLite(旧 small-system.xml 与水力单系统文件首次读取自动迁移) |
| 2.7 规范知识库 | DesignQaService(本地规则应答)+ 知识库窗口 | 🟡 规则版,待接 AI |
| 2.3/2.4 水力计算 | 风系统 / 水系统录入窗 + **全站汇总窗**(`HydraulicCalculator` / `RevitHydraulicReader` / `HydraulicSummaryService`) | ✅ 已实装:模型里选系统 → 读管网 → 连接件拓扑求**最不利环路** → 需求全压(Pa)/ 扬程(m)+ 设备校核;**并联环路平衡**(Kv / 阀权度 / 需增加 ζ)、**系统阻力特性曲线**、**全站多系统汇总**(按「介质 + 系统编号」upsert)与 **Excel 导出**;系数全部可见可改(§4.10) |
| 2.5 材料表、2.6 出图 | — | ⬜ 未开始(入口已就位,点击给口径说明) |

## 6. 关键 TODO(按技能规范)

1. **大系统公式核对**:主计算链完成(算例 30 项逐格一致,2026-09-04);**排烟计算已按已定口径实装**(计算=面积×60,选型=×1.2,风机 2 台取站厅/站台大者,结果以表格给出);
   待补:**防烟分区几何**(分区面积、挡烟垂壁、储烟仓)未接入模型 —— 现按公共区整体作为一个分区,量偏大,接入后应逐分区取量、风机按最大分区选型。
2. ~~大系统输入 F4/F6/C5 接项目信息~~ 已实现(`ProjectDesignSync`,默认联动+可手工覆盖);~~接气象数据库~~ 已实现(**内嵌 GB 50736-2012 附录A 全国 294 台站**,按省市选取)。
   剩余:① **附录A 表19**(标准对咸阳/黔南州/新疆塔城等 6 个台站未记录夏季湿球温度,需按表19 或当地资料补);
   ② 源文件里 2 处海拔疑似笔误(西藏山南地区 9280→约 4280 m、青海黄南州 8500 m,由气压列反证),待人工核对。
3. SQLite 化:实现 `IDataRepository` 的 SQLite 版(需求:数据库 SQLite)。
4. Revit 读取:空间面积/体积/高度→D55/D56/C13/C14 **已实现**(含链接模型、自动识别+手动拾取,实机待验);
   剩余:**墙长**(小系统"与土壤接触外墙长度")、**与土壤接触屋顶面积**、参数回写、批量空间分区。
5. ~~计算书升级 Excel~~ **已实现**(水力计算书:Core 内自写最小 XLSX 写入器,单系统 6 页 / 全站汇总多页,**不引 EPPlus/OpenXML**);
   剩余:PDF 导出、出图/标注/图例,以及把 Excel 导出推广到大系统负荷/排烟/小系统计算书。
6. ~~按钮图标~~ 已实装(22 个图标 ×16/32px,`tools/HVACIDA.IconGen` 生成并内嵌 DLL,见 `docs/UI设计规范.md` §4.0.1);
   剩余:中英文界面、操作日志与撤销。
7. 数值回归:Smoke 工程已含北京算例 30 项断言 + 结构自检(7 面板/22 按钮、仓库往返、知识库、小系统、**空间聚合、气象联动**)、**水力计算(风/水,手算复算)**——
   `tools\HVACIDA.Smoke\bin\Debug\net48\HVACIDA.Smoke.exe`。
8. 水力计算待补(需求 2.3 / 2.4 已能算,下面几条要按项目补):
   ① **局部阻力系数取的是手册常用值**(不是唯一值),项目应按手册图表或**设备样本**替换 `HydraulicLocalLossTable` 的取值;
   ② 模型里若没给风机「全压」/ 水泵「扬程」参数,需手工填额定值才能校核;
   ③ 三通未区分直通/分流,管件族名**匹配不到的管件不计局部阻力**(界面逐条提示,可手工加到管段 Σζ);
   ④ **风机/水泵工况点**:插件只给**系统侧**阻力特性曲线(设计流量 50%~130%),**工况点要用厂家性能曲线与本表求交** —— 不内置设备曲线,也不假装算了工况点;
   ⑤ 允许不平衡率默认 15%(可改);平衡阀 **Kv 是按"需吸收压差 + 支路流量"反算**的,选型时还应核对厂家阀门的 Kv 档位与可调范围。

## 6b. 四项自检(不需要打开 Revit)
```powershell
# 1) 数值 + 结构断言(30 项北京算例 + 7 面板/22 按钮 + 仓库往返 + 知识库 + 小系统 + 空间聚合 + 气象联动
#    + 水力计算 / 多系统汇总 / Excel 导出(解压 xlsx 校验工作表与单元格))
& ".\tools\HVACIDA.Smoke\bin\Debug\net48\HVACIDA.Smoke.exe"

# 2) 窗口装载自检:15 个 WPF 窗口真构造 + Show + Close(抓 XAML/绑定致命错误)
#    + 静态绑定一致性(§5.2:12 个窗口约 470 条 {Binding} 路径逐条反射校验)
#    + 气象联动/计算结果窗同源 + 公共区自动识别与手动拾取回填
#    + 打开即算 / 计算即保存(打开就出结果、只是打开不写盘、点计算即落盘、空系统不落盘)
#    + 水力计算窗(风/水两介质、管段表/系数表、全站多系统汇总 20 列、「—」、并联环路平衡与阻力特性曲线表、Excel 落盘)
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\tools\HVACIDA.Smoke\window-smoke.ps1 `
  -UiDir .\src\HVACIDA.UI\bin\Release\net48

# 3) Ribbon 结构自检:22 个命令注册 + [Transaction] 标注 + 22×2 内嵌图标(齐全/尺寸/可解码/覆盖率)
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
| 缺记录处理 | 标准未记录的格(附录A 条文说明点名:咸阳、黔南州、新疆塔城等共 **6 个台站**的夏季空调湿球温度)在 CSV 里留**空**,取用时**不覆盖原值**并给出告警 —— **不猜值、不用邻近台站顶替** |

生成器为什么要存在(而不是手抄 CSV):源文件是分块 HTML 表格,块与块**共享省名上下文**,且有两类缺省形状 ——
① 省份格只剩计数(如 `(2)` = 天津续块、`(10)` = 河北续块);② 少数块整行没有省行(39 行布局)。
生成器按列位展开 `rowspan/colspan`、左起逐格推断"当前省",并用**每个省声明的台站数做 checksum**
(30 个省全部相符),另有"湿球 ≤ 干球""台站号唯一且 5 位""大气压力与海拔物理自洽"等断言。

> 已知问题(已记录,不影响计算):源文件里 **2 处海拔疑似笔误** —— 西藏山南地区标 9280 m、
> 青海黄南州标 8500 m;由**气压列反证**其真值约为 4280 m / 3400 m(相应高度的大气压才对得上)。
> `HVACIDA.Smoke` 场景10 断言"气压/海拔不一致的**只有**这 2 个已知台站",防止列位串行被放过。
## 7. 与 AI 协作(DSH 技能)

`.dsh\skills\revit-hvac-2020\SKILL.md` 已被 DSH 自动发现;任何会话写本项目代码前都会加载它
(命名空间/框架/API 兼容/公式核对纪律)。修改技能文件即改规则,立即对新会话生效。
