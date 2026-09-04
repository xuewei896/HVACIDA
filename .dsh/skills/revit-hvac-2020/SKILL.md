---
name: revit-hvac-2020
description: Revit 2020 API + C#/.NET Framework 4.8 开发规范,以及 HVACIDA 地铁暖通插件(负荷/通风/水力/统计/出图)的架构与编码约定
whenToUse: 编写、修改或审查 Revit 2020 插件代码(HVACIDA 或同类暖通 Add-in),包括 C# 源码、.addin 清单、WPF 界面、负荷计算公式、SQLite 存储
metadata:
  revitVersion: 2020
  dotnet: net48
  project: HVACIDA
---

# Revit 2020 插件开发规范(HVACIDA)

## 1. 用途

本技能服务一个基于 **Revit 2020 API** 的地铁暖通设计辅助插件 **HVACIDA**。
任何与该项目相关的 C# 代码产出前,先遵循本文件约定;产出后自查一遍。

## 2. 技术基线(与《HVACIDA_需求分析文档.md》一致)

| 项 | 值 |
|---|---|
| Revit | 2020(API 20.0.x,RevitAPI.dll / RevitAPIUI.dll 位于 `C:\Program Files\Autodesk\Revit 2020\`) |
| 语言 | C# 7.3(LangVersion=7.3,避免用 8.0+ 语法) |
| 运行时 | .NET Framework 4.8(net48),不引 NuGet 的 netstandard 桥接为默认 |
| UI | WPF(UseWPF),经典 MVVM(自写 INotifyPropertyChanged + RelayCommand,不引入第三方 MVVM 包) |
| 数据库 | SQLite(先抽象 IDataRepository,JSON/XML 落盘可先行,SQLite 作为后续实现) |
| 进程位数 | AnyCPU(Revit 2020 为 64 位,加载时解析为 x64) |
| 交互入口 | IExternalApplication(App.cs)注册 Ribbon;IExternalCommand 弹 WPF 对话框 |
| 解决方案 | src/HVACIDA.Core + src/HVACIDA.UI + src/HVACIDA.Revit(分层) |

分层规则:
- **Core**:无 Revit 依赖。Models/Services(计算、焓湿图、报告、仓库)。所有计算算法放这里,便于单元测试。
- **UI**:WPF View/ViewModel,引用 Core,不引用 Revit API。
- **Revit**:App/Ribbon/Command/Revit 数据读取(空间、墙、面积),引用 UI 与 Core。FullClassName = `HVACIDA.Revit.App`。

## 3. Revit 2020 API 硬性规则(代码审查清单)

1. **线程**:Revit API 只能在 Revit 主线程调用。所有 UI 交互都发生在 IExternalCommand.Execute(主线程)内;
   长时间计算不得阻塞主线程(用 IExternalEventHandler / ExternalEvent,或先同步算完再进事务)。
2. **事务**:凡修改文档必须 `using (var t = new Transaction(doc, "说明")) { t.Start(); ...; t.Commit(); }`;
   只读操作(doc.Spaces、ElementCollector)不需要事务。IExternalCommand.Execute 有自带事务上下文时
   不得在 Idling/外部事件外误开“手动模式”。
3. **命令签名**:`public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)`。
4. **程序集引用**:`RevitAPI.dll`、`RevitAPIUI.dll` 用 HintPath 直引,`<Private>false</Private>`、`<CopyLocal>false</CopyLocal>`,
   绝不拷入输出目录;目标框架 net48。添加 `ResolveAssemblyReference` 提示避免版本冲突。
5. **.addin 清单**:Application 插件节点含 Name/Assembly/AddInId(GUID)/FullClassName/VendorId;
   部署到 `C:\ProgramData\Autodesk\Revit\Addins\2020\`。不要在插件里硬编码另一台机器的绝对路径。
6. **异常**:所有命令 try/catch,用 `TaskDialog.Show` 或日志(`%AppData%\HVACIDA\logs`)回显错误;Revit 崩溃等于 0 容忍。
7. **WPF 模态**:对话框以 `ShowDialog()`,owner 用 Revit 主窗口句柄(见 UI 层 DialogService),
   避免窗口无主导致 alt-tab 失效;不要在非 UI 线程 new Window。
8. **命名**:Revit 文件单元英尺/平方英尺,BIM 模型长度 = 英尺(Feet);UI 展示用 mm/m/℃,转换集中写 Utils(注意 `DisplayUnitType`/`ForgeTypeId` 仅 2021+ 才有,2020 用 DisplayUnitType)。
9. **兼容**:只用 Revit 2020 存在的 API(ForgeTypeId、UnitUtils.Convert 重载等 2021+ API 禁用)。

## 4. 模块地图(源自需求文档,命名空间与文件应一一对应)

- 项目信息:ProjectInfoModel + 气象参数(DesignConditionParams) → UI“项目信息”
- 大系统负荷:Models/LargeSystemInput、LargeSystemResult;Services/LargeSystemLoadCalculator
- 小系统负荷:SmallSystemInput(系统类型 enum: 全空气一次回风/多联机+新风/排风/排烟/送排烟/加压送风)、SmallSystemResult
- 焓湿图:PsychrometricHelper(饱和水蒸气分压、含湿量、焓、露点、热湿比)——Core/Services
- 排烟:站厅/站台面积→60 次/小时 换气→风机 2 台、单台取大者一半
- 选型:组合式空调机组(送风/制冷量各取总量一半)、回排风机(回风量一半)、排烟风机(最大排烟量一半)
- 水力(风/水)、材料表、出图、AI 问答:后续模块,先保接口占位

## 5. 公式与数值(重要!)

`D:\DSH\大系统负荷计算公式.docx` 与 `HVACIDA_需求分析文档.md` 是唯一公式权威。
代码中凡出现"系数/公式"必须:
- 在方法 XML 注释写“公式来源”;
- 把易变系数定义为 `internal const double`,集中放 HvacConstants,并标注待与 docx 核对;
- 不要凭空发明地铁规范系数;不确定处留 `// TODO(公式核对):参照 大系统负荷计算公式.docx` 并默认给常见工程值。
焓湿图采用:饱和分压(Magnus)ps=610.94·exp(17.625t/(t+243.04));含湿量 d=0.622·φ·ps/(p−φ·ps);
焓 h=1.006t+d(2501+1.86t)(d 以 kg/kg);热湿比 ε=Q/W。

## 6. 建议实现顺序(骨架已就绪,按此推进)

1. Core:Models + PsychrometricHelper + Large/Small 计算器(公式核对) → 单元级可测
2. Repository(JSON/XML→SQLite)持久化项目信息/气象参数/结果
3. UI:项目信息 → 大系统 → 小系统 表单与结果展示
4. Revit 读取:空间(Space)面积/体积/高度、墙长(按类别过滤),回填参数
5. 报告:计算书(.txt/.csv → Excel via EPPlus/OpenXML 待定)与模型参数回写
6. 水力、材料表、出图、AI 问答

## 7. 每次提交前自查

- [ ] 是否引入 8.0+ 语法或 2021+ API?
- [ ] 修改文档是否包了 Transaction?
- [ ] 新系数是否登记并标注来源?
- [ ] 是否只把 RevitAPI*.dll 引用为 Private=false?
- [ ] UI 是否走 MVVM、对话框是否带 owner?
