using System;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace HVACIDA.Revit.Commands
{
    /// <summary>
    /// 命令宿主辅助:统一 try/catch 与错误提示。
    /// Ribbon 定稿后有 22 个按钮,命令类只声明"打开哪个窗口",样板代码集中在这里。
    /// </summary>
    internal static class CommandHost
    {
        /// <summary>模态显示一个窗口(Owner = Revit 主窗口)。</summary>
        public static Result Show(Func<Window> windowFactory, ref string message)
        {
            try
            {
                Services.DialogService.ShowModal(windowFactory());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("HVACIDA 错误", ex.ToString());
                return Result.Failed;
            }
        }

        /// <summary>
        /// 打开「待实现 / 说明」信息窗:内容取自 <see cref="Core.Services.ModuleCatalog"/>,
        /// 与 Ribbon 按钮文字同源,不伪装可用。
        /// </summary>
        public static Result ShowModuleInfo(string moduleKey, ref string message)
        {
            return Show(
                () =>
                {
                    var module = Core.Services.ModuleCatalog.Get(moduleKey);
                    var viewModel = module != null
                        ? UI.ViewModels.InfoViewModel.ForModule(module)
                        : UI.ViewModels.InfoViewModel.Guide();
                    return new UI.Views.InfoWindow(viewModel);
                },
                ref message);
        }
    }
}
