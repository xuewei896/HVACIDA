using System.Windows;
using HVACIDA.UI.ViewModels;

namespace HVACIDA.UI.Views
{
    /// <summary>
    /// 出图 → 明细表(材料表统计,需求 2.5)窗。
    /// <para>
    /// code-behind 只做两件事:无参构造(给自检用)与**重新读取请求转发** ——
    /// 读模型要走 Revit API,而 WPF 模态窗会禁用 Revit 主窗,故按既有闭环:
    /// 置标记 → 关窗 → 命令层读模型 → 用同一 ViewModel 重开窗。
    /// </para>
    /// </summary>
    public partial class MaterialTakeoffWindow : Window
    {
        public MaterialTakeoffWindow()
        {
            InitializeComponent();
            DataContext = new MaterialTakeoffViewModel();
        }

        public MaterialTakeoffWindow(MaterialTakeoffViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        /// <summary>【重新读取模型】已请求(本窗已关闭,命令层据此读模型)。</summary>
        public bool ReloadRequested { get; private set; }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            var viewModel = DataContext as MaterialTakeoffViewModel;
            if (viewModel != null && viewModel.ReloadRequested) ReloadRequested = true;
            base.OnClosing(e);
        }
    }
}
