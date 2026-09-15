using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HVACIDA.Core.Models;
using HVACIDA.UI.Views;

namespace HVACIDA.Revit.Commands
{
    // =========================================================================
    // 小系统面板 7 键(2026-09-11 Ribbon 定稿):
    //   全空气一次回风系统 / 多联机+新风系统 / 排风系统 / 送风排风排烟系统 /
    //   加压送风系统 / 排烟系统 / 计算结果
    //
    // 2026-09-15:六类系统全部实装(公式源《小系统空调负荷、送排风、排烟计算公式.docx》)。
    // 本文件承载**模型拾取闭环**(需求 2.2.3.2):
    //   · 拾取空间 → 建房间列表(面积/层高/屋顶面积由模型给出);
    //   · 拾取墙体 → 与土壤接触外墙长度取所选墙体长度之和。
    // 拾取规则与「公共区参数」窗一致:WPF 模态窗会在 Win32 层禁用 Revit 主窗,
    // 故必须"关窗 → 命令层拾取 → 用同一 ViewModel 重开窗",循环设上限防死循环。
    // =========================================================================

    /// <summary>
    /// 小系统"按系统类型打开计算窗"命令基类:子类只声明系统类型。
    /// 窗内是"多房间录入 + 三张结果表",房间列表与系统参数按"类型+编号"存进小系统工程。
    /// </summary>
    public abstract class SmallSystemWindowCommandBase : IExternalCommand
    {
        private const int MaxPickRounds = 8;

        /// <summary>本按钮对应的系统类型。</summary>
        protected abstract Core.Models.SmallSystemType SystemType { get; }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uidoc = commandData?.Application?.ActiveUIDocument;
                bool pickAvailable = uidoc != null;

                // 仓库交给 VM:它自己按"系统类型 + 编号"取输入(室外参数自动回填),编号可在窗内填写
                var repository = new Core.Services.XmlProjectRepository();
                var viewModel = new UI.ViewModels.SmallSystemViewModel(SystemType, repository, pickAvailable);

                for (int round = 0; round < MaxPickRounds; round++)
                {
                    var window = new SmallSystemWindow(viewModel);
                    Services.DialogService.ShowModal(window);

                    if (!window.PickSpacesRequested && !window.PickWallRequested) break;

                    if (window.PickSpacesRequested)
                    {
                        viewModel.ClearPickRequests();
                        string note;
                        var spaces = Services.RevitSpaceReader.PickSpaces(uidoc,
                            "请选择本系统负责的房间空间(可多选,回车结束;Esc 取消)", "房间", out note);
                        if (spaces == null || spaces.Count == 0)
                        {
                            viewModel.SetStatus("已取消拾取空间;房间列表未变。");
                            continue;
                        }
                        viewModel.ApplyPickedSpaces(spaces, note);
                        continue;
                    }

                    // 拾取墙体求外墙总长:必须先选中房间行(窗内已判空,这里再兜一层)
                    viewModel.ClearPickRequests();
                    string wallNote;
                    double? totalLength = Services.RevitElementPicker.PickWallLengthMeters(uidoc, out wallNote);
                    if (!totalLength.HasValue)
                    {
                        viewModel.SetStatus("已取消拾取墙体;外墙长度未变。");
                        continue;
                    }
                    viewModel.ApplyPickedWallLength(totalLength.Value, wallNote);
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

    /// <summary>全空气一次回风系统(多个弱电/强电房间共用一台柜式空调机组)。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowSmallAllAirCommand : SmallSystemWindowCommandBase
    {
        protected override Core.Models.SmallSystemType SystemType =>
            Core.Models.SmallSystemType.AllAirOnceReturn;
    }

    /// <summary>多联机 + 新风系统(多个人员房间)。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowSmallVrfCommand : SmallSystemWindowCommandBase
    {
        protected override Core.Models.SmallSystemType SystemType =>
            Core.Models.SmallSystemType.VrfWithFreshAir;
    }

    /// <summary>排风系统(卫生间、泵房等,多房间排风)。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowSmallExhaustCommand : SmallSystemWindowCommandBase
    {
        protected override Core.Models.SmallSystemType SystemType =>
            Core.Models.SmallSystemType.ExhaustVentilation;
    }

    /// <summary>送风排风排烟系统(环控机房 + 气瓶间)。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowSmallSupplyExhaustSmokeCommand : SmallSystemWindowCommandBase
    {
        protected override Core.Models.SmallSystemType SystemType =>
            Core.Models.SmallSystemType.SupplyExhaustSmoke;
    }

    /// <summary>加压送风系统(楼梯间)。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowSmallPressurizationCommand : SmallSystemWindowCommandBase
    {
        protected override Core.Models.SmallSystemType SystemType =>
            Core.Models.SmallSystemType.PressurizationSupply;
    }

    /// <summary>排烟系统(多个防烟分区排烟 + 补风)。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowSmallSmokeCommand : SmallSystemWindowCommandBase
    {
        protected override Core.Models.SmallSystemType SystemType =>
            Core.Models.SmallSystemType.SmokeExhaust;
    }
}
