using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;
using HVACIDA.UI.Views;

namespace HVACIDA.Revit.Commands
{
    // =========================================================================
    // 水力计算 3 键(需求 2.3 风系统 / 2.4 水系统):
    //   风系统 / 水系统 / 计算结果
    //
    // 2026-09-15 实装(此前为"待实现说明窗")。
    // 本文件承载**选系统 → 读管网 → 算最不利环路**的闭环,与「小系统:拾取空间/墙体」同一套路:
    //   WPF 模态窗会在 Win32 层禁用 Revit 主窗,所以必须先关窗,再由命令层在模型里拾取,
    //   然后用**同一个 ViewModel** 重开窗(用户已改的系数/参数不丢)。循环设上限防死循环。
    // =========================================================================

    /// <summary>
    /// 水力计算窗命令基类:子类只声明介质(风 / 水)。
    /// 拾取→读取→重算→重开窗的闭环在这里;读管网的活交给
    /// <see cref="Services.RevitHydraulicReader"/>。
    /// </summary>
    public abstract class HydraulicWindowCommandBase : IExternalCommand
    {
        private const int MaxPickRounds = 8;

        /// <summary>本按钮对应的介质。</summary>
        protected abstract HydraulicKind Kind { get; }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uidoc = commandData?.Application?.ActiveUIDocument;
                var doc = uidoc == null ? null : uidoc.Document;
                bool pickAvailable = uidoc != null && doc != null;

                var repository = new XmlProjectRepository();
                var coefficients = new HydraulicInputService(repository).LoadCoefficients();
                var viewModel = new UI.ViewModels.HydraulicSystemViewModel(Kind, repository, pickAvailable);

                for (int round = 0; round < MaxPickRounds; round++)
                {
                    var window = new HydraulicSystemWindow(viewModel);
                    Services.DialogService.ShowModal(window);

                    if (!window.PickRequested) break;

                    viewModel.ClearPickRequest();
                    string pickNote;
                    var pickedIds = Services.RevitHydraulicReader.PickSystemMembers(uidoc, Kind, out pickNote);
                    if (pickedIds == null || pickedIds.Count == 0)
                    {
                        viewModel.SetStatus("已取消拾取(" + (pickNote ?? "") + ");管网数据未变。");
                        continue;
                    }

                    string readNote;
                    var input = Services.RevitHydraulicReader.ReadSystem(doc, pickedIds, Kind, coefficients, out readNote);
                    if (input == null)
                    {
                        viewModel.SetStatus("没有读到系统数据:" + (readNote ?? ""));
                        continue;
                    }
                    viewModel.ApplyPickedSystem(input, readNote);
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

    /// <summary>水力计算 → 风系统(需求 2.3):读风管管网,算最不利环路与需求风机全压。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowAirHydraulicCommand : HydraulicWindowCommandBase
    {
        protected override HydraulicKind Kind => HydraulicKind.AirDuct;
    }

    /// <summary>水力计算 → 水系统(需求 2.4):读水管管网,算最不利环路与需求水泵扬程。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowWaterHydraulicCommand : HydraulicWindowCommandBase
    {
        protected override HydraulicKind Kind => HydraulicKind.WaterPipe;
    }

    /// <summary>水力计算 → 计算结果:风 / 水两系统汇总结论(打开即算,不需要再点一次计算)。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowHydraulicResultCommand : ShowDialogCommandBase<UI.Views.HydraulicResultWindow>
    {
    }
}
