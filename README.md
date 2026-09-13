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
│  ├─ HVACIDA.Core     领域模型/计算/焓湿图/仓库/报告(无 Revit 依赖,可单测)
│  ├─ HVACIDA.UI       WPF 窗口+MVVM(不引用 Revit API)
│  └─ HVACIDA.Revit    ExternalApplication/Ribbon/Command(引用前两者+Revit 2020 API)
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
| 2.2.3.1 大系统负荷 | 公共区参数窗 + 负荷计算窗 + 计算结果窗(LargeSystemLoadCalculator) | ✅ 已按公式文档移植;北京站算例 30 项逐格一致 |
| 2.2.3.2 小系统负荷 | 全空气一次回风窗 + 计算结果窗(SmallSystemLoadCalculator) | 🟡 仅一类实现,其余 5 类给待实现说明 |
| 排烟计算(2.2.3.1) | — | ⬜ 待实现(需防烟分区几何) |
| 焓湿图 | PsychrometricHelper(饱和分压/含湿量/焓/露点/热湿比/除热风量) | ✅ 标准公式 |
| 2.2.4 结果管理 | TextReportGenerator(文本计算书,`%AppData%\HVACIDA\Reports`) | 🟡 文本版 |
| 存储 | IDataRepository → XmlProjectRepository(project.xml / large-system.xml / small-system.xml) | 🟡 待换 SQLite |
| 2.7 规范知识库 | DesignQaService(本地规则应答)+ 知识库窗口 | 🟡 规则版,待接 AI |
| 2.3/2.4 水力、2.5 材料表、2.6 出图 | — | ⬜ 未开始(入口已就位,点击给口径说明) |

## 6. 关键 TODO(按技能规范)

1. **大系统公式核对**:主计算链完成(算例 30 项逐格一致,2026-09-04);待补:**排烟"防烟分区"选型口径**(计算风量=面积×60,选型=×1.2,需分区几何输入)。
2. 大系统输入 F4/F6/C5(站厅/站台设计温度、室外湿球)改接 `DesignConditionParams`/项目信息自动回填(当前为模型默认值)。
3. SQLite 化:实现 `IDataRepository` 的 SQLite 版(需求:数据库 SQLite)。
4. Revit 读取:空间(Space)面积/体积/高度、墙长;参数回写;批量空间分区。
5. 计算书升级 Excel(EPPlus/OpenXML)与 PDF;出图/标注/图例。
6. 按钮图标(PushButtonData.Image/ImageLarge)、中英文界面、操作日志与撤销。
7. 数值回归:Smoke 工程已含北京算例 30 项断言 + 结构自检(7 面板/22 按钮、仓库往返、知识库、小系统)——
   `tools\HVACIDA.Smoke\bin\Debug\net48\HVACIDA.Smoke.exe`。

## 6b. 三项自检(不需要打开 Revit)
```powershell
# 1) 数值 + 结构断言(30 项北京算例 + 7 面板/22 按钮 + 仓库往返 + 知识库 + 小系统)
& ".\tools\HVACIDA.Smoke\bin\Debug\net48\HVACIDA.Smoke.exe"

# 2) 窗口装载自检:11 个 WPF 窗口真构造 + Show + Close(抓 XAML/绑定致命错误)
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\tools\HVACIDA.Smoke\window-smoke.ps1 `
  -UiDir .\src\HVACIDA.UI\bin\Release\net48

# 3) Ribbon 结构自检:反射检查 22 个命令注册 + [Transaction] 标注
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\tools\HVACIDA.Smoke\ribbon-smoke.ps1 `
  -BinDir .\src\HVACIDA.Revit\bin\Release\net48
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

## 7. 与 AI 协作(DSH 技能)

`.dsh\skills\revit-hvac-2020\SKILL.md` 已被 DSH 自动发现;任何会话写本项目代码前都会加载它
(命名空间/框架/API 兼容/公式核对纪律)。修改技能文件即改规则,立即对新会话生效。
