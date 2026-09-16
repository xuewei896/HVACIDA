using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;
using HVACIDA.Core.Utils;

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
    /// 场景8:空间分类/聚合(公共区几何由模型空间获取);
    /// 场景9:气象参数联动(C5/F4/F6 ← 项目信息,含端到端复核北京算例);
    /// 场景10:全国省市气象数据库(GB 50736-2012 附录A,294 台站);
    /// 场景11:大系统排烟计算(面积×60 / 选型×1.2 / 2 台 / 取大者);
    /// 场景12:小系统六类系统 —— 按《小系统空调负荷、送排风、排烟计算公式.docx》示例逐格复算。
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
            RunSpaceAggregatorChecks();
            RunWeatherSyncChecks(calculator);
            RunWeatherDatabaseChecks();
            RunLargeSmokeChecks();
            RunSmallSystemChecks();
            RunHydraulicChecks();
            RunHydraulicSummaryChecks();
            RunCalculationExcelChecks();
            RunMaterialTakeoffChecks();
            RunSheetCatalogChecks();
            RunKnowledgeChecks();
            RunLegendChecks();

            Console.WriteLine("==================================================");
            Console.WriteLine(_failures == 0
                ? "全部断言通过:数值与《大系统负荷计算公式-示例.xls》逐格一致;Ribbon 目录/仓库/知识库/小系统六类/空间聚合/气象联动/省市气象库/排烟计算/水力计算(含多系统汇总与 Excel 导出)/计算书 Excel 导出推广自检通过。"
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
                small.Rooms.Add(SmallRoomInput.Create("烟测房间", 88.5, 4.5));
                small.SystemType = SmallSystemType.AllAirOnceReturn;
                repo.SaveSmallSystem(small);
                var small2 = repo.LoadSmallSystem();
                Check("小系统输入往返 房间面积", small2.Rooms[0].AreaM2, 88.5);
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
            var codeHits = string.Join(",", System.Text.RegularExpressions.Regex.Matches(
                ResultFormatter.FormatLarge(beijing, synced), @"\b[A-Z]{1,2}\d{2,3}\b").Cast<System.Text.RegularExpressions.Match>()
                .Select(m => m.Value).Distinct().ToArray());
            CheckInt("大系统计算书不含单元格编号(命中: " + codeHits + ")", codeHits.Length == 0 ? 0 : 1, 0);
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

        // =====================================================================
        // 场景10:全国省市气象数据库(GB 50736-2012 附录A)
        // =====================================================================
        private static void RunWeatherDatabaseChecks()
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("场景10:全国省市气象数据库(内嵌 weather-db.csv)");
            Console.WriteLine("==================================================");

            var db = WeatherDatabase.Default;
            Console.WriteLine("台站数 = " + db.All.Count + ",省级行政区 = " + db.Provinces.Count);
            Console.WriteLine(db.SourceNote.Replace("\n", " | "));
            Console.WriteLine("-- 自检 --");

            // 规模:附录A 条文说明为"28 个省级行政区、4 个直辖市所属 294 个台站"
            CheckInt("台站数 = 294", db.All.Count, 294);
            CheckInt("省级行政区数 = 31", db.Provinces.Count, 31);
            CheckText("首个省级行政区 = 北京(按标准顺序)", db.Provinces[0], "北京");
            CheckInt("含全部 4 个直辖市",
                (db.Provinces.Contains("北京") ? 1 : 0) + (db.Provinces.Contains("天津") ? 1 : 0) +
                (db.Provinces.Contains("上海") ? 1 : 0) + (db.Provinces.Contains("重庆") ? 1 : 0), 4);
            CheckInt("含全部 5 个自治区",
                (db.Provinces.Contains("内蒙古") ? 1 : 0) + (db.Provinces.Contains("广西") ? 1 : 0) +
                (db.Provinces.Contains("西藏") ? 1 : 0) + (db.Provinces.Contains("宁夏") ? 1 : 0) +
                (db.Provinces.Contains("新疆") ? 1 : 0), 5);

            // 逐行完整性:台站号唯一且 5 位;驱动计算的字段必须有值;湿球不得大于干球
            int badId = 0, badWet = 0, missingRequired = 0;
            var ids = new HashSet<string>();
            var pressureMismatch = new List<string>();
            foreach (var s in db.All)
            {
                if (s.StationId.Length != 5 || !ids.Add(s.StationId)) badId++;
                if (!s.SummerAcDryBulbC.HasValue || !s.SummerVentDryBulbC.HasValue ||
                    !s.WinterVentOutdoorC.HasValue || !s.WinterAcOutdoorC.HasValue ||
                    !s.SummerVentRhPct.HasValue || !s.SummerAtmPressureHpa.HasValue ||
                    !s.WinterAtmPressureHpa.HasValue) missingRequired++;
                if (s.SummerAcWetBulbC.HasValue && s.SummerAcDryBulbC.HasValue &&
                    s.SummerAcWetBulbC.Value > s.SummerAcDryBulbC.Value) badWet++;

                // 大气压力与海拔必须物理自洽(海拔高→气压低):这条能独立抓出列位串行
                if (s.ElevationM.HasValue && s.SummerAtmPressureHpa.HasValue)
                {
                    double expected = 1013.25 * Math.Pow(1.0 - 2.25577e-5 * Math.Max(s.ElevationM.Value, 0), 5.25588);
                    if (Math.Abs(expected - s.SummerAtmPressureHpa.Value) > 60.0) pressureMismatch.Add(s.City);
                }
            }
            CheckInt("台站号均为 5 位且唯一(0 = 正常)", badId, 0);
            CheckInt("关键字段无缺失(0 = 正常)", missingRequired, 0);
            CheckInt("湿球 ≤ 干球(0 = 正常)", badWet, 0);

            // 气压/海拔自洽检查的目的是"抓列位串行"。源文件有 2 处海拔笔误,并被**气压列反证**:
            // 山南地区标 9280 m(该高度应 ~295 hPa,表里是 602.7 hPa ⇒ 实为 ~4280 m,疑 4→9 笔误);
            // 黄南州标 8500 m 同理。故断言"不一致的**只有**这 2 个已知台站" —— 换别的台站出问题就 FAIL。
            CheckInt("气压/海拔不一致的台站 = 2 个(已知源文件海拔笔误)", pressureMismatch.Count, 2);
            CheckInt("不一致者含 山南地区(报告中已记录待人工核对)",
                pressureMismatch.Contains("山南地区") ? 1 : 0, 1);
            CheckInt("不一致者含 黄南州(报告中已记录待人工核对)",
                pressureMismatch.Contains("黄南州") ? 1 : 0, 1);

            // 标准本身留空的湿球温度:必须保持"未填"而不是被猜出来
            var noWet = db.All.Where(s => !s.SummerAcWetBulbC.HasValue).Select(s => s.City).ToList();
            CheckInt("湿球温度缺记录 = 6 个台站(附录A 条文说明所列缺口)", noWet.Count, 6);
            CheckInt("咸阳在其中(条文说明点名)", noWet.Contains("咸阳") ? 1 : 0, 1);
            CheckInt("黔南州在其中(条文说明点名)", noWet.Contains("黔南州") ? 1 : 0, 1);

            // 逐值核对北京台站(与源文件表格同一行)
            var bj = db.Find("北京", "北京");
            CheckInt("能按省+市取到北京台站", bj == null ? 0 : 1, 1);
            if (bj != null)
            {
                CheckText("北京 台站号 = 54511", bj.StationId, "54511");
                Check("北京 夏季空调干球 33.5", bj.SummerAcDryBulbC ?? -999, 33.5);
                Check("北京 夏季空调湿球 26.4", bj.SummerAcWetBulbC ?? -999, 26.4);
                Check("北京 夏季通风 29.7", bj.SummerVentDryBulbC ?? -999, 29.7);
                Check("北京 冬季空调 -9.9", bj.WinterAcOutdoorC ?? -999, -9.9);
                Check("北京 冬季通风 -3.6", bj.WinterVentOutdoorC ?? -999, -3.6);
                Check("北京 供暖室外 -7.6", bj.HeatingOutdoorC ?? -999, -7.6);
                Check("北京 夏季通风相对湿度 61", bj.SummerVentRhPct ?? -999, 61);
                Check("北京 夏季大气压力 1000.2 hPa", bj.SummerAtmPressureHpa ?? -999, 1000.2);
                Check("北京 冬季大气压力 1021.7 hPa", bj.WinterAtmPressureHpa ?? -999, 1021.7);
                Check("北京 海拔 31.3 m", bj.ElevationM ?? -999, 31.3);
                CheckText("北京 统计年份 1971~2000", bj.StatsPeriod, "1971~2000");
            }

            // 省→市级联
            var gd = db.CitiesOf("广东");
            CheckInt("广东省城市数 = 15", gd.Count, 15);
            CheckInt("广东含深圳", gd.Contains("深圳") ? 1 : 0, 1);
            CheckInt("广东含汕头", gd.Contains("汕头") ? 1 : 0, 1);
            CheckInt("未知省返回空列表", db.CitiesOf("不存在的省").Count, 0);
            CheckInt("未知城市返回 null", db.Find("广东", "不存在的市") == null ? 1 : 0, 1);

            // 源文件杂质("、27.7")必须已被规范化,不能变成缺值
            var st = db.Find("广东", "汕头");
            Check("汕头 湿球 = 27.7(源文件写作「、27.7」)", st == null ? -999 : (st.SummerAcWetBulbC ?? -999), 27.7);

            // 回填映射:室外 8 项 + 大气压力 + 相对湿度;室内设计参数不得被动
            var design = new DesignConditionParams();
            design.LargeSystemIndoor.HallDryBulbC = 29.0;
            var apply = WeatherDatabase.Apply(bj, design);
            CheckInt("回填项数 = 10", apply.FilledCount, 10);
            Check("→ 大系统夏季空调干球", design.LargeSystemOutdoor.SummerACDryBulbC, 33.5);
            Check("→ 大系统夏季空调湿球", design.LargeSystemOutdoor.SummerACWetBulbC, 26.4);
            Check("→ 大系统夏季通风", design.LargeSystemOutdoor.SummerVentDryBulbC, 29.7);
            Check("→ 大系统冬季通风", design.LargeSystemOutdoor.WinterVentDryBulbC, -3.6);
            Check("→ 大系统冬季空调", design.LargeSystemOutdoor.WinterACDryBulbC, -9.9);
            Check("→ 小系统夏季空调干球(与室外同源)", design.SmallSystemOutdoor.SummerACDryBulbC, 33.5);
            Check("→ 小系统夏季空调湿球(与室外同源)", design.SmallSystemOutdoor.SummerACWetBulbC, 26.4);
            Check("→ 小系统夏季通风(与室外同源)", design.SmallSystemOutdoor.SummerVentDryBulbC, 29.7);
            Check("→ 大气压力取夏季值 1000.2 hPa = 100.02 kPa", design.Common.AtmosphericPressureKPa, 100.02);
            Check("→ 室外相对湿度取夏季通风值", design.Common.OutdoorRelativeHumidityPercent, 61);
            Check("室内设计参数不被气象库改动(站厅 29 ℃)", design.LargeSystemIndoor.HallDryBulbC, 29.0);
            CheckText("状态文案含台站", apply.Note.Contains("54511") ? "1" : "0", "1");

            // 缺湿球台站:不得覆盖,且必须给出告警
            var xianyang = db.Find("陕西", "咸阳");
            var design2 = new DesignConditionParams();
            design2.LargeSystemOutdoor.SummerACWetBulbC = 25.5;
            var apply2 = WeatherDatabase.Apply(xianyang, design2);
            Check("缺湿球时不覆盖原值", design2.LargeSystemOutdoor.SummerACWetBulbC, 25.5);
            CheckInt("缺湿球时给出告警", apply2.Warning.Length > 0 ? 1 : 0, 1);
            // 湿球同时喂"大系统室外"与"小系统室外"两处,故缺 1 个源值 → 少 2 个写入项
            CheckInt("缺湿球时回填项数 = 8(10 - 2,湿球喂大小系统两处)", apply2.FilledCount, 8);

            // 与"气象参数联动"串起来:北京台站回填后,大系统 C5 必须取 26.4(而非旧的内置典型值 25.0)
            var design3 = new DesignConditionParams();
            WeatherDatabase.Apply(bj, design3);
            var largeInput = new LargeSystemInput();
            var sync = ProjectDesignSync.ApplyWeather(design3, largeInput);
            // 气象库只提供**室外**参数:F4/F6(站厅/站台室内设计温度)属设计取值,不由气象库改动,
            // 故此时只有 C5 会变(与 LargeSystemInput 默认的 30/28 相同 → 不计入变化格数)。
            CheckInt("北京台站 → 只有 C5 被回填(室内设计温度不由气象库决定)", sync.AppliedCount, 1);
            Check("北京 C5 湿球 = 26.4(GB 50736 值,而非旧内置典型值 25.0)", largeInput.OutdoorWetBulbC, 26.4);
            Check("F4 站厅设计温度保持设计值 30 ℃(非气象数据)", largeInput.HallDesignTempC, 30);

            Console.WriteLine();
        }

        // =====================================================================
        // 场景11:大系统排烟计算(需求 2.2.3.1)
        // =====================================================================
        private static void RunLargeSmokeChecks()
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("场景11:大系统排烟计算(面积×60 → 选型×1.2 → 2 台取大者)");
            Console.WriteLine("==================================================");

            var areas = new LargeSystemInput { HallAreaM2 = 2000, PlatformAreaM2 = 1620 };  // 北京算例面积
            var parameters = new LargeSmokeInput();                                        // 默认 60 / 1.2 / 2 台
            var result = new LargeSmokeCalculator().Calculate(areas, parameters);
            Console.WriteLine(ResultFormatter.FormatLargeSmoke(areas, parameters, result));
            Console.WriteLine("-- 自检 --");

            CheckInt("结果表行数 = 2(站厅/站台)", result.Zones.Count, 2);
            CheckInt("默认参数 = 60 / 1.2 / 2 台",
                parameters.SmokeRateM3HPerM2 == 60.0 && parameters.SelectionFactor == 1.2 && parameters.FanUnitCount == 2.0 ? 1 : 0, 1);

            var hall = result.Zones[0];
            var platform = result.Zones[1];

            // 计算排烟量 = 面积 × 60(示例 C171 = D55×60、D171 = D56×60)
            Check("C171 站厅计算排烟量 = 2000×60", hall.CalculatedFlowM3H, 120000);
            Check("D171 站台计算排烟量 = 1620×60", platform.CalculatedFlowM3H, 97200);

            // 选型排烟量 = 计算 × 1.2(2026-09-04 决策)
            Check("站厅选型排烟量 = ×1.2", hall.SelectionFlowM3H, 144000);
            Check("站台选型排烟量 = ×1.2", platform.SelectionFlowM3H, 116640);

            // 风机 2 台:单台 = 选型 / 2
            Check("站厅单台风机风量 = 选型/2", hall.UnitFlowM3H, 72000);
            Check("站台单台风机风量 = 选型/2", platform.UnitFlowM3H, 58320);

            // 取大者:站厅(120000 > 97200)
            CheckText("选型基准区 = 站厅公共区(界面不体现单元格编号)", result.GoverningZoneName, "站厅公共区");
            CheckInt("基准区标记唯一", result.Zones.FindAll(z => z.IsGoverning).Count, 1);
            CheckInt("基准区是站厅", hall.IsGoverning ? 1 : 0, 1);
            Check("基准区选型风量 = 144000", result.GoverningSelectionFlowM3H, 144000);
            Check("单台选型风量 = 144000/2 = 72000", result.UnitSelectionFlowM3H, 72000);

            // 公式文档口径参考值 E178 = MAX/2(不含选型系数),必须与 30 项回归口径一致
            Check("参考 E178 = MAX(C171,D171)/2 = 60000", result.UnitFlowPerFormulaDocM3H, 60000);
            Check("E178 与负荷计算器的同口径值一致(C171/D171/E178)",
                Math.Abs(new LargeSystemLoadCalculator().Calculate(areas).UnitSmokeFlowM3H - result.UnitFlowPerFormulaDocM3H) < 1e-9 ? 1 : 0, 1);

            // 站台面积更大时,基准区必须切换(防"写死站厅")
            var swapped = new LargeSmokeCalculator().Calculate(
                new LargeSystemInput { HallAreaM2 = 800, PlatformAreaM2 = 1620 }, parameters);
            CheckText("站台面积更大时基准区 = 站台公共区", swapped.GoverningZoneName, "站台公共区");
            Check("此时单台选型风量 = 1620×60×1.2/2", swapped.UnitSelectionFlowM3H, 58320);

            // 台数/系数可调:4 台、系数 1.0
            var custom = new LargeSmokeCalculator().Calculate(areas,
                new LargeSmokeInput { SmokeRateM3HPerM2 = 72, SelectionFactor = 1.0, FanUnitCount = 4 });
            Check("单位面积 72 时站厅计算排烟量 = 144000", custom.Zones[0].CalculatedFlowM3H, 144000);
            Check("系数 1.0 时选型 = 计算", custom.Zones[0].SelectionFlowM3H, 144000);
            Check("4 台时单台 = 选型/4", custom.Zones[0].UnitFlowM3H, 36000);

            // 单元格编号:界面/交付件不体现,但数据与"核对视图"仍保留(可追溯回公式文档)
            CheckText("区域名不含单元格编号(评审要求)", result.Zones[0].ZoneName, "站厅公共区");
            CheckText("编号保留在数据字段(供悬停/核对)", result.Zones[0].Cell, "D55");
            CheckInt("默认计算书不含单元格编号",
                System.Text.RegularExpressions.Regex.IsMatch(
                    ResultFormatter.FormatLargeSmoke(areas, parameters, result), @"\b[A-Z]{1,2}\d{2,3}\b") ? 1 : 0, 0);
            CheckInt("核对视图(显式开启)仍带单元格编号",
                ResultTable.ForLargeSystem(areas, new LargeSystemLoadCalculator().Calculate(areas)).ToText(true)
                    .Contains("D107") ? 1 : 0, 1);

            CheckInt("口径说明非空", result.Note.Length > 0 ? 1 : 0, 1);
            CheckInt("过渡口径(防烟分区)必须写明", result.PendingNote.Contains("防烟分区") ? 1 : 0, 1);

            // 数据仓库往返
            string dir = Path.Combine(Path.GetTempPath(), "HVACIDA-Smoke-" + Guid.NewGuid().ToString("N"));
            try
            {
                var repo = new XmlProjectRepository(dir);
                var loaded = repo.LoadLargeSmoke();
                Check("排烟参数默认值往返:60", loaded.SmokeRateM3HPerM2, 60);
                loaded.SelectionFactor = 1.25;
                repo.SaveLargeSmoke(loaded);
                Check("排烟参数往返 选型系数", repo.LoadLargeSmoke().SelectionFactor, 1.25);
                File.WriteAllText(Path.Combine(dir, "large-smoke.xml"), "<broken");
                Check("排烟参数损坏文件回退默认", repo.LoadLargeSmoke().SelectionFactor, 1.2);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* 忽略清理失败 */ }
            }

            Console.WriteLine();
        }
        // =====================================================================
        // 场景12:小系统六类系统(《小系统空调负荷、送排风、排烟计算公式.docx》示例复算)
        // =====================================================================
        private static void RunSmallSystemChecks()
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("场景12:小系统六类系统(按公式文档示例复算)");
            Console.WriteLine("==================================================");

            var calc = new SmallSystemLoadCalculator();

            // ---------- 1) 全空气一次回风:文档「弱电房间 AHU-A101」21 个房间示例 ----------
            var allAir = new SmallSystemInput
            {
                SystemType = SmallSystemType.AllAirOnceReturn,
                SystemCode = "AHU-A101",
                IndoorTempC = 27, SupplyTempDiffC = 10, DuctTempRiseC = 1.5,
                LightingIndexWm2 = 20,      // 示例工程用 20 W/m²(文档默认 8)
                WallMoistureEmission = 2,
                OutdoorWetBulbC = 28.2      // 示例"新风状态点焓 91.60"对应的室外湿球温度
            };
            // 房间数据直接取自文档示例表(面积/层高/外墙长/屋顶面积/设备冷负荷)
            double[,] rooms =
            {
                {75.76,5.9,0.00,75.76,8400},{24.37,5.9,6.10,60.36,1000},{26.25,5.9,0.00,26.25,800},
                {13.66,5.9,0.00,13.66,1000},{24.40,5.9,4.00,24.40,2400},{53.56,5.9,8.56,53.56,30000},
                {54.25,5.9,8.75,54.25,6000},{45.31,5.9,7.25,45.31,8000},{7.81,5.9,1.45,7.81,1000},
                {32.18,5.9,4.95,32.18,3000},{19.69,5.9,3.15,19.69,2000},{30.62,5.9,8.10,30.62,6000},
                {42.25,5.9,6.80,42.25,6000},{20.30,5.9,0.00,20.30,7000},{11.12,4.55,0.00,0.00,1000},
                {26.03,4.55,0.00,0.00,1800},{21.17,4.55,0.00,0.00,2400},{25.53,4.55,0.00,0.00,1800},
                {14.50,4.55,0.00,0.00,1000},{9.76,4.55,0.00,0.00,1000},{12.24,4.55,0.00,0.00,1000}
            };
            for (int i = 0; i < rooms.GetLength(0); i++)
            {
                var room = SmallRoomInput.Create("房间" + (i + 1), rooms[i, 0], rooms[i, 1]);
                room.WallLengthM = rooms[i, 2];
                room.RoofAreaM2 = rooms[i, 3];
                room.EquipmentCoolingW = rooms[i, 4];
                room.AirChangePerHour = 6;      // 文档默认
                allAir.Rooms.Add(room);
            }
            var allAirResult = calc.Calculate(allAir);
            Console.WriteLine("—— 全空气一次回风(文档示例:21 房间,合计 面积 590.76 / 冷负荷 104.42 / 湿负荷 0.475083 / 送风 32539 / 冷量 157.6)——");

            Check("总面积 = 590.76 m²", allAirResult.TotalAreaM2, 590.76);
            Check("冷负荷合计 M37 = 104.42 kW", allAirResult.TotalCoolingKw, 104.42, 0.02);
            Check("湿负荷合计 N37 = 0.475083 g/s", allAirResult.TotalMoistureGps, 0.475083, 1e-5);
            Check("热湿比 C39 = 219783 kJ/kg", allAirResult.HeatHumidityRatio, 219783, 1.0);
            Check("送风点焓 B47 ≈ 43.90 kJ/kg(文档各点参数四舍五入)", allAirResult.SupplyEnthalpy, 43.90, 0.15);
            Check("露点焓 C50 ≈ 42.30 kJ/kg", allAirResult.DewPointEnthalpy, 42.30, 0.20);
            Check("室内状态点焓 C53 ≈ 54.30 kJ/kg", allAirResult.IndoorEnthalpy, 54.30, 0.15);
            Check("总送风量 R37 ≈ 32539 m³/h", allAirResult.TotalSupplyM3H, 32539, 30.0);
            Check("总回风量 W37 ≈ 29285 m³/h(新风比 10%)", allAirResult.TotalReturnM3H, 29285, 30.0);
            Check("新风比 E39 = 0.10", allAirResult.FreshAirRatio, 0.10, 1e-6);
            Check("单房间校验:1 号房间实际通风量 ≈ 2985 m³/h", allAirResult.Rooms[0].ActualVentilationM3H, 2985, 3.0);
            Check("单房间校验:2 号房间实际通风量 = 863 m³/h(换气风量占优)", allAirResult.Rooms[1].ActualVentilationM3H, 863, 1.0);
            Check("设备选型 AHU 风量 ≈ 35792 m³/h(32539×1.1)", allAirResult.Equipments[0].FlowM3H, 35792, 33.0);
            Check("设备选型 RAF 风量 ≈ 32213 m³/h(29285×1.1)", allAirResult.Equipments[1].FlowM3H, 32213, 33.0);
            Check("设备选型 AHU 冷量(V37×1.1)", allAirResult.Equipments[0].CoolingKw, allAirResult.TotalUnitCoolingKw * 1.1, 1e-9);

            // 口径(2026-09-15 已确认):**一律按公式文档的文字公式计算,示例仅用于理解公式**。
            // 公式写 V27 = 实际通风量 × (混合焓 − 露点焓) × 1.15 / 3600;文档示例中 2/5/10/16/19 号房间的
            // 冷量值对应的是"消除余热通风量",故示例合计 157.6 与本实现 162.2 有约 3% 差异 —— 属预期,不作为基准。
            Check("按公式(实际通风量)复算 V37 ≈ 162.2 kW(示例 157.6 属示例取值,不作为基准)",
                allAirResult.TotalUnitCoolingKw, 162.2, 0.6);
            CheckInt("口径说明已改为「按公式计算、示例仅作理解参照」",
                allAirResult.PendingNote.Contains("按公式") && allAirResult.PendingNote.Contains("示例") ? 1 : 0, 1);
            Console.WriteLine("     ✓ 口径已确认:按公式文字计算(实际通风量 R27);示例中 2/5/10/16/19 号房间用的是 O27," +
                              "故示例合计 157.6 与本实现 162.2 差约 3%,属预期。");

            // ---------- 2) 多联机+新风:文档「人员房间 VRV」11 个房间示例 ----------
            var vrf = new SmallSystemInput
            {
                SystemType = SmallSystemType.VrfWithFreshAir,
                SystemCode = "VRV-1",
                IndoorTempC = 27, TransitionOutdoorC = 14,
                LightingIndexWm2 = 20, WallMoistureEmission = 2, IndoorRhPct = 50
            };
            double[,] vrfRooms =
            {
                {8.49,3.5,6,1000,3},{9.26,3.5,6,1000,3},{10.12,3.5,6,1000,3},{20.01,3.5,6,1000,3},
                {15.47,3.5,6,1000,3},{28.12,3.5,6,1000,20},{14.95,3.5,6,1000,2},{22.08,3.5,6,1000,3},
                {25.36,3.5,6,1000,3},{17.79,3.5,6,1000,4},{15.36,3.5,6,1000,4}
            };
            for (int i = 0; i < vrfRooms.GetLength(0); i++)
            {
                var room = SmallRoomInput.Create("房间" + (i + 1), vrfRooms[i, 0], vrfRooms[i, 1]);
                room.AirChangePerHour = vrfRooms[i, 2];
                room.EquipmentCoolingW = vrfRooms[i, 3];
                room.Occupants = vrfRooms[i, 4];
                vrf.Rooms.Add(room);
            }
            var vrfResult = calc.Calculate(vrf);
            Console.WriteLine("—— 多联机+新风(文档示例:11 房间,合计 面积 187.01 / 冷负荷 21.57 / 湿负荷 0.001733 / 人员新风 1560)——");
            Check("总面积 = 187.01 m²", vrfResult.TotalAreaM2, 187.01, 0.01);
            Check("冷负荷合计 I79 = 21.57 kW", vrfResult.TotalCoolingKw, 21.57, 0.01);
            Check("湿负荷合计 = 0.001733 kg/s", vrfResult.TotalMoistureGps, 1.733, 0.005);
            Check("人员新风量合计(本夹具 11 房间 51 人)= 1530 m³/h", vrfResult.TotalFreshAirM3H, 1530, 0.01);
            Console.WriteLine("     ⚠ 文档 M79=1560 含「保洁清扫间」(12 房间 52 人),而 I79=21.57 只含 11 房间,两表口径不同。");
            Check("热湿比 ≈ 12448 kJ/kg", vrfResult.HeatHumidityRatio, 12448.61, 5.0);
            Check("设备选型 PEU 风量 = 1530×1.1 = 1683 m³/h", vrfResult.Equipments[0].FlowM3H, 1683, 0.01);
            Check("设备选型 FAF 风量 = 实际通风量合计×1.1", vrfResult.Equipments[1].FlowM3H,
                vrfResult.TotalSupplyM3H * 1.1, 1e-9);

            // ---------- 3) 排风系统:文档「卫生间、泵房等通风」8 行示例 ----------
            var exhaust = new SmallSystemInput { SystemType = SmallSystemType.ExhaustVentilation, SystemCode = "EAF-A601" };
            double[,] exhaustRooms = { {5.83,5.90,20},{6.82,5.90,20},{4.23,4.55,10},{4.40,4.55,10},
                                       {11.46,4.55,20},{15.62,4.55,20},{7.87,4.55,20},{17.62,4.55,4} };
            for (int i = 0; i < exhaustRooms.GetLength(0); i++)
            {
                var room = SmallRoomInput.Create("房间" + (i + 1), exhaustRooms[i, 0], exhaustRooms[i, 1]);
                room.AirChangePerHour = exhaustRooms[i, 2];
                exhaust.Rooms.Add(room);
            }
            var exhaustResult = calc.Calculate(exhaust);
            Console.WriteLine("—— 排风系统(文档示例:8 房间,合计排风量 5386 m³/h,EAF-A601 ×1.3 = 7002)——");
            Check("计算排风量合计 = 5386 m³/h", exhaustResult.TotalExhaustM3H, 5386, 1.0);
            Check("单房间校验:男卫 = 5.83×5.9×20 = 688 m³/h", exhaustResult.Rooms[0].ExhaustM3H, 688, 1.0);
            Check("默认系数 1.1 时选型风量(文档文字口径)", exhaustResult.Equipments[0].FlowM3H, 5386 * 1.1, 1.1);
            exhaust.SelectionFactor = HvacConstants.ExhaustSelectionFactorInSample;   // 1.3(仅用于核对示例)
            var exhaustSample = calc.Calculate(exhaust);
            Check("示例系数 1.3 时选型风量 = 7002 m³/h(即文档示例值,仅作理解参照)",
                exhaustSample.Equipments[0].FlowM3H, 7002, 1.0);
            Check("默认按公式取 1.1(示例的 1.3 不作为基准)", exhaustResult.Equipments[0].Factor, 1.1, 1e-9);

            // ---------- 4) 排烟系统:文档「走道排烟及补风」2 个防烟分区示例 ----------
            var smoke = new SmallSystemInput { SystemType = SmallSystemType.SmokeExhaust, SystemCode = "SEF-A501" };
            var z1 = SmallRoomInput.Create("防烟分区2", 300, 0); z1.IsSmokeZone = true;
            var z2 = SmallRoomInput.Create("防烟分区3", 277, 0); z2.IsSmokeZone = true;
            smoke.Rooms.Add(z1); smoke.Rooms.Add(z2);
            var smokeResult = calc.Calculate(smoke);
            Console.WriteLine("—— 排烟系统(文档示例:2 分区,排烟 18000+16620 = 34620,补风 10800+9972 = 20772)——");
            Check("计算排烟量 = 300×60 = 18000 m³/h", smokeResult.Rooms[0].SmokeM3H, 18000, 0.01);
            Check("计算排烟量 = 277×60 = 16620 m³/h", smokeResult.Rooms[1].SmokeM3H, 16620, 0.01);
            Check("计算排烟量合计 = 34620 m³/h", smokeResult.TotalSmokeM3H, 34620, 0.01);
            Check("计算补风量 = 18000×0.6 = 10800 m³/h", smokeResult.Rooms[0].MakeupAirM3H, 10800, 0.01);
            Check("计算补风量合计 = 20772 m³/h", smokeResult.TotalMakeupAirM3H, 20772, 0.01);
            Check("排烟风机 SEF = 34620×1.2 = 41544 m³/h", smokeResult.Equipments[0].FlowM3H, 41544, 0.5);
            Check("补风机 FAF = 20772×1.1 = 22849 m³/h", smokeResult.Equipments[1].FlowM3H, 22849.2, 0.5);

            // ---------- 5) 送风排风排烟系统:文档「环控机房 + 气瓶间」5 行示例 ----------
            var ses = new SmallSystemInput { SystemType = SmallSystemType.SupplyExhaustSmoke, SystemCode = "FAF-A401" };
            var machine = SmallRoomInput.Create("通风空调机房", 517, 5.9); machine.AirChangePerHour = 6;
            machine.RoomType = "环控机房"; machine.IsSmokeZone = true;
            ses.Rooms.Add(machine);
            double[,] other = { {36.56,5.9},{9.38,5.9},{18.35,5.9},{26.82,4.55} };
            for (int i = 0; i < other.GetLength(0); i++)
            {
                var room = SmallRoomInput.Create("房间" + (i + 1), other[i, 0], other[i, 1]);
                room.AirChangePerHour = 4;
                ses.Rooms.Add(room);
            }
            var sesResult = calc.Calculate(ses);
            Console.WriteLine("—— 送风排风排烟(文档示例:排风 20307 / 送风 18302 / 排烟 31020 / 补风 18612)——");
            Check("计算排风量合计 = 20307 m³/h", sesResult.TotalExhaustM3H, 20307, 1.0);
            Check("环控机房计算排风量 = 517×5.9×6 = 18302 m³/h", sesResult.Rooms[0].ExhaustM3H, 18302, 1.0);
            Check("环控机房计算排烟量 = 517×60 = 31020 m³/h", sesResult.Rooms[0].SmokeM3H, 31020, 0.01);
            Check("环控机房计算补风量 = 31020×0.6 = 18612 m³/h", sesResult.Rooms[0].MakeupAirM3H, 18612, 0.01);
            Check("排风机 EAF = 20307×1.1 = 22338 m³/h", sesResult.Equipments[0].FlowM3H, 22338, 0.5);
            Check("排烟风机 SEF = 31020×1.2 = 37224 m³/h", sesResult.Equipments[1].FlowM3H, 37224, 0.5);
            Check("补风机/送风机 FAF = 18612×1.1 = 20473 m³/h(取送风与补风之大者)",
                sesResult.Equipments[2].FlowM3H, 20473, 0.5);

            // ---------- 6) 加压送风系统:文档公式(无示例,按默认门参数自洽校验) ----------
            var press = new SmallSystemInput { SystemType = SmallSystemType.PressurizationSupply, SystemCode = "SAF-1" };
            var pressResult = calc.Calculate(press);
            Console.WriteLine("—— 加压送风(文档公式,默认门参数 1.5×2.1 / 风速 1 / ΔP 12 / 余压阀 0.5×2)——");
            Check("门面积 D388 = 1.5×2.1 = 3.15 m²", pressResult.DoorAreaM2, 3.15, 1e-9);
            Check("门开启风量 G388 = 3.15×1×1 = 3.15 m³/s → 11340 m³/h", pressResult.DoorOpenFlowM3H, 11340, 0.01);
            Check("单门有效漏风面积 I388 = (1.5+2.1)×2×0.004 = 0.0288 m²",
                (1.5 + 2.1) * 2 * HvacConstants.DoorGapWidthFactor, 0.0288, 1e-9);
            Check("门缝漏风 N388 = 0.827×0.0288×√12×1.25×1 = 0.10312 m³/s → 371.2 m³/h",
                pressResult.DoorLeakFlowM3H, 371.23, 0.5);
            Check("余压阀漏风 R388 = 0.083×0.5×2 = 0.083 m³/s → 298.8 m³/h",
                pressResult.ReliefValveLeakFlowM3H, 298.8, 0.02);
            Check("楼梯间加压送风量 S388 = (3.15+0.10312+0.083)×3600 = 12010 m³/h",
                pressResult.PressurizationFlowM3H, 12010, 0.5);
            Check("加压送风机 D392 = S388×1.2 = 14412 m³/h", pressResult.Equipments[0].FlowM3H, 14412.4, 0.5);

            // ---------- 7) 行业口径:房间类型默认换气次数 + 结果表/明细表结构 ----------
            CheckInt("卫生间默认换气次数 = 20", (int)SmallSystemInput.DefaultAirChangePerHour("男卫生间"), 20);
            CheckInt("淋浴间默认换气次数 = 10", (int)SmallSystemInput.DefaultAirChangePerHour("淋浴间"), 10);
            CheckInt("环控机房默认换气次数 = 6", (int)SmallSystemInput.DefaultAirChangePerHour("环控机房"), 6);
            CheckInt("气瓶间默认换气次数 = 4", (int)SmallSystemInput.DefaultAirChangePerHour("气瓶间"), 4);
            CheckInt("未识别类型默认换气次数 = 4", (int)SmallSystemInput.DefaultAirChangePerHour("其他房间"), 4);

            CheckInt("全空气结果表分区数 >= 4", ResultTable.ForSmallSystem(allAir, allAirResult).Sections.Count >= 4 ? 1 : 0, 1);
            CheckInt("排烟系统房间明细列数 = 5(序号/分区/面积/排烟量/补风量)",
                SmallRoomTable.ColumnsFor(SmallSystemType.SmokeExhaust).Count, 5);
            CheckInt("全空气房间明细列数 = 17", SmallRoomTable.ColumnsFor(SmallSystemType.AllAirOnceReturn).Count, 17);
            CheckInt("计算书含房间明细表", ResultFormatter.FormatSmall(allAir, allAirResult).Contains("房间明细") ? 1 : 0, 1);
            CheckInt("计算书含设备选型表", ResultFormatter.FormatSmall(allAir, allAirResult).Contains("设备选型") ? 1 : 0, 1);

            // ---------- 8) 六类系统的口径/计算书文案都不得出现公式文档单元格编号 ----------
            // (2026-09-15 评审:插件界面与交付计算书都不体现单元格编号;编号只保留在代码 XML 注释与 ToText(true) 核对视图里)
            var codePattern = new System.Text.RegularExpressions.Regex(@"(?<![-A-Z])\b[A-Z]{1,2}[0-9]{2,3}\b");
            var codeOffenders = new List<string>();
            foreach (SmallSystemType type in System.Enum.GetValues(typeof(SmallSystemType)))
            {
                var probe = new SmallSystemInput { SystemType = type, SystemCode = "X-1" };
                if (type != SmallSystemType.PressurizationSupply)
                {
                    var room = SmallRoomInput.Create("探针房间", 100, 4.5);
                    room.Occupants = 2; room.EquipmentCoolingW = 3000; room.AirChangePerHour = 6;
                    room.RoomType = "卫生间"; room.IsSmokeZone = true;
                    probe.Rooms.Add(room);
                }
                var probeResult = calc.Calculate(probe);
                string text = ResultFormatter.FormatSmall(probe, probeResult);
                foreach (System.Text.RegularExpressions.Match m in codePattern.Matches(text))
                {
                    codeOffenders.Add(type + ":" + m.Value);
                }
            }
            CheckInt("六类系统的计算书正文与口径说明均不含单元格编号(0 = 正常," +
                     string.Join(",", codeOffenders.Distinct().ToArray()) + ")",
                codeOffenders.Count, 0);

            // ---------- 8) 室外参数回填(项目信息 → 小系统 E4/E5) ----------
            string dir = Path.Combine(Path.GetTempPath(), "HVACIDA-Small-" + Guid.NewGuid().ToString("N"));
            try
            {
                var repo = new XmlProjectRepository(dir);
                var project = repo.LoadProject();
                project.Design.LargeSystemOutdoor.SummerACDryBulbC = 34.2;
                project.Design.LargeSystemOutdoor.SummerACWetBulbC = 27.8;
                repo.SaveProject(project);

                var service = new SmallSystemInputService(repo);
                var loaded = service.Load();
                Check("室外干球回填 E4 = 34.2 ℃", loaded.OutdoorDryBulbC, 34.2, 1e-9);
                Check("室外湿球回填 E5 = 27.8 ℃", loaded.OutdoorWetBulbC, 27.8, 1e-9);
                CheckInt("回填状态可用", service.WeatherApplied ? 1 : 0, 1);

                loaded.WeatherManuallyOverridden = true;
                loaded.OutdoorDryBulbC = 35.0;
                service.Save(loaded);
                Check("脱离联动后保留手工值", service.Load().OutdoorDryBulbC, 35.0, 1e-9);

                // 多系统容器往返(LoadSmallSystem 只是兼容入口:返回容器里第一套,不再等于"刚保存的那套")
                var roundTrip = repo.LoadSmallSystems();
                var sefRoundTrip = new SmallSystemInput { SystemType = SmallSystemType.SmokeExhaust, SystemCode = "SEF-T1" };
                sefRoundTrip.Rooms.Add(SmallRoomInput.Create("防烟分区1", 300, 0));
                roundTrip.Upsert(sefRoundTrip);
                repo.SaveSmallSystems(roundTrip);
                var back = repo.LoadSmallSystems().Find(SmallSystemType.SmokeExhaust, "SEF-T1");
                CheckInt("容器往返:取回该系统", back == null ? 0 : 1, 1);
                CheckInt("房间列表往返 房间数", back.Rooms.Count, 1);
                Check("房间列表往返 面积", back.Rooms[0].AreaM2, 300, 1e-9);
                CheckText("系统类型往返", back.SystemType.ToString(), SmallSystemType.SmokeExhaust.ToString());
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }

            // ---------- 9) 多系统汇总(需求 2.2.3.2:全站多套小系统) ----------
            var smallProject = new SmallSystemProject();

            var ahu1 = new SmallSystemInput { SystemType = SmallSystemType.AllAirOnceReturn, SystemCode = "AHU-A101" };
            ahu1.Rooms.Add(SmallRoomInput.Create("弱电间1", 50, 5.9));
            var ahu2 = new SmallSystemInput { SystemType = SmallSystemType.AllAirOnceReturn, SystemCode = "AHU-A201" };
            ahu2.Rooms.Add(SmallRoomInput.Create("强电间1", 80, 4.55));
            var eaf = new SmallSystemInput { SystemType = SmallSystemType.ExhaustVentilation, SystemCode = "EAF-A601" };
            var toilet = SmallRoomInput.Create("男卫", 5.83, 5.9); toilet.RoomType = "男卫生间";
            eaf.Rooms.Add(toilet);
            var sef = new SmallSystemInput { SystemType = SmallSystemType.SmokeExhaust, SystemCode = "SEF-A501" };
            sef.Rooms.Add(SmallRoomInput.Create("防烟分区1", 300, 0));

            smallProject.Upsert(ahu1);
            smallProject.Upsert(ahu2);
            smallProject.Upsert(eaf);
            smallProject.Upsert(sef);
            CheckInt("多系统工程:4 套系统", smallProject.Systems.Count, 4);

            // 同类型同编号 → 覆盖,不新增
            var ahu1b = new SmallSystemInput { SystemType = SmallSystemType.AllAirOnceReturn, SystemCode = "AHU-A101" };
            ahu1b.Rooms.Add(SmallRoomInput.Create("弱电间1改", 60, 5.9));
            smallProject.Upsert(ahu1b);
            CheckInt("同类型同编号 upsert 不新增系统", smallProject.Systems.Count, 4);
            CheckText("upsert 覆盖了原系统", smallProject.Find(SmallSystemType.AllAirOnceReturn, "AHU-A101").Rooms[0].Name, "弱电间1改");
            CheckInt("按类型找(编号为空取第一个)", smallProject.Find(SmallSystemType.AllAirOnceReturn, "") == null ? 0 : 1, 1);
            CheckInt("删除系统", smallProject.Remove(SmallSystemType.SmokeExhaust, "SEF-A501") ? 1 : 0, 1);
            CheckInt("删除后剩 3 套", smallProject.Systems.Count, 3);
            smallProject.Upsert(sef);

            var summary = new SmallSystemSummaryService().Summarize(smallProject);
            Console.WriteLine("—— 全站汇总 —— " + summary.Note);
            CheckInt("汇总:系统套数 = 4", summary.SystemCount, 4);
            CheckInt("汇总:房间/分区合计 = 4", summary.RoomCount, 4);
            CheckInt("汇总:逐系统行数 = 4", summary.Rows.Count, 4);
            CheckInt("汇总:类型分布文本含套数", summary.TypeBreakdown.Contains("全空气一次回风系统 2 套") ? 1 : 0, 1);

            // 合计 == 逐系统相加(不跨系统重算)
            double manualCooling = 0, manualSupply = 0, manualExhaust = 0, manualSmoke = 0;
            var one = new SmallSystemLoadCalculator();
            foreach (var s in smallProject.Systems)
            {
                var rr = one.Calculate(s);
                manualCooling += rr.TotalCoolingKw;
                manualSupply += rr.TotalSupplyM3H;
                manualExhaust += rr.TotalExhaustM3H;
                manualSmoke += rr.TotalSmokeM3H;
            }
            Check("汇总冷负荷 = 逐系统相加", summary.TotalCoolingKw, manualCooling, 1e-9);
            Check("汇总送风量 = 逐系统相加", summary.TotalSupplyM3H, manualSupply, 1e-9);
            Check("汇总排风量 = 逐系统相加", summary.TotalExhaustM3H, manualExhaust, 1e-9);
            Check("汇总排烟量 = 逐系统相加", summary.TotalSmokeM3H, manualSmoke, 1e-9);
            CheckInt("逐系统行带各自的计算书文本",
                summary.Rows.FindAll(x => x.ResultText.Contains("设备选型")).Count, 4);

            var summaryTable = ResultTable.ForSmallSystemSummary(summary);
            CheckInt("汇总结果表分区数 = 3(合计 / 风量 / 逐系统)", summaryTable.Sections.Count, 3);
            CheckInt("汇总表逐系统列数 = 13", SmallRoomTable.SummaryColumns().Count, 13);
            CheckInt("汇总口径说明写明只相加不重算", summary.Note.Contains("不跨系统重算") ? 1 : 0, 1);

            // 仓库:多系统往返 + 旧单系统文件自动迁移
            string dir7 = Path.Combine(Path.GetTempPath(), "HVACIDA-Multi-" + Guid.NewGuid().ToString("N"));
            try
            {
                var repo7 = new XmlProjectRepository(dir7);
                repo7.SaveSmallSystems(smallProject);
                var back7 = repo7.LoadSmallSystems();
                CheckInt("小系统工程往返:4 套", back7.Systems.Count, 4);
                Check("往返:第二套屋顶面积(默认=面积)", back7.Find(SmallSystemType.AllAirOnceReturn, "AHU-A201").Rooms[0].RoofAreaM2, 80, 1e-9);

                // 兼容入口:SaveSmallSystem(单系统)应 upsert 进容器
                var single = new SmallSystemInput { SystemType = SmallSystemType.PressurizationSupply, SystemCode = "SAF-1" };
                repo7.SaveSmallSystem(single);
                CheckInt("单系统旧接口 upsert 进容器 → 5 套", repo7.LoadSmallSystems().Systems.Count, 5);

                // 迁移:只有旧 small-system.xml 时应自动搬进容器
                string dir8 = Path.Combine(Path.GetTempPath(), "HVACIDA-Migrate-" + Guid.NewGuid().ToString("N"));
                try
                {
                    var legacyRepo = new XmlProjectRepository(dir8);
                    Directory.CreateDirectory(dir8);
                    var legacy = new SmallSystemInput { SystemType = SmallSystemType.ExhaustVentilation, SystemCode = "EAF-OLD" };
                    legacy.Rooms.Add(SmallRoomInput.Create("老卫生间", 6, 4.5));
                    new XmlProjectRepository(dir8).SaveSmallSystem(legacy);   // 写出容器
                    File.Delete(Path.Combine(dir8, "small-systems.xml"));      // 只留旧文件
                    var migration = new XmlProjectRepository(dir8);
                    var legacySingle = legacy;                                  // 用旧接口写单系统文件
                    var serializer = new System.Xml.Serialization.XmlSerializer(typeof(SmallSystemInput));
                    using (var w = System.Xml.XmlWriter.Create(Path.Combine(dir8, "small-system.xml")))
                    {
                        serializer.Serialize(w, legacySingle);
                    }
                    var migrated = migration.LoadSmallSystems();
                    CheckInt("旧单系统文件自动迁移为容器", migrated.Systems.Count, 1);
                    CheckText("迁移保留了系统编号", migrated.Systems[0].SystemCode, "EAF-OLD");
                    CheckInt("迁移后已落盘 small-systems.xml",
                        File.Exists(Path.Combine(dir8, "small-systems.xml")) ? 1 : 0, 1);
                }
                finally
                {
                    try { Directory.Delete(dir8, true); } catch { }
                }
            }
            finally
            {
                try { Directory.Delete(dir7, true); } catch { }
            }

            Console.WriteLine();
        }

        // =====================================================================
        // 场景13:水力计算(风系统 / 水系统)—— 公式与数值得独立复算
        //   期望值由公式**独立手算**(脚本另算一遍)：
        //   ① 圆形风管 Φ0.5 m、L=10 m、Q=3600 m³/h、K=0.15 mm、Σζ=1.0、20 ℃:
        //      A=πd²/4=0.1963495 m²,v=Q/3600/A=5.092958 m/s,Re=v·d/ν=168641.0,
        //      λ=0.11(K/d+68/Re)^0.25=0.0179129,动压 ρv²/2=15.62778 Pa(ρ=1.205),
        //      R=λ/d×动压=0.559878 Pa/m,沿程=5.598778 Pa,局部=1.0×动压=15.62778 Pa,段=21.22656 Pa。
        //   ② 矩形风管 1.2×0.4、L=20 m、Q=7200 m³/h、Σζ=0.35:A=0.48,d=4A/U=0.6 m,
        //      v=4.166667 m/s,Re=165562.9,λ=0.01763588,动压=10.46007 Pa,R=0.3074537,
        //      沿程=6.149083 Pa,局部=3.661024 Pa,段=9.810107 Pa。
        //   ③ 水管 Φ0.1 m、L=50 m、Q=36 m³/h、K=0.2 mm、Σζ=2.0、10 ℃(ρ=999.70、ν=1.306e-6):
        //      v=1.273240 m/s,Re=97491.5,λ=0.0250688,动压=810.3263 Pa,R=203.1388 Pa/m,
        //      沿程=10156.94 Pa,局部=1620.65 Pa,段=11777.59 Pa。
        // =====================================================================
        private static void RunHydraulicChecks()
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("场景13:水力计算(风系统 / 水系统)");
            Console.WriteLine("==================================================");

            var calc = new HydraulicCalculator();
            var coefficients = HydraulicCoefficients.CreateDefault();

            // ---------- 1) 圆形风管:单段,逐项复算 ----------
            var air = new HydraulicInput
            {
                Kind = HydraulicKind.AirDuct,
                SystemName = "机械送风 1",
                SystemTypeName = "送风",
                MediumTempC = 20,
                FromModel = false
            };
            air.Segments.Add(new HydraulicSegment
            {
                Name = "送风主管",
                ElementId = 0,
                Shape = HydraulicShape.Round,
                DiameterM = 0.5,
                LengthM = 10,
                FlowM3H = 3600,
                LocalZetaSum = 1.0,
                LocalNote = "90°弯头×2(0.25×2)",
                OnCriticalPath = true
            });
            air.Terminals.Add(new HydraulicTerminal
            {
                Name = "散流器 1", Kind = HydraulicItemKind.Terminal, ResistancePa = 0,
                Source = "示例不设末端阻力", OnCriticalPath = true
            });

            var airResult = calc.Calculate(air, coefficients);
            Check("风管 ρ(20 ℃)", airResult.DensityKgM3, 1.205, 1e-9);
            Check("风管 ν(20 ℃)", airResult.KinematicViscosityM2S, 1.51e-5, 1e-12);
            Check("圆形风管 断面积 m²", airResult.Segments[0].AreaM2, 0.1963495, 1e-7);
            Check("圆形风管 流速 m/s", airResult.Segments[0].VelocityMs, 5.092958, 1e-6);
            Check("圆形风管 雷诺数", airResult.Segments[0].Reynolds, 168641.0, 1.0);
            Check("圆形风管 摩擦系数 λ", airResult.Segments[0].FrictionFactor, 0.0179129, 1e-7);
            Check("圆形风管 动压 Pa", airResult.Segments[0].DynamicPressurePa, 15.62778, 1e-4);
            Check("圆形风管 比摩阻 Pa/m", airResult.Segments[0].SpecificFrictionPaPerM, 0.559878, 1e-5);
            Check("圆形风管 沿程阻力 Pa", airResult.Segments[0].FrictionLossPa, 5.598778, 1e-4);
            Check("圆形风管 局部阻力 Pa", airResult.Segments[0].LocalLossPa, 15.62778, 1e-4);
            Check("圆形风管 段合计 Pa", airResult.Segments[0].TotalLossPa, 21.22656, 1e-4);
            Check("圆形风管 实际粗糙度 K mm(未填 → 按介质默认)", airResult.Segments[0].RoughnessMm, 0.15, 1e-9);

            // 出口动压:风系统开式出口,未指定出口段 → 按环路上动压最大段(此处唯一一段)取值
            Check("出口动压 Pa(按最大动压段)", airResult.OutletDynamicPa, 15.62778, 1e-4);
            Check("计算总阻力 Pa(段 + 出口动压)", airResult.TotalResistancePa, 36.85434, 1e-4);
            Check("需求全压 Pa(× 富余 1.1)", airResult.RequiredPressurePa, 40.53977, 1e-4);
            Check("需求扬程 m(风系统为 0)", airResult.RequiredHeadM, 0.0, 1e-12);
            CheckText("未读到风机额定全压 → 不校核(不编额定值,MarginPct = NaN)",
                double.IsNaN(airResult.MarginPct) ? "NaN" : airResult.MarginPct.ToString("0.#"), "NaN");
            CheckText("校核结论写明未校核", airResult.CheckVerdict.Contains("未校核") ? "未校核" : airResult.CheckVerdict, "未校核");
            CheckText("待补说明写明出口动压按最大动压段取",
                airResult.PendingNote.Contains("动压最大") ? "有" : airResult.PendingNote, "有");

            // 指定出口段后:出口动压按该段取,且不再提示"按最大动压段"
            air.Segments[0].ElementId = 555;
            air.OutletSegmentElementId = 555;
            var airResult2 = calc.Calculate(air, coefficients);
            Check("指定出口段 出口动压 Pa", airResult2.OutletDynamicPa, 15.62778, 1e-4);
            CheckText("指定出口段后不再按最大动压段兜底",
                airResult2.PendingNote.Contains("动压最大") ? "仍兜底" : "按指定段", "按指定段");

            // 额定全压校核:给 50 Pa → 余量 (50-40.53977)/40.53977 = 23.34% → 满足
            air.RatedPressurePa = 50;
            var airResult3 = calc.Calculate(air, coefficients);
            Check("风机额定 50 Pa 时余量 %", airResult3.MarginPct, 23.3357, 1e-3);
            CheckText("校核结论 = 满足", airResult3.CheckVerdict.Contains("满足") ? "满足" : airResult3.CheckVerdict, "满足");

            // 额压不足:给 30 Pa → 余量 -26.0% → 不足
            air.RatedPressurePa = 30;
            var airResult4 = calc.Calculate(air, coefficients);
            Check("风机额定 30 Pa 时余量 %", airResult4.MarginPct, -25.9986, 1e-3);
            CheckText("校核结论 = 不足", airResult4.CheckVerdict.Contains("不足") ? "不足" : airResult4.CheckVerdict, "不足");

            // ---------- 2) 矩形风管:水力直径 = 流速当量直径 ----------
            var rect = new HydraulicInput { Kind = HydraulicKind.AirDuct, MediumTempC = 20, SystemName = "机械排风 1" };
            rect.Segments.Add(new HydraulicSegment
            {
                Shape = HydraulicShape.Rectangular, WidthM = 1.2, HeightM = 0.4,
                LengthM = 20, FlowM3H = 7200, LocalZetaSum = 0.35, OnCriticalPath = true, Name = "排风管 1200×400"
            });
            rect.IncludeOutletDynamic = false;      // 闭式/不算出口动压的情形
            var rectResult = calc.Calculate(rect, coefficients);
            Check("矩形风管 断面积 m²", rectResult.Segments[0].AreaM2, 0.48, 1e-9);
            Check("矩形风管 水力直径 m(= 2ab/(a+b))", rectResult.Segments[0].HydraulicDiameterM, 0.6, 1e-9);
            Check("矩形风管 流速 m/s", rectResult.Segments[0].VelocityMs, 4.166667, 1e-6);
            Check("矩形风管 雷诺数", rectResult.Segments[0].Reynolds, 165562.9, 1.0);
            Check("矩形风管 摩擦系数 λ", rectResult.Segments[0].FrictionFactor, 0.01763588, 1e-7);
            Check("矩形风管 动压 Pa", rectResult.Segments[0].DynamicPressurePa, 10.46007, 1e-4);
            Check("矩形风管 比摩阻 Pa/m", rectResult.Segments[0].SpecificFrictionPaPerM, 0.3074537, 1e-6);
            Check("矩形风管 沿程阻力 Pa", rectResult.Segments[0].FrictionLossPa, 6.149083, 1e-4);
            Check("矩形风管 局部阻力 Pa(Σζ=0.35)", rectResult.Segments[0].LocalLossPa, 3.661024, 1e-4);
            Check("矩形风管 段合计 Pa", rectResult.Segments[0].TotalLossPa, 9.810107, 1e-4);
            Check("不计出口动压时 出口动压 = 0", rectResult.OutletDynamicPa, 0.0, 1e-12);

            // ---------- 3) 水管:单段 + 末端 + 机组 + 校核 ----------
            var water = new HydraulicInput
            {
                Kind = HydraulicKind.WaterPipe,
                SystemName = "冷冻水供回水",
                SystemTypeName = "冷冻水",
                MediumTempC = 10,
                StaticHeightM = 0,          // 闭式环路
                ExtraFactor = 1.1
            };
            water.Segments.Add(new HydraulicSegment
            {
                Name = "供水干管", Shape = HydraulicShape.Round, DiameterM = 0.1,
                LengthM = 50, FlowM3H = 36, LocalZetaSum = 2.0, OnCriticalPath = true,
                LocalNote = "90°弯头×2(1.0×2)"
            });
            water.Terminals.Add(new HydraulicTerminal
            {
                Name = "空调机组 AHU-B101 盘管", Kind = HydraulicItemKind.Equipment,
                ResistancePa = 30000, Source = "设备样本水阻 30 kPa", OnCriticalPath = true
            });
            water.Terminals.Add(new HydraulicTerminal
            {
                Name = "风机盘管末端", Kind = HydraulicItemKind.Terminal,
                ResistancePa = 20000, Source = "样本 20 kPa", OnCriticalPath = true
            });

            var waterResult = calc.Calculate(water, coefficients);
            Check("水管 ρ(10 ℃)", waterResult.DensityKgM3, 999.70, 1e-9);
            Check("水管 ν(10 ℃)", waterResult.KinematicViscosityM2S, 1.306e-6, 1e-15);
            Check("水管 流速 m/s", waterResult.Segments[0].VelocityMs, 1.273240, 1e-6);
            Check("水管 雷诺数", waterResult.Segments[0].Reynolds, 97491.5, 1.0);
            Check("水管 摩擦系数 λ", waterResult.Segments[0].FrictionFactor, 0.0250688, 1e-7);
            Check("水管 动压 Pa", waterResult.Segments[0].DynamicPressurePa, 810.3263, 1e-3);
            Check("水管 比摩阻 Pa/m", waterResult.Segments[0].SpecificFrictionPaPerM, 203.1388, 1e-3);
            Check("水管 沿程阻力 Pa", waterResult.Segments[0].FrictionLossPa, 10156.94, 1e-2);
            Check("水管 局部阻力 Pa(Σζ=2.0)", waterResult.Segments[0].LocalLossPa, 1620.65, 1e-2);
            Check("水管 段合计 Pa", waterResult.Segments[0].TotalLossPa, 11777.59, 1e-2);
            Check("水系统 实际粗糙度 K mm(未填 → 0.2)", waterResult.Segments[0].RoughnessMm, 0.2, 1e-9);
            Check("水系统 静压 Pa(闭式 = 0)", waterResult.StaticPa, 0.0, 1e-9);
            Check("水系统 出口动压不计(= 0)", waterResult.OutletDynamicPa, 0.0, 1e-9);
            Check("水系统 计算总阻力 Pa", waterResult.TotalResistancePa, 61777.59, 1e-2);
            Check("水系统 需求全压(换算值)Pa", waterResult.RequiredPressurePa, 67955.35, 1e-2);
            Check("水系统 需求扬程 m", waterResult.RequiredHeadM, 6.929230, 1e-5);

            // 静压高差:10 m → ρg·h = 999.70×9.81×10 = 98070.57 Pa,计入总阻力
            water.StaticHeightM = 10;
            var waterWithStatic = calc.Calculate(water, coefficients);
            Check("静压高差 10 m → Pa", waterWithStatic.StaticPa, 999.70 * 9.81 * 10, 1e-6);

            // 水泵额定扬程校核:8 m → 余量 (8 - 6.92923)/6.92923 = 15.45% → 满足
            water.StaticHeightM = 0;
            water.RatedHeadM = 8;
            var waterRated = calc.Calculate(water, coefficients);
            Check("水泵额定 8 m 时余量 %", waterRated.MarginPct, 15.4530, 1e-3);
            CheckText("扬程校核结论 = 满足", waterRated.CheckVerdict.Contains("满足") ? "满足" : waterRated.CheckVerdict, "满足");

            // ---------- 4) 公式边界:层流区 λ = 64/Re ----------
            Check("层流 Re=1354 → λ = 64/Re", HydraulicCalculator.FrictionFactor(1354, 0.01), 64.0 / 1354.0, 1e-12);
            Check("临界 Re=2320 → 走湍流式", HydraulicCalculator.FrictionFactor(2320, 0.01),
                0.11 * Math.Pow(0.01 + 68.0 / 2320.0, 0.25), 1e-12);
            Check("雷诺数 0 → λ = 0(不产生 NaN/Inf)", HydraulicCalculator.FrictionFactor(0, 0.01), 0.0, 1e-12);
            Check("空气密度按温度插值(30 ℃)", HydraulicCalculator.Density(HydraulicKind.AirDuct, 30), 1.165, 1e-9);
            Check("水密度按温度插值(45 ℃ = 中间插值)",
                HydraulicCalculator.Density(HydraulicKind.WaterPipe, 45), (992.22 + 988.03) / 2.0, 1e-6);

            // ---------- 5) 没有拓扑信息(手工录入)→ 全部按环路计,并在待补说明里写清 ----------
            var manual = new HydraulicInput { Kind = HydraulicKind.AirDuct, MediumTempC = 20 };
            manual.Segments.Add(new HydraulicSegment
            {
                Name = "手工段 1", Shape = HydraulicShape.Round, DiameterM = 0.5,
                LengthM = 10, FlowM3H = 3600, LocalZetaSum = 1.0
            });
            var manualResult = calc.Calculate(manual, coefficients);
            CheckInt("无拓扑标记 → 全部段计入环路", manualResult.CriticalSegmentCount, 1);
            CheckText("无拓扑标记时明确写出保守口径",
                manualResult.PendingNote.Contains("保守计入") ? "有" : manualResult.PendingNote, "有");

            // ---------- 6) 空输入:不出数字,只给指引(没算过就不摆结果) ----------
            var emptyResult = calc.Calculate(new HydraulicInput { Kind = HydraulicKind.AirDuct }, coefficients);
            CheckInt("空输入 无管段", emptyResult.Segments.Count, 0);
            Check("空输入 总阻力 = 0", emptyResult.TotalResistancePa, 0.0, 1e-12);
            CheckText("空输入 给出「没读到任何管段」的指引",
                emptyResult.PendingNote.Contains("没有读到任何管段") ? "有" : emptyResult.PendingNote, "有");

            // ---------- 7) 并联环路平衡(水):逐支路累计 → 不平衡率 → 平衡阀 Kv / 阀权度 ----------
            //   期望值独立手算(两条支路同管径同流量 → 比摩阻 R=203.1388 Pa/m、动压 810.3263 Pa 相同):
            //   支路 A(最不利)L=50、Σζ=2.0、末端 20000 Pa:段 = R×50 + 2×动压 = 11777.5915,合计 = 31777.5915
            //   支路 B L=20、Σζ=2.0、末端 20000 Pa:段 = R×20 + 2×动压 = 5683.4282,合计 = 25683.4282
            //   不平衡 = 31777.5915 − 25683.4282 = 6094.1634 Pa = 19.1775%(> 允许 15%)
            //   平衡阀 Kv = Q ÷ √(ΔP[bar]) = 36 ÷ √(6094.1634/100000) = 145.8295;阀权度 = 6094.1634/31777.5915 = 0.19178
            var parallelWater = new HydraulicInput
            {
                Kind = HydraulicKind.WaterPipe,
                SystemName = "冷冻水并联支路",
                MediumTempC = 10
            };
            parallelWater.Segments.Add(new HydraulicSegment
            {
                Name = "支路 A 供水管", ElementId = 301, Shape = HydraulicShape.Round,
                DiameterM = 0.1, LengthM = 50, FlowM3H = 36, LocalZetaSum = 2.0, OnCriticalPath = true
            });
            parallelWater.Segments.Add(new HydraulicSegment
            {
                Name = "支路 B 供水管", ElementId = 302, Shape = HydraulicShape.Round,
                DiameterM = 0.1, LengthM = 20, FlowM3H = 36, LocalZetaSum = 2.0
            });
            parallelWater.Terminals.Add(new HydraulicTerminal
            {
                Name = "末端 A", ElementId = 401, Kind = HydraulicItemKind.Terminal,
                ResistancePa = 20000, OnCriticalPath = true
            });
            parallelWater.Terminals.Add(new HydraulicTerminal
            {
                Name = "末端 B", ElementId = 402, Kind = HydraulicItemKind.Terminal,
                ResistancePa = 20000
            });
            parallelWater.Branches.Add(new HydraulicBranch
            {
                Name = "末端 A", TerminalElementId = 401, IsCritical = true,
                SegmentSummary = "支路 A 供水管", SegmentElementIds = { 301 }
            });
            parallelWater.Branches.Add(new HydraulicBranch
            {
                Name = "末端 B", TerminalElementId = 402,
                SegmentSummary = "支路 B 供水管", SegmentElementIds = { 302 }
            });

            var parallelResult = calc.Calculate(parallelWater, coefficients);
            CheckInt("并联支路数 = 2", parallelResult.Branches.Count, 2);
            CheckInt("超出允许不平衡率的支路数 = 1", parallelResult.UnbalancedBranchCount, 1);
            Check("最大不平衡率 %", parallelResult.MaxImbalancePct, 19.1775, 1e-3);

            var branchA = parallelResult.Branches[0];
            var branchB = parallelResult.Branches[1];
            Check("最不利环路标记落在支路 A", branchA.IsCritical ? 1 : 0, 1);
            Check("支路 A 合计阻力 Pa", branchA.TotalLossPa, 31777.5915, 1e-2);
            Check("支路 B 管段阻力 Pa", branchB.SegmentLossPa, 5683.4282, 1e-2);
            Check("支路 B 合计阻力 Pa", branchB.TotalLossPa, 25683.4282, 1e-2);
            Check("支路 B 不平衡 Pa", branchB.ImbalancePa, 6094.1634, 1e-2);
            Check("支路 B 不平衡 %", branchB.ImbalancePct, 19.1775, 1e-3);
            Check("支路 B 需吸收压差 Pa", branchB.RequiredAbsorbPa, 6094.1634, 1e-2);
            Check("支路 B 平衡阀 Kv", branchB.ValveKv, 145.8295, 1e-2);
            Check("支路 B 阀权度", branchB.ValveAuthority, 0.19178, 1e-4);
            CheckText("支路 B 判定为超限(需设平衡装置)",
                branchB.WithinLimit ? "在范围内" : "超限", "超限");
            CheckText("最不利环路不设平衡装置", branchA.RequiredAbsorbPa <= 0 ? "不设" : "设了", "不设");

            // ---------- 8) 并联环路平衡(风):超限支路给出「需增加的局部阻力系数 ζ」 ----------
            //   支路 A(最不利)Φ0.5、L=10、Σζ=1.0:21.22656 Pa;支路 B Φ0.5、L=5、Σζ=0.5:10.61328 Pa
            //   不平衡 = 10.61328 Pa(50.00%);末端管段动压 15.62778 Pa → 需增加 ζ = 10.61328/15.62778 = 0.67913
            var parallelAir = new HydraulicInput { Kind = HydraulicKind.AirDuct, MediumTempC = 20 };
            parallelAir.Segments.Add(new HydraulicSegment
            {
                Name = "风管 A", ElementId = 501, Shape = HydraulicShape.Round,
                DiameterM = 0.5, LengthM = 10, FlowM3H = 3600, LocalZetaSum = 1.0, OnCriticalPath = true
            });
            parallelAir.Segments.Add(new HydraulicSegment
            {
                Name = "风管 B", ElementId = 502, Shape = HydraulicShape.Round,
                DiameterM = 0.5, LengthM = 5, FlowM3H = 3600, LocalZetaSum = 0.5
            });
            parallelAir.Terminals.Add(new HydraulicTerminal { Name = "风口 A", ElementId = 601, OnCriticalPath = true });
            parallelAir.Terminals.Add(new HydraulicTerminal { Name = "风口 B", ElementId = 602 });
            parallelAir.Branches.Add(new HydraulicBranch
            {
                Name = "风口 A", TerminalElementId = 601, IsCritical = true, SegmentElementIds = { 501 }
            });
            parallelAir.Branches.Add(new HydraulicBranch
            {
                Name = "风口 B", TerminalElementId = 602, SegmentElementIds = { 502 }
            });

            var parallelAirResult = calc.Calculate(parallelAir, coefficients);
            CheckInt("风系统并联支路数 = 2", parallelAirResult.Branches.Count, 2);
            Check("风系统支路 B 不平衡 %", parallelAirResult.Branches[1].ImbalancePct, 50.0, 1e-6);
            Check("风系统支路 B 参考动压 Pa", parallelAirResult.Branches[1].ReferenceDynamicPa, 15.62778, 1e-4);
            Check("风系统支路 B 需增加 ζ", parallelAirResult.Branches[1].ZetaToAdd, 0.67913, 1e-4);
            CheckText("风系统平衡结论写明 ζ",
                parallelAirResult.Branches[1].Conclusion.Contains("ζ") ? "有" : parallelAirResult.Branches[1].Conclusion, "有");

            // ---------- 9) 系统阻力特性曲线:ΔP(Q) = 静压 + (总阻力 − 静压) × (Q ÷ Q设计)² ----------
            CheckInt("阻力特性曲线点数 = 9(50%~130% 每 10%)", parallelResult.Curve.Count, 9);
            Check("曲线 50% 系统阻力 Pa", parallelResult.Curve[0].ResistancePa, 7944.3979, 1e-2);
            Check("曲线 50% 需求值 Pa(= ×1.1)", parallelResult.Curve[0].RequiredPa, 8738.8377, 1e-2);
            Check("曲线 50% 流量 m³/h", parallelResult.Curve[0].FlowM3H, 18.0, 1e-9);
            Check("曲线 100% 系统阻力 Pa(设计点 = 计算总阻力)", parallelResult.Curve[5].ResistancePa, 31777.5915, 1e-2);
            Check("曲线 130% 系统阻力 Pa", parallelResult.Curve[8].ResistancePa, 53704.1297, 1e-2);
            Check("曲线 130% 流量 m³/h", parallelResult.Curve[8].FlowM3H, 46.8, 1e-9);

            // 静压不随流量变化:曲线起点也必须含静压(而不是跟着平方缩小)
            water.StaticHeightM = 10;
            var staticCurve = calc.Calculate(water, coefficients);
            water.StaticHeightM = 0;
            double staticPa = 999.70 * 9.81 * 10;
            Check("有静压时 50% 点 = 静压 + (总−静压)×0.25",
                staticCurve.Curve[0].ResistancePa,
                staticPa + (staticCurve.TotalResistancePa - staticPa) * 0.25, 1e-2);
            Check("有静压时 130% 点 = 静压 + (总−静压)×1.69",
                staticCurve.Curve[8].ResistancePa,
                staticPa + (staticCurve.TotalResistancePa - staticPa) * 1.69, 1e-2);

            // ---------- 10) 没有支路拓扑数据时:不做平衡分析(不猜),口径里写明 ----------
            CheckInt("单段 fixture 没有支路数据", waterResult.Branches.Count, 0);
            CheckText("无支路时不假装做了平衡分析",
                waterResult.BalanceNote.Contains("未做并联环路平衡分析") ? "有" : waterResult.BalanceNote, "有");
            CheckText("口径写明工况点要与厂家曲线求交",
                waterResult.BalanceNote.Contains("工况点") ? "有" : waterResult.BalanceNote, "有");
            CheckText("计算书含并联环路平衡表",
                ResultFormatter.FormatHydraulic(parallelWater, parallelResult, coefficients).Contains("并联环路平衡") ? "有" : "无", "有");
            CheckText("计算书含阻力特性曲线表",
                ResultFormatter.FormatHydraulic(parallelWater, parallelResult, coefficients).Contains("系统阻力特性曲线") ? "有" : "无", "有");

            // ---------- 11) 结果表 / 计算书:与结构同源,且不出现公式文档单元格编号 ----------
            var table = ResultTable.ForHydraulic(water, waterRated);
            CheckInt("水力结果表 分区数 = 6", table.Sections.Count, 6);
            CheckText("需求扬程行是合计行",
                table.Sections[2].Rows[table.Sections[2].Rows.Count - 1].IsTotal ? "是" : "否", "是");
            var report = ResultFormatter.FormatHydraulic(water, waterRated, coefficients);
            CheckText("计算书含逐段明细", report.Contains("管段明细") ? "有" : report, "有");
            CheckText("计算书含口径说明", report.Contains("阿尔特舒利") ? "有" : report, "有");
            CheckText("计算书含局部阻力系数取值", report.Contains("局部阻力系数取值") ? "有" : report, "有");
            CheckText("计算书不含单元格编号",
                System.Text.RegularExpressions.Regex.IsMatch(report, @"(?<![-A-Z])\b[A-Z]{1,2}[0-9]{2,3}\b") ? "有" : "无", "无");

            // ---------- 8) 落盘往返(系数集 + 两次读取的管网输入) ----------
            string dir = Path.Combine(Path.GetTempPath(), "HVACIDA-Hyd-" + Guid.NewGuid().ToString("N"));
            try
            {
                var repository = new XmlProjectRepository(dir);
                var service = new HydraulicInputService(repository);
                CheckText("未拾取过的介质返回 null(界面据此提示先去拾取)",
                    service.Calculate(HydraulicKind.AirDuct) == null ? "null" : "非 null", "null");

                var defaultProject = service.LoadProject();
                CheckInt("默认系数集自带风管管件表(13 条)", HydraulicLocalLossTable.CreateDuctDefaults().Count, 13);
                CheckInt("默认系数集自带水管管件表(12 条)", HydraulicLocalLossTable.CreatePipeDefaults().Count, 12);
                CheckInt("首次读取即补齐管件表", defaultProject.Coefficients.LocalLossItems.Count, 25);
                CheckText("管件表每条都写明取值来源",
                    HydraulicLocalLossTable.ZetaOf(defaultProject.Coefficients.LocalLossItems, "90°弯头(圆形 R/D=1.0)") > 0
                        && HydraulicLocalLossTable.CreateDuctDefaults()[0].Source.Length > 10 ? "有" : "缺", "有");
                CheckText("查不到的管件返回 NaN(不当 0)",
                    double.IsNaN(HydraulicLocalLossTable.ZetaOf(defaultProject.Coefficients.LocalLossItems, "不存在的管件"))
                        ? "NaN" : "有值", "NaN");

                service.Save(air);
                service.Save(water);
                var reloaded = service.LoadProject();
                CheckInt("落盘往返:风系统管段数", reloaded.Find(HydraulicKind.AirDuct, "").Segments.Count, 1);
                CheckText("落盘往返:风系统名称", reloaded.Find(HydraulicKind.AirDuct, "").SystemName, "机械送风 1");
                CheckInt("落盘往返:水系统设备/末端项数", reloaded.Find(HydraulicKind.WaterPipe, "").Terminals.Count, 2);
                CheckText("落盘往返:水系统机组阻力来源",
                    reloaded.Find(HydraulicKind.WaterPipe, "").Terminals[0].Source, "设备样本水阻 30 kPa");
                Check("落盘往返:需求扬程一致", service.Calculate(HydraulicKind.WaterPipe).RequiredHeadM,
                    waterRated.RequiredHeadM, 1e-9);

                service.Clear(HydraulicKind.AirDuct);
                CheckInt("清空风系统后不再有风系统输入", service.Load(HydraulicKind.AirDuct) == null ? 0 : 1, 0);
                CheckInt("清空风系统不影响水系统", service.Load(HydraulicKind.WaterPipe) == null ? 0 : 1, 1);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }

            Console.WriteLine();
        }

        // =====================================================================
        // 场景14:水力计算 全站多系统汇总 + Excel(.xlsx)导出
        //   期望值独立手算(与场景13 同一套公式):
        //   风 1:Φ0.5、L=10、Q=3600、Σζ=1.0 → 段 21.2266 + 出口动压 15.6278 = 36.8543 → 需求 40.5398 Pa
        //   风 2:Φ0.5、L=20、Q=7200、Σζ=1.0 → 段 103.6736 + 出口动压 62.5111 = 166.1847 → 需求 182.8032 Pa
        //   水 1:Φ0.1、L=50、Q=36、Σζ=2.0(10 ℃)→ 段 11777.5915 → 需求 12955.3507 Pa = 扬程 1.3210 m
        // =====================================================================
        private static void RunHydraulicSummaryChecks()
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("场景14:水力计算 多系统汇总 + Excel(.xlsx)导出");
            Console.WriteLine("==================================================");

            var calc = new HydraulicCalculator();
            string dir = Path.Combine(Path.GetTempPath(), "HVACIDA-HydSum-" + Guid.NewGuid().ToString("N"));
            try
            {
                var repository = new XmlProjectRepository(dir);
                var service = new HydraulicInputService(repository, calc);

                // ---------- 1) 按「介质 + 系统编号」upsert ----------
                service.Save(BuildAirSystem("SAF-1", "机械送风 1", 3600, 10));
                service.Save(BuildAirSystem("SAF-2", "机械送风 2", 7200, 20));
                int count = service.Save(BuildWaterSystem("CHWS-1", "冷冻水供水", 36, 50));
                CheckInt("多系统保存:全站 3 套", count, 3);
                CheckInt("风系统 2 套", service.LoadAll(HydraulicKind.AirDuct).Count, 2);
                CheckInt("水系统 1 套", service.LoadAll(HydraulicKind.WaterPipe).Count, 1);

                service.Save(BuildAirSystem("SAF-1", "机械送风 1(改)", 3600, 10));
                CheckInt("同编号 upsert 不增加套数", service.LoadProject().SystemCount, 3);
                CheckText("同编号被覆盖", service.Load(HydraulicKind.AirDuct, "SAF-1").SystemName, "机械送风 1(改)");
                CheckInt("删除一套系统", service.Remove(HydraulicKind.AirDuct, "SAF-2") ? 1 : 0, 1);
                CheckInt("删除后剩 2 套", service.LoadProject().SystemCount, 2);
                service.Save(BuildAirSystem("SAF-2", "机械送风 2", 7200, 20));

                // ---------- 2) 逐系统现算 + 汇总(可加量求和 / 压力取最大) ----------
                var summary = service.Summarize();
                CheckInt("汇总行数 = 3", summary.Rows.Count, 3);
                CheckInt("风系统数 = 2", summary.AirCount, 2);
                CheckInt("水系统数 = 1", summary.WaterCount, 1);
                CheckInt("汇总管段总数 = 3", summary.SegmentCount, 3);
                Check("汇总风量合计 m³/h", summary.TotalAirFlowM3H, 3600 + 7200, 1e-6);
                Check("汇总水量合计 m³/h", summary.TotalWaterFlowM3H, 36, 1e-6);
                Check("汇总管段总长 m", summary.TotalLengthM, 10 + 20 + 50, 1e-6);
                Check("最大需求全压 Pa(风 2 最大)", summary.MaxRequiredPressurePa, 182.8032, 1e-2);
                Check("最大需求扬程 m(水 1)", summary.MaxRequiredHeadM, 1.3210, 1e-4);
                CheckText("汇总口径写明压力不可相加", summary.Note.Contains("不可相加") ? "有" : summary.Note, "有");
                CheckText("最大需求全压是逐系统取最大(不是求和)",
                    Math.Abs(summary.MaxRequiredPressurePa - (40.5398 + 182.8032)) > 1.0 ? "取最大" : "像求和", "取最大");

                // 汇总里的数字必须与单系统现算一致(汇总不重算);行序固定为"风在前、各按编号排序"
                CheckText("汇总行序固定:风在前按编号", summary.Rows[0].SystemCode + "/" + summary.Rows[1].SystemCode,
                    "SAF-1/SAF-2");
                CheckText("汇总行序固定:水在后", summary.Rows[2].SystemCode, "CHWS-1");
                var saf2 = service.Calculate(HydraulicKind.AirDuct, "SAF-2");
                Check("汇总行与单系统现算一致", summary.Rows[1].RequiredPressurePa, saf2.RequiredPressurePa, 1e-9);
                Check("单系统设计流量 m³/h", saf2.DesignFlowM3H, 7200, 1e-9);

                // ---------- 3) 旧版单系统文件自动迁移 ----------
                string legacyDir = Path.Combine(Path.GetTempPath(), "HVACIDA-HydLegacy-" + Guid.NewGuid().ToString("N"));
                try
                {
                    Directory.CreateDirectory(legacyDir);
                    var legacy = new HydraulicProject
                    {
                        Coefficients = HydraulicCoefficients.CreateDefault(),
                        LegacyAir = BuildAirSystem("SAF-OLD", "旧版风系统", 3600, 10),
                        LegacyWater = BuildWaterSystem("CHWS-OLD", "旧版水系统", 36, 50)
                    };
                    using (var stream = File.Create(Path.Combine(legacyDir, "hydraulic.xml")))
                    {
                        new System.Xml.Serialization.XmlSerializer(typeof(HydraulicProject)).Serialize(stream, legacy);
                    }

                    var legacyRepo = new XmlProjectRepository(legacyDir);
                    var legacyService = new HydraulicInputService(legacyRepo, calc);
                    var migrated = legacyService.LoadProject();
                    CheckInt("旧版 Air/Water 迁进多系统容器", migrated.SystemCount, 2);
                    CheckText("迁移保留了系统编号", migrated.Find(HydraulicKind.AirDuct, "SAF-OLD").SystemCode, "SAF-OLD");
                    CheckInt("迁移发生了落盘", legacyService.LastLoadMigrated ? 1 : 0, 1);
                    CheckInt("迁移后文件里已无 Air 元素(不再写回)",
                        System.Text.RegularExpressions.Regex.IsMatch(
                            File.ReadAllText(Path.Combine(legacyDir, "hydraulic.xml")), "<Air>|:Air>") ? 1 : 0, 0);
                }
                finally
                {
                    try { Directory.Delete(legacyDir, true); } catch { }
                }

                // ---------- 4) Excel 导出:结构 + 内容都要能验 ----------
                var air1 = service.Load(HydraulicKind.AirDuct, "SAF-1");
                var air1Result = service.Calculate(HydraulicKind.AirDuct, "SAF-1");
                var book = HydraulicExcelExporter.BuildSystem(air1, air1Result, service.LoadCoefficients());
                CheckInt("单系统工作簿 6 页", book.SheetCount, 6);

                string bookPath = Path.Combine(dir, "单系统水力计算书.xlsx");
                book.Save(bookPath);
                CheckInt("xlsx 已写出", File.Exists(bookPath) ? 1 : 0, 1);

                string workbookXml, sheet1Xml, entries;
                ReadXlsx(bookPath, out workbookXml, out sheet1Xml, out entries);
                CheckText("工作簿含 6 张表名",
                    workbookXml.Contains("汇总") && workbookXml.Contains("管段明细") && workbookXml.Contains("环路阻力项") &&
                    workbookXml.Contains("并联环路平衡") && workbookXml.Contains("阻力特性曲线") && workbookXml.Contains("取值与口径")
                        ? "齐" : workbookXml, "齐");
                CheckText("xlsx 是合法 ZIP(含工作簿与 6 张表)", entries, "条目 11 个");
                CheckText("汇总页写出了需求全压(标签 + 数值单元格)",
                    sheet1Xml.Contains("需求全压") && sheet1Xml.Contains("40.539770908214884") ? "有" : "缺", "有");

                var summaryBook = HydraulicExcelExporter.BuildSummary(summary);
                CheckInt("全站汇总工作簿页数 = 3 + 2×3 套系统", summaryBook.SheetCount, 9);
                string summaryPath = Path.Combine(dir, "全站水力汇总.xlsx");
                summaryBook.Save(summaryPath);
                string sWorkbook, sSheet1, sEntries;
                ReadXlsx(summaryPath, out sWorkbook, out sSheet1, out sEntries);
                CheckText("全站工作簿含全站汇总/逐系统/取值与口径",
                    sWorkbook.Contains("全站汇总") && sWorkbook.Contains("逐系统") && sWorkbook.Contains("取值与口径") ? "有" : sWorkbook, "有");
                CheckText("全站工作簿含逐系统明细页(段-风-SAF-1)",
                    sWorkbook.Contains("段-风-SAF-1") ? "有" : sWorkbook, "有");
                CheckText("全站汇总页写明压力不可加",
                    sSheet1.Contains("不可加") ? "有" : "缺", "有");

                // ---------- 5) 全站汇总的文本计算书 ----------
                string summaryText = ResultFormatter.FormatHydraulicSummary(summary);
                CheckText("全站计算书含逐系统表", summaryText.Contains("逐系统") ? "有" : "无", "有");
                CheckText("全站计算书含每套系统的完整计算书", summaryText.Contains("机械送风 2") ? "有" : "无", "有");
                CheckText("全站计算书不含单元格编号",
                    System.Text.RegularExpressions.Regex.IsMatch(summaryText, @"(?<![-A-Z])\b[A-Z]{1,2}[0-9]{2,3}\b") ? "有" : "无", "无");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }

            Console.WriteLine();
        }

        private static HydraulicInput BuildAirSystem(string code, string name, double flowM3H, double lengthM)
        {
            var input = new HydraulicInput
            {
                Kind = HydraulicKind.AirDuct,
                SystemCode = code,
                SystemName = name,
                MediumTempC = 20,
                FromModel = false
            };
            input.Segments.Add(new HydraulicSegment
            {
                Name = name + " 主管", Shape = HydraulicShape.Round, DiameterM = 0.5,
                LengthM = lengthM, FlowM3H = flowM3H, LocalZetaSum = 1.0, OnCriticalPath = true
            });
            return input;
        }

        private static HydraulicInput BuildWaterSystem(string code, string name, double flowM3H, double lengthM)
        {
            var input = new HydraulicInput
            {
                Kind = HydraulicKind.WaterPipe,
                SystemCode = code,
                SystemName = name,
                MediumTempC = 10,
                FromModel = false
            };
            input.Segments.Add(new HydraulicSegment
            {
                Name = name + " 干管", Shape = HydraulicShape.Round, DiameterM = 0.1,
                LengthM = lengthM, FlowM3H = flowM3H, LocalZetaSum = 2.0, OnCriticalPath = true
            });
            return input;
        }

        /// <summary>读 .xlsx(它是 ZIP):取 workbook.xml、sheet1.xml 与条目数摘要(供断言)。</summary>
        private static void ReadXlsx(string path, out string workbookXml, out string sheet1Xml, out string entries)
        {
            workbookXml = "";
            sheet1Xml = "";
            entries = "";
            using (var zip = System.IO.Compression.ZipFile.OpenRead(path))
            {
                var names = new System.Collections.Generic.List<string>();
                foreach (var entry in zip.Entries)
                {
                    names.Add(entry.FullName);
                    if (entry.FullName == "xl/workbook.xml") workbookXml = ReadEntry(entry);
                    if (entry.FullName == "xl/worksheets/sheet1.xml") sheet1Xml = ReadEntry(entry);
                }
                entries = "条目 " + names.Count + " 个";
            }
        }

        private static string ReadEntry(System.IO.Compression.ZipArchiveEntry entry)
        {
            using (var stream = entry.Open())
            using (var reader = new System.IO.StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        // =====================================================================
        // 场景15:计算书 Excel 导出推广(大系统负荷 / 排烟 / 小系统 / 小系统全站汇总)
        //   断言方式:写出 .xlsx → 解压 → 校验工作表名与"关键单元格确实写进去了",
        //   且写出的数值与 Core 结果表里的**同一个数**逐字符一致(导出器不另算数字)。
        // =====================================================================
        private static void RunCalculationExcelChecks()
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("场景15:计算书 Excel 导出(大系统负荷 / 排烟 / 小系统 / 全站汇总)");
            Console.WriteLine("==================================================");

            string dir = Path.Combine(Path.GetTempPath(), "HVACIDA-Xlsx-" + Guid.NewGuid().ToString("N"));
            try
            {
                // ---------- 1) 大系统负荷 ----------
                var loadInput = BuildBeijingSample();
                var loadResult = new LargeSystemLoadCalculator().Calculate(loadInput);
                var loadBook = LargeSystemExcelExporter.BuildLoad(loadInput, loadResult);
                CheckInt("大系统负荷工作簿 2 页(负荷汇总 + 口径)", loadBook.SheetCount, 2);

                string loadPath = Path.Combine(dir, "大系统负荷计算书.xlsx");
                loadBook.Save(loadPath);
                string wb, s1, entries;
                ReadXlsx(loadPath, out wb, out s1, out entries);
                CheckText("大系统工作簿页名", wb.Contains("负荷汇总") && wb.Contains("口径与待补") ? "齐" : wb, "齐");
                CheckText("工作表名都合法(≤31 字符、无非法字符)", SheetNamesValid(wb) ? "合法" : wb, "合法");

                double? cooling = null;
                var loadTable = ResultTable.ForLargeSystem(loadInput, loadResult);
                foreach (var section in loadTable.Sections)
                {
                    foreach (var row in section.Rows)
                    {
                        if (row.Value.HasValue && row.Label.Contains("总制冷量")) cooling = row.Value;
                    }
                }
                CheckText("Excel 里的总制冷量与结果表逐字符一致",
                    cooling.HasValue && s1.Contains(cooling.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
                        ? "一致" : "不一致", "一致");

                // ---------- 2) 大系统排烟 ----------
                var smokeInput = new LargeSmokeInput();
                var smokeResult = new LargeSmokeCalculator().Calculate(loadInput, smokeInput);
                var smokeBook = LargeSystemExcelExporter.BuildSmoke(smokeInput, smokeResult);
                CheckInt("排烟工作簿 3 页(分区 + 选型 + 口径)", smokeBook.SheetCount, 3);

                string smokePath = Path.Combine(dir, "大系统排烟计算书.xlsx");
                smokeBook.Save(smokePath);
                string swb, ss1, sentries;
                ReadXlsx(smokePath, out swb, out ss1, out sentries);
                CheckText("排烟分区表含区域名与单台风量列",
                    ss1.Contains(smokeResult.Zones[0].ZoneName) && ss1.Contains("单台风机风量") ? "有" : "缺", "有");
                CheckText("排烟分区表写出计算排烟量数值",
                    ss1.Contains(smokeResult.Zones[0].CalculatedFlowM3H.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
                        ? "有" : "缺", "有");

                // ---------- 3) 小系统(单系统)----------
                var smallInput = new SmallSystemInput
                {
                    SystemType = SmallSystemType.AllAirOnceReturn,
                    SystemCode = "AHU-X101",
                    IndoorTempC = 27, SupplyTempDiffC = 10, DuctTempRiseC = 1.5,
                    LightingIndexWm2 = 20, WallMoistureEmission = 2, OutdoorWetBulbC = 28.2
                };
                smallInput.Rooms.Add(SmallRoomInput.Create("弱电间1", 50, 5.9));
                smallInput.Rooms.Add(SmallRoomInput.Create("弱电间2", 30, 5.9));
                var smallResult = new SmallSystemLoadCalculator().Calculate(smallInput);
                var smallBook = SmallSystemExcelExporter.BuildSystem(smallInput, smallResult);
                CheckInt("小系统工作簿 4 页(系统结果/房间明细/设备选型/口径)", smallBook.SheetCount, 4);

                string smallPath = Path.Combine(dir, "小系统计算书.xlsx");
                smallBook.Save(smallPath);
                string xwb, xs1, xentries, roomXml, equipXml;
                ReadXlsxSheets(smallPath, out xwb, out xs1, out xentries,
                    new[] { "xl/worksheets/sheet2.xml", "xl/worksheets/sheet3.xml" }, out roomXml, out equipXml);
                CheckText("小系统房间明细页含房间名与面积",
                    roomXml.Contains("弱电间1") && roomXml.Contains("50") ? "有" : "缺", "有");
                CheckText("小系统设备选型页含设备代码", equipXml.Contains("AHU-X101") ? "有" : "缺", "有");
                CheckText("小系统工作簿页名",
                    xwb.Contains("系统结果") && xwb.Contains("房间明细") && xwb.Contains("设备选型") &&
                    xwb.Contains("口径与待补") ? "齐" : xwb, "齐");

                // ---------- 4) 小系统全站汇总(多系统 + 每套明细页)----------
                string sumDir = Path.Combine(Path.GetTempPath(), "HVACIDA-SmallSum-" + Guid.NewGuid().ToString("N"));
                try
                {
                    var sumRepo = new XmlProjectRepository(sumDir);
                    var sumService = new SmallSystemInputService(sumRepo);
                    var a1 = new SmallSystemInput { SystemType = SmallSystemType.AllAirOnceReturn, SystemCode = "AHU-A101" };
                    a1.Rooms.Add(SmallRoomInput.Create("弱电间1", 50, 5.9));
                    sumService.Save(a1);
                    var a2 = new SmallSystemInput { SystemType = SmallSystemType.AllAirOnceReturn, SystemCode = "AHU-A201" };
                    a2.Rooms.Add(SmallRoomInput.Create("强电间1", 80, 4.55));
                    sumService.Save(a2);
                    var ef = new SmallSystemInput { SystemType = SmallSystemType.ExhaustVentilation, SystemCode = "EAF-A601" };
                    var room = SmallRoomInput.Create("男卫", 5.83, 5.9);
                    room.RoomType = "男卫生间";
                    ef.Rooms.Add(room);
                    sumService.Save(ef);

                    var summary = new SmallSystemSummaryService(new SmallSystemLoadCalculator()).Summarize(sumService.LoadProject());
                    var sumBook = SmallSystemExcelExporter.BuildSummary(summary);
                    // 3 页(逐系统 / 全站合计 / 口径与待补) + 3 套 × 2 页明细 = 9
                    CheckInt("小系统全站汇总工作簿 9 页", sumBook.SheetCount, 9);

                    string sumPath = Path.Combine(dir, "小系统全站汇总.xlsx");
                    sumBook.Save(sumPath);
                    string sumWb, sumS1, sumEntries;
                    ReadXlsx(sumPath, out sumWb, out sumS1, out sumEntries);
                    CheckText("汇总工作簿含逐系统/全站合计/口径与每套明细页",
                        sumWb.Contains("逐系统") && sumWb.Contains("全站合计") && sumWb.Contains("口径与待补") &&
                        sumWb.Contains("房-AHU-A101") && sumWb.Contains("设-EAF-A601") ? "齐" : sumWb, "齐");
                    CheckText("逐系统页含系统编号与逐系统冷负荷",
                        sumS1.Contains("AHU-A101") &&
                        sumS1.Contains(summary.Rows[0].TotalCoolingKw.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
                            ? "有" : "缺", "有");
                    CheckText("汇总工作簿工作表名都合法", SheetNamesValid(sumWb) ? "合法" : sumWb, "合法");
                }
                finally
                {
                    try { Directory.Delete(sumDir, true); } catch { }
                }
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }

            Console.WriteLine();
        }

        /// <summary>校验 workbook.xml 里的工作表名符合 Excel 约束(≤31 字符、不含 []:*?/\)。</summary>
        private static bool SheetNamesValid(string workbookXml)
        {
            foreach (System.Text.RegularExpressions.Match match in
                System.Text.RegularExpressions.Regex.Matches(workbookXml, "name=\"([^\"]*)\""))
            {
                string name = match.Groups[1].Value;
                if (name.Length == 0 || name.Length > 31) return false;
                foreach (char c in name)
                {
                    if (c == '[' || c == ']' || c == ':' || c == '*' || c == '?' || c == '/' || c == '\\') return false;
                }
            }
            return true;
        }

        /// <summary>读 .xlsx 中 workbook、sheet1 与两张指定工作表的内容(供逐页断言)。</summary>
        private static void ReadXlsxSheets(string path, out string workbookXml, out string sheet1Xml, out string entries,
            string[] wanted, out string sheetA, out string sheetB)
        {
            sheetA = "";
            sheetB = "";
            workbookXml = "";
            sheet1Xml = "";
            entries = "";
            using (var zip = System.IO.Compression.ZipFile.OpenRead(path))
            {
                int count = 0;
                foreach (var entry in zip.Entries)
                {
                    count++;
                    if (entry.FullName == "xl/workbook.xml") workbookXml = ReadEntry(entry);
                    if (entry.FullName == "xl/worksheets/sheet1.xml") sheet1Xml = ReadEntry(entry);
                    if (wanted != null && wanted.Length > 0 && entry.FullName == wanted[0]) sheetA = ReadEntry(entry);
                    if (wanted != null && wanted.Length > 1 && entry.FullName == wanted[1]) sheetB = ReadEntry(entry);
                }
                entries = "条目 " + count + " 个";
            }
        }

        // =====================================================================
        // 场景16:材料表统计(需求 2.5)—— 归并键含单位、类别小计遇混合单位不累加
        // =====================================================================
        private static void RunMaterialTakeoffChecks()
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("场景16:材料表统计(归并口径 / 类别小计 / Excel)");
            Console.WriteLine("==================================================");

            var service = new MaterialTakeoffService();
            var items = new List<MaterialItem>
            {
                Duct("矩形风管", "1200×400", "m", 10),
                Duct("矩形风管", "1200×400", "m", 20),
                Duct("矩形风管", "1200×400", "m", 5),
                // 同一类型的这一段没取到长度曲线 → 按 1 件计(单位不同,故不能与上面的米相加)
                Duct("矩形风管", "1200×400", "个", 1),
                Piece(MaterialCategory.DuctFitting, "弯头", "90°弯头", 2),
                Piece(MaterialCategory.DuctFitting, "弯头", "90°弯头", 1),
                new MaterialItem
                {
                    Category = MaterialCategory.Insulation, CategoryName = "保温", FamilyName = "风管保温",
                    TypeName = "30 mm 玻璃棉", Unit = "m", Quantity = 20, Count = 1, Note = "长度沿宿主管道量取"
                }
            };

            var result = service.Summarize(items);
            CheckInt("归并后类型数 = 4(m 段 / 个段 / 管件 / 保温)", result.Rows.Count, 4);
            CheckInt("读到构件数 = 7", result.ItemCount, 7);
            Check("构件总件数 = 8(风管 4 件 + 管件 3 件 + 保温 1 件)", result.TotalCount, 8, 1e-9);
            CheckText("按类别排序(风管在最前)", result.Rows[0].CategoryName, "风管");
            Check("风管 m 行合计 = 35", result.Rows[0].TotalQuantity, 35, 1e-9);
            Check("风管 m 行件数 = 3", result.Rows[0].TotalCount, 3, 1e-9);
            Check("风管 个 行合计 = 1(不与米相加)", result.Rows[1].TotalQuantity, 1, 1e-9);
            CheckText("风管 个 行的单位", result.Rows[1].Unit, "个");
            Check("管件归并后合计 = 3", result.Rows[2].TotalQuantity, 3, 1e-9);
            Check("保温合计 = 20 m", result.Rows[3].TotalQuantity, 20, 1e-9);

            var ductTotal = result.CategoryTotals[0];
            CheckText("风管类别小计:混合单位不累加", ductTotal.Unit, "混合单位(不累加)");
            Check("风管类别小计数量 = 0(不累加)", ductTotal.TotalQuantity, 0, 1e-9);
            Check("风管类别小计件数 = 4", ductTotal.TotalCount, 4, 1e-9);
            CheckInt("风管类别类型数 = 2", ductTotal.TypeCount, 2);
            CheckText("口径说明写明单位进归并键", result.Note.Contains("单位进归并键") ? "有" : result.Note, "有");

            var table = MaterialTakeoffTable.ForSummary(result);
            CheckInt("类别小计表 2 个分区(概况 + 类别小计)", table.Sections.Count, 2);
            var report = MaterialTakeoffTable.ToText(result);
            CheckText("文本材料表含类别与类型", report.Contains("风管") && report.Contains("矩形风管") ? "有" : "无", "有");
            CheckText("文本材料表不含单元格编号",
                System.Text.RegularExpressions.Regex.IsMatch(report, @"(?<![-A-Z])\b[A-Z]{1,2}[0-9]{2,3}\b") ? "有" : "无", "无");

            string dir = Path.Combine(Path.GetTempPath(), "HVACIDA-Takeoff-" + Guid.NewGuid().ToString("N"));
            try
            {
                var book = MaterialTakeoffExcelExporter.Build(result);
                CheckInt("材料表工作簿 4 页(含图例表)", book.SheetCount, 4);
                string path = Path.Combine(dir, "材料表统计.xlsx");
                book.Save(path);
                string wb, s1, entries, detailXml, noteXml;
                ReadXlsxSheets(path, out wb, out s1, out entries,
                    new[] { "xl/worksheets/sheet2.xml", "xl/worksheets/sheet3.xml" }, out detailXml, out noteXml);
                CheckText("工作簿页名(类别小计 / 逐类型明细 / 口径与待补)",
                    wb.Contains("类别小计") && wb.Contains("逐类型明细") && wb.Contains("口径与待补") ? "齐" : wb, "齐");
                CheckText("类别小计页写出混合单位口径", s1.Contains("混合单位") ? "有" : "缺", "有");
                CheckText("逐类型明细页写出族名与数量 35",
                    detailXml.Contains("矩形风管") && detailXml.Contains("35") ? "有" : "缺", "有");
                CheckText("工作表名合法", SheetNamesValid(wb) ? "合法" : wb, "合法");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }

            var empty = service.Summarize(new List<MaterialItem>());
            CheckInt("空清单:不产生行", empty.Rows.Count, 0);
            CheckText("空清单:给出指引而不摆结果",
                empty.Note.Contains("没有读到任何构件") ? "有" : empty.Note, "有");
            Console.WriteLine();
        }

        private static MaterialItem Duct(string family, string type, string unit, double quantity)
        {
            return new MaterialItem
            {
                Category = MaterialCategory.Duct, CategoryName = "风管", FamilyName = family, TypeName = type,
                Unit = unit, Quantity = quantity, Count = 1,
                Note = unit == "m" ? "长度取定位线曲线长度" : "未取到长度曲线,按 1 件计(请在模型里核对)"
            };
        }

        private static MaterialItem Piece(MaterialCategory category, string family, string type, double count)
        {
            return new MaterialItem
            {
                Category = category, CategoryName = MaterialTakeoffService.CategoryName(category),
                FamilyName = family, TypeName = type, Unit = "个", Quantity = count, Count = count, Note = "按件数计"
            };
        }

        // =====================================================================
        // 场景17:图纸清单与批量出图(需求 2.6)—— 排序 / 空图框 / 图框统计 / 导出记录 / Excel
        // =====================================================================
        private static void RunSheetCatalogChecks()
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("场景17:图纸清单与批量出图(图框统计 / 导出记录 / Excel)");
            Console.WriteLine("==================================================");

            var service = new SheetCatalogService();
            var sheets = new List<SheetItem>
            {
                new SheetItem
                {
                    SheetNumber = "A-02", SheetName = "站厅层通风平面", TitleBlockFamily = "标准图框",
                    TitleBlockType = "A1", WidthMm = 841, HeightMm = 594,
                    Views = new List<SheetViewItem>
                    {
                        new SheetViewItem { ViewName = "站厅层通风平面", ViewType = "FloorPlan", Scale = 100 },
                        new SheetViewItem { ViewName = "站厅层空调水", ViewType = "FloorPlan", Scale = 100 }
                    }
                },
                new SheetItem
                {
                    SheetNumber = "A-01", SheetName = "设计说明", TitleBlockFamily = "标准图框",
                    TitleBlockType = "A1", WidthMm = 841, HeightMm = 594,
                    Views = new List<SheetViewItem> { new SheetViewItem { ViewName = "设计说明", ViewType = "DraftingView" } }
                },
                new SheetItem
                {
                    SheetNumber = "A-03", SheetName = "预留", TitleBlockFamily = "标准图框",
                    TitleBlockType = "A2", WidthMm = 594, HeightMm = 420
                }
            };

            var result = service.Summarize(sheets);
            CheckInt("图纸数 = 3", result.SheetCount, 3);
            CheckInt("空图框 = 1(A-03)", result.EmptySheetCount, 1);
            CheckInt("视图总数 = 3", result.ViewCount, 3);
            CheckText("按图纸编号排序", result.Sheets[0].SheetNumber, "A-01");
            CheckInt("图框类型小计 = 2(A1 / A2)", result.TitleBlocks.Count, 2);
            Check("A1 图框张数 = 2", result.TitleBlocks[0].SheetCount, 2, 1e-9);
            CheckInt("A1 图框视图数 = 3", result.TitleBlocks[0].ViewCount, 3);
            CheckText("图幅文字取图框尺寸", result.Sheets[0].SizeText, "841 × 594 mm");
            CheckText("空图框判定", result.Sheets[2].IsEmpty ? "空" : "有视图", "空");
            CheckText("待补提示报出空图框数", result.PendingNote.Contains("空图框") ? "有" : result.PendingNote, "有");

            SheetCatalogService.AddExport(result, new SheetExportRecord
            {
                SheetNumber = "A-01", SheetName = "设计说明", Format = "DWG", Succeeded = true,
                OutputPath = "A-01_设计说明.dwg"
            });
            SheetCatalogService.AddExport(result, new SheetExportRecord
            {
                SheetNumber = "A-03", SheetName = "预留", Format = "PDF", Succeeded = false,
                Message = "本机没有可用的 PDF 打印机"
            });
            CheckInt("导出成功数 = 1", result.ExportSucceeded, 1);
            CheckInt("导出失败数 = 1", result.ExportFailed, 1);
            var table = SheetCatalogTable.ForSummary(result);
            CheckInt("概况表 2 个分区(概况 + 图框统计)", table.Sections.Count, 2);
            var report = SheetCatalogTable.ToText(result);
            CheckText("文本清单含图纸编号与视图", report.Contains("A-02") && report.Contains("站厅层通风平面") ? "有" : "无", "有");
            CheckText("文本清单含出图记录", report.Contains("批量出图") && report.Contains("成功") ? "有" : "无", "有");

            string dir = Path.Combine(Path.GetTempPath(), "HVACIDA-Sheets-" + Guid.NewGuid().ToString("N"));
            try
            {
                var book = SheetCatalogExcelExporter.Build(result);
                CheckInt("图纸清单工作簿 4 页", book.SheetCount, 4);
                string path = Path.Combine(dir, "图纸清单.xlsx");
                book.Save(path);
                string wb, s1, entries, detailXml, exportXml;
                ReadXlsxSheets(path, out wb, out s1, out entries,
                    new[] { "xl/worksheets/sheet2.xml", "xl/worksheets/sheet3.xml" }, out detailXml, out exportXml);
                CheckText("工作簿页名(概况与图框 / 逐张图纸 / 批量出图记录 / 口径与待补)",
                    wb.Contains("概况与图框") && wb.Contains("逐张图纸") && wb.Contains("批量出图记录") &&
                    wb.Contains("口径与待补") ? "齐" : wb, "齐");
                CheckText("逐张图纸页写出图纸编号与图幅",
                    detailXml.Contains("A-02") && detailXml.Contains("841") ? "有" : "缺", "有");
                CheckText("批量出图记录页写出失败原因",
                    exportXml.Contains("没有可用的 PDF 打印机") ? "有" : "缺", "有");
                CheckText("工作表名合法", SheetNamesValid(wb) ? "合法" : wb, "合法");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }

            var empty = service.Summarize(new List<SheetItem>());
            CheckInt("空清单:图纸数 0", empty.SheetCount, 0);
            CheckText("空清单:给出指引而不摆结果",
                empty.Note.Contains("没有读到图纸") ? "有" : empty.Note, "有");
            Console.WriteLine();
        }

        // =====================================================================
        // 场景18:规范 / 口径知识库(需求 2.7)—— 条目完整(带出处)、检索加权、答不出明说范围、Excel
        // =====================================================================
        private static void RunKnowledgeChecks()
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("场景18:规范/口径知识库(条目/检索/范围说明/Excel)");
            Console.WriteLine("==================================================");

            var entries = KnowledgeBase.All;
            CheckText("条目数 ≥ 15", entries.Count >= 15 ? "够" : entries.Count.ToString(), "够");

            int noSource = 0, noQuestion = 0, noAnswer = 0, noKeywords = 0;
            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.Source)) noSource++;
                if (string.IsNullOrEmpty(entry.Question)) noQuestion++;
                if (string.IsNullOrEmpty(entry.Answer)) noAnswer++;
                if (entry.Keywords.Count == 0) noKeywords++;
            }
            CheckInt("每条都带出处(0 = 正常)", noSource, 0);
            CheckInt("每条都有典型问法(0 = 正常)", noQuestion, 0);
            CheckInt("每条都有正文(0 = 正常)", noAnswer, 0);
            CheckInt("每条都有关键词(0 = 正常)", noKeywords, 0);

            var hit = KnowledgeBase.Answer("排烟风机怎么选?");
            CheckText("命中排烟条", hit.HasAnswer && hit.Top != null && hit.Top.Id == "large-smoke"
                ? "命中" : (hit.Top == null ? "无" : hit.Top.Id), "命中");
            CheckText("答复含选型系数 1.2", hit.AnswerText.Contains("1.2") ? "有" : "缺", "有");
            CheckText("答复挂出处", hit.AnswerText.Contains("出处:") ? "有" : "缺", "有");

            var water = KnowledgeBase.Answer("水系统扬程怎么算?");
            CheckText("命中水力条", water.HasAnswer && water.Top != null && water.Top.Id == "hydraulic-formula"
                ? "命中" : (water.Top == null ? "无" : water.Top.Id), "命中");
            CheckText("扬程答复含 ρg 口径", water.AnswerText.Contains("ρg") ? "有" : "缺", "有");

            var outside = KnowledgeBase.Answer("混凝土强度等级怎么定");
            CheckText("范围外问题:不命中(不编答案)", outside.HasAnswer ? "编了" : "未命中", "未命中");
            CheckText("范围外:给出知识范围说明",
                outside.ScopeNote.Contains("不在当前知识库范围内") ? "有" : outside.ScopeNote, "有");
            CheckText("范围外:明确不编答案",
                outside.ScopeNote.Contains("不会为范围外的问题编答案") ? "有" : "缺", "有");

            var search = KnowledgeBase.Search("水力 扬程");
            CheckText("检索按得分排序(水力条在首位)",
                search.Count > 0 && search[0].Entry.Id == "hydraulic-formula" ? "对" : (search.Count == 0 ? "无" : search[0].Entry.Id), "对");
            CheckInt("检索结果条数不超过上限 5",
                KnowledgeBase.Search("小系统 排烟 水力 气象 材料表 图纸").Count <= KnowledgeBase.MaxMatches ? 1 : 0, 1);

            CheckInt("规范依据分类非空", KnowledgeBase.ByCategory(KnowledgeCategory.Standard).Count > 0 ? 1 : 0, 1);
            CheckInt("待补与局限分类非空", KnowledgeBase.ByCategory(KnowledgeCategory.Pending).Count > 0 ? 1 : 0, 1);
            CheckInt("按编号取条目", KnowledgeBase.Find("hydraulic-formula") != null ? 1 : 0, 1);
            CheckText("示例问题数 = 条目数", KnowledgeBase.SampleQuestions().Count == entries.Count ? "齐" : "缺", "齐");
            CheckText("分类中文名", KnowledgeBase.CategoryName(KnowledgeCategory.Pending), "待补与局限");

            string dir = Path.Combine(Path.GetTempPath(), "HVACIDA-Kb-" + Guid.NewGuid().ToString("N"));
            try
            {
                var book = KnowledgeBaseExcelExporter.Build(entries);
                CheckInt("知识库工作簿 2 页", book.SheetCount, 2);
                string path = Path.Combine(dir, "知识库条目.xlsx");
                book.Save(path);
                string wb, s1, sheetEntries;
                ReadXlsx(path, out wb, out s1, out sheetEntries);
                CheckText("工作簿页名(条目清单 / 说明)",
                    wb.Contains("条目清单") && wb.Contains("说明") ? "齐" : wb, "齐");
                CheckText("条目清单页含标题与出处列",
                    s1.Contains("出处") && s1.Contains("排烟") ? "有" : "缺", "有");
                CheckText("工作表名合法", SheetNamesValid(wb) ? "合法" : wb, "合法");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
            Console.WriteLine();
        }

        // =====================================================================
        // 场景19:图例表(复用材料表)+ 自动标注结果口径(需求 2.6)
        // =====================================================================
        private static void RunLegendChecks()
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("场景19:图例表(复用材料表)+ 自动标注结果");
            Console.WriteLine("==================================================");

            var service = new MaterialTakeoffService();
            var items = new List<MaterialItem>
            {
                Duct("矩形风管", "1200×400", "m", 10),
                Duct("矩形风管", "1200×400", "m", 20),
                Duct("矩形风管", "1200×400", "m", 5),
                Piece(MaterialCategory.DuctFitting, "弯头", "90°弯头", 2)
            };
            var result = service.Summarize(items);
            var legend = MaterialLegendBuilder.Build(result);
            CheckInt("图例行数 = 材料表行数(2)", legend.Count, 2);
            CheckInt("图例按类别从 1 开始编号", legend[0].Index, 1);
            Check("图例数量与材料表一致(35 m)", legend[0].Quantity, 35, 1e-9);
            CheckText("图例类型含族与类型", legend[0].TypeName.Contains("矩形风管") ? "有" : legend[0].TypeName, "有");
            CheckText("图例规格文字含类别/数量/单位",
                legend[0].Spec.Contains("风管") && legend[0].Spec.Contains("35") && legend[0].Spec.Contains("m")
                    ? "有" : legend[0].Spec, "有");

            var table = MaterialLegendBuilder.ForLegend(legend);
            CheckInt("图例表 1 个分区", table.Sections.Count, 1);
            CheckText("图例表口径写明不自动在图纸上排版",
                table.Note.Contains("不自动在图纸上排版图例") ? "有" : "缺", "有");
            CheckText("图例表数量与材料表同源(35)",
                table.Sections[0].Rows[0].Value.HasValue && Math.Abs(table.Sections[0].Rows[0].Value.Value - 35) < 1e-9
                    ? "一致" : "不一致", "一致");
            CheckText("文本图例含类别与标题", MaterialLegendBuilder.ToText(legend).Contains("图例表") ? "有" : "无", "有");
            CheckInt("无材料表数据时图例为空", MaterialLegendBuilder.Build(new MaterialTakeoffResult()).Count, 0);
            CheckInt("材料表 Excel 增图例表页(4 页)", MaterialTakeoffExcelExporter.Build(result).SheetCount, 4);

            var tag = new AutoTagResult();
            tag.Views.Add(new AutoTagViewResult { ViewName = "站厅层通风平面", Added = 12, Message = "已添加 12 个空间标注" });
            tag.Views.Add(new AutoTagViewResult { ViewName = "站台层通风平面", Skipped = 8, Message = "已有同类标注,跳过" });
            tag.AddedTotal = 12;
            tag.SkippedTotal = 8;
            CheckInt("标注结果:视图数 2", tag.ViewCount, 2);
            CheckInt("标注结果:新增合计 12", tag.AddedTotal, 12);
            CheckInt("标注结果:跳过合计 8", tag.SkippedTotal, 8);
            CheckText("标注口径写明跳过已有标注", tag.Note.Contains("跳过") ? "有" : "缺", "有");
            CheckText("范围说明写明只做空间名称编号标注",
                tag.PendingNote.Contains("只做空间名称/编号标注") ? "有" : "缺", "有");
            Console.WriteLine();
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
            Check(name, actual, expected, 0);
        }

        /// <summary>带绝对容差的断言(公式文档示例含四舍五入,需容差)。</summary>
        private static void Check(string name, double actual, double expected, double absTolerance)
        {
            double tolerance = Math.Max(absTolerance, Math.Max(1e-6, Math.Abs(expected) * 1e-6));
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
