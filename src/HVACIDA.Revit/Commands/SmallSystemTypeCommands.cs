using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace HVACIDA.Revit.Commands
{
    // =========================================================================
    // 小系统面板 7 键(2026-09-11 Ribbon 定稿):
    //   全空气一次回风系统 / 多联机+新风系统 / 排风系统 / 送风排风排烟系统 /
    //   加压送风系统 / 排烟系统 / 计算结果
    // 除"全空气一次回风系统"外均为待实现 —— 打开统一的说明窗(口径 + 待补项),
    // 说明文字取自 Core.Services.ModuleCatalog,与 Ribbon 按钮同源。
    // =========================================================================

    /// <summary>小系统「待实现类型」说明命令基类。</summary>
    public abstract class SmallSystemNotImplementedCommandBase : IExternalCommand
    {
        /// <summary>ModuleCatalog 中的模块键。</summary>
        protected abstract string ModuleKey { get; }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return CommandHost.ShowModuleInfo(ModuleKey, ref message);
        }
    }

    /// <summary>全空气一次回风系统(已实现:打开计算窗口)。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowSmallAllAirCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return CommandHost.Show(
                () =>
                {
                    var viewModel = new UI.ViewModels.SmallSystemViewModel(Core.Models.SmallSystemType.AllAirOnceReturn);
                    return new UI.Views.SmallSystemWindow(viewModel);
                },
                ref message);
        }
    }

    /// <summary>多联机 + 新风系统(待实现)。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowSmallVrfCommand : SmallSystemNotImplementedCommandBase
    {
        protected override string ModuleKey => "small-vrf";
    }

    /// <summary>排风系统(待实现,含环控机房通风与卫生间排风)。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowSmallExhaustCommand : SmallSystemNotImplementedCommandBase
    {
        protected override string ModuleKey => "small-exhaust";
    }

    /// <summary>送风排风排烟系统(待实现)。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowSmallSupplyExhaustSmokeCommand : SmallSystemNotImplementedCommandBase
    {
        protected override string ModuleKey => "small-sesmoke";
    }

    /// <summary>加压送风系统(待实现)。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowSmallPressurizationCommand : SmallSystemNotImplementedCommandBase
    {
        protected override string ModuleKey => "small-press";
    }

    /// <summary>排烟系统(待实现)。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowSmallSmokeCommand : SmallSystemNotImplementedCommandBase
    {
        protected override string ModuleKey => "small-smoke";
    }
}
