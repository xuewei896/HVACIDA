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
2. 重启 Revit 2020 → Ribbon 出现 **HVACIDA** 页:**项目信息 / 大系统负荷计算 / 小系统负荷计算**。
3. 卸载:删除 `C:\ProgramData\Autodesk\Revit\Addins\2020\HVACIDA.addin`。

> 若本机 2020 之外还要支持 2017/2018:改 `deploy\install.ps1 -RevitYear 2018`,并把各 csproj 的
> RevitAPI HintPath 指向对应年份目录(API 差异需另行适配,本项目按需求仅锁 2020)。

## 5. 模块-需求映射与完成度

| 需求章节 | 模块 | 现状 |
|---|---|---|
| 2.1 项目信息 | ProjectInfoModel/DesignConditionParams + 项目信息窗(保存 XML) | ✅ 骨架可用 |
| 2.2.3.1 大系统负荷 | LargeSystemLoadCalculator(客流/照明/设备/送风/新风/排烟/选型) | ✅ 已按公式文档移植;北京站算例 30 项逐格一致 |
| 2.2.3.2 小系统负荷 | SmallSystemLoadCalculator(全空气一次回风已实现,其余 6 类返回提示) | 🟡 仅一类实现 |
| 焓湿图 | PsychrometricHelper(饱和分压/含湿量/焓/露点/热湿比/除热风量) | ✅ 标准公式 |
| 2.2.4 结果管理 | TextReportGenerator(文本计算书,`%AppData%\HVACIDA\Reports`) | 🟡 文本版 |
| 存储 | IDataRepository → XmlProjectRepository(project.xml) | 🟡 待换 SQLite |
| 2.3/2.4 水力、2.5 材料表、2.6 出图、2.7 AI 问答 | — | ⬜ 未开始 |

## 6. 关键 TODO(按技能规范)

1. **大系统公式核对**:主计算链完成(算例 30 项逐格一致,2026-09-04);待补:**排烟"防烟分区"选型口径**(计算风量=面积×60,选型=×1.2,需分区几何输入)。
2. 大系统输入 F4/F6/C5(站厅/站台设计温度、室外湿球)改接 `DesignConditionParams`/项目信息自动回填(当前为模型默认值)。
3. SQLite 化:实现 `IDataRepository` 的 SQLite 版(需求:数据库 SQLite)。
4. Revit 读取:空间(Space)面积/体积/高度、墙长;参数回写;批量空间分区。
5. 计算书升级 Excel(EPPlus/OpenXML)与 PDF;出图/标注/图例。
6. 按钮图标(PushButtonData.Image/ImageLarge)、中英文界面、操作日志与撤销。
7. 数值回归:Smoke 工程已含北京算例 30 项断言(tools/HVACIDA.Smoke);建议补 xUnit 工程并持续追加用例。

## 7. 与 AI 协作(DSH 技能)

`.dsh\skills\revit-hvac-2020\SKILL.md` 已被 DSH 自动发现;任何会话写本项目代码前都会加载它
(命名空间/框架/API 兼容/公式核对纪律)。修改技能文件即改规则,立即对新会话生效。
