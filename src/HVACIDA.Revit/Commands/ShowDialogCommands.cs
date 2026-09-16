using System;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace HVACIDA.Revit.Commands
{
    /// <summary>
    /// 命令基类:模态显示一个 WPF 窗口(需无参构造函数)。
    /// 在 IExternalCommand.Execute(Revit 主线程)内同步显示,不触碰文档事务。
    /// </summary>
    /// <typeparam name="TWindow">要显示的 WPF 窗口(需无参构造函数)。</typeparam>
    public abstract class ShowDialogCommandBase<TWindow> : IExternalCommand
        where TWindow : Window, new()
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return CommandHost.Show(() => new TWindow(), ref message);
        }
    }

    // =========================================================================
    // Ribbon 定稿(2026-09-11):7 面板 / 22 PushButton。
    // 本文件承载"直接打开功能窗"的命令;查看/说明类命令见 ModuleInfoCommands.cs。
    // 注意:Revit 2020 要求每个 IExternalCommand 标注 [Transaction];命令只弹窗/读写
    //       HVACIDA 自己的 XML 数据,不动模型 → 统一 Manual。
    // =========================================================================

    /// <summary>项目信息 → 工程信息(需求 2.1.1)。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowEngineeringInfoCommand : ShowDialogCommandBase<UI.Views.ProjectInfoWindow>
    {
    }

    /// <summary>项目信息 → 气象参数(需求 2.1.2)。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowWeatherCommand : ShowDialogCommandBase<UI.Views.WeatherWindow>
    {
    }

    /// <summary>
    /// 大系统 → 公共区参数(需求 2.2.3.1 用户输入:几何 + 高峰客流)。
    /// 该命令需要"模型空间取值 + 拾取"闭环,见 <see cref="ShowPublicAreaCommand"/>。
    /// </summary>

    /// <summary>大系统 → 负荷计算(需求 2.2.3.1)。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowLargeSystemCommand : ShowDialogCommandBase<UI.Views.LargeSystemWindow>
    {
    }

    /// <summary>大系统 → 排烟计算(需求 2.2.3.1;结果以表格呈现)。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowLargeSmokeCommand : ShowDialogCommandBase<UI.Views.LargeSmokeWindow>
    {
    }

    /// <summary>大系统 → 计算结果。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowLargeResultCommand : ShowDialogCommandBase<UI.Views.LargeSystemResultWindow>
    {
    }

    /// <summary>小系统 → 计算结果。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowSmallResultCommand : ShowDialogCommandBase<UI.Views.SmallSystemResultWindow>
    {
    }

    /// <summary>AI问答 → 规范知识库(需求 2.7;当前为本地规则应答)。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowKnowledgeCommand : ShowDialogCommandBase<UI.Views.KnowledgeWindow>
    {
    }

    /// <summary>
    /// AI问答 → 操作指南:**Revit 软件操作指南**(不只本插件),按分组浏览 + 关键词检索。
    /// 打开可检索的知识库窗并定位到「操作步骤」分类(内容取自 Core 的 <c>RevitOperationGuide</c>)。
    /// </summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowGuideCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return CommandHost.Show(
                () => new UI.Views.KnowledgeWindow(new UI.ViewModels.KnowledgeViewModel(null, "操作步骤")),
                ref message);
        }
    }

    /// <summary>产品支持 → 帮助 / 关于。</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowHelpCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return CommandHost.Show(() => new UI.Views.InfoWindow(UI.ViewModels.InfoViewModel.Help()), ref message);
        }
    }
}
