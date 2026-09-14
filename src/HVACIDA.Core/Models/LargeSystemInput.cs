using System;

namespace HVACIDA.Core.Models
{
    /// <summary>
    /// 大系统负荷计算输入(需求文档 2.2.3.1)。
    /// 字段 1:1 映射《大系统负荷计算公式.docx》参数表,默认值与公式文档完全一致;
    /// 单元格代号(如 D55、C39)在注释中保留,便于与 Excel 计算书逐格核对。
    /// 默认值即公式文档初值;其中 F4/F6/C5 由 <see cref="Services.ProjectDesignSync"/> 从
    /// 「项目信息 → 气象参数」自动回填,公共区几何(D55/D56/C13/C14)可由 Revit 空间自动取值
    /// (见 <see cref="Services.PublicAreaAggregator"/>),两者都保留手工覆盖。
    /// </summary>
    [Serializable]
    public class LargeSystemInput
    {
        public LargeSystemInput()
        {
            SetDocumentDefaults();
        }

        // ---------- 一、基本参数 ----------

        /// <summary>
        /// C5 夏季空调室外计算湿球温度 ℃。默认联动「项目信息 → 气象参数」
        /// (<see cref="Services.ProjectDesignSync"/>),联动手动关闭后由用户在「大系统 → 负荷计算」窗直接输入。
        /// </summary>
        public double OutdoorWetBulbC { get; set; }

        /// <summary>F4 站厅夏季空调计算干球温度 ℃(同上,默认联动气象参数)。</summary>
        public double HallDesignTempC { get; set; }

        /// <summary>F6 站台夏季空调计算干球温度 ℃(同上,默认联动气象参数)。</summary>
        public double PlatformDesignTempC { get; set; }

        /// <summary>
        /// 是否已脱离「项目信息 → 气象参数」的自动联动(C5/F4/F6)。
        /// <para>
        /// 取名"覆盖"而不是"自动",是为了旧数据兼容:XmlSerializer 反序列化时缺元素取默认值 false,
        /// 即"未覆盖" = 自动联动开启 —— 老版本 large-system.xml 打开后能自动获得联动,不需要迁移。
        /// </para>
        /// </summary>
        public bool WeatherManuallyOverridden { get; set; }

        /// <summary>C8 站厅公共区风温差 ℃(默认 10)。
        /// 领域确认(2026-09-04):站厅/站台送风温度必须一致,送风点由站厅送风温度统一确定(见 A118/A121)。</summary>
        public double SupplyTempDiffC { get; set; }

        /// <summary>C10 管道温升 ℃(默认 1.5)</summary>
        public double DuctTempRiseC { get; set; }

        /// <summary>C118 露点相对湿度 %(默认 95)</summary>
        public double DewPointRelativeHumidityPercent { get; set; }

        /// <summary>A91 壁面产湿量 g/(m²·h)(默认 1)</summary>
        public double WallMoistureEmission { get; set; }

        /// <summary>D110 站厅层其他湿负荷(默认 0)</summary>
        public double HallOtherMoisture { get; set; }

        /// <summary>E109 站台层结构散湿负荷(默认 0,可由 A91×面积自动计算,保留手动覆盖)</summary>
        public double PlatformStructureMoistureOverride { get; set; }

        /// <summary>E110 站台层其他湿负荷(默认 0)</summary>
        public double PlatformOtherMoisture { get; set; }

        /// <summary>B132 空调季新风量指标 m³/(h·人)(默认 20)</summary>
        public double FreshAirPerPersonM3H { get; set; }

        // ---------- 车站几何(可由模型空间获取,见 PublicAreaAggregator) ----------
        /// <summary>D55 站厅层公共区面积 m²</summary>
        public double HallAreaM2 { get; set; }

        /// <summary>D56 站台层公共区面积 m²</summary>
        public double PlatformAreaM2 { get; set; }

        /// <summary>C13 站厅公共区层高 m</summary>
        public double HallHeightM { get; set; }

        /// <summary>C14 站厅公共区长度 m</summary>
        public double HallLengthM { get; set; }

        // ---------- 出入口尺寸(渗透负荷计算) ----------
        public double EntranceAWidthM { get; set; }
        public double EntranceAHeightM { get; set; }
        public double EntranceBWidthM { get; set; }
        public double EntranceBHeightM { get; set; }
        public double EntranceCWidthM { get; set; }
        public double EntranceCHeightM { get; set; }
        public double EntranceDWidthM { get; set; }
        public double EntranceDHeightM { get; set; }

