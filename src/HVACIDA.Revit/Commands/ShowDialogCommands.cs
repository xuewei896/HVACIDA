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
    /// <remarks>Revit 2020 要求每个 IExternalCommand 必须标注 [Transaction];本项目命令只弹窗/读模型,
    /// 采用 Manual——Revit 不自动开事务,未来需写回模型时可自行 Start/Commit。</remarks>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowProjectInfoCommand : ShowDialogCommandBase<UI.Views.ProjectInfoWindow>
    {
    }

    /// <summary>打开“大系统负荷计算”窗口。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowLargeSystemCommand : ShowDialogCommandBase<UI.Views.LargeSystemWindow>
    {
    }
}
