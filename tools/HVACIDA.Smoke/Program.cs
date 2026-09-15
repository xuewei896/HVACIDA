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

            Console.WriteLine("==================================================");
            Console.WriteLine(_failures == 0
                ? "全部断言通过:数值与《大系统负荷计算公式-示例.xls》逐格一致;Ribbon 目录/仓库/知识库/小系统六类/空间聚合/气象联动/省市气象库/排烟计算自检通过。"
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

            // ⚠ 口径存疑(必须显式记录、不静默):
            // 文档公式写 V27=R27×(C52−C50)×1.15/3600(用"实际通风量"R27),示例合计 157.6;
            // 但示例中 2/5/10/16/19 号房间的值只有用"消除余热通风量 O27"才算得出来
            // (例:2 号房间 448×15.7×1.15/3600 = 2.25 ≈ 表列 2.2;用 R27=863 得 4.33)。
            Check("按文档公式 R27 复算 V37 ≈ 162.2 kW(示例为 157.6,差 2.9%,已记录待确认)",
                allAirResult.TotalUnitCoolingKw, 162.2, 0.6);
            Console.WriteLine("     ⚠ V37 口径存疑:公式文字用 R27(得 162.2),而示例 2/5/10/16/19 号房间" +
                              "只有用 O27 才算得出表列值(合计 157.6);请确认以哪个为准。");

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
            exhaust.SelectionFactor = HvacConstants.ExhaustSelectionFactorInSample;   // 1.3(示例口径)
            var exhaustSample = calc.Calculate(exhaust);
            Check("示例系数 1.3 时选型风量 = 7002 m³/h(与文档示例一致)", exhaustSample.Equipments[0].FlowM3H, 7002, 1.0);
            Console.WriteLine("     ⚠ 排风选型系数口径存疑:文档文字 1.1、示例 1.3(已记录待确认)。");

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

                var roundTrip = repo.LoadSmallSystem();
                roundTrip.SystemType = SmallSystemType.SmokeExhaust;
                roundTrip.Rooms.Add(SmallRoomInput.Create("防烟分区1", 300, 0));
                repo.SaveSmallSystem(roundTrip);
                var back = repo.LoadSmallSystem();
                CheckInt("房间列表往返 房间数", back.Rooms.Count, 1);
                Check("房间列表往返 面积", back.Rooms[0].AreaM2, 300, 1e-9);
                CheckText("系统类型往返", back.SystemType.ToString(), SmallSystemType.SmokeExhaust.ToString());
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }

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
