using System;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace HVACIDA.Revit.Commands
{
    /// <summary>
    /// 命令基类:模态显示一个 WPF 窗口。
    /// 在 IExternalCommand.Execute(Revit 主线程)内同步显示,不触碰文档事务。
    /// </summary>
    /// <typeparam name="TWindow">要显示的 WPF 窗口(需无参构造函数)。</typeparam>
    public abstract class ShowDialogCommandBase<TWindow> : IExternalCommand
        where TWindow : Window, new()
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Services.DialogService.ShowModal(new TWindow());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("HVACIDA 错误", ex.ToString());
                return Result.Failed;
            }
        }
    }

    /// <summary>打开“项目信息与气象参数”窗口。</summary>
    public class ShowProjectInfoCommand : ShowDialogCommandBase<UI.Views.ProjectInfoWindow>
    {
    }

    /// <summary>打开“大系统负荷计算”窗口。</summary>
    public class ShowLargeSystemCommand : ShowDialogCommandBase<UI.Views.LargeSystemWindow>
    {
    }

    /// <summary>打开“小系统负荷计算”窗口。</summary>
    public class ShowSmallSystemCommand : ShowDialogCommandBase<UI.Views.SmallSystemWindow>
    {
    }
}
