using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.Smoke
{
    /// <summary>
    /// 数值自测 + 结构自测:
    /// 场景1:默认参数(客流=0)不崩溃、无负数(含焓差退化保护);
    /// 场景2:典型高峰客流自检;
    /// 场景3:北京站算例回归 —— 输入取自《大系统负荷计算公式-示例.xls》,逐格断言比对(源:工作簿首表);
    /// 场景4:Ribbon 模块目录自检(7 面板 / 22 按钮,与 App.cs 的 CommandMap 键一一对应);
    /// 场景5:数据仓库(XML)往返 + 损坏文件回退;
    /// 场景6:规范知识库规则应答;
    /// 场景7:小系统计算(骨架)自检;
    /// 场景8:空间分类/聚合(公共区几何由模型空间获取);
    /// 场景9:气象参数联动(C5/F4/F6 ← 项目信息,含端到端复核北京算例)。
    /// 退出码 0 = 全部通过;1 = 存在偏差。
    /// </summary>
    internal static class Program
    {
        private static int _failures;

        private static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            var calculator = new LargeSystemLoadCalculator();

            RunScenario(calculator, "场景1:默认参数(客流=0)", new LargeSystemInput());
            RunScenario(calculator, "场景2:典型高峰客流", BuildBusyScenario());
            RunScenario(calculator, "场景3:北京站算例回归(源:大系统负荷计算公式-示例.xls)", BuildBeijingSample());
            RunCatalogChecks();
            RunRepositoryChecks();
            RunQaChecks();
            RunSmallSystemChecks();
            RunSpaceAggregatorChecks();
            RunWeatherSyncChecks(calculator);

            Console.WriteLine("==================================================");
            Console.WriteLine(_failures == 0
                ? "全部断言通过:数值与《大系统负荷计算公式-示例.xls》逐格一致;Ribbon 目录/仓库/知识库/小系统/空间聚合/气象联动自检通过。"
                : "存在 " + _failures + " 处偏差,请核对上方 FAIL 行。");
            Console.WriteLine("==================================================");
            return _failures == 0 ? 0 : 1;
        }

        // =====================================================================
        // 场景4:Ribbon 模块目录自检
        // =====================================================================
        private static void RunCatalogChecks()
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("场景4:Ribbon 模块目录自检(7 面板 / 22 按钮)");
            Console.WriteLine("==================================================");

            string[] expectedPanels = { "项目信息", "大系统", "小系统", "水力计算", "出图", "AI问答", "产品支持" };
            int[] expectedCounts = { 2, 4, 7, 3, 2, 2, 2 };

            CheckInt("面板数 = 7", ModuleCatalog.PanelOrder.Count, 7);
            CheckInt("按钮数 = 22", ModuleCatalog.All.Count, 22);
            CheckInt("面板顺序一致", string.Join(",", ModuleCatalog.PanelOrder) == string.Join(",", expectedPanels) ? 1 : 0, 1);

            for (int i = 0; i < expectedPanels.Length; i++)
            {
                CheckInt("面板「" + expectedPanels[i] + "」按钮数", ModuleCatalog.ByPanel(expectedPanels[i]).Count, expectedCounts[i]);
            }

            var keys = new HashSet<string>();
            int bad = 0;
            foreach (var module in ModuleCatalog.All)
            {
                if (string.IsNullOrWhiteSpace(module.Key) ||
                    string.IsNullOrWhiteSpace(module.Title) ||
                    string.IsNullOrWhiteSpace(module.Summary)) bad++;
                if (!keys.Add(module.Key)) bad++;
            }
            CheckInt("键唯一且字段完整(0 = 正常)", bad, 0);

            const string expectedKeys =
                "eng-info,weather,public-area,large-load,large-smoke,large-result," +
                "small-allair,small-vrf,small-exhaust,small-sesmoke,small-press,small-smoke,small-result," +
                "hyd-air,hyd-water,hyd-result,schedule,titleblock,guide,knowledge,feedback,help";
            CheckInt("键清单与 App.cs 命令注册一致", string.Join(",", ModuleCatalog.Keys) == expectedKeys ? 1 : 0, 1);

            // 待实现类模块必须写出"待补/待实现"口径,不能只是空壳说明
            int thin = 0;
            foreach (var module in ModuleCatalog.All)
            {
                if (module.Status != ModuleStatus.Implemented && module.Notes.Count == 0) thin++;
            }
            CheckInt("未实现模块均带口径说明(0 = 正常)", thin, 0);
            Console.WriteLine();
        }

        // =====================================================================
        // 场景5:数据仓库往返
        // =====================================================================
        private static void RunRepositoryChecks()
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("场景5:数据仓库(XML)往返与容错");
            Console.WriteLine("==================================================");

            string dir = Path.Combine(Path.GetTempPath(), "HVACIDA-Smoke-" + Guid.NewGuid().ToString("N"));
            try
            {
                var repo = new XmlProjectRepository(dir);

                var project = repo.LoadProject();
                project.Basic.ProjectName = "烟测工程";
                project.Basic.StationName = "XX 站";
                project.Design.LargeSystemOutdoor.SummerACWetBulbC = 25.5;
                project.Design.SmallSystemOutdoor.SummerVentDryBulbC = 26.4;
                repo.SaveProject(project);
                var project2 = repo.LoadProject();
                CheckText("工程信息往返 名称", project2.Basic.ProjectName, "烟测工程");
                Check("工程信息往返 大系统室外湿球", project2.Design.LargeSystemOutdoor.SummerACWetBulbC, 25.5);
                Check("工程信息往返 小系统室外通风", project2.Design.SmallSystemOutdoor.SummerVentDryBulbC, 26.4);

                var large = repo.LoadLargeSystem();
                large.HallAreaM2 = 2345.5;
                large.UpLineBoardCount = 777;
                repo.SaveLargeSystem(large);
                var large2 = repo.LoadLargeSystem();
                Check("大系统输入往返 站厅面积", large2.HallAreaM2, 2345.5);
                Check("大系统输入往返 上行上客量", large2.UpLineBoardCount, 777);

                var small = repo.LoadSmallSystem();
                small.AreaM2 = 88.5;
                small.SystemType = SmallSystemType.AllAirOnceReturn;
                repo.SaveSmallSystem(small);
                var small2 = repo.LoadSmallSystem();
                Check("小系统输入往返 面积", small2.AreaM2, 88.5);
                CheckText("小系统输入往返 类型", small2.SystemType.ToString(), SmallSystemType.AllAirOnceReturn.ToString());

                // 损坏文件必须回退默认而不是抛异常(插件不能因数据文件坏掉而打不开窗口)
                File.WriteAllText(Path.Combine(dir, "large-system.xml"), "<broken");
                CheckInt("损坏文件回退默认", repo.LoadLargeSystem().HallAreaM2 > 0 ? 1 : 0, 1);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* 忽略清理失败 */ }
            }
            Console.WriteLine();
        }

        // =====================================================================
        // 场景6:规范知识库
        // =====================================================================
        private static void RunQaChecks()
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("场景6:规范知识库规则应答");
            Console.WriteLine("==================================================");

            var qa = new DesignQaService();
            string a1 = qa.Answer("站厅夏季送风温差取多少?");
            CheckInt("送风温差命中(含「10 ℃」)", a1.Contains("10 ℃") ? 1 : 0, 1);

            string a2 = qa.Answer("排烟风机怎么选?");
            CheckInt("排烟命中(含 60 m³/(h·m²))", a2.Contains("60") ? 1 : 0, 1);
            CheckInt("排烟命中(含选型系数 1.2)", a2.Contains("1.2") ? 1 : 0, 1);

            string a3 = qa.Answer("新风量怎么确定?");
            CheckInt("新风命中(含 20 m³/(h·人))", a3.Contains("20") ? 1 : 0, 1);

            string a4 = qa.Answer("今天天气怎么样");
            CheckInt("未覆盖问题给出知识范围", a4.Contains("知识库") ? 1 : 0, 1);
            CheckInt("常用问题数量 >= 5", qa.SampleQuestions.Count >= 5 ? 1 : 0, 1);
            Console.WriteLine();
        }

        // =====================================================================
        // 场景7:小系统计算(骨架)
        // =====================================================================
        private static void RunSmallSystemChecks()
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("场景7:小系统计算(骨架)自检");
            Console.WriteLine("==================================================");

            var input = new SmallSystemInput();
            var result = new SmallSystemLoadCalculator().Calculate(input);
            Console.WriteLine(ResultFormatter.FormatSmall(input, result));
            Console.WriteLine("-- 自检 --");
            CheckInt("总冷负荷 > 0", result.TotalCoolingW > 0 ? 1 : 0, 1);
            CheckInt("实际通风量 >= 换气次数通风量", result.ActualVentilationM3H >= result.VentilationByACHM3H ? 1 : 0, 1);
            CheckInt("新风量 <= 实际通风量", result.FreshAirM3H <= result.ActualVentilationM3H + 1e-9 ? 1 : 0, 1);
            Console.WriteLine();
        }

        // =====================================================================
        // 场景8:空间分类与公共区几何聚合(需求 2.2.1 / 2.2.3.1 D55/D56/C13/C14)
        // =====================================================================
        private static void RunSpaceAggregatorChecks()
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("场景8:空间分类/聚合(公共区几何 ← 模型空间)");
            Console.WriteLine("==================================================");

            var spaces = new List<SpaceSnapshot>
            {
                Space("站厅层-公共区A", "101", "站厅层", 1200, 4.9, 0, 0, 100, 40),
                Space("站厅层-公共区B", "102", "站厅层", 800, 4.9, 100, 0, 160, 40),
                Space("站厅层-付费区", "103", "站厅层", 300, 4.9, 0, 0, 20, 20),
                Space("站台层-公共区", "201", "站台层", 1620, 4.5, 0, 0, 140, 12),
                Space("站台层-设备房", "202", "站台层", 50, 3.0, 200, 0, 205, 10),
                Space("活塞风道", "301", "站厅层", 40, 4.0, 300, 0, 305, 8),
                Space("环控机房", "302", "设备层", 80, 3.5, 0, 60, 10, 68),
                Space("未放置空间", "401", "站厅层", 0, 0, 0, 0, 0, 0)
            };

            var c = PublicAreaAggregator.Classify(spaces);
            Console.WriteLine("分类:" + c.Summary);
            Console.WriteLine("聚合(站厅):" + PublicAreaAggregator.Aggregate(c.Hall, PublicAreaTarget.Hall).Summary);
            Console.WriteLine("聚合(站台):" + PublicAreaAggregator.Aggregate(c.Platform, PublicAreaTarget.Platform).Summary);
            Console.WriteLine("-- 自检 --");

            CheckInt("站厅识别 2 个(「付费区」「活塞风道」被「公共区」口径筛掉)", c.Hall.Count, 2);
            CheckInt("站厅按公共区筛选 = true", c.HallPublicAreaOnly ? 1 : 0, 1);
            CheckInt("站厅被筛掉 2 个且列入明细", c.HallExcluded.Count, 2);
            CheckInt("站台识别 1 个(「设备房」被筛掉)", c.Platform.Count, 1);
            CheckInt("站台按公共区筛选 = true", c.PlatformPublicAreaOnly ? 1 : 0, 1);
            CheckInt("站台被筛掉 1 个且列入明细", c.PlatformExcluded.Count, 1);
            CheckInt("未识别 1 个(设备层环控机房)", c.Unclassified.Count, 1);
            CheckInt("未放置空间被跳过", c.SkippedUnplaced, 1);

            var hall = PublicAreaAggregator.Aggregate(c.Hall, PublicAreaTarget.Hall);
            Check("站厅面积合计 = 2000 m²", hall.AreaM2, 2000);
            Check("站厅层高 4.9 m", hall.HeightM, 4.9);
            Check("站厅长度 = 包围盒长边 160 m", hall.LengthM, 160);
            CheckInt("站厅参与合计空间数 = 2", hall.SpaceCount, 2);

            var platform = PublicAreaAggregator.Aggregate(c.Platform, PublicAreaTarget.Platform);
            Check("站台面积 = 1620 m²", platform.AreaM2, 1620);
            Check("站台层高 4.5 m", platform.HeightM, 4.5);

            // 手动拾取:目标已知,不做名称推断,也不做"公共区"筛选
            var manual = PublicAreaAggregator.Aggregate(new[] { spaces[2], spaces[5] }, PublicAreaTarget.Hall);
            Check("手动拾取按面积加权(300×4.9 + 40×4.0)/340", manual.HeightM, (300 * 4.9 + 40 * 4.0) / 340);
            Check("手动拾取合计面积 340 m²", manual.AreaM2, 340);

            var unplacedOnly = PublicAreaAggregator.Aggregate(
                new[] { spaces.Find(s => s.Name == "未放置空间") }, PublicAreaTarget.Hall);
            CheckInt("全部未放置时合计 0 个空间", unplacedOnly.SpaceCount, 0);
            CheckInt("全部未放置时计入跳过数", unplacedOnly.SkippedCount, 1);

            // 缺高度的空间:不参与层高加权,但要计数提示
            var noHeight = new List<SpaceSnapshot>
            {
                Space("站厅层-公共区", "1", "站厅层", 100, 0, 0, 0, 10, 10),
                Space("站厅层-公共区", "2", "站厅层", 100, 5.0, 10, 0, 20, 10)
            };
            var mixed = PublicAreaAggregator.Aggregate(noHeight, PublicAreaTarget.Hall);
            Check("缺高度空间不参与加权(层高仍为 5.0)", mixed.HeightM, 5.0);
            CheckInt("缺高度空间计数 = 1", mixed.HeightMissingCount, 1);
            Console.WriteLine();
        }

        // =====================================================================
        // 场景9:气象参数联动(C5/F4/F6)与端到端复核
        // =====================================================================
        private static void RunWeatherSyncChecks(ILargeSystemLoadCalculator calculator)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("场景9:气象参数联动(项目信息 → 大系统 C5/F4/F6)");
            Console.WriteLine("==================================================");

            // (1) 典型北京气象参数回填
            var design = TypicalBeijingDesign();
            var input = new LargeSystemInput();
            var sync = ProjectDesignSync.ApplyWeather(design, input);
            Console.WriteLine(sync.Note);
            CheckInt("三格全部被回填", sync.AppliedCount, 3);
            Check("C5 湿球 ← 室外", input.OutdoorWetBulbC, 25);
            Check("F4 站厅干球 ← 室内", input.HallDesignTempC, 29);
            Check("F6 站台干球 ← 室内", input.PlatformDesignTempC, 27);
            CheckInt("无未填告警", sync.UnsetFields.Count, 0);
            CheckText("告警文案为空", sync.Warning, "");

            // (2) 端到端:北京算例其余格照抄,只靠联动补 C5/F4/F6 → 结果必须与手填逐格一致
            var beijing = BuildBeijingSample();
            beijing.OutdoorWetBulbC = 0;
            beijing.HallDesignTempC = 0;
            beijing.PlatformDesignTempC = 0;
            var beijingSync = ProjectDesignSync.ApplyWeather(design, beijing);
            CheckInt("北京算例三格由气象参数补入", beijingSync.AppliedCount, 3);
            LargeSystemResult synced = calculator.Calculate(beijing);
            Console.WriteLine("-- 端到端:E159 总制冷量(手填算例 = 381.598170929697)--");
            Check("E159 总制冷量(联动后)", synced.TotalCoolingKw, 381.598170929697);
            Check("C125 总送风量(联动后)", synced.TotalSupplyFlowM3H, 87767.9950526333);
            Check("A165 单端机组风量(联动后)", synced.UnitSupplyFlowM3H, 43883.9975263166);
            Check("B165 单端机组冷量(联动后)", synced.UnitCoolingKw, 190.799085464848);

            // (3) 气象参数未填时不得覆盖用户已填值,只登记待补
            //     注:DesignConditionParams 的室内干球默认 30/28 是"有值"的,故只有 C5 会判为未填
            var blank = new DesignConditionParams();
            var keep = new LargeSystemInput { OutdoorWetBulbC = 25.5 };
            var blankSync = ProjectDesignSync.ApplyWeather(blank, keep);
            CheckInt("未填时不产生回填", blankSync.AppliedCount, 0);
            Check("未填时不覆盖已填的 C5", keep.OutdoorWetBulbC, 25.5);
            CheckInt("未填项被登记(仅 C5)", blankSync.UnsetFields.Count, 1);
            CheckInt("未填项文案指向 C5", blankSync.UnsetFields[0].StartsWith("C5") ? 1 : 0, 1);
            CheckInt("未填时给出告警", blankSync.Warning.Length > 0 ? 1 : 0, 1);

            // (4) 服务层:勾选/取消联动与落盘往返
            string dir = Path.Combine(Path.GetTempPath(), "HVACIDA-Weather-" + Guid.NewGuid().ToString("N"));
            try
            {
                var repo = new XmlProjectRepository(dir);
                var project = repo.LoadProject();
                project.Design = TypicalBeijingDesign();
                repo.SaveProject(project);

                var service = new LargeSystemInputService(repo);
                var loaded = service.Load();
                Check("服务层 Load 自动回填 C5", loaded.OutdoorWetBulbC, 25);
                Check("服务层 Load 自动回填 F4", loaded.HallDesignTempC, 29);

                loaded.WeatherManuallyOverridden = true;
                loaded.OutdoorWetBulbC = 27.9;
                service.Save(loaded);

                var reloaded = service.Load();
                Check("脱离联动后保留手工值", reloaded.OutdoorWetBulbC, 27.9);

                reloaded.WeatherManuallyOverridden = false;
                var back = service.Sync(reloaded);
                CheckInt("重新联动恢复回填", back.AppliedCount, 1);
                Check("恢复联动后 C5 回到项目信息值", reloaded.OutdoorWetBulbC, 25);

                // 旧版 XML(没有 WeatherManuallyOverridden 元素)必须默认开启联动
                File.WriteAllText(Path.Combine(dir, "large-system.xml"),
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?><LargeSystemInput><OutdoorWetBulbC>0</OutdoorWetBulbC>" +
                    "<HallAreaM2>1500</HallAreaM2></LargeSystemInput>");
                var legacy = new LargeSystemInputService(new XmlProjectRepository(dir)).Load();
                Check("旧版 XML 缺元素 → 默认开启联动(C5 被回填)", legacy.OutdoorWetBulbC, 25);
                Check("旧版 XML 其余字段保留", legacy.HallAreaM2, 1500);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* 忽略清理失败 */ }
            }

            Console.WriteLine();
        }

        /// <summary>构造一个空间快照(测试数据;面积/层高/包围盒单位已是 m)。</summary>
        private static SpaceSnapshot Space(string name, string number, string level, double areaM2, double heightM,
            double minX, double minY, double maxX, double maxY)
        {
            return new SpaceSnapshot
            {
                ElementId = Math.Abs((name + number).GetHashCode() % 100000),
                Name = name,
                Number = number,
                LevelName = level,
                AreaM2 = areaM2,
                VolumeM3 = areaM2 * heightM,
                HeightM = heightM,
                MinXM = minX,
                MinYM = minY,
                MaxXM = maxX,
                MaxYM = maxY
            };
        }

        /// <summary>典型北京气象参数(与「气象参数」窗【从气象数据库获取】内置值一致)。</summary>
        private static DesignConditionParams TypicalBeijingDesign()
        {
            var d = new DesignConditionParams();
            d.LargeSystemOutdoor.SummerACDryBulbC = 31.0;
            d.LargeSystemOutdoor.SummerACWetBulbC = 25.0;
            d.LargeSystemOutdoor.SummerVentDryBulbC = 26.4;
            d.LargeSystemIndoor.HallDryBulbC = 29.0;
            d.LargeSystemIndoor.PlatformDryBulbC = 27.0;
            return d;
        }

        private static LargeSystemInput BuildBusyScenario()
        {
            return new LargeSystemInput
            {
                OutdoorWetBulbC = 27.9,
                HallDesignTempC = 30,
                PlatformDesignTempC = 28,
                UpLineBoardCount = 6000,
                DownLineBoardCount = 6500,
                UpLineAlightCount = 5800,
                DownLineAlightCount = 6200,
                TransferBoardCount = 3000,
                TransferAlightCount = 2800,
                HallAreaM2 = 1500,
                PlatformAreaM2 = 1200
            };
        }

        /// <summary>北京站算例输入(与工作簿首表一致)。</summary>
        private static LargeSystemInput BuildBeijingSample()
        {
            var x = new LargeSystemInput
            {
                OutdoorWetBulbC = 25,
                HallDesignTempC = 29,
                PlatformDesignTempC = 27,
                SupplyTempDiffC = 10,
                DuctTempRiseC = 1.5,
                DewPointRelativeHumidityPercent = 95,
                FreshAirPerPersonM3H = 20,

                HallAreaM2 = 2000,
                PlatformAreaM2 = 1620,
                HallHeightM = 4.9,
                HallLengthM = 101,

                EntranceAWidthM = 5.3, EntranceAHeightM = 4,
                EntranceBWidthM = 5.3, EntranceBHeightM = 4,
                EntranceCWidthM = 6, EntranceCHeightM = 4,
                EntranceDWidthM = 4, EntranceDHeightM = 4,
                EntranceLoadIndexW = 200,

                UpLineBoardCount = 701,
                UpLineAlightCount = 1633,
                DownLineBoardCount = 1206,
                DownLineAlightCount = 574,
                TransferBoardCount = 0,
                TransferAlightCount = 0,

                HallBoardStayMin = 2, HallAlightStayMin = 1.5,
                HallTransferBoardStayMin = 2, HallTransferAlightStayMin = 1.5,
                PlatformBoardStayMin = 2, PlatformAlightStayMin = 1.5,
                PlatformTransferBoardStayMin = 2, PlatformTransferAlightStayMin = 1.5,

                ClusterFactor = 0.89,
                SuperPeakHourFactor = 1,

                HallLightingWm2 = 8, PlatformLightingWm2 = 8,
                HallAdvertKw = 60, PlatformAdvertKw = 10,
                EscalatorKwPerUnit = 1.5, EscalatorCount = 4,
                ElevatorKwPerUnit = 1.5, ElevatorCount = 1,
                AfcKwPerUnit = 18, AfcCount = 1,

                PsdHeatTransferCoeffWm2C = 3.2,
                PsdHeightM = 3, PsdLengthM = 292, PsdTempDiffC = 8, PsdHeatSafetyFactor = 1.5,
                HallPsdTransferKw = 0, HallPsdLeakKw = 30, HallPsdHeatKw = 0,
                PlatformPsdLeakKw = 45, PlatformPsdSystemHeatKw = 4
            };
            return x;
        }

        private static void RunScenario(ILargeSystemLoadCalculator calculator, string title, LargeSystemInput input)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine(title);
            Console.WriteLine("==================================================");
            LargeSystemResult r = calculator.Calculate(input);

            if (title.StartsWith("场景3"))
            {
                AssertBeijing(r);
            }
            else
            {
                Console.WriteLine(ResultFormatter.FormatLarge(input, r));
                Console.WriteLine("-- 自检 --");
                Check("送风量>=0", r.TotalSupplyFlowM3H >= 0 ? 1 : 0, 1);
                Check("制冷量>=0", r.TotalCoolingKw >= 0 ? 1 : 0, 1);
                Check("排烟站厅=面积×60", r.HallSmokeFlowM3H, input.HallAreaM2 * 60);
                Check("排烟站台=面积×60", r.PlatformSmokeFlowM3H, input.PlatformAreaM2 * 60);
            }
            Console.WriteLine();
        }

        /// <summary>北京算例逐格断言(期望值来自工作簿计算单元格)。</summary>
        private static void AssertBeijing(LargeSystemResult r)
        {
            Check("C39/C40 高峰客流", r.HallPeakFlowPm, 105.680083333333);
            Check("D107 站厅冷负荷合计", r.HallTotalCoolingKw, 163.463775166667);
            Check("E107 站台冷负荷合计", r.PlatformTotalCoolingKw, 128.476495083333);
            Check("D112 站厅湿负荷", r.HallTotalMoistureGps, 7.05388268518519);
            Check("E112 站台湿负荷", r.PlatformTotalMoistureGps, 5.69498226851852);
            Check("D113 站厅热湿比", r.HallHeatHumidityRatio, 23173.5885698778);
            Check("E113 站台热湿比", r.PlatformHeatHumidityRatio, 22559.5952762738);
            Check("A118 站厅送风温度", r.HallSupplyTempC, 19);
            Check("B118 露点温度", r.DewPointTempC, 17.5);
            Check("D118 饱和含湿量(露点)", r.SaturatedMoistureAtDewGkg, 12.5129773453979);
            Check("E118 露点含湿量", r.DewPointMoistureGkg, 11.8873284781281);
            Check("A121 送风点焓", r.HallSupplyEnthalpy, 49.7239021989155);
            Check("B121 站厅室内含湿量", r.HallIndoorHumidityGkg, 12.3683477263088);
            Check("C121 站厅室内焓", r.HallIndoorEnthalpy, 61.2708443504478);
            Check("D121 站台室内含湿量", r.PlatformIndoorHumidityGkg, 12.279882937592);
            Check("E121 站台室内焓", r.PlatformIndoorEnthalpy, 58.9797719283197);
            Check("A125 站厅送风量", r.HallSupplyFlowM3H, 44315.8613564506);
            Check("B125 站台送风量", r.PlatformSupplyFlowM3H, 43452.1336961827);
            Check("C125 总送风量", r.TotalSupplyFlowM3H, 87767.9950526333);
            Check("A136 实际新风量", r.ActualFreshAirM3H, 8776.79950526333);
            Check("B136 新风比例", r.FreshAirRatio, 0.1);
            Check("C136 站厅回风量", r.HallReturnFlowM3H, 39884.2752208055);
            Check("D136 站台回风量", r.PlatformReturnFlowM3H, 39106.9203265645);
            Check("E136 总回风量", r.TotalReturnFlowM3H, 78991.1955473699);
            Check("C145 回风混合焓", r.ReturnMixEnthalpy, 60.1365813980556);
            Check("C149 新风焓", r.FreshEnthalpy, 76.6369145488281);
            Check("C146 新回风混合焓", r.FreshReturnMixEnthalpy, 61.7866147131328);
            Check("C143 露点焓", r.DewPointEnthalpy, 48.1760931723159);
            Check("E159 总制冷量", r.TotalCoolingKw, 381.598170929697);
            Check("A165 单端机组风量", r.UnitSupplyFlowM3H, 43883.9975263166);
            Check("B165 单端机组冷量", r.UnitCoolingKw, 190.799085464848);
        }

        private static void Check(string name, double actual, double expected)
        {
            double tolerance = Math.Max(1e-6, Math.Abs(expected) * 1e-6);
            bool pass = Math.Abs(actual - expected) <= tolerance;
            Console.WriteLine((pass ? "PASS  " : "FAIL  ") + name +
                              "  actual=" + actual.ToString("R") + "  expected=" + expected.ToString("R"));
            if (!pass) _failures++;
        }

        /// <summary>整数/布尔型断言(结构自检用)。</summary>
        private static void CheckInt(string name, int actual, int expected)
        {
            bool pass = actual == expected;
            Console.WriteLine((pass ? "PASS  " : "FAIL  ") + name +
                              "  actual=" + actual + "  expected=" + expected);
            if (!pass) _failures++;
        }

        /// <summary>字符串断言(结构自检用)。</summary>
        private static void CheckText(string name, string actual, string expected)
        {
            bool pass = string.Equals(actual, expected, StringComparison.Ordinal);
            Console.WriteLine((pass ? "PASS  " : "FAIL  ") + name +
                              "  actual=\"" + actual + "\"  expected=\"" + expected + "\"");
            if (!pass) _failures++;
        }
    }
}
