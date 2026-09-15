using System.Windows;
using HVACIDA.UI.ViewModels;

namespace HVACIDA.UI.Views
{
    /// <summary>
    /// 小系统负荷计算窗(Ribbon「小系统」六类按钮共用,系统类型由按钮决定)。
    /// <para>
    /// code-behind 只做三件事:**按 Core 的列定义生成 DataGrid 列**、无参构造、事件转发;
    /// 数值口径、表头文案、勾稽关系全部在 Core(View / ViewModel 都不写死结果)。
    /// </para>
    /// <para>
    /// **模型拾取闭环**(需求 2.2.3.2;与 <see cref="PublicAreaWindow"/> 同构):
    /// 两个拾取按钮只把请求标记置到 ViewModel 上并 <c>Close()</c> 本窗 —— WPF 模态窗会在 Win32 层
    /// 禁用 Owner(Revit 主窗),模态期间模型不可点选,必须让模态循环真正结束;
    /// 关闭后由命令层调 Revit 拾取,再用**同一个 ViewModel** 重开本窗(用户已填内容不丢)。
    /// </para>
    /// </summary>
    public partial class SmallSystemWindow : Window
    {
        public SmallSystemWindow()
        {
            InitializeComponent();
            DataContext = new SmallSystemViewModel();
            ApplyViewModel();
        }

        public SmallSystemWindow(SmallSystemViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            ApplyViewModel();
        }

        /// <summary>【从模型拾取空间…】已请求(本窗已关闭,命令层据此执行拾取)。</summary>
        public bool PickSpacesRequested { get; private set; }

        /// <summary>【拾取墙体求外墙总长…】已请求(本窗已关闭,命令层据此执行拾取)。</summary>
        public bool PickWallRequested { get; private set; }

        /// <summary>标题与两张由 Core 定义列的 DataGrid(录入表 / 房间明细表)在此装配。</summary>
        private void ApplyViewModel()
        {
            var viewModel = DataContext as SmallSystemViewModel;
            if (viewModel == null) return;

            Title = "小系统 — " + viewModel.SystemTypeName + " - HVACIDA";
            SmallSystemColumns.BuildInput(InputGrid, viewModel.Input.SystemType);
            SmallSystemColumns.BuildRoomDetail(RoomDetailGrid, viewModel.Input.SystemType);
        }

        /// <summary>拾取空间:置请求标记后关窗(可多选,由命令层执行 Selection.PickObjects)。</summary>
        private void OnPickSpacesClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as SmallSystemViewModel;
            if (viewModel == null || !viewModel.IsPickAvailable) return;

            viewModel.RequestPickSpaces();
            PickSpacesRequested = true;
            Close();
        }

        /// <summary>拾取墙体求外墙总长:必须先选中房间行,为空时只提示、不关窗。</summary>
        private void OnPickWallClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as SmallSystemViewModel;
            if (viewModel == null || !viewModel.IsPickAvailable) return;

            if (viewModel.SelectedRoom == null)
            {
                viewModel.SetStatus("请先在房间表里选中一行,再点【拾取墙体求外墙总长…】。");
                return;
            }

            viewModel.RequestPickWall();
            PickWallRequested = true;
            Close();
        }
    }
}
