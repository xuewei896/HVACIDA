using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace HVACIDA.Revit.Commands
{
    // =========================================================================
    // 出图面板(需求 2.5 材料表统计 / 2.6 图纸与批量出图)。
    // 2026-09-16:先实装「明细表」(材料表统计);「图框」仍在后续阶段。
    // =========================================================================

    /// <summary>
    /// 出图 → 明细表(**材料表统计**,需求 2.5):
    /// 打开窗前先读一遍模型(只读,不开事务),把构件清单交给 ViewModel;
    /// 窗内【重新读取模型】走"关窗 → 命令层再读 → 同一 ViewModel 重开窗"的既有闭环
    /// (模态窗会禁用 Revit 主窗,读模型虽不需要交互,但保持与其它窗一致的刷新路径)。
    /// </summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowScheduleCommand : IExternalCommand
    {
        private const int MaxRounds = 4;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uidoc = commandData?.Application?.ActiveUIDocument;
                var doc = uidoc == null ? null : uidoc.Document;
                var viewModel = new UI.ViewModels.MaterialTakeoffViewModel();

                if (doc == null)
                {
                    viewModel.ApplyTakeoff(null, "没有活动文档,无法统计材料表。");
                    Services.DialogService.ShowModal(new UI.Views.MaterialTakeoffWindow(viewModel));
                    return Result.Succeeded;
                }

                for (int round = 0; round < MaxRounds; round++)
                {
                    if (round == 0 || viewModel.ReloadRequested)
                    {
                        viewModel.ClearReloadRequest();
                        string note;
                        var items = Services.RevitMaterialTakeoffReader.Read(doc, out note);
                        viewModel.ApplyTakeoff(items, note);
                    }

                    var window = new UI.Views.MaterialTakeoffWindow(viewModel);
                    Services.DialogService.ShowModal(window);
                    if (!window.ReloadRequested) break;
                }

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
}
