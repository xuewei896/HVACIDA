using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HVACIDA.UI.ViewModels;

namespace HVACIDA.UI.Views
{
    /// <summary>规范知识库窗(Ribbon「AI问答 → 规范知识库」)。</summary>
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

        /// <summary>点常用问题按钮 → 直接提问。</summary>
        private void OnSampleClick(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as KnowledgeViewModel;
            var button = sender as Button;
            if (vm == null || button == null) return;

            vm.Question = button.Content as string ?? "";
            vm.Ask();
        }

        /// <summary>输入框回车 = 提问。</summary>
        private void OnQuestionKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            var vm = DataContext as KnowledgeViewModel;
            if (vm != null) vm.Ask();
            e.Handled = true;
        }
    }
}
