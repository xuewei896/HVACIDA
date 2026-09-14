using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;
using HVACIDA.UI.Views;

namespace HVACIDA.Revit.Commands
{
    /// <summary>
    /// 大系统 → 公共区参数(需求 2.2.3.1:公共区几何由模型空间获取 + 高峰客流手工输入)。
    /// <para>
    /// 与其它"直接弹窗"命令不同,本命令承担模型取值闭环:
    /// <list type="number">
    ///   <item>先在 <c>Execute</c> 里把全模型空间读成快照(只读,无需事务),注入 ViewModel;</item>
    ///   <item>模态显示窗口;窗口内的【自动识别全模型空间】只操作内存快照(不碰 Revit API);</item>
    ///   <item>点【拾取站厅/站台空间…】时窗口<strong>关闭</strong>并置标记 —— 只有这样才结束模态循环、
    ///         解除对 Revit 主窗的 Win32 禁用,模型才可点选;</item>
    ///   <item>确认没有模态窗后调用 <c>Selection.PickObjects</c>,再把结果回填到<strong>同一个 ViewModel</strong>
    ///         重新开窗(用户已填的客流等不丢);最多循环 5 次,避免异常情况下死循环。</item>
    /// </list>
    /// </para>
    /// </summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowPublicAreaCommand : IExternalCommand
    {
        private const int MaxPickRounds = 5;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // UIDocument 必须写全限定名:RevitAPIUI.dll 内还有一个全局命名空间下的 internal UIDocument
                Autodesk.Revit.UI.UIDocument uidoc = commandData?.Application?.ActiveUIDocument;
                Document doc = uidoc?.Document;

                string readNote;
                IList<SpaceSnapshot> spaces = Services.RevitSpaceReader.ReadAll(doc, out readNote);

                var viewModel = new UI.ViewModels.PublicAreaViewModel(
                    new XmlProjectRepository(), spaces, readNote, uidoc != null);

                for (int round = 0; round < MaxPickRounds; round++)
                {
                    var window = new PublicAreaWindow(viewModel);
                    Services.DialogService.ShowModal(window);

                    if (!window.PickRequested) break;   // 正常关闭/确定/Esc 都走这里

                    PublicAreaTarget target = viewModel.PendingPickTarget ?? PublicAreaTarget.Hall;
                    viewModel.ClearPendingPick();

                    if (uidoc == null)
                    {
                        viewModel.SetStatus("没有活动文档,无法拾取空间。");
                        continue;
                    }

                    string pickNote;
                    IList<SpaceSnapshot> picked = Services.RevitSpaceReader.PickMany(uidoc, target, out pickNote);
                    if (picked == null || picked.Count == 0)
                    {
                        viewModel.SetStatus("已取消拾取(未选择空间),几何保持原值。");
                        continue;
                    }

                    viewModel.ApplyPick(target, picked, pickNote);
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
