using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace HVACIDA.Revit.Commands
{
    // =========================================================================
    // 小系统面板 7 键(2026-09-11 Ribbon 定稿):
    //   全空气一次回风系统 / 多联机+新风系统 / 排风系统 / 送风排风排烟系统 /
    //   加压送风系统 / 排烟系统 / 计算结果
    //
    // 2026-09-15:**六类系统全部实装** —— 都按《小系统空调负荷、送排风、排烟计算公式.docx》
    // 的公式链计算(见 Core.Services.SmallSystemLoadCalculator),命令统一打开
    // SmallSystemWindow(系统类型由按钮决定,窗内只读显示)。
    // =========================================================================

    /// <summary>
    /// 小系统"按系统类型打开计算窗"命令基类:子类只声明系统类型。
    /// 窗内是"多房间录入 + 结果表格",房间列表与系统参数存 small-system.xml。
    /// </summary>
    public abstract class SmallSystemWindowCommandBase : IExternalCommand
    {
        /// <summary>本按钮对应的系统类型。</summary>
        protected abstract Core.Models.SmallSystemType SystemType { get; }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return CommandHost.Show(
                () =>
                {
                    var viewModel = new UI.ViewModels.SmallSystemViewModel(SystemType);
                    return new UI.Views.SmallSystemWindow(viewModel);
                },
                ref message);
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
