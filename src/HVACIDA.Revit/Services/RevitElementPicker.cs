using System;
using Autodesk.Revit.DB;

namespace HVACIDA.Revit.Services
{
    /// <summary>
    /// 手动拾取 Revit 实体(需求 2.2.3.2:小系统"与土壤接触外墙长度"由模型测量取得)。
    /// <para>
    /// 2026-09-24 用户口径变更:原先"选取多段墙体取长度之和"改为**点击后直接在模型里两点测量**
    /// (见 <see cref="MeasureTwoPointsLengthMeters"/>),故墙体选择器与求和逻辑已删除。
    /// </para>
    /// <para>
    /// <strong>必须在没有模态窗口的 IExternalCommand 上下文里调用</strong> —— 见 PublicAreaWindow 注释:
    /// WPF 模态窗会在 Win32 层禁用 Revit 主窗,故流程是"关窗 → 拾取 → 用同一 ViewModel 重开窗"。
    /// 只读操作,不需要 Transaction。
    /// </para>
    /// </summary>
    internal static class RevitElementPicker
    {

        /// <summary>
        /// **两点测距**:在模型里依次点取起点、终点,返回两点距离(米)。
        /// <para>
        /// 2026-09-24 用户口径:"与土壤接触外墙长度填写时,点击后直接从模型测量长度,自动输入进去",
        /// 并删掉"拾取墙体求外墙总长"按钮 —— 不再要求模型里存在可拾取的墙(不限视图剖切、不受墙分段影响),
        /// 直接用 <see cref="Autodesk.Revit.UI.Selection.Selection.PickPoint(string)"/> 量取。
        /// </para>
        /// <para>必须在平面视图(有有效工作平面)中使用;只读操作,不需要 Transaction。</para>
        /// </summary>
        /// <returns>两点距离(米,保留 1 位小数);用户 Esc 取消时返回 null。</returns>
        public static double? MeasureTwoPointsLengthMeters(Autodesk.Revit.UI.UIDocument uidoc, out string note)
        {
            note = "";
            if (uidoc == null)
            {
                note = "没有活动文档,无法测量。";
                return null;
            }

            XYZ first;
            XYZ second;
            try
            {
                first = uidoc.Selection.PickPoint("点取外墙测量起点(平面视图;Esc 取消)");
                second = uidoc.Selection.PickPoint("点取外墙测量终点(平面视图;Esc 取消)");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                note = "已取消";
                return null;
            }

            double feet = first.DistanceTo(second);
            if (feet <= 0)
            {
                note = "两点重合,未取到长度。";
                return null;
            }

            double meters = ToMeters(feet);
            // 与"模型拾取数据保留 1 位小数"口径一致(2026-09-24)。
            double rounded = Math.Round(meters, 1, MidpointRounding.AwayFromZero);
            note = "两点测量长度 " + rounded.ToString("0.0") + " m";
            return rounded;
        }

        /// <summary>英尺 → 米。单元换算集中在这里:Revit 内部长度为英尺,对外统一 m。</summary>
        private static double ToMeters(double feet)
        {
            return UnitUtils.ConvertFromInternalUnits(feet, DisplayUnitType.DUT_METERS);
        }
    }
}
