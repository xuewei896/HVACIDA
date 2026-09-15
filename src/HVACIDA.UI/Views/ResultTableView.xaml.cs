using System.Windows;
using System.Windows.Controls;
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
    }
}
