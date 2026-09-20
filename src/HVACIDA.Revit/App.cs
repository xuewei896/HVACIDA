using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;
using HVACIDA.Core.Services;

namespace HVACIDA.Revit
{
    /// <summary>
    /// HVACIDA 插件入口(ExternalApplication):启动时在 Revit 增加“HVACIDA”功能页。
    /// .addin 中 FullClassName 必须为 HVACIDA.Revit.App。
    ///
    /// Ribbon 结构(2026-09-11 评审定稿,7 面板 / 22 PushButton):
    ///   项目信息:工程信息 / 气象参数
    ///   大系统  :公共区参数 / 负荷计算 / 排烟计算 / 计算结果
    ///   小系统  :全空气一次回风系统 / 多联机+新风系统 / 排风系统 /
    ///            送风排风排烟系统 / 加压送风系统 / 排烟系统 / 计算结果
    ///   水力计算:风系统 / 水系统 / 计算结果
    ///   出图    :明细表 / 图框
    ///   AI问答  :操作指南 / 规范知识库
    ///   产品支持:问题反馈 / 帮助
    /// 面板名与按钮文字取自 <see cref="ModuleCatalog"/>(单一数据源,避免两头维护)。
    /// </summary>
    public class App : IExternalApplication
    {
        public const string TabName = "HVACIDA";
        public const string VendorId = "HVACIDA";
        public const string VendorDescription = "暖通空调智能设计助手(骨架版)";

        /// <summary>模块键 → 命令类型(键与 ModuleCatalog 中的 Key 一致)。</summary>
        private static readonly Dictionary<string, Type> CommandMap = new Dictionary<string, Type>
        {
            // 1. 项目信息
            { "eng-info", typeof(Commands.ShowEngineeringInfoCommand) },
            { "weather", typeof(Commands.ShowWeatherCommand) },

            // 2. 大系统
            { "public-area", typeof(Commands.ShowPublicAreaCommand) },
            { "large-load", typeof(Commands.ShowLargeSystemCommand) },
            { "large-smoke", typeof(Commands.ShowLargeSmokeCommand) },
            { "large-result", typeof(Commands.ShowLargeResultCommand) },

            // 3. 小系统(7 键)
            { "small-allair", typeof(Commands.ShowSmallAllAirCommand) },
            { "small-vrf", typeof(Commands.ShowSmallVrfCommand) },
            { "small-exhaust", typeof(Commands.ShowSmallExhaustCommand) },
            { "small-sesmoke", typeof(Commands.ShowSmallSupplyExhaustSmokeCommand) },
            { "small-press", typeof(Commands.ShowSmallPressurizationCommand) },
            { "small-smoke", typeof(Commands.ShowSmallSmokeCommand) },
            { "small-result", typeof(Commands.ShowSmallResultCommand) },

            // 4. 水力计算
            { "hyd-air", typeof(Commands.ShowAirHydraulicCommand) },
            { "hyd-water", typeof(Commands.ShowWaterHydraulicCommand) },
            { "hyd-result", typeof(Commands.ShowHydraulicResultCommand) },

            // 5. 出图
            { "schedule", typeof(Commands.ShowScheduleCommand) },
            { "titleblock", typeof(Commands.ShowTitleBlockCommand) },

            // 6. AI问答
            { "guide", typeof(Commands.ShowGuideCommand) },
            { "knowledge", typeof(Commands.ShowKnowledgeCommand) },
            { "ai-chat", typeof(Commands.ShowAiAssistantCommand) },

            // 7. 产品支持
            { "feedback", typeof(Commands.ShowFeedbackCommand) },
            { "help", typeof(Commands.ShowHelpCommand) }
        };

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                CreateRibbon(application);

