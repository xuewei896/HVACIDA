using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HVACIDA.Core.Services;

namespace HVACIDA.UI.Views
{
    /// <summary>
    /// 计算结果表格视图(可复用):绑定一份 <see cref="ResultTable"/>,按分区渲染成多张表。
    /// <para>
    /// 大系统负荷计算窗、大系统计算结果窗、小系统计算结果窗共用本控件 —— 结果呈现形式统一,
    /// 且与导出的计算书同源(都由 <see cref="ResultTable"/> 定义)。
    /// </para>
    /// </summary>
    public partial class ResultTableView : UserControl
    {
        /// <summary>要展示的结果表。</summary>
        public static readonly DependencyProperty TableProperty =
            DependencyProperty.Register("Table", typeof(ResultTable), typeof(ResultTableView),
                new PropertyMetadata(null));

        public ResultTableView()
        {
            InitializeComponent();
        }

        /// <summary>结果表(分区 + 指标行)。</summary>
        public ResultTable Table
        {
            get => (ResultTable)GetValue(TableProperty);
            set => SetValue(TableProperty, value);
        }

        /// <summary>
        /// 滚轮转发:结果区由多张 <c>DataGrid</c> 拼成,而 DataGrid 内部自带 ScrollViewer、
        /// 会把 <c>MouseWheel</c> 吃掉(标记 Handled),表现为"鼠标停在表格上时结果区滚不动"。
        /// 这里在最外层 ScrollViewer 上直接滚动,保证滚轮在整个结果区任何位置都有效。
        /// </summary>
        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (TableScroll == null || e.Delta == 0) return;

            TableScroll.ScrollToVerticalOffset(TableScroll.VerticalOffset - e.Delta);
            e.Handled = true;
        }
    }
}
