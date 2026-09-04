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

            RibbonPanel loadPanel = application.CreateRibbonPanel(TabName, "负荷计算");
            AddButton(loadPanel, "HVACIDA.LargeSystem", "大系统\n负荷计算", "地铁站厅/站台空调\n负荷、风量与选型",
                typeof(Commands.ShowLargeSystemCommand));
            AddButton(loadPanel, "HVACIDA.SmallSystem", "小系统\n负荷计算", "管理/设备用房等\n小系统负荷计算",
                typeof(Commands.ShowSmallSystemCommand));
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
