using System;
using System.Collections.Generic;
using HVACIDA.Core.Models;
using HVACIDA.Core.Utils;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// 小系统计算(需求 2.2.3.2)。
    /// <para>
    /// <strong>公式权威 =《小系统空调负荷、送排风、排烟计算公式.docx》</strong>(仓库根目录交付件,
    /// 正文副本见 <c>docs/小系统计算公式-空调负荷送排风排烟.md</c>)。逐条公式都标了文档单元格代号,
    /// 便于与文档示例逐格核对;六类系统各一条公式链:
    /// </para>
    /// <list type="number">
    ///   <item><b>全空气一次回风</b>:房间负荷 → 露点/送风点/室内焓 → 消除余热通风量 O27 与换气次数通风量 Q27
    ///         取大 → 新回风混合焓 C52 → 空调器冷量 V27;输出柜式空调机组送风量 R37 / 制冷量 V37 / 回排风机回风量 W37;</item>
    ///   <item><b>多联机+新风</b>:房间负荷 → 消除余热通风量 J64(A17 用 过渡季温差)/ 换气次数通风量 L64 取大 →
    ///         新风焓 C87 与室内焓 C86 → 新风冷负荷 P64;输出新风机 D92/E92、送风机 D93、排风机 D94、多联机 E99;</item>
    ///   <item><b>排风系统</b>:计算排风量 = 面积×层高×换气次数,选型 = ×系数(文档文字 1.1,示例用 1.3);</item>
    ///   <item><b>排烟系统</b>:计算排烟量 = 面积×60,选型 ×1.2;计算补风量 = 排烟×0.6,选型 ×1.1;</item>
    ///   <item><b>送风排风排烟</b>:排风(面积×层高×换气次数)、送风 = 排风×0.9、排烟 = 面积×60、补风 = 排烟×0.6,
    ///         补风机取"计算送风量与计算补风量的最大值";</item>
    ///   <item><b>加压送风</b>:门开启风量 G388 + 门缝漏风 N388 + 余压阀漏风 R388 = 楼梯间加压送风量 S388,
    ///         选型 = ×1.2。</item>
    /// </list>
    /// <para>
    /// 纪律:公式文档**没有给出**的量一律不外推 —— 例如排风系统"选型系数文字 1.1 / 示例 1.3"的不一致,
    /// 代码按文字取默认并允许用户改,同时在结果里写明存疑项,绝不悄悄挑一个。
    /// </para>
    /// </summary>
    public class SmallSystemLoadCalculator : ISmallSystemLoadCalculator
    {
        public SmallSystemResult Calculate(SmallSystemInput x)
        {
            if (x == null) throw new ArgumentNullException(nameof(x));

            switch (x.SystemType)
            {
                case SmallSystemType.AllAirOnceReturn: return CalculateAllAirOnceReturn(x);
                case SmallSystemType.VrfWithFreshAir: return CalculateVrfWithFreshAir(x);
                case SmallSystemType.ExhaustVentilation: return CalculateExhaust(x);
                case SmallSystemType.SmokeExhaust: return CalculateSmoke(x);
                case SmallSystemType.SupplyExhaustSmoke: return CalculateSupplyExhaustSmoke(x);
                case SmallSystemType.PressurizationSupply: return CalculatePressurization(x);
                default:
                    return new SmallSystemResult
                    {
                        SystemType = x.SystemType,
                        PendingNote = "未知系统类型,未计算。"
                    };
            }
        }

        // ==================================================================
        // 一、全空气一次回风系统(公式文档「全空气一次回风系统」)
        // ==================================================================
        private static SmallSystemResult CalculateAllAirOnceReturn(SmallSystemInput x)
        {
            var r = new SmallSystemResult
            {
                SystemType = x.SystemType,
                Note = "公式链:照明冷负荷 = 照明指标 × 面积;人员冷负荷 = 134 × 人数;" +
                       "结构湿负荷 = (层高 × 与土壤接触外墙长 + 与土壤接触屋顶面积) × 壁面单位面积产湿量;" +
                       "送风温度 = 室内计算温度 − 送风温差;露点温度 = 送风温度 − 管道温升;" +
                       "露点含湿量 = (露点相对湿度 / 100) × 露点饱和含湿量(公式文档给定 7 次多项式);" +
                       "送风点焓由送风温度与露点含湿量算得;热湿比 = 冷负荷合计 / 湿负荷合计 × 1000;" +
                       "室内含湿量由热湿比与送风点焓反解,再得室内焓;" +
                       "消除余热通风量 = 冷负荷 / (室内焓 − 送风点焓) / 1.15 × 3600;" +
                       "换气次数通风量 = 换气次数 × 面积 × 层高;实际通风量取两者之大;" +
                       "新回风混合焓按新风比加权;空调器冷量 = 实际通风量 × (混合焓 − 露点焓) × 1.15 / 3600。" +
                       "输出:柜式空调机组送风量与制冷量、回排风机回风量。",
                PendingNote = "口径(2026-09-15 确认:一律按公式文档的文字公式计算,示例仅用于理解公式):" +
                              "① 空调器冷量用**实际通风量**(实际通风量 × 焓差 × 1.15 / 3600);文档示例中部分房间的冷量值对应" +
                              "消除余热通风量,故本实现与示例合计存在约 3% 差异,属正常;" +
                              "② 消除余热通风量公式里的「送风点焓」文档未单独定义,取送风点焓(示例反算亦为送风点焓);" +
                              "③ 示例工程的照明指标(20 W/m²)与换气次数是工程取值,不是公式默认值 —— 均为可改输入。"
            };

            // ---- 状态点(系统级,与房间无关)----
            double supplyTemp = x.IndoorTempC - x.SupplyTempDiffC;                    // B44
            double dewTemp = supplyTemp - x.DuctTempRiseC;                            // C44
            double satAtDew = PsychrometricHelper.SaturatedHumidityRatioGkg(dewTemp); // E44
            double dewHumidity = x.DewPointRhPct / 100.0 * satAtDew;                  // F44
            double supplyEnthalpy = PsychrometricHelper.EnthalpyFromMoistureGkg(supplyTemp, dewHumidity);   // B47
            double dewEnthalpy = PsychrometricHelper.EnthalpyFromMoistureGkg(dewTemp, dewHumidity);         // C50
            double satAtOutdoor = PsychrometricHelper.SaturatedHumidityRatioGkg(x.OutdoorWetBulbC);         // E54
            double freshEnthalpy = PsychrometricHelper.EnthalpyFromMoistureGkg(x.OutdoorWetBulbC, satAtOutdoor); // C54

            // ---- 逐房间负荷与风量 ----
            int index = 0;
            double sumCoolingKw = 0, sumMoistureGps = 0, sumFlow = 0, sumFreshPerson = 0, sumSystemFresh = 0;
            foreach (var room in x.Rooms ?? new List<SmallRoomInput>())
            {
                var row = new SmallRoomResult
                {
                    Index = ++index,
                    Name = room.Name,
                    RoomType = room.RoomType,
                    AreaM2 = room.AreaM2,
                    HeightM = room.HeightM,
                    WallLengthM = room.WallLengthM,
                    RoofAreaM2 = room.RoofAreaM2,
                    EquipmentCoolingW = room.EquipmentCoolingW,
                    Occupants = room.Occupants,
                    AirChangePerHour = GetAch(x, room)
                };

                row.LightingCoolingW = x.LightingIndexWm2 * room.AreaM2;                       // H27
                row.PeopleCoolingW = x.PersonCoolingW * room.Occupants;                        // J27
                row.TotalCoolingKw = (row.EquipmentCoolingW + row.LightingCoolingW + row.PeopleCoolingW) / 1000.0; // M27
                row.PeopleMoistureGH = x.PersonMoistureGH * room.Occupants;                    // K27
                row.StructureMoistureGH = (room.HeightM * room.WallLengthM + room.RoofAreaM2) * x.WallMoistureEmission; // L27
                row.TotalMoistureGps = (row.PeopleMoistureGH + row.StructureMoistureGH) / 3600.0;  // N27
                row.FreshAirPersonM3H = x.FreshAirPerPersonM3H * room.Occupants;               // T27

                sumCoolingKw += row.TotalCoolingKw;
                sumMoistureGps += row.TotalMoistureGps;
                sumFreshPerson += row.FreshAirPersonM3H;
                r.Rooms.Add(row);
            }

            double ratio = sumMoistureGps > 1e-12 ? sumCoolingKw / sumMoistureGps * 1000.0 : double.PositiveInfinity; // C39
            double indoorHumidity = IndoorHumidityFromRatio(x.IndoorTempC, supplyEnthalpy, dewHumidity, ratio);        // C47
            double indoorEnthalpy = PsychrometricHelper.EnthalpyFromMoistureGkg(x.IndoorTempC, indoorHumidity);       // C53

            foreach (var row in r.Rooms)
            {
                row.HeatVentilationM3H = HeatVentilation(row.TotalCoolingKw, indoorEnthalpy - supplyEnthalpy);  // O27(C51=B47)
                row.AchVentilationM3H = row.AreaM2 * row.HeightM * row.AirChangePerHour;                       // Q27
                row.ActualVentilationM3H = Math.Max(row.HeatVentilationM3H, row.AchVentilationM3H);             // R27
                row.ActualAch = row.AreaM2 * row.HeightM > 1e-9
                    ? row.ActualVentilationM3H / row.AreaM2 / row.HeightM : 0;                                   // S27
                row.FreshAirSystemM3H = row.ActualVentilationM3H * HvacConstants.SmallSystemFreshAirRatio;      // U27
                sumFlow += row.ActualVentilationM3H;
                sumSystemFresh += row.FreshAirSystemM3H;
            }

            double designFresh = Math.Max(sumFreshPerson, sumSystemFresh);                                       // MAX(T37,U37)
            double mixEnthalpy = sumFlow > 1e-9
                ? (freshEnthalpy * designFresh + indoorEnthalpy * (sumFlow - designFresh)) / sumFlow              // C52
                : 0;
            double freshRatio = sumFlow > 1e-9 ? designFresh / sumFlow : 0;                                       // E39

            foreach (var row in r.Rooms)
            {
                row.UnitCoolingKw = row.ActualVentilationM3H * (mixEnthalpy - dewEnthalpy) *
                                    HvacConstants.SmallEnthalpyFlowFactor / 3600.0;                                // V27
                row.ReturnAirM3H = row.ActualVentilationM3H * (1 - freshRatio);                                    // W27
            }

            double totalUnitCooling = 0, totalReturn = 0;
            foreach (var row in r.Rooms)
            {
                totalUnitCooling += row.UnitCoolingKw;      // V37
                totalReturn += row.ReturnAirM3H;            // W37
            }

            r.TotalAreaM2 = TotalArea(x);
            r.TotalCoolingKw = sumCoolingKw;                 // M37
            r.TotalMoistureGps = sumMoistureGps;             // N37
            r.HeatHumidityRatio = ratio;
            r.TotalSupplyM3H = sumFlow;                      // R37
            r.TotalReturnM3H = totalReturn;                  // W37
            r.TotalFreshAirM3H = sumFreshPerson;             // T37
            r.TotalSystemFreshAirM3H = sumSystemFresh;       // U37
            r.DesignFreshAirM3H = designFresh;
            r.FreshAirRatio = freshRatio;
            r.TotalUnitCoolingKw = totalUnitCooling;         // V37
            r.SupplyTempC = supplyTemp;
            r.DewPointTempC = dewTemp;
            r.DewPointHumidityGkg = dewHumidity;
            r.SupplyEnthalpy = supplyEnthalpy;
            r.IndoorHumidityGkg = indoorHumidity;
            r.IndoorEnthalpy = indoorEnthalpy;
            r.DewPointEnthalpy = dewEnthalpy;
            r.FreshEnthalpy = freshEnthalpy;
            r.MixEnthalpy = mixEnthalpy;

            double factor = x.EffectiveSelectionFactor();
            r.Equipments.Add(new SmallEquipmentSelection
            {
                Code = string.IsNullOrEmpty(x.SystemCode) ? "AHU" : x.SystemCode,
                Name = "柜式空调机组",
                Factor = factor,
                FlowM3H = sumFlow * factor,
                CoolingKw = totalUnitCooling * factor,
                HasCooling = true
            });
            double rafFactor = factor;
            r.Equipments.Add(new SmallEquipmentSelection
            {
                Code = string.IsNullOrEmpty(x.SystemCode) ? "RAF" : "RAF-" + Suffix(x.SystemCode),
                Name = "回排风机",
                Factor = rafFactor,
                FlowM3H = totalReturn * rafFactor
            });
            return r;
        }

        // ==================================================================
        // 二、多联机 + 新风系统(公式文档「多联机+新风系统」)
        // ==================================================================
        private static SmallSystemResult CalculateVrfWithFreshAir(SmallSystemInput x)
        {
            var r = new SmallSystemResult
            {
                SystemType = x.SystemType,
                Note = "公式链:照明冷负荷 = 照明指标 × 面积;人员冷负荷 = 134 × 人数;" +
                       "房间冷负荷 = (设备 + 照明 + 人员) / 1000;" +
                       "消除余热通风量 = 3600 × 房间冷负荷 / (1.01 × (室内温度 − 过渡季通风室外温度)) / 1.2;" +
                       "换气次数通风量 = 面积 × 层高 × 换气次数;实际通风量取两者之大;人员新风量 = 30 × 人数;" +
                       "室内含湿量 = (室内相对湿度 / 100) × 室内饱和含湿量(7 次多项式),据此得室内焓;新风焓按室外湿球温度算;" +
                       "新风冷负荷 = 人员新风量 × (新风焓 − 室内焓) × 1.15 / 3600。" +
                       "输出:新风机组送风量与制冷量、送风机风量、排风机风量、多联机室外机制冷量。",
                PendingNote = "口径(2026-09-15 确认:按公式文档文字计算,示例仅用于理解公式):" +
                              "① 消除余热通风量的分母为「1.01 × (室内温度 − 过渡季通风室外温度)」,即按过渡季温差计算;" +
                              "② 室内计算温度取公式文档默认 27 ℃(示例的室内状态点焓 61.80 对应约 29 ℃,属示例取值,不作为基准)。"
            };

            double indoorHumidity = x.IndoorRhPct / 100.0 * PsychrometricHelper.SaturatedHumidityRatioGkg(x.IndoorTempC); // E83
            double indoorEnthalpy = PsychrometricHelper.EnthalpyFromMoistureGkg(x.IndoorTempC, indoorHumidity);            // C86
            double freshEnthalpy = PsychrometricHelper.EnthalpyFromMoistureGkg(
                x.OutdoorWetBulbC, PsychrometricHelper.SaturatedHumidityRatioGkg(x.OutdoorWetBulbC));                     // C87

            int index = 0;
            double sumCooling = 0, sumMoisture = 0, sumFlow = 0, sumFreshAir = 0, sumFreshCooling = 0;
            double tempDiff = x.IndoorTempC - x.TransitionOutdoorC;

            foreach (var room in x.Rooms ?? new List<SmallRoomInput>())
            {
                var row = new SmallRoomResult
                {
                    Index = ++index,
                    Name = room.Name,
                    RoomType = room.RoomType,
                    AreaM2 = room.AreaM2,
                    HeightM = room.HeightM,
                    EquipmentCoolingW = room.EquipmentCoolingW,
                    WallLengthM = room.WallLengthM,
                    RoofAreaM2 = room.RoofAreaM2,
                    Occupants = room.Occupants,
                    AirChangePerHour = GetAch(x, room)
                };

                row.LightingCoolingW = x.LightingIndexWm2 * room.AreaM2;                    // F64
                row.PeopleCoolingW = x.PersonCoolingW * room.Occupants;                     // H64
                row.TotalCoolingKw = (row.EquipmentCoolingW + row.LightingCoolingW + row.PeopleCoolingW) / 1000.0; // I64
                row.HeatVentilationM3H = Math.Abs(tempDiff) > 1e-9
                    ? 3600.0 * row.TotalCoolingKw / (1.01 * tempDiff) / 1.2 : 0;            // J64
                row.AchVentilationM3H = room.VolumeFlowByAchM3H;                            // L64
                row.ActualVentilationM3H = Math.Max(row.HeatVentilationM3H, row.AchVentilationM3H);  // N64
                row.ActualAch = room.AreaM2 * room.HeightM > 1e-9
                    ? row.ActualVentilationM3H / room.AreaM2 / room.HeightM : 0;            // O64
                row.FreshAirPersonM3H = x.FreshAirPerPersonM3H * room.Occupants;            // M64
                row.UnitCoolingKw = row.FreshAirPersonM3H * (freshEnthalpy - indoorEnthalpy) *
                                    HvacConstants.SmallEnthalpyFlowFactor / 3600.0;         // P64
                // 人员湿负荷与结构湿负荷:多联机条目未列 K/L 两式,按全空气同源口径给出(用于热湿比)
                row.PeopleMoistureGH = x.PersonMoistureGH * room.Occupants;
                row.StructureMoistureGH = (room.HeightM * room.WallLengthM + room.RoofAreaM2) * x.WallMoistureEmission;
                row.TotalMoistureGps = (row.PeopleMoistureGH + row.StructureMoistureGH) / 3600.0;

                sumCooling += row.TotalCoolingKw;          // I79
                sumMoisture += row.TotalMoistureGps;
                sumFlow += row.ActualVentilationM3H;       // N79
                sumFreshAir += row.FreshAirPersonM3H;      // M79
                sumFreshCooling += row.UnitCoolingKw;      // P79
                r.Rooms.Add(row);
            }

            r.TotalAreaM2 = TotalArea(x);
            r.TotalCoolingKw = sumCooling;
            r.TotalMoistureGps = sumMoisture;
            r.HeatHumidityRatio = sumMoisture > 1e-12 ? sumCooling / sumMoisture * 1000.0 : double.PositiveInfinity;
            r.TotalSupplyM3H = sumFlow;
            r.TotalFreshAirM3H = sumFreshAir;
            r.TotalUnitCoolingKw = sumFreshCooling;
            r.IndoorHumidityGkg = indoorHumidity;
            r.IndoorEnthalpy = indoorEnthalpy;
            r.FreshEnthalpy = freshEnthalpy;
            r.SupplyEnthalpy = indoorEnthalpy;      // 多联机无一次回风送风点,送风点焓按室内处理点显示
            r.DewPointEnthalpy = indoorEnthalpy;
            r.FreshAirRatio = sumFlow > 1e-9 ? sumFreshAir / sumFlow : 0;

            double factor = x.EffectiveSelectionFactor();
            r.Equipments.Add(new SmallEquipmentSelection
            {
                Code = string.IsNullOrEmpty(x.SystemCode) ? "PEU" : x.SystemCode,
                Name = "新风机组",
                Factor = factor,
                FlowM3H = sumFreshAir * factor,              // D92
                CoolingKw = sumFreshCooling * factor,        // E92
                HasCooling = true
            });
            r.Equipments.Add(new SmallEquipmentSelection
            {
                Code = "FAF", Name = "送风机", Factor = factor, FlowM3H = sumFlow * factor   // D93
            });
            r.Equipments.Add(new SmallEquipmentSelection
            {
                Code = "EAF", Name = "排风机", Factor = factor, FlowM3H = sumFlow * factor   // D94
            });
            r.Equipments.Add(new SmallEquipmentSelection
            {
                Code = "VRV", Name = "多联机室外机", Factor = factor,
                CoolingKw = sumCooling * factor, HasCooling = true                          // E99
            });
            return r;
        }

        // ==================================================================
        // 三、排风系统(公式文档「排风系统」:卫生间、泵房等通风)
        // ==================================================================
        private static SmallSystemResult CalculateExhaust(SmallSystemInput x)
        {
            var r = new SmallSystemResult
            {
                SystemType = x.SystemType,
                Note = "计算排风量 = 空间面积 × 高度 × 换气次数;选型排风量 = 计算排风量 × 选型系数。" +
                       "换气次数按房间类型取默认值(卫生间 20、淋浴间 10、环控机房 6、气瓶间/电缆引入间/泵房/其他 4),可逐房间修改。",
                PendingNote = "口径(2026-09-15 确认:按公式文档文字计算,示例仅用于理解公式):" +
                              "选型排风量 = 计算排风量 × 1.1。示例 EAF-A601 用的 1.3 属示例取值,不作为基准;" +
                              "选型系数可在本窗修改。"
            };

            int index = 0;
            foreach (var room in x.Rooms ?? new List<SmallRoomInput>())
            {
                double ach = GetAch(x, room);
                var row = new SmallRoomResult
                {
                    Index = ++index,
                    Name = room.Name,
                    RoomType = room.RoomType,
                    AreaM2 = room.AreaM2,
                    HeightM = room.HeightM,
                    AirChangePerHour = ach,
                    ActualAch = ach,
                    ExhaustM3H = room.AreaM2 * room.HeightM * ach      // 计算排风量
                };
                r.Rooms.Add(row);
                r.TotalExhaustM3H += row.ExhaustM3H;
            }

            r.TotalAreaM2 = TotalArea(x);
            double factor = x.EffectiveSelectionFactor();
            r.Equipments.Add(new SmallEquipmentSelection
            {
                Code = string.IsNullOrEmpty(x.SystemCode) ? "EAF" : x.SystemCode,
                Name = "排风机",
                Factor = factor,
                FlowM3H = r.TotalExhaustM3H * factor
            });
            return r;
        }

        // ==================================================================
        // 四、排烟系统(公式文档「排烟系统」:走道排烟及补风)
        // ==================================================================
        private static SmallSystemResult CalculateSmoke(SmallSystemInput x)
        {
            var r = new SmallSystemResult
            {
                SystemType = x.SystemType,
                Note = "计算排烟量 = 空间面积 × 60 m³/(h·m²);选型排烟量 = 计算排烟量 × 1.2。" +
                       "计算补风量 = 计算排烟量 × 0.6;选型补风量 = 计算补风量 × 1.1。",
                PendingNote = "口径说明:排烟按**防烟分区**逐区出量,分区面积由模型空间/房间行给出;" +
                              "排烟风机风量取选型排烟量合计(示例 SEF-A501 = 34620×1.2 = 41544)," +
                              "补风机风量取选型补风量合计(示例 FAF-A501 = 20772×1.1 = 22849)。"
            };

            int index = 0;
            foreach (var room in x.Rooms ?? new List<SmallRoomInput>())
            {
                var row = new SmallRoomResult
                {
                    Index = ++index,
                    Name = room.Name,
                    RoomType = room.RoomType,
                    AreaM2 = room.AreaM2,
                    HeightM = room.HeightM,
                    IsSmokeZone = room.IsSmokeZone
                };
                row.SmokeM3H = room.AreaM2 * HvacConstants.SmokeSystemRateM3HPerM2;              // 计算排烟量
                row.MakeupAirM3H = row.SmokeM3H * x.MakeupAirRatio;                              // 计算补风量
                r.Rooms.Add(row);
                r.TotalSmokeM3H += row.SmokeM3H;
                r.TotalMakeupAirM3H += row.MakeupAirM3H;
            }

            r.TotalAreaM2 = TotalArea(x);
            r.Equipments.Add(new SmallEquipmentSelection
            {
                Code = "SEF", Name = "排烟风机",
                Factor = x.SmokeSelectionFactor, FlowM3H = r.TotalSmokeM3H * x.SmokeSelectionFactor
            });
            r.Equipments.Add(new SmallEquipmentSelection
            {
                Code = "FAF", Name = "补风机",
                Factor = x.MakeupAirSelectionFactor, FlowM3H = r.TotalMakeupAirM3H * x.MakeupAirSelectionFactor
            });
            return r;
        }

        // ==================================================================
        // 五、送风排风排烟系统(公式文档「送风排风排烟系统」:环控机房 + 气瓶间)
        // ==================================================================
        private static SmallSystemResult CalculateSupplyExhaustSmoke(SmallSystemInput x)
        {
            var r = new SmallSystemResult
            {
                SystemType = x.SystemType,
                Note = "计算排风量 = 面积 × 层高 × 换气次数;计算送风量 = 计算排风量 × 0.9;" +
                       "计算排烟量 = 面积 × 60;计算补风量 = 计算排烟量 × 0.6。" +
                       "选型:排风机 ×1.1、排烟风机 ×1.2、送风机与补风机取「计算送风量与计算补风量的最大值」×1.1。",
                PendingNote = "口径说明:① 补风机风量按文档\"环控机房的补风机放了为计算送风量和计算补风量的最大值\"," +
                              "示例 FAF-A401 = 18612×1.1 = 20473(18612 为补风量与送风量之大者);" +
                              "② 只有环控机房参与送风/排烟/补风,气瓶间等房间只出排风量(示例气瓶间排烟量为 0)。"
            };

            int index = 0;
            double maxSupplyOrMakeup = 0;
            foreach (var room in x.Rooms ?? new List<SmallRoomInput>())
            {
                double ach = GetAch(x, room);
                var row = new SmallRoomResult
                {
                    Index = ++index,
                    Name = room.Name,
                    RoomType = room.RoomType,
                    AreaM2 = room.AreaM2,
                    HeightM = room.HeightM,
                    AirChangePerHour = ach,
                    ActualAch = ach
                };
                row.ExhaustM3H = room.AreaM2 * room.HeightM * ach;                       // 计算排风量
                if (room.IsSmokeZone)                                                    // 环控机房(参与送/排烟/补风)
                {
                    row.SupplyM3H = row.ExhaustM3H * x.SupplyFromExhaustRatio;           // 计算送风量 = 排风×0.9
                    row.SmokeM3H = room.AreaM2 * HvacConstants.SmokeSystemRateM3HPerM2;  // 计算排烟量
                    row.MakeupAirM3H = row.SmokeM3H * x.MakeupAirRatio;                   // 计算补风量
                    maxSupplyOrMakeup += Math.Max(row.SupplyM3H, row.MakeupAirM3H);
                }
                r.Rooms.Add(row);
                r.TotalExhaustM3H += row.ExhaustM3H;
                r.TotalSmokeM3H += row.SmokeM3H;
                r.TotalMakeupAirM3H += row.MakeupAirM3H;
                r.TotalSupplyM3H += row.SupplyM3H;
            }

            r.TotalAreaM2 = TotalArea(x);
            double exhaustFactor = x.EffectiveSelectionFactor();
            r.Equipments.Add(new SmallEquipmentSelection
            {
                Code = "EAF", Name = "排风机", Factor = exhaustFactor,
                FlowM3H = r.TotalExhaustM3H * exhaustFactor                                // EAF-A401 = 20307×1.1
            });
            r.Equipments.Add(new SmallEquipmentSelection
            {
                Code = "SEF", Name = "排烟风机", Factor = x.SmokeSelectionFactor,
                FlowM3H = r.TotalSmokeM3H * x.SmokeSelectionFactor                         // SEF-A401 = 31020×1.2
            });
            r.Equipments.Add(new SmallEquipmentSelection
            {
                Code = "FAF", Name = "补风机/送风机", Factor = HvacConstants.SupplySelectionFactor,
                FlowM3H = maxSupplyOrMakeup * HvacConstants.SupplySelectionFactor          // FAF-A401 = 18612×1.1
            });
            return r;
        }

        // ==================================================================
        // 六、加压送风系统(公式文档「加压送风系统」:楼梯间)
        // ==================================================================
        private static SmallSystemResult CalculatePressurization(SmallSystemInput x)
        {
            var r = new SmallSystemResult
            {
                SystemType = x.SystemType,
                Note = "门面积 = 一层内可开启门宽度 × 门高度;" +
                       "门开启风量 = 门面积 × 门缝隙漏风风速 × 疏散门开启数量;" +
                       "单个疏散门的有效漏风面积 = (门宽度 + 门高度) × 2 × 0.004;" +
                       "门缝漏风量 = 0.827 × 有效漏风面积 × √(平均压力差) × 1.25 × 漏风疏散门数量;" +
                       "余压阀漏风量 = 0.083 × 单个余压阀面积 × 余压阀数量;" +
                       "楼梯间加压送风量 = (门开启风量 + 门缝漏风量 + 余压阀漏风量) × 3600;" +
                       "选型风量 = 楼梯间加压送风量 × 1.2。",
                PendingNote = "口径(按公式计算):加压送风量 = (门开启风量 + 门缝漏风量 + 余压阀漏风量) × 3600," +
                              "量纲为 m³/h —— 公式里的 ×3600 已决定单位,文档单位标注(m³/s)与公式自相矛盾,按公式取 m³/h。"
            };

            double doorArea = x.DoorWidthM * x.DoorHeightM;                                          // D388
            double openFlow = doorArea * x.DoorLeakageVelocityMs * x.OpenDoorCount;                  // G388 (m³/s)
            double gapArea = (x.DoorWidthM + x.DoorHeightM) * 2 * HvacConstants.DoorGapWidthFactor;  // I388
            double leakFlow = HvacConstants.DoorLeakageFactor * gapArea *
                              Math.Sqrt(Math.Max(x.PressureDiffPa, 0)) *
                              HvacConstants.DoorLeakageAdditional * x.LeakDoorCount;                 // N388 (m³/s)
            double valveFlow = HvacConstants.ReliefValveLeakageFactor * x.ReliefValveAreaM2 * x.ReliefValveCount; // R388 (m³/s)

            r.DoorAreaM2 = doorArea;
            r.DoorOpenFlowM3H = openFlow * 3600.0;                    // 折算为 m³/h 便于统一显示
            r.DoorLeakFlowM3H = leakFlow * 3600.0;
            r.ReliefValveLeakFlowM3H = valveFlow * 3600.0;
            r.TotalSupplyM3H = (openFlow + leakFlow + valveFlow) * 3600.0;                            // S388
            r.PressurizationFlowM3H = r.TotalSupplyM3H;

            r.Equipments.Add(new SmallEquipmentSelection
            {
                Code = "SAF", Name = "加压送风机",
                Factor = HvacConstants.PressurizationSelectionFactor,
                FlowM3H = r.TotalSupplyM3H * HvacConstants.PressurizationSelectionFactor              // D392
            });
            return r;
        }

        // ================================================================== 辅助

        /// <summary>消除余热通风量 O27 = 冷负荷(kW)/(室内焓 − 送风点焓)/1.15×3600。</summary>
        private static double HeatVentilation(double coolingKw, double enthalpyDiff)
        {
            if (enthalpyDiff <= 1e-9) return 0;
            return coolingKw / enthalpyDiff / HvacConstants.SmallEnthalpyFlowFactor * 3600.0;
        }

        /// <summary>
        /// 全空气 C47:由热湿比与送风点焓反解室内含湿量 g/kg
        /// d = (h_送风×1000 − d_露点×ε − 1.01·t·1000)/((2500+1.84·t) − ε)。
        /// </summary>
        private static double IndoorHumidityFromRatio(double indoorTempC, double supplyEnthalpy,
            double dewHumidityGkg, double ratio)
        {
            double denominator = (2500.0 + 1.84 * indoorTempC) - ratio;
            if (Math.Abs(denominator) < 1e-9) return 0;
            double numerator = supplyEnthalpy * 1000.0 - dewHumidityGkg * ratio - 1.01 * indoorTempC * 1000.0;
            double humidity = numerator / denominator;
            return humidity < 0 ? 0 : humidity;
        }

        /// <summary>房间换气次数:未填(0)时按房间类型取文档默认值。</summary>
        private static double GetAch(SmallSystemInput x, SmallRoomInput room)
        {
            if (room.AirChangePerHour > 0) return room.AirChangePerHour;
            return string.IsNullOrEmpty(room.RoomType)
                ? HvacConstants.SmallRoomAirChangePerHour
                : SmallSystemInput.DefaultAirChangePerHour(room.RoomType);
        }

        private static double TotalArea(SmallSystemInput x)
        {
            double total = 0;
            foreach (var room in x.Rooms ?? new List<SmallRoomInput>()) total += room.AreaM2;
            return total;
        }

        /// <summary>由主设备编号推回排风机编号(如 AHU-A101 → A101)。</summary>
        private static string Suffix(string code)
        {
            int dash = code.IndexOf('-');
            return dash >= 0 && dash + 1 < code.Length ? code.Substring(dash + 1) : code;
        }
    }
}