        /// <summary>B82 出入口负荷指标 W(默认 200)</summary>
        public double EntranceLoadIndexW { get; set; }

        // ---------- 高峰客流资料(必须由用户输入) ----------
        /// <summary>A27 上行线(南→北)上客量 人次/h</summary>
        public double UpLineBoardCount { get; set; }

        /// <summary>C27 下行线(北→南)上客量 人次/h</summary>
        public double DownLineBoardCount { get; set; }

        /// <summary>B27 上行线(南→北)下客量 人次/h</summary>
        public double UpLineAlightCount { get; set; }

        /// <summary>D27 下行线(北→南)下客量 人次/h</summary>
        public double DownLineAlightCount { get; set; }

        /// <summary>E27 换乘客流上客量 人次/h</summary>
        public double TransferBoardCount { get; set; }

        /// <summary>F27 换乘客流下客量 人次/h</summary>
        public double TransferAlightCount { get; set; }

        // 停站时间 min(站厅 D29~D32,站台 F29~F32)
        public double HallBoardStayMin { get; set; }
        public double HallAlightStayMin { get; set; }
        public double HallTransferBoardStayMin { get; set; }
        public double HallTransferAlightStayMin { get; set; }
        public double PlatformBoardStayMin { get; set; }
        public double PlatformAlightStayMin { get; set; }
        public double PlatformTransferBoardStayMin { get; set; }
        public double PlatformTransferAlightStayMin { get; set; }

        /// <summary>C37 集群系数(默认 0.89)</summary>
        public double ClusterFactor { get; set; }

        /// <summary>F37 超高峰小时系数(默认 1)</summary>
        public double SuperPeakHourFactor { get; set; }

        // ---------- 人员散热/散湿标准 ----------
        /// <summary>D45 站厅人员散热显热 W(默认 40)</summary>
        public double HallOccupantSensibleW { get; set; }

        /// <summary>E45 站厅人员散热潜热 W(默认 142)</summary>
        public double HallOccupantLatentW { get; set; }

        /// <summary>F45 站厅人员散湿量 g/h(默认 212)</summary>
        public double HallOccupantMoistureGH { get; set; }

        /// <summary>D46 站台人员散热显热 W(默认 51)</summary>
        public double PlatformOccupantSensibleW { get; set; }

        /// <summary>E46 站台人员散热潜热 W(默认 130)</summary>
        public double PlatformOccupantLatentW { get; set; }

        /// <summary>F46 站台人员散湿量 g/h(默认 194)</summary>
        public double PlatformOccupantMoistureGH { get; set; }

        // ---------- 照明/广告/设备发热标准 ----------
        /// <summary>C55 站厅照明指标 W/m²(默认 8)</summary>
        public double HallLightingWm2 { get; set; }

        /// <summary>C56 站台照明指标 W/m²(默认 8)</summary>
        public double PlatformLightingWm2 { get; set; }

        /// <summary>C58 站厅广告牌发热量 kW(默认 60)</summary>
        public double HallAdvertKw { get; set; }

        /// <summary>C59 站台广告牌发热量 kW(默认 10)</summary>
        public double PlatformAdvertKw { get; set; }

        /// <summary>B62 公共区扶梯发热指标 kW/台(默认 1.5)</summary>
        public double EscalatorKwPerUnit { get; set; }

        /// <summary>B63 公共区扶梯数量 台(默认 4)</summary>
        public double EscalatorCount { get; set; }

        /// <summary>D62 公共区直梯发热指标 kW/台(默认 1.5)</summary>
        public double ElevatorKwPerUnit { get; set; }

        /// <summary>D63 公共区直梯数量 台(默认 1)</summary>
        public double ElevatorCount { get; set; }

        /// <summary>E62 公共区 AFC 设备发热指标 kW/台(默认 18)</summary>
        public double AfcKwPerUnit { get; set; }

        /// <summary>E63 公共区 AFC 设备数量 台(默认 1)</summary>
        public double AfcCount { get; set; }

        // ---------- 屏蔽门(传热/漏风/发热) ----------
        /// <summary>A71 屏蔽门传热系数 W/(m²·℃)(默认 3.2)</summary>
        public double PsdHeatTransferCoeffWm2C { get; set; }

        /// <summary>B71 屏蔽门高 m(默认 3)</summary>
        public double PsdHeightM { get; set; }

        /// <summary>C71 屏蔽门长 m(默认 292)</summary>
        public double PsdLengthM { get; set; }

