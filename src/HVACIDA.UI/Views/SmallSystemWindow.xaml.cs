using System;
using System.Windows;
using System.Windows.Controls;
using HVACIDA.UI.ViewModels;

namespace HVACIDA.UI.Views
{
    /// <summary>
    /// 小系统负荷计算窗(Ribbon「小系统」六类按钮共用,系统类型由按钮决定)。
    /// <para>
    /// 2026-09-20 用户口径:**一个模型会有多套系统** —— 本窗按「系统编号1、2、3…」纵向排列,
    /// 每套列出本系统房间参数并在最下行给出合计(总送风量 / 总回风量 / 总制冷量);
    /// 「计算参数」全窗共用(不再有「系统编号」输入框,编号按窗口内顺序自动生成)。
    /// </para>
    /// <para>
    /// code-behind 只做三件事:**按 Core 的列定义给每套系统的 DataGrid 生成列**、无参构造、事件转发;
    /// 数值口径、表头文案、勾稽关系全部在 Core(View / ViewModel 都不写死结果)。
    /// </para>
    /// <para>
    /// **模型拾取闭环**(需求 2.2.3.2;与 <see cref="PublicAreaWindow"/> 同构):
    /// 两个拾取按钮先把该按钮所在系统置为当前系统,再置请求标记并 <c>Close()</c> 本窗 —— WPF 模态窗会在 Win32 层
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

        private void ApplyViewModel()
        {
            var viewModel = DataContext as SmallSystemViewModel;
            if (viewModel == null) return;

            Title = "小系统 — " + viewModel.SystemTypeName + " - HVACIDA";
        }

        /// <summary>每套系统的房间录入表列由 Core 定义(按系统类型取舍),在 DataGrid 装载时装配一次。</summary>
        private void OnRoomGridLoaded(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as SmallSystemViewModel;
            var grid = sender as DataGrid;
            if (viewModel == null || grid == null) return;
            if (grid.Columns.Count > 0) return;                 // 模板复用时会重复触发,装一次即可

            if (string.Equals(grid.Name, "MergedRoomsGrid", StringComparison.Ordinal))
            {
                // 全空气一次回风:参考表 23 列(前段可编辑、后段自动算)
                SmallSystemColumns.BuildReference(grid);
                return;
            }

            SmallSystemColumns.BuildInput(grid, viewModel.Input.SystemType);
        }

        /// <summary>【确 定】= 保存全部系统并关闭;保存失败/没内容时不关窗,状态栏给出原因。</summary>
        private void OnConfirmClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as SmallSystemViewModel;
            if (viewModel == null)
            {
                Close();
                return;
            }

            if (viewModel.TrySaveAll()) Close();
        }

        /// <summary>拾取空间:把该按钮所在的系统置为当前系统,置请求标记后关窗(可多选,由命令层执行 Selection.PickObjects)。</summary>
        private void OnPickSpacesClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as SmallSystemViewModel;
            if (viewModel == null || !viewModel.IsPickAvailable) return;

            SelectBlockOf(sender, viewModel);
            viewModel.RequestPickSpaces();
            PickSpacesRequested = true;
            Close();
        }

        /// <summary>拾取墙体求外墙总长:必须先在该系统的房间表里选中房间行,为空时只提示、不关窗。</summary>
        private void OnPickWallClick(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as SmallSystemViewModel;
            if (viewModel == null || !viewModel.IsPickAvailable) return;

            SelectBlockOf(sender, viewModel);
            if (viewModel.SelectedRoom == null)
            {
                viewModel.SetStatus("请先在房间表里选中一行,再点【拾取墙体求外墙总长…】。");
                return;
            }

            viewModel.RequestPickWall();
            PickWallRequested = true;
            Close();
        }

        /// <summary>把按钮所属的系统块设为当前系统(按钮在系统的 DataTemplate 里,DataContext 即该块)。</summary>
        private static void SelectBlockOf(object sender, SmallSystemViewModel viewModel)
        {
            var element = sender as FrameworkElement;
            var block = element == null ? null : element.DataContext as SmallSystemBlockViewModel;
            if (block != null) viewModel.SelectedSystem = block;
        }
    }
}
