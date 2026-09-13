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

            // 7. 产品支持
            { "feedback", typeof(Commands.ShowFeedbackCommand) },
            { "help", typeof(Commands.ShowHelpCommand) }
        };

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                CreateRibbon(application);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("HVACIDA 加载失败", ex.ToString());
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
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
                LongDescription = module.Summary + Environment.NewLine + "【状态】" + module.StatusText
            };

            PushButton button = panel.AddItem(data) as PushButton;
            if (button == null) return;

            button.ToolTip = data.ToolTip;
            button.LongDescription = data.LongDescription;
        }
    }
}
