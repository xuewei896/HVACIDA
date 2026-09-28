using System;
using System.Globalization;
using System.Windows.Data;

namespace HVACIDA.UI.Views
{
    /// <summary>
    /// 数值输入框 / 表格单元格的「显示 + 写回」转换器(2026-09-28 用户口径:面积、层高、长度等几何参数
    /// 只保留 1 位小数;此前只改了结果表,录入框仍显示 2 位)。
    /// <para>
    /// <c>Convert</c>:按 <c>ConverterParameter</c> 给的小数位格式化显示(千分位 + 固定小数位);
    /// <c>ConvertBack</c>:把输入解析成数字并按同一小数位**四舍五入**写回 —— **界面看到的位数 == 存进去的位数**,
    /// 不会出现"显示 32.2、算的时候用 32.18"。
    /// </para>
    /// <para>
    /// 解析不了(半截数字 / 非法字符)返回 <see cref="Binding.DoNothing"/>:不抛异常、不改数据(宁可保持原值);
    /// 清空当 0(与直接绑 <c>double</c> 时 TextBox 清空的行为一致)。
    /// </para>
    /// <para>
    /// <strong>用它的绑定必须配 <c>UpdateSourceTrigger=LostFocus</c></strong>(DataGrid 单元格本来就是离开即提交):
    /// 若用 <c>PropertyChanged</c>,每次按键都会回写并重新格式化,输入 "32.18" 会被打断成 "3.0"、"32.0"…
    /// </para>
    /// </summary>
    public sealed class DecimalConverter : IValueConverter
    {
        /// <summary>小数位上限(参数写错时不至于把界面格式化成奇怪的样子)。</summary>
        private const int MaxDecimals = 6;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return "";

            double number;
            try
            {
                number = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return value.ToString();      // 非数值(理论上不会走到)原样显示,不做二次处理
            }

            int decimals = DecimalsOf(parameter);
            return number.ToString(decimals <= 0 ? "N0" : "N" + decimals, culture ?? CultureInfo.CurrentCulture);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var text = value as string;
            if (text == null) return Binding.DoNothing;

            text = text.Trim();
            if (text.Length == 0) return 0d;

            double number;
            const NumberStyles style = NumberStyles.Number | NumberStyles.AllowExponent;
            if (!double.TryParse(text, style, culture ?? CultureInfo.CurrentCulture, out number) &&
                !double.TryParse(text, style, CultureInfo.InvariantCulture, out number))
            {
                return Binding.DoNothing;     // 输入不合法:保持原值,由用户自己改
            }

            return Math.Round(number, DecimalsOf(parameter), MidpointRounding.AwayFromZero);
        }

        /// <summary>读 <c>ConverterParameter</c> 里的小数位;缺省 1 位、负数归 0、过大截到上限。</summary>
        private static int DecimalsOf(object parameter)
        {
            if (parameter == null) return 1;

            int decimals;
            if (!int.TryParse(parameter.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out decimals))
            {
                return 1;
            }

            if (decimals < 0) return 0;
            return decimals > MaxDecimals ? MaxDecimals : decimals;
        }
    }
}