        /// <summary>D71 屏蔽门内外温差 ℃(默认 8)</summary>
        public double PsdTempDiffC { get; set; }

        /// <summary>E71 屏蔽门传热安全系数(默认 1.5)</summary>
        public double PsdHeatSafetyFactor { get; set; }

        /// <summary>D103 站厅层屏蔽门传热量 kW(默认 0,站厅无屏蔽门时置 0)</summary>
        public double HallPsdTransferKw { get; set; }

        /// <summary>A75 站厅层屏蔽门系统漏风量负荷 kW(默认 30)</summary>
        public double HallPsdLeakKw { get; set; }

        /// <summary>D105 站厅层屏蔽门发热量 kW(默认 0)</summary>
        public double HallPsdHeatKw { get; set; }

        /// <summary>B75 站台层屏蔽门系统漏风量负荷 kW(默认 45)</summary>
        public double PlatformPsdLeakKw { get; set; }

        /// <summary>C76 屏蔽门系统发热量 kW(默认 4)</summary>
        public double PlatformPsdSystemHeatKw { get; set; }

        // ---------- 其他补充 ----------
        /// <summary>D106 站厅层其他需补充发热量 kW(默认 0)</summary>
        public double HallExtraHeatKw { get; set; }

        /// <summary>E106 站台层其他需补充发热量 kW(默认 0)</summary>
        public double PlatformExtraHeatKw { get; set; }

        /// <summary>依据《大系统负荷计算公式.docx》参数表设置默认值。</summary>
        private void SetDocumentDefaults()
        {
            // C5/F4/F6 为公式文档中标注"从项目信息调取"的三格,由 ProjectDesignSync 联动覆盖;
            // 0 = 未填(气象参数未填时不会被联动冲掉,见 ProjectDesignSync.UnsetTemperatureC)。
            OutdoorWetBulbC = 0;
            HallDesignTempC = 30;
            PlatformDesignTempC = 28;
            WeatherManuallyOverridden = false;
            SupplyTempDiffC = 10;
            DuctTempRiseC = 1.5;
            DewPointRelativeHumidityPercent = 95;
            WallMoistureEmission = 1;
            HallOtherMoisture = 0;
            PlatformStructureMoistureOverride = 0;
            PlatformOtherMoisture = 0;
            FreshAirPerPersonM3H = 20;

            HallAreaM2 = 1500;
            PlatformAreaM2 = 1200;
            HallHeightM = 4.5;
            HallLengthM = 120;

            EntranceAWidthM = 5.3; EntranceAHeightM = 4;
            EntranceBWidthM = 5.3; EntranceBHeightM = 4;
            EntranceCWidthM = 5.3; EntranceCHeightM = 4;
            EntranceDWidthM = 5.3; EntranceDHeightM = 4;
            EntranceLoadIndexW = 200;

            UpLineBoardCount = 0; UpLineAlightCount = 0;
            DownLineBoardCount = 0; DownLineAlightCount = 0;
            TransferBoardCount = 0; TransferAlightCount = 0;

            HallBoardStayMin = 2; HallAlightStayMin = 1.5;
            HallTransferBoardStayMin = 2; HallTransferAlightStayMin = 1.5;
            PlatformBoardStayMin = 2; PlatformAlightStayMin = 1.5;
            PlatformTransferBoardStayMin = 2; PlatformTransferAlightStayMin = 1.5;

            ClusterFactor = 0.89;
            SuperPeakHourFactor = 1;

            HallOccupantSensibleW = 40; HallOccupantLatentW = 142; HallOccupantMoistureGH = 212;
            PlatformOccupantSensibleW = 51; PlatformOccupantLatentW = 130; PlatformOccupantMoistureGH = 194;

            HallLightingWm2 = 8; PlatformLightingWm2 = 8;
            HallAdvertKw = 60; PlatformAdvertKw = 10;

            EscalatorKwPerUnit = 1.5; EscalatorCount = 4;
            ElevatorKwPerUnit = 1.5; ElevatorCount = 1;
            AfcKwPerUnit = 18; AfcCount = 1;

            PsdHeatTransferCoeffWm2C = 3.2;
            PsdHeightM = 3; PsdLengthM = 292; PsdTempDiffC = 8; PsdHeatSafetyFactor = 1.5;
            HallPsdTransferKw = 0; HallPsdLeakKw = 30; HallPsdHeatKw = 0;
            PlatformPsdLeakKw = 45; PlatformPsdSystemHeatKw = 4;

            HallExtraHeatKw = 0; PlatformExtraHeatKw = 0;
        }
    }
}
