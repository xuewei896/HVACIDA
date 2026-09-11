using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace HVACIDA.Revit.Commands
{
    /// <summary>
    /// 小系统「未实现类型」提示命令基类。
    /// 六类小系统已提升为 Ribbon 一级按钮(2026-09-11 评审决定);
    /// 当前仅"全空气一次回风"完成计算链路,其余五类给出待实现说明,不伪装可用。
    /// </summary>
    public abstract class SmallSystemNotImplementedCommandBase : IExternalCommand
    {
        /// <summary>类型中文名(用于提示)。</summary>
        protected abstract string TypeName { get; }

        /// <summary>该类型的计算要点(需求 2.2.3.2)。</summary>
        protected abstract string CalculationNotes { get; }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            TaskDialog.Show(
                "HVACIDA — 小系统负荷计算",
                "「" + TypeName + "」尚未实现(骨架阶段)。" + Environment.NewLine + Environment.NewLine +
                "计算要点:" + Environment.NewLine + CalculationNotes + Environment.NewLine + Environment.NewLine +
                "当前已实现:全空气一次回风系统。");
            return Result.Succeeded;
        }
    }

    /// <summary>全空气一次回风系统(已实现:打开计算窗口)。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowSmallAllAirCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var vm = new UI.ViewModels.SmallSystemViewModel(Core.Models.SmallSystemType.AllAirOnceReturn);
                Services.DialogService.ShowModal(new UI.Views.SmallSystemWindow(vm));
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

    /// <summary>多联机 + 新风系统(待实现)。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowSmallVrfCommand : SmallSystemNotImplementedCommandBase
    {
        protected override string TypeName => "多联机 + 新风系统";
        protected override string CalculationNotes =>
            "夏季/过渡季温度、空间物理参数、负荷指标、人员数量、换气次数 → 照明/人员/设备/新风冷负荷 → " +
            "消除余热通风量与换气次数通风量 → 新风机组、送风机、排风机、多联机外机选型。";
    }

    /// <summary>排风系统(待实现,含环控机房通风与卫生间排风)。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowSmallExhaustCommand : SmallSystemNotImplementedCommandBase
    {
        protected override string TypeName => "排风系统";
        protected override string CalculationNotes =>
            "按换气次数确定通风量;子类型:环控机房通风系统、卫生间排风系统(分别取换气次数指标)。";
    }

    /// <summary>排烟系统(待实现)。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowSmallSmokeCommand : SmallSystemNotImplementedCommandBase
    {
        protected override string TypeName => "排烟系统";
        protected override string CalculationNotes =>
            "按防烟分区面积 × 计算换气次数(60 倍/小时)得计算风量;风机选型风量 = 计算风量 × 1.2。";
    }

    /// <summary>送风排风排烟系统(待实现)。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowSmallSupplyExhaustSmokeCommand : SmallSystemNotImplementedCommandBase
    {
        protected override string TypeName => "送风排风排烟系统";
        protected override string CalculationNotes =>
            "送风/排风/排烟共用系统:需风量叠加、工况切换(空调/通风/火灾)与阀门联锁逻辑。";
    }

    /// <summary>加压送风系统(待实现)。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowSmallPressurizationCommand : SmallSystemNotImplementedCommandBase
    {
        protected override string TypeName => "加压送风系统";
        protected override string CalculationNotes =>
            "楼梯间/前室加压送风量:按规范查表取值,并进行门洞风速与余压校核。";
    }
}
