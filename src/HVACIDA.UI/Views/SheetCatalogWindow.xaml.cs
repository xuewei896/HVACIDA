using System.ComponentModel;
using System.Windows;
using HVACIDA.UI.ViewModels;

namespace HVACIDA.UI.Views
{
    /// <summary>
    /// 出图 → 图框(图纸清单与批量出图,需求 2.6)窗。
    /// code-behind 只做无参构造与**请求转发**:读模型 / 批量出图都要 Revit API,而模态窗会禁用 Revit 主窗,
    /// 故按既有闭环:置标记 → 关窗 → 命令层执行 → 用同一 ViewModel 重开窗(导出记录回注到界面)。
    /// </summary>
    public partial class SheetCatalogWindow : Window
    {
        public SheetCatalogWindow()
        {
            InitializeComponent();
            DataContext = new SheetCatalogViewModel();
        }

        public SheetCatalogWindow(SheetCatalogViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        /// <summary>【重新读取图纸】已请求。</summary>
        public bool ReloadRequested { get; private set; }

        /// <summary>【导出 DWG/DXF/PDF】已请求(格式见 <see cref="PendingFormat"/>)。</summary>
        public bool ExportRequested { get; private set; }

        /// <summary>本次请求的导出格式。</summary>
        public string PendingFormat { get; private set; } = "";

        /// <summary>【批量标注空间】已请求。</summary>
        public bool TagRequested { get; private set; }

        protected override void OnClosing(CancelEventArgs e)
        {
            var viewModel = DataContext as SheetCatalogViewModel;
            if (viewModel != null)
            {
                if (viewModel.ReloadRequested) ReloadRequested = true;
                if (viewModel.ExportRequested)
                {
                    ExportRequested = true;
                    PendingFormat = viewModel.PendingFormat;
                }
                if (viewModel.TagRequested) TagRequested = true;
            }
            base.OnClosing(e);
        }
    }
}
