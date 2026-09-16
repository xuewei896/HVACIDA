using System.Windows;
using System.Windows.Controls;
using HVACIDA.UI.ViewModels;

namespace HVACIDA.UI.Views
{
    /// <summary>规范 / 口径知识库窗(Ribbon「AI问答 → 规范知识库」;需求 2.7)。</summary>
    public partial class KnowledgeWindow : Window
    {
        public KnowledgeWindow()
        {
            InitializeComponent();
            DataContext = new KnowledgeViewModel();
        }

        public KnowledgeWindow(KnowledgeViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        /// <summary>点「典型问法」按钮 → 填进问题框并直接提问。</summary>
        private void OnSampleClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as KnowledgeViewModel;
            var button = sender as Button;
            if (viewModel == null || button == null) return;

            viewModel.Question = button.Content as string ?? "";
            viewModel.Ask();
        }

        /// <summary>切换分类 → 过滤条目(取消 ComboBox 的选中提示,只做命令转发)。</summary>
        private void OnCategoryChanged(object sender, SelectionChangedEventArgs e)
        {
            var viewModel = DataContext as KnowledgeViewModel;
            var combo = sender as ComboBox;
            if (viewModel == null || combo == null) return;

            string category = combo.SelectedItem as string;
            if (category == null) return;
            viewModel.PendingCategory = category;
            if (viewModel.CategoryCommand != null && viewModel.CategoryCommand.CanExecute(null))
                viewModel.CategoryCommand.Execute(null);
        }
    }
}
