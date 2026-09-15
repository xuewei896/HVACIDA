namespace HVACIDA.Core.Utils
{
    /// <summary>
    /// 暖通常用物性常量与待核对系数集中处。
    /// 说明:大系统空调主计算链已按《大系统负荷计算公式.docx》移植并与《大系统负荷计算公式-示例.xls》逐格核对
    /// (30 项断言,见 tools/HVACIDA.Smoke);剩余待定口径(如防烟分区选型)见各常量注释。
    /// </summary>
    public static class HvacConstants
    {
        /// <summary>空气密度 kg/m³(标准状态常用值)</summary>
        public const double AirDensity = 1.2;

        /// <summary>空气定压比热 kJ/(kg·K)</summary>
        public const double AirCp = 1.01;

        /// <summary>水的汽化潜热 kJ/kg(用于散湿量→潜热)</summary>
        public const double WaterLatentHeat = 2501.0;

        /// <summary>饱和水蒸气分压公式(Magnus)常数 Pa</summary>
        public const double MagnusA = 610.94;
        public const double MagnusB = 17.625;
        public const double MagnusC = 243.04;

        /// <summary>水蒸气与干空气气体常数比 0.622</summary>
        public const double GasConstantRatio = 0.622;

        /// <summary>排烟换气次数 次/h(需求文档:按 60 倍/小时)= 排烟风机计算风量口径</summary>
        public const double SmokeAirChangesPerHour = 60.0;

        /// <summary>
        /// 排烟风机选型系数 1.2(领域确认 2026-09-04):
        /// 计算风量 = 面积×60;选型风量 = 计算风量×1.2(等效 防烟分区面积×72)。
        /// TODO(防烟分区):按"防烟分区"逐区计算风机选型需分区几何输入,当前模型未含,选型暂按公式文档 MAX(站厅,站台)/2 过渡。
        /// </summary>
        public const double SmokeExhaustSelectionFactor = 1.2;

        /// <summary>组合式空调机组台数(需求文档:取总量一半,共 2 台)</summary>
        public const double AhUnitCount = 2.0;

        /// <summary>排烟风机台数(需求文档:2 台)</summary>
        public const double SmokeFanUnitCount = 2.0;

        // =====================================================================
        // 小系统系数(权威源:《小系统空调负荷、送排风、排烟计算公式.docx》)
        // 单元格代号保留在注释里便于逐格核对;文档文字与示例冲突处已注明。
        // =====================================================================

        /// <summary>J27/H64 人员冷负荷 W/人(= 134 × 人数)</summary>
        public const double SmallPersonCoolingW = 134.0;

        /// <summary>K27 人员湿负荷 g/(h·人)(= 115 × 人数)</summary>
        public const double SmallPersonMoistureGH = 115.0;

        /// <summary>T27/M64 人员新风量 m³/(h·人)(= 30 × 人数)</summary>
        public const double SmallFreshAirPerPersonM3H = 30.0;

        /// <summary>U27 10% 系统新风量(新风比取"人员新风量"与"10% 系统新风量"的较大值)</summary>
        public const double SmallSystemFreshAirRatio = 0.1;

        /// <summary>V27 焓差→冷量换算系数(= R27×(C52−C50)×1.15/3600)</summary>
        public const double SmallEnthalpyFlowFactor = 1.15;

        /// <summary>N4 送风温差默认 10 ℃</summary>
        public const double SmallSupplyTempDiffC = 10.0;

        /// <summary>E9 管道温升默认 1.5 ℃</summary>
        public const double SmallDuctTempRiseC = 1.5;

        /// <summary>E7 室内计算干球温度默认 27 ℃(用户可改)</summary>
        public const double SmallIndoorTempC = 27.0;

        /// <summary>N8 过渡季通风室外计算干球温度默认 14 ℃(多联机+新风用)</summary>
        public const double SmallTransitionOutdoorC = 14.0;

        /// <summary>D44 露点相对湿度默认 95 %(全空气一次回风用)</summary>
        public const double SmallDewPointRhPct = 95.0;

        /// <summary>C83 室内相对湿度默认 50 %(多联机+新风用)</summary>
        public const double SmallIndoorRhPct = 50.0;

        /// <summary>A21 照明指标默认 8 W/m²(文档示例工程用 20)</summary>
        public const double SmallLightingIndexWm2 = 8.0;

        /// <summary>A17 壁面单位面积产湿量默认 2 g/(m²·h)</summary>
        public const double SmallWallMoistureEmission = 2.0;

        /// <summary>G27/E64 房间设备冷负荷默认 1000 W</summary>
        public const double SmallEquipmentCoolingW = 1000.0;

        /// <summary>P27/K64 房间换气次数默认 6 次/h</summary>
        public const double SmallRoomAirChangePerHour = 6.0;

        /// <summary>
        /// 饱和空气含湿量 7 次多项式系数 g/kg(文档 E44/E54/E83/E87,自 t⁷ 降幂):
        /// d = −4.171e-10·t⁷ + 4.843e-8·t⁶ − 2.133e-6·t⁵ + 5.009e-5·t⁴ − 4.032e-4·t³ + 0.01264·t² + 0.265·t + 3.787
        /// </summary>
        public static readonly double[] SaturatedHumidityPolyGkg =
        {
            -0.0000000004171, 0.00000004843, -0.000002133, 0.00005009,
            -0.0004032, 0.01264, 0.265, 3.787
        };

        /// <summary>排烟系统:计算排烟量 = 空间面积 × 60 m³/(h·m²)(文档明确)</summary>
        public const double SmokeSystemRateM3HPerM2 = 60.0;

        /// <summary>排烟系统选型系数:选型排烟量 = 计算排烟量 × 1.2(文档明确)</summary>
        public const double SmokeSystemSelectionFactor = 1.2;

        /// <summary>补风量比例:计算补风量 = 计算排烟量 × 0.6(文档明确)</summary>
        public const double MakeupAirRatio = 0.6;

        /// <summary>补风选型系数:选型补风量 = 计算补风量 × 1.1(文档明确)</summary>
        public const double MakeupAirSelectionFactor = 1.1;

        /// <summary>
        /// 排风系统 / 送排风排烟系统 的排风选型系数 1.1(文档文字:"选型排风量为计算排风量×1.1")。
        /// ⚠ 文档示例(卫生间排风 EAF-A601)用的系数是 **1.3**(5386×1.3 = 7002),与文字不一致;
        /// 代码按文字取 1.1 作默认且允许修改,示例口径在自检里单独断言。
        /// </summary>
        public const double ExhaustSelectionFactor = 1.1;

        /// <summary>文档示例中卫生间排风系统实际使用的选型系数(1.3)</summary>
        public const double ExhaustSelectionFactorInSample = 1.3;

        /// <summary>送风量比例:计算送风量 = 计算排风量 × 0.9(送排风排烟系统)</summary>
        public const double SupplyFromExhaustRatio = 0.9;

        /// <summary>送风选型系数:选型送风量 = 计算排风量 × 1.1(文档文字)</summary>
        public const double SupplySelectionFactor = 1.1;

        /// <summary>加压送风选型系数:D392 = S388 × 1.2(文档明确)</summary>
        public const double PressurizationSelectionFactor = 1.2;

        /// <summary>疏散门缝隙有效宽度系数(I388=(B388+C388)×2×0.004)</summary>
        public const double DoorGapWidthFactor = 0.004;

        /// <summary>门缝漏风量系数(N388=0.827×I388×ΔP^0.5×1.25×M388)</summary>
        public const double DoorLeakageFactor = 0.827;

        /// <summary>门缝漏风附加系数 1.25(N388)</summary>
        public const double DoorLeakageAdditional = 1.25;

        /// <summary>余压阀漏风系数(R388=0.083×O388×P388)</summary>
        public const double ReliefValveLeakageFactor = 0.083;

        // =====================================================================
        // 水力计算(需求 2.3 / 2.4)系数与物性
        // 口径来源:用户 2026-09-15 明确「没有具体计算公式,由你实现该功能」——
        // 因此这里采用**暖通/流体力学的通用公式与常用取值**,并把每一个系数的来源写清:
        //   · 公式:沿程 Darcy-Weisbach(λ 用阿尔特舒利显式式)、局部 ζ·ρv²/2、水系统 H = ΣΔP/(ρg);
        //   · 物性:空气/水的常用物性表(按温度插值),算完在结果里显示实际取值;
        //   · 系数:粗糙度 / 富余系数 / 局部阻力 ζ 表,全部集中在 HydraulicCoefficients 里**可见可改**,
        //           并在计算书末尾逐项列出本次实际取值 —— 不藏在代码里。
        // 待项目确认项见 HydraulicLocalLossTable 每条取值的来源说明。
        // =====================================================================

        /// <summary>重力加速度 m/s²(水系统静压与扬程换算)</summary>
        public const double GravityM2S = 9.81;

        /// <summary>风管绝对粗糙度 K mm(镀锌钢板风管常用值)</summary>
        public const double DuctRoughnessMm = 0.15;

        /// <summary>水管绝对粗糙度 K mm(焊接钢管常用值;不锈钢/铜管更小,可在界面改)</summary>
        public const double PipeRoughnessMm = 0.2;

        /// <summary>风系统 / 水系统富余系数(需求全压/扬程 = 计算总阻力 × 1.1)</summary>
        public const double HydraulicExtraFactor = 1.1;

        /// <summary>层流临界雷诺数(Re 低于它按 λ = 64/Re;之上按湍流式)</summary>
        public const double LaminarReynoldsLimit = 2320.0;
    }
}
