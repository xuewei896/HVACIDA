using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HVACIDA.Revit.Services;

namespace HVACIDA.Revit.Commands
{
    /// <summary>
    /// 「AI问答 → AI助手」:显示右侧的 AI 助手停靠面板(再点一次收起 —— 承参考文档的 Toggle)。
    /// <para>
    /// 面板是**停靠面板**而不是模态窗:打开后仍然可以正常操作模型(这也是参考文档选它的理由)。
    /// </para>
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class ShowAiAssistantCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData == null ? null : commandData.Application;
                DockablePane pane = uiApp.GetDockablePane(AiPaneProvider.PaneId);

                // 每次打开都按当前文档/命令集刷新一次(工作区、命令数、开关状态都可能变)
                if (App.PaneProvider != null && App.PaneProvider.Panel != null)
                {
                    App.PaneProvider.Panel.RefreshOnShow();
                }

                pane.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("HVACIDA AI 助手", "打开 AI 助手面板失败:" + ex.Message);
                return Result.Failed;
            }
        }
    }
}
