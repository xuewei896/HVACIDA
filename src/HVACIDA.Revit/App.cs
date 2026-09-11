using System;
using System.Linq;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace HVACIDA.Revit
{
    /// <summary>
    /// HVACIDA 插件入口(ExternalApplication):启动时在 Revit 增加“HVACIDA”功能页与按钮。
    /// .addin 中 FullClassName 必须为 HVACIDA.Revit.App。
    /// </summary>
    public class App : IExternalApplication
    {
        public const string TabName = "HVACIDA";
        public const string VendorId = "HVACIDA";
        public const string VendorDescription = "暖通空调智能设计助手(骨架版)";

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

        private static void CreateRibbon(UIControlledApplication application)
        {
            // 幂等:页已存在则跳过(避免 AddInManager 热加载时重复建页)。
            try
            {
                if (application.GetRibbonPanels(TabName).Count > 0) return;
            }
            catch
            {
                // 页不存在 → 继续创建
            }

            application.CreateRibbonTab(TabName);

            RibbonPanel projectPanel = application.CreateRibbonPanel(TabName, "项目");
            AddButton(projectPanel, "HVACIDA.ProjectInfo", "项目信息", "维护工程基本\n信息与气象参数",
                typeof(Commands.ShowProjectInfoCommand));

            // 大系统:一级大按钮(2026-09-11 评审决定)
            RibbonPanel largePanel = application.CreateRibbonPanel(TabName, "大系统负荷计算");
            AddButton(largePanel, "HVACIDA.LargeSystem", "大系统\n负荷计算", "地铁站厅/站台空调\n负荷、风量与选型",
                typeof(Commands.ShowLargeSystemCommand));

            // 小系统:六类系统各一个按钮,3 行 × 2 列堆叠(2026-09-11 评审决定:由选择对话框提升到 Ribbon)
            RibbonPanel smallPanel = application.CreateRibbonPanel(TabName, "小系统负荷计算");
            smallPanel.AddStackedItems(
                NewSmallButton("HVACIDA.Small.AllAir", "全空气一次回风", "照明/人员/设备负荷、除热通风量、换气次数、新风量 → 柜式机组与回排风机选型",
                    typeof(Commands.ShowSmallAllAirCommand)),
                NewSmallButton("HVACIDA.Small.Vrf", "多联机+新风", "多联机 + 新风系统计算(待实现)",
                    typeof(Commands.ShowSmallVrfCommand)),
                NewSmallButton("HVACIDA.Small.Exhaust", "排风系统", "环控机房通风 / 卫生间排风(待实现)",
                    typeof(Commands.ShowSmallExhaustCommand)));
            smallPanel.AddStackedItems(
                NewSmallButton("HVACIDA.Small.Smoke", "排烟系统", "防烟分区排烟量与风机选型(待实现)",
                    typeof(Commands.ShowSmallSmokeCommand)),
                NewSmallButton("HVACIDA.Small.SupplyExhaustSmoke", "送风排风排烟", "送/排/排烟共用系统(待实现)",
                    typeof(Commands.ShowSmallSupplyExhaustSmokeCommand)),
                NewSmallButton("HVACIDA.Small.Pressurization", "加压送风", "楼梯间/前室加压送风(待实现)",
                    typeof(Commands.ShowSmallPressurizationCommand)));
        }

        /// <summary>构造小型堆叠按钮数据。</summary>
        private static PushButtonData NewSmallButton(string name, string text, string description, Type commandType)
        {
            return new PushButtonData(name, text, commandType.Assembly.Location, commandType.FullName)
            {
                ToolTip = description,
                LongDescription = description
            };
        }

        private static void AddButton(
            RibbonPanel panel,
            string name,
            string text,
            string description,
            Type commandType)
        {
            var data = new PushButtonData(
                name,
                text,
                commandType.Assembly.Location,
                commandType.FullName)
            {
                LongDescription = description,
                ToolTip = description
            };

            PushButton button = panel.AddItem(data) as PushButton;
            if (button != null)
            {
                button.ToolTip = description;
                button.LongDescription = description;
            }
        }
    }
}
