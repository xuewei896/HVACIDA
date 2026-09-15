using System.Collections.Generic;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>小系统汇总的一个系统行(全站多系统汇总表用)。</summary>
    public class SmallSystemSummaryRow
    {
        /// <summary>系统类型中文名。</summary>
        public string TypeName { get; set; } = "";

        /// <summary>系统编号(如 AHU-A101;可为空)。</summary>
        public string SystemCode { get; set; } = "";

        public SmallSystemType SystemType { get; set; }

        /// <summary>房间/分区数。</summary>
        public int RoomCount { get; set; }

        /// <summary>总面积 m²(逐系统面积之和)。</summary>
        public double TotalAreaM2 { get; set; }

        public double TotalCoolingKw { get; set; }
        public double TotalSupplyM3H { get; set; }
        public double TotalReturnM3H { get; set; }
        public double TotalExhaustM3H { get; set; }
        public double TotalSmokeM3H { get; set; }
        public double TotalMakeupAirM3H { get; set; }
        public double TotalFreshAirM3H { get; set; }
        public double TotalUnitCoolingKw { get; set; }

        /// <summary>设备台数(该系统的选型条目数)。</summary>
        public int EquipmentCount { get; set; }

        /// <summary>该系统的口径/待补说明(界面展开时显示)。</summary>
        public string Note { get; set; } = "";

        /// <summary>计算书文本(导出全站计算书时拼接)。</summary>
        public string ResultText { get; set; } = "";
    }

    /// <summary>
    /// 小系统**全站汇总**计算结果:逐系统一行 + 合计行(需求 2.2.3.2「小系统计算结果」)。
    /// 纯 Core(不碰 Revit),可被自检直接断言。
    /// </summary>
    public class SmallSystemSummary
    {
        /// <summary>逐系统汇总行(按录入顺序)。</summary>
        public List<SmallSystemSummaryRow> Rows { get; } = new List<SmallSystemSummaryRow>();

        // ---- 全站合计 ----
        public int SystemCount { get; set; }
        public int RoomCount { get; set; }
        public double TotalAreaM2 { get; set; }
        public double TotalCoolingKw { get; set; }
        public double TotalSupplyM3H { get; set; }
        public double TotalReturnM3H { get; set; }
        public double TotalExhaustM3H { get; set; }
        public double TotalSmokeM3H { get; set; }
        public double TotalMakeupAirM3H { get; set; }
        public double TotalFreshAirM3H { get; set; }
        public double TotalUnitCoolingKw { get; set; }
        public int EquipmentCount { get; set; }

        /// <summary>口径说明(界面提示条)。</summary>
        public string Note { get; set; } = "";

        /// <summary>各系统类型的中文名与台数汇总文本(如 "全空气一次回风系统 2 套")。</summary>
        public string TypeBreakdown { get; set; } = "";
    }

    /// <summary>
    /// 小系统多系统汇总器:把小系统工程里的**每一套系统各自计算**,再汇总成"逐系统一行 + 全站合计"。
    /// <para>
    /// 只做汇总与相加,不跨系统重算 —— 每套系统的公式链仍由 <see cref="SmallSystemLoadCalculator"/> 负责,
    /// 保证"汇总表里的数字 == 单系统计算书里的数字"。
    /// </para>
    /// </summary>
    public class SmallSystemSummaryService
    {
        private readonly ISmallSystemLoadCalculator _calculator;

        public SmallSystemSummaryService()
            : this(null)
        {
        }

        public SmallSystemSummaryService(ISmallSystemLoadCalculator calculator)
        {
            _calculator = calculator ?? new SmallSystemLoadCalculator();
        }

        /// <summary>汇总计算(逐系统 + 合计)。</summary>
        public SmallSystemSummary Summarize(SmallSystemProject project)
        {
            var summary = new SmallSystemSummary();
            if (project == null || project.Systems.Count == 0)
            {
                summary.Note = "还没有保存过任何小系统 —— 请先在「小系统」各按钮里录入并点【保存参数】,再回到本窗汇总。";
                return summary;
            }

            var typeCount = new Dictionary<SmallSystemType, int>();
            foreach (var system in project.Systems)
            {
                var result = _calculator.Calculate(system);
                var row = new SmallSystemSummaryRow
                {
                    SystemType = system.SystemType,
                    TypeName = ResultTable.SystemTypeName(system.SystemType),
                    SystemCode = system.SystemCode ?? "",
                    RoomCount = result.Rooms.Count,
                    TotalAreaM2 = result.TotalAreaM2,
                    TotalCoolingKw = result.TotalCoolingKw,
                    TotalSupplyM3H = result.TotalSupplyM3H,
                    TotalReturnM3H = result.TotalReturnM3H,
                    TotalExhaustM3H = result.TotalExhaustM3H,
                    TotalSmokeM3H = result.TotalSmokeM3H,
                    TotalMakeupAirM3H = result.TotalMakeupAirM3H,
                    TotalFreshAirM3H = result.TotalFreshAirM3H,
                    TotalUnitCoolingKw = result.TotalUnitCoolingKw,
                    EquipmentCount = result.Equipments.Count,
                    Note = result.PendingNote,
                    ResultText = ResultFormatter.FormatSmall(system, result)
                };
                summary.Rows.Add(row);

                summary.RoomCount += row.RoomCount;
                summary.TotalAreaM2 += row.TotalAreaM2;
                summary.TotalCoolingKw += row.TotalCoolingKw;
                summary.TotalSupplyM3H += row.TotalSupplyM3H;
                summary.TotalReturnM3H += row.TotalReturnM3H;
                summary.TotalExhaustM3H += row.TotalExhaustM3H;
                summary.TotalSmokeM3H += row.TotalSmokeM3H;
                summary.TotalMakeupAirM3H += row.TotalMakeupAirM3H;
                summary.TotalFreshAirM3H += row.TotalFreshAirM3H;
                summary.TotalUnitCoolingKw += row.TotalUnitCoolingKw;
                summary.EquipmentCount += row.EquipmentCount;

                int n;
                typeCount[system.SystemType] = typeCount.TryGetValue(system.SystemType, out n) ? n + 1 : 1;
            }

            summary.SystemCount = summary.Rows.Count;
            var parts = new List<string>();
            foreach (SmallSystemType type in System.Enum.GetValues(typeof(SmallSystemType)))
            {
                int n;
                if (typeCount.TryGetValue(type, out n)) parts.Add(ResultTable.SystemTypeName(type) + " " + n + " 套");
            }
            summary.TypeBreakdown = string.Join("、", parts.ToArray());
            summary.Note = "共 " + summary.SystemCount + " 套小系统(" + summary.TypeBreakdown + ")," +
                           "合计房间/分区 " + summary.RoomCount + " 个、设备 " + summary.EquipmentCount + " 台。" +
                           "各系统口径见其计算书;汇总只做相加,不跨系统重算。";
            return summary;
        }
    }
}
