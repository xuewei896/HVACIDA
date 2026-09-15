using System;
using System.Collections.Generic;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// **常用局部阻力系数(ζ)表**。
    /// <para>
    /// 为什么需要一张表:Revit 模型里能读到"这里有个 90°弯头"(族/类别),但**读不到它的 ζ 值** ——
    /// ζ 是水力计算手册里的经验取值,不在模型参数里。所以做法是:
    /// ① 读取器按管件族/类型**匹配到表里的一条**,逐件把 ζ 累加到所在管段,并在段说明里写下用了哪一条;
    /// ② 匹配不到的管件**不按 0 静默处理**,而是计入"未识别管件"清单,在界面红字提示(见读取器的 PendingNote);
    /// ③ 整张表可在界面查看与修改,ζ 随计算书一起输出 —— 评审/项目可以按手册或设备样本替换。
    /// </para>
    /// <para>
    /// 取值口径:暖通水力计算手册常用表(《实用供热空调设计手册》局部阻力系数常用表 /
    /// 《全国民用建筑工程设计技术措施 — 暖通空调·动力》同类表)的**常用简化值**。
    /// 三通、阀门的 ζ 随流速比与开度变化很大,这里取的是工程常用简化值,**不是**通用唯一值 ——
    /// 凡此类取值都在 <see cref="HydraulicLocalLossItem.Source"/> 里注明。
    /// </para>
    /// </summary>
    public static class HydraulicLocalLossTable
    {
        /// <summary>风管管件取值说明(统一给同一条来源,便于替换)。</summary>
        public const string DuctSource =
            "风管管件常用取值(《实用供热空调设计手册》局部阻力系数常用表口径;弯头按 R/D≈1.0,项目可按手册图表或厂家样本替换)";

        /// <summary>水管管件取值说明。</summary>
        public const string PipeSource =
            "水管管件常用取值(《实用供热空调设计手册》/《建筑给水排水设计手册》局部阻力系数常用表口径;阀门按全开,项目可按样本替换)";

        /// <summary>风口取值说明。</summary>
        public const string OutletSource =
            "风口局部阻力常用取值(送风口按散流器、回风口按格栅;项目按风口样本的 ζ 替换)";

        /// <summary>风管常用管件与 ζ。</summary>
        public static List<HydraulicLocalLossItem> CreateDuctDefaults()
        {
            return new List<HydraulicLocalLossItem>
            {
                Item("90°弯头(圆形 R/D=1.0)", "风管", 0.25, DuctSource),
                Item("90°弯头(矩形 R/b=1.0)", "风管", 0.25, DuctSource),
                Item("45°弯头", "风管", 0.15, DuctSource),
                Item("渐缩管(圆锥角≤30°)", "风管", 0.10, DuctSource),
                Item("渐扩管(圆锥角≤30°)", "风管", 0.30, DuctSource),
                Item("三通(直通)", "风管", 0.10, DuctSource + ";三通 ζ 随流速比变化,取简化值"),
                Item("三通(支流)", "风管", 1.00, DuctSource + ";三通 ζ 随流速比变化,取简化值"),
                Item("多叶调节阀(全开)", "风管", 0.50, DuctSource),
                Item("蝶阀(全开)", "风管", 0.20, DuctSource),
                Item("防火阀(全开)", "风管", 0.50, DuctSource),
                Item("软接头", "风管", 0.20, DuctSource),
                Item("送风口(散流器)", "风管", 2.00, OutletSource),
                Item("回风口(格栅)", "风管", 1.50, OutletSource)
            };
        }

        /// <summary>水管常用管件与 ζ。</summary>
        public static List<HydraulicLocalLossItem> CreatePipeDefaults()
        {
            return new List<HydraulicLocalLossItem>
            {
                Item("90°弯头(焊接)", "水管", 1.00, PipeSource),
                Item("90°弯头(螺纹,DN≤50)", "水管", 1.50, PipeSource),
                Item("45°弯头", "水管", 0.30, PipeSource),
                Item("渐缩管", "水管", 0.50, PipeSource),
                Item("渐扩管", "水管", 1.00, PipeSource),
                Item("三通(直通)", "水管", 1.00, PipeSource + ";三通 ζ 随流速比变化,取简化值"),
                Item("三通(分流/合流)", "水管", 1.50, PipeSource + ";三通 ζ 随流速比变化,取简化值"),
                Item("闸阀(全开)", "水管", 0.20, PipeSource),
                Item("截止阀(全开)", "水管", 6.00, PipeSource),
                Item("止回阀", "水管", 2.00, PipeSource),
                Item("Y 型过滤器", "水管", 2.50, PipeSource),
                Item("平衡阀(全开)", "水管", 2.50, PipeSource)
            };
        }

        /// <summary>整表默认值(风管 + 水管)。</summary>
        public static List<HydraulicLocalLossItem> CreateDefaults()
        {
            var items = CreateDuctDefaults();
            items.AddRange(CreatePipeDefaults());
            return items;
        }

        /// <summary>按介质取默认表。</summary>
        public static List<HydraulicLocalLossItem> CreateDefaults(HydraulicKind kind)
        {
            return kind == HydraulicKind.WaterPipe ? CreatePipeDefaults() : CreateDuctDefaults();
        }

        /// <summary>按名称精确查 ζ;找不到返回 <see cref="double.NaN"/>(调用方必须记"未识别",不许当 0)。</summary>
        public static double ZetaOf(IList<HydraulicLocalLossItem> items, string name)
        {
            if (items == null || string.IsNullOrEmpty(name)) return double.NaN;
            foreach (var item in items)
            {
                if (item == null) continue;
                if (string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)) return item.Zeta;
            }
            return double.NaN;
        }

        private static HydraulicLocalLossItem Item(string name, string appliesTo, double zeta, string source)
        {
            return new HydraulicLocalLossItem { Name = name, AppliesTo = appliesTo, Zeta = zeta, Source = source };
        }
    }
}
