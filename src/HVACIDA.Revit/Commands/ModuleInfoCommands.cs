using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace HVACIDA.Revit.Commands
{
    // =========================================================================
    // 说明 / 待实现类命令(2026-09-11 Ribbon 定稿)。
    // 统一打开 InfoWindow,内容取自 Core.Services.ModuleCatalog:
    // 写清「已定口径」与「待补项」,不伪装可用、不给假数据。
    // =========================================================================

    /// <summary>说明类命令基类:子类只声明 ModuleCatalog 里的模块键。</summary>
    public abstract class ModuleInfoCommandBase : IExternalCommand
    {
        /// <summary>ModuleCatalog 中的模块键。</summary>
        protected abstract string ModuleKey { get; }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return CommandHost.ShowModuleInfo(ModuleKey, ref message);
        }
    }

    /// <summary>出图 → 明细表 / 材料表统计(待实现)。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowScheduleCommand : ModuleInfoCommandBase
    {
        protected override string ModuleKey => "schedule";
    }

    /// <summary>出图 → 图框 / 图纸与批量出图(待实现)。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowTitleBlockCommand : ModuleInfoCommandBase
    {
        protected override string ModuleKey => "titleblock";
    }

    /// <summary>产品支持 → 问题反馈(待接入反馈服务)。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowFeedbackCommand : ModuleInfoCommandBase
    {
        protected override string ModuleKey => "feedback";
    }
}