                // AI 助手停靠面板(参考文档 2.1):
                //  ① Provider 用**静态字段**持有(写成局部变量会被 GC 回收,之后点按钮报 pane has not been created yet);
                //  ② 命令集初始化放在 ApplicationInitialized(那时 Revit 才完全就绪),Idling 再兜底一次。
                PaneProvider = new Services.AiPaneProvider();
                application.RegisterDockablePane(Services.AiPaneProvider.PaneId,
                    Services.AiPaneProvider.PaneTitle, PaneProvider);
                application.ControlledApplication.ApplicationInitialized += (s, e) => OnApplicationInitialized(s);
                application.Idling += (s, e) => OnIdling();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("HVACIDA 加载失败", ex.ToString());
                return Result.Failed;
            }
        }

        /// <summary>
        /// AI 助手面板提供者(**必须静态持有**:Revit 不负责保活,一旦被 GC 回收,
        /// 下次点「AI助手」就会报「pane has not been created yet」)。
        /// </summary>
        internal static Services.AiPaneProvider PaneProvider;

        private static UIApplication _uiApplication;

        /// <summary>Revit 完全初始化后装载 AI 命令集(照参考文档:不在 OnStartup 里直接初始化)。</summary>
        private static void OnApplicationInitialized(object sender)
        {
            try
            {
                var app = sender as Autodesk.Revit.ApplicationServices.Application;
                if (app == null) return;
                _uiApplication = new UIApplication(app);
                InitializeAiCommandBus(_uiApplication);
            }
            catch (Exception ex)
            {
                // 命令集装载失败不影响插件其它功能;面板会显示"命令集尚未加载",Idling 还会再试
                AiCommandBus.Clear();
                System.Diagnostics.Debug.WriteLine("AI 命令集初始化失败:" + ex.Message);
            }
        }

        /// <summary>兜底:命令集还没就绪时,每次空闲再试一次(参考文档的做法)。</summary>
        private static void OnIdling()
        {
            if (AiCommandBus.IsReady) return;
            try
            {
                InitializeAiCommandBus(_uiApplication);
            }
            catch
            {
                // 静默重试:Idling 每秒都会来,重复弹窗反而打扰用户
            }
        }

        /// <summary>装载命令集 + ExternalEvent 通道(幂等)。</summary>
        private static void InitializeAiCommandBus(UIApplication uiApp)
        {
            if (uiApp == null || AiCommandBus.IsReady) return;
            if (PaneProvider != null) PaneProvider.UiApplication = uiApp;
            AiCommandBus.Register(new Services.RevitAiToolHost(uiApp));
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            try
            {
                AiCommandBus.Clear();
            }
            catch
            {
                // 关闭阶段不打扰用户
            }
            return Result.Succeeded;
        }

        /// <summary>按 ModuleCatalog 建 7 个面板与 22 个按钮(幂等:页已存在则跳过)。</summary>
        private static void CreateRibbon(UIControlledApplication application)
        {
            try
            {
                if (application.GetRibbonPanels(TabName).Count > 0) return;
            }
            catch
            {
                // 页不存在 → 继续创建
            }

            application.CreateRibbonTab(TabName);

            // 图标自检:22 个模块的 16/32 图标必须齐全(内嵌资源)。缺失就一次报清楚,
            // 避免"按钮在、图标空白"这种只能靠肉眼看出来的问题。
            System.Collections.Generic.IList<string> missingIcons = ModuleIcons.FindMissing(ModuleCatalog.Keys);
            if (missingIcons.Count > 0)
            {
                throw new InvalidOperationException(
                    "以下模块缺少 Ribbon 图标(每个模块需要 16×16 与 32×32 两套):" + string.Join(", ", missingIcons) +
                    Environment.NewLine +
                    "请重新生成并编译:HVACIDA.IconGen.exe --out src\\HVACIDA.Revit\\Resources\\Icons");
            }

            foreach (string panelName in ModuleCatalog.PanelOrder)
            {
                RibbonPanel panel = application.CreateRibbonPanel(TabName, panelName);

                foreach (ModuleInfo module in ModuleCatalog.ByPanel(panelName))
                {
                    Type commandType;
                    if (!CommandMap.TryGetValue(module.Key, out commandType))
                    {
                        // 目录里有、命令没实现 —— 不静默丢按钮,直接暴露出来
                        throw new InvalidOperationException("模块「" + module.Key + "」缺少对应命令类型注册。");
                    }

                    AddButton(panel, module, commandType);
                }
            }
        }

        private static void AddButton(RibbonPanel panel, ModuleInfo module, Type commandType)
        {
            var data = new PushButtonData(
                "HVACIDA." + module.Key,
                module.Title,
                commandType.Assembly.Location,
                commandType.FullName)
            {
                ToolTip = module.Summary,
                LongDescription = module.Summary + Environment.NewLine + "【状态】" + module.StatusText,
                // 图标:内嵌 PNG(16 供小图标位,32 供大按钮/高 DPI),见 ModuleIcons
                Image = ModuleIcons.Get(module.Key, ModuleIcons.SmallSize),
                LargeImage = ModuleIcons.Get(module.Key, ModuleIcons.LargeSize)
            };

            PushButton button = panel.AddItem(data) as PushButton;
            if (button == null) return;

            button.ToolTip = data.ToolTip;
            button.LongDescription = data.LongDescription;
            if (data.Image != null) button.Image = data.Image;
            if (data.LargeImage != null) button.LargeImage = data.LargeImage;
        }
    }
}
