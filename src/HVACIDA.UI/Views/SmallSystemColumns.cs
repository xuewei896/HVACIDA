using System;
using System.Windows.Controls;
using System.Windows.Data;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.UI.Views
{
    /// <summary>
    /// 小系统各窗的 DataGrid 列生成器。
    /// <para>
    /// **列定义(表头 / 顺序 / 宽度 / 小数位)一律取自 Core 的 <see cref="SmallRoomTable"/>**
    /// (<c>InputColumnsFor</c> = 房间录入列,<c>ColumnsFor</c> = 房间明细结果列);
    /// 界面只负责把 <see cref="RoomColumn"/> 翻译成 WPF 列,不自己写死表头,避免"窗口改了、计算书没改"。
    /// </para>
    /// </summary>
    internal static class SmallSystemColumns
    {
        /// <summary>房间/分区**录入**表列(可编辑;bool 用复选框,其余用文本框)。</summary>
        internal static void BuildInput(DataGrid grid, SmallSystemType type)
        {
            if (grid == null) return;
            grid.Columns.Clear();

            foreach (var column in SmallRoomTable.InputColumnsFor(type))
            {
                var binding = new Binding(column.Property);

                if (string.Equals(column.Kind, "bool", StringComparison.OrdinalIgnoreCase))
                {
                    binding.UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged;
                    grid.Columns.Add(new DataGridCheckBoxColumn
                    {
                        Header = column.Header,
                        Width = column.Width,
                        Binding = binding
                    });
                    continue;
                }

                if (string.Equals(column.Kind, "text", StringComparison.OrdinalIgnoreCase))
                {
                    binding.UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged;
                }
                else
                {
                    ApplyNumberFormat(binding, column);      // 录入的数值:按 Core 小数位显示 + 写回取整
                }

                grid.Columns.Add(new DataGridTextColumn
                {
                    Header = column.Header,
                    Width = column.Width,
                    Binding = binding
                });
            }
        }

        /// <summary>
        /// 「全空气一次回风」**参考表列**(23 列:前段可编辑、后段只读);
        /// 列定义来自 Core 的 <see cref="SmallRoomTable.ReferenceColumnsForAllAir"/>。
        /// </summary>
        internal static void BuildReference(DataGrid grid)
        {
            if (grid == null) return;
            grid.Columns.Clear();

            foreach (var column in SmallRoomTable.ReferenceColumnsForAllAir())
            {
                var binding = new Binding(column.Property);
                if (column.IsEditable)
                {
                    if (string.Equals(column.Kind, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        binding.UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged;
                    }
                    else
                    {
                        ApplyNumberFormat(binding, column);   // 录入的数值(面积/层高/外墙长度/屋顶面积…)
                    }
                }
                else if (!string.Equals(column.Kind, "text", StringComparison.OrdinalIgnoreCase))
                {
                    binding.StringFormat = NumberFormat(column.Decimals);      // 只读列:按 Core 小数位显示
                }

                grid.Columns.Add(new DataGridTextColumn
                {
                    Header = column.Header,
                    Width = column.Width,
                    IsReadOnly = !column.IsEditable,
                    Binding = binding
                });
            }
        }

        /// <summary>
        /// 可编辑数值列的绑定口径(2026-09-28 用户口径「面积、层高、长度只保留 1 位小数」):
        /// 用 <see cref="DecimalConverter"/> 按 Core 给的小数位**显示**并在**写回时四舍五入**,
        /// 使"界面看到的位数 == 算的时候用的位数";触发方式必须是 LostFocus(见转换器注释:PropertyChanged 会打断输入)。
        /// </summary>
        private static void ApplyNumberFormat(Binding binding, RoomColumn column)
        {
            binding.Converter = DecimalConverter;
            binding.ConverterParameter = column.Decimals.ToString();
            binding.UpdateSourceTrigger = UpdateSourceTrigger.LostFocus;
        }

        /// <summary>转换器实例(无状态,全窗共用一个即可)。</summary>
        private static readonly DecimalConverter DecimalConverter = new DecimalConverter();

        /// <summary>房间**明细(结果)**表列(只读;数值按 Core 给的小数位显示)。</summary>
        internal static void BuildRoomDetail(DataGrid grid, SmallSystemType type)
        {
            if (grid == null) return;
            grid.Columns.Clear();

            foreach (var column in SmallRoomTable.ColumnsFor(type))
            {
                grid.Columns.Add(new DataGridTextColumn
                {
                    Header = column.Header,
                    Width = column.Width,
                    Binding = new Binding(column.Property)
                    {
                        Mode = BindingMode.OneWay,
                        StringFormat = NumberFormat(column.Decimals)
                    }
                });
            }
        }

        /// <summary>小数值的显示格式(千分位 + 固定小数位;文本属性不受影响)。</summary>
        private static string NumberFormat(int decimals)
        {
            return decimals <= 0 ? "{0:N0}" : "{0:N" + decimals.ToString() + "}";
        }

        /// <summary>
        /// 全站汇总表列(逐系统一行;列由 Core 的 <c>SmallRoomTable.SummaryColumns</c> 生成 = 13 列)。
        /// 数据源是显示用行对象(<c>SmallSummaryRowView</c>),数值列已由它把 0 转成「—」,
        /// 故这里不再设 StringFormat(否则千分位与「—」会互相打架)。
        /// </summary>
        internal static void BuildSummary(DataGrid grid)
        {
            if (grid == null) return;
            grid.Columns.Clear();

            foreach (var column in SmallRoomTable.SummaryColumns())
            {
                grid.Columns.Add(new DataGridTextColumn
                {
                    Header = column.Header,
                    Width = column.Width,
                    Binding = new Binding(column.Property) { Mode = BindingMode.OneWay }
                });
            }
        }
    }
}
