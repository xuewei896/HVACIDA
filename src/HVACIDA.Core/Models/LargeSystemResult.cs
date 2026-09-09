using System;

namespace HVACIDA.Core.Models
{
    /// <summary>
    /// 大系统负荷计算结果(逐格镜像《大系统负荷计算公式.docx》).
    /// 属性名带单元格代号(如 HallPeakFlowPm=C39),kW / m³/h / kJ/kg / g/kg / g/s 单位见各注释。
    /// 计算器与格式化器共用本结构,便于与 Excel 计算书逐格比对。
    /// </summary>
    [Serializable]
    public class LargeSystemResult
    {
        // ---- 高峰客流(个/min) ----
        /// <summary>C35 站厅上车高峰客流</summary>
        public double HallBoardingFlowPm { get; set; }

        /// <summary>C36 站厅下车高峰客流</summary>
        public double HallAlightingFlowPm { get; set; }

        /// <summary>F35 站台上车高峰客流</summary>
        public double PlatformBoardingFlowPm { get; set; }

        /// <summary>F36 站台下车高峰客流</summary>
        public double PlatformAlightingFlowPm { get; set; }

        /// <summary>C39 站厅公共区高峰客流 个/min</summary>
        public double HallPeakFlowPm { get; set; }

        /// <summary>C40 站台公共区高峰客流 个/min</summary>
        public double PlatformPeakFlowPm { get; set; }

        // ---- 站厅冷负荷(kW) ----
        public double HallSensibleKw { get; set; }        // D95
        public double HallLatentKw { get; set; }          // D96
        public double HallLightingKw { get; set; }        // D97
        public double HallAdvertKw { get; set; }          // D98
        public double EscalatorTotalKw { get; set; }      // B64
        public double HallEscalatorKw { get; set; }       // D99
        public double ElevatorTotalKw { get; set; }       // D64
        public double HallElevatorKw { get; set; }        // D100
        public double AfcTotalKw { get; set; }            // E64
        public double HallAfcKw { get; set; }             // D101
        public double HallEntranceInfiltrationKw { get; set; } // D102
        public double HallPsdTransferKw { get; set; }     // D103
        public double HallPsdLeakKw { get; set; }         // D104
        public double HallPsdHeatKw { get; set; }         // D105
        public double HallTotalCoolingKw { get; set; }    // D107

        // ---- 站台冷负荷(kW) ----
        public double PlatformSensibleKw { get; set; }    // E95
        public double PlatformLatentKw { get; set; }      // E96
        public double PlatformLightingKw { get; set; }    // E97
        public double PlatformAdvertKw { get; set; }      // E98
        public double PlatformEscalatorKw { get; set; }   // E99
        public double PlatformElevatorKw { get; set; }    // E100
        public double PlatformPsdTransferKw { get; set; } // E103 (=F71)
        public double PlatformPsdLeakKw { get; set; }     // E104
        public double PlatformPsdHeatKw { get; set; }     // E105
        public double PlatformTotalCoolingKw { get; set; }// E107

        // ---- 湿负荷 ----
        public double HallPeopleMoisture { get; set; }    // D108
        public double HallStructureSurfaceM2 { get; set; }// B91
        public double HallStructureMoisture { get; set; } // D109
        public double HallTotalMoistureGps { get; set; }  // D112 g/s
        public double PlatformPeopleMoisture { get; set; }// E108
        public double PlatformTotalMoistureGps { get; set; } // E112 g/s

        // ---- 热湿比 / 焓 / 含湿量 ----
        public double HallHeatHumidityRatio { get; set; }   // D113 kJ/kg
        public double PlatformHeatHumidityRatio { get; set; } // E113 kJ/kg
        public double HallSupplyTempC { get; set; }         // A118
        public double DewPointTempC { get; set; }           // B118
        public double SaturatedMoistureAtDewGkg { get; set; } // D118 g/kg
        public double DewPointMoistureGkg { get; set; }     // E118 g/kg
        public double HallSupplyEnthalpy { get; set; }      // A121 kJ/kg
        public double HallIndoorHumidityGkg { get; set; }   // B121 g/kg
        public double HallIndoorEnthalpy { get; set; }      // C121 kJ/kg
        public double PlatformIndoorHumidityGkg { get; set; } // D121 g/kg
        public double PlatformIndoorEnthalpy { get; set; }  // E121 kJ/kg

        // ---- 送风量(m³/h) ----
        public double HallSupplyFlowM3H { get; set; }       // A125
        public double PlatformSupplyFlowM3H { get; set; }   // B125
        public double TotalSupplyFlowM3H { get; set; }      // C125

        // ---- 新风/回风 ----
        public double PublicAreaPeople { get; set; }        // A132 人
        public double ActualFreshAirM3H { get; set; }       // A136
        public double FreshAirRatio { get; set; }           // B136
        public double HallReturnFlowM3H { get; set; }       // C136
        public double PlatformReturnFlowM3H { get; set; }   // D136
        public double TotalReturnFlowM3H { get; set; }      // E136

        // ---- 制冷量 ----
        public double ReturnMixEnthalpy { get; set; }       // C145 kJ/kg
        public double FreshSaturatedMoistureGkg { get; set; } // D149 g/kg
        public double FreshEnthalpy { get; set; }           // C149 kJ/kg
        public double FreshReturnMixEnthalpy { get; set; }  // C146 kJ/kg
        public double DewPointEnthalpy { get; set; }        // C143 kJ/kg
        public double TotalCoolingKw { get; set; }          // E159

        // ---- 排烟 ----
        public double HallSmokeFlowM3H { get; set; }        // C171 = D55×60
        public double PlatformSmokeFlowM3H { get; set; }    // D171 = D56×60

        // ---- 选型(单台) ----
        public double UnitSupplyFlowM3H { get; set; }       // A165 = C125/2
        public double UnitCoolingKw { get; set; }           // B165 = E159/2
        public double UnitReturnFlowM3H { get; set; }       // C178 = E136/2
        public double UnitSmokeFlowM3H { get; set; }        // E178 = MAX(C171,D171)/2
    }
}
