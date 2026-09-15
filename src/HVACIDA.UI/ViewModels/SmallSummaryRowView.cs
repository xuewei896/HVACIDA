using System;
using System.Globalization;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.UI.ViewModels
{
    /// <summary>
    /// 小系统**全站汇总表**的一行(界面显示用)。
    /// <para>
    /// 为什么不直接把 Core 的 <see cref="SmallSystemSummaryRow"/> 交给 DataGrid:各系统类型吃到的量不同
    /// (排风系统没有冷负荷、空调系统没有排烟量…),汇总表里"该系统不涉及"的列全是 0,
    /// 直接绑定会铺满 0.00 / 0,读不出"不涉及"的意思。本类按
    /// <see cref="SmallRoomTable.SummaryColumns"/> 的列名逐一同名暴露属性,但把数值列先转成**显示文本**:
    /// 0 一律显示「—」(不涉及),非 0 按千分位 + Core 给的小数位显示。
    /// </para>
    /// <para>
    /// 因此汇总表的表头 / 顺序 / 宽度仍然只由 Core 的列定义决定(界面只做显示转换,不写死口径);
    /// 选中某一行后,可经 <see cref="System"/> 取回该项目里那一套 <see cref="SmallSystemInput"/> 现场重算,
    /// 得到结构化的房间明细表与设备选型表。
    /// </para>
    /// </summary>
    public class SmallSummaryRowView
    {
        /// <summary>零值的显示文本(不涉及)。</summary>
        private const string Dash = "—";

        private readonly SmallSystemSummaryRow _row;
        private readonly SmallSystemInput _system;

        public SmallSummaryRowView(SmallSystemSummaryRow row)
            : this(row, null)
        {
        }

        public SmallSummaryRowView(SmallSystemSummaryRow row, SmallSystemInput system)
        {
            _row = row ?? new SmallSystemSummaryRow();
            _system = system;
        }

        /// <summary>原始汇总行(计算书全文等仍取 Core 生成的结果)。</summary>
        public SmallSystemSummaryRow Row => _row;

        /// <summary>该行对应的小系统工程里的那一套系统(选中后据此现场重算;可能为 null)。</summary>
        public SmallSystemInput System => _system;

        public SmallSystemType SystemType => _row.SystemType;

        // ---- 列名与 SmallRoomTable.SummaryColumns() 逐一同名 ----

        public string TypeName => _row.TypeName ?? "";

        public string SystemCode => _row.SystemCode ?? "";

        public string RoomCount => Count(_row.RoomCount);

        public string TotalAreaM2 => Num(_row.TotalAreaM2, 2);

        public string TotalCoolingKw => Num(_row.TotalCoolingKw, 2);

        public string TotalSupplyM3H => Num(_row.TotalSupplyM3H, 0);

        public string TotalReturnM3H => Num(_row.TotalReturnM3H, 0);

        public string TotalFreshAirM3H => Num(_row.TotalFreshAirM3H, 0);

        public string TotalExhaustM3H => Num(_row.TotalExhaustM3H, 0);

        public string TotalSmokeM3H => Num(_row.TotalSmokeM3H, 0);

        public string TotalMakeupAirM3H => Num(_row.TotalMakeupAirM3H, 0);

        public string TotalUnitCoolingKw => Num(_row.TotalUnitCoolingKw, 2);

        public string EquipmentCount => Count(_row.EquipmentCount);

        /// <summary>数值显示:0(不涉及)显示「—」,否则千分位 + 固定小数位。</summary>
        private static string Num(double value, int decimals)
        {
            if (Math.Abs(value) < 1e-9) return Dash;
            return value.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        /// <summary>台数 / 个数的显示:0(不涉及)显示「—」。</summary>
        private static string Count(int value)
        {
            return value == 0 ? Dash : value.ToString("N0", CultureInfo.InvariantCulture);
        }
    }
}
