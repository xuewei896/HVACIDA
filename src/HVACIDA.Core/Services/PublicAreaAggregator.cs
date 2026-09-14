using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>公共区几何的目标侧(需求 2.2.3.1:D55 站厅 / D56 站台)。</summary>
    public enum PublicAreaTarget
    {
        /// <summary>站厅层公共区(D55 / C13 / C14)。</summary>
        Hall,

        /// <summary>站台层公共区(D56)。</summary>
        Platform
    }

    /// <summary>
    /// 空间集合 → 公共区几何的聚合与分类(需求 2.2.1 空间划分 / 2.2.3.1 由模型空间获取)。
    /// <para>
    /// 纯 Core 逻辑(不碰 Revit API),因此可被 <c>HVACIDA.Smoke</c> 直接断言;
    /// Revit 侧只负责把 <c>Space</c> 读成 <see cref="SpaceSnapshot"/> 并换算单位。
    /// </para>
    /// </summary>
    public static class PublicAreaAggregator
    {
        /// <summary>站厅关键词(小写比较;英文模型用 concourse)。</summary>
        public static readonly string[] HallKeywords = { "站厅", "concourse" };

        /// <summary>站台关键词(小写比较;英文模型用 platform)。</summary>
        public static readonly string[] PlatformKeywords = { "站台", "platform" };

        /// <summary>公共区关键词:D55/D56 的口径是"公共区面积",命名里带该词的优先。</summary>
        public const string PublicAreaKeyword = "公共区";

        /// <summary>
        /// 按名称/编号/标高关键词把空间分为站厅、站台、未分类三组。
        /// <list type="number">
        ///   <item>未放置空间(面积 = 0)一律排除,只计数;</item>
        ///   <item>同时含两侧关键词的歧义空间进"未分类",不猜;</item>
        ///   <item>某一侧存在<strong>名称</strong>含"公共区"的空间时,该侧只取这些空间(口径对齐 D55/D56),
        ///         被筛掉的同侧空间进 <c>HallExcluded/PlatformExcluded</c> 并在明细里列出 ——
        ///         <strong>不能默默丢掉</strong>,否则"合计是不是漏了付费区"无从核对。</item>
        /// </list>
        /// 未匹配到任何关键词的空间进入 Unclassified。<strong>任何一组都不会被自动并入合计。</strong>
        /// </summary>
        public static PublicAreaClassification Classify(IEnumerable<SpaceSnapshot> spaces)
        {
            var result = new PublicAreaClassification();
            if (spaces == null) return result;

            foreach (var space in spaces)
            {
                if (space == null) continue;
                if (!space.IsPlaced)
                {
                    result.SkippedUnplaced++;
                    continue;
                }

                string text = space.MatchText;
                bool hall = ContainsAny(text, HallKeywords);
                bool platform = ContainsAny(text, PlatformKeywords);

                if (hall && platform) result.Unclassified.Add(space);      // 歧义:不猜
                else if (hall) result.Hall.Add(space);
                else if (platform) result.Platform.Add(space);
                else result.Unclassified.Add(space);
            }

            result.HallPublicAreaOnly = NarrowToPublicArea(result.Hall, result.HallExcluded);
            result.PlatformPublicAreaOnly = NarrowToPublicArea(result.Platform, result.PlatformExcluded);

            SortByLevelThenNumber(result.Hall);
            SortByLevelThenNumber(result.Platform);
            SortByLevelThenNumber(result.Unclassified);
            SortByLevelThenNumber(result.HallExcluded);
            SortByLevelThenNumber(result.PlatformExcluded);

            result.Summary = DescribeClassified(result);
            return result;
        }

        /// <summary>
        /// 把一组空间聚合成一个公共区几何值(面积合计 / 面积加权平均层高 / 包围盒长边)。
        /// 用于「拾取站厅空间」「拾取站台空间」这类目标已知的场景。
        /// </summary>
        public static SpaceAggregate Aggregate(IEnumerable<SpaceSnapshot> spaces, PublicAreaTarget target)
        {
            var result = new SpaceAggregate { Target = target };
            if (spaces == null) return result;

            double areaSum = 0;
            double weightedHeight = 0;
            double heightWeight = 0;
            double heightMin = double.MaxValue;
            double heightMax = double.MinValue;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            bool hasExtent = false;

            foreach (var space in spaces)
            {
                if (space == null) continue;
                if (!space.IsPlaced)
                {
                    result.SkippedCount++;
                    continue;
                }

                result.Spaces.Add(space);
                areaSum += space.AreaM2;

                if (space.HeightM > 0)
                {
                    weightedHeight += space.HeightM * space.AreaM2;
                    heightWeight += space.AreaM2;
                    if (space.HeightM < heightMin) heightMin = space.HeightM;
                    if (space.HeightM > heightMax) heightMax = space.HeightM;
                }
                else
                {
                    result.HeightMissingCount++;
                }

                if (space.HasExtent)
                {
                    hasExtent = true;
                    if (space.MinXM < minX) minX = space.MinXM;
                    if (space.MinYM < minY) minY = space.MinYM;
                    if (space.MaxXM > maxX) maxX = space.MaxXM;
                    if (space.MaxYM > maxY) maxY = space.MaxYM;
                }
            }

            result.AreaM2 = areaSum;
            result.HeightM = heightWeight > 0 ? weightedHeight / heightWeight : 0;
            result.HeightMinM = heightMin == double.MaxValue ? 0 : heightMin;
            result.HeightMaxM = heightMax == double.MinValue ? 0 : heightMax;
            result.LengthM = hasExtent ? Math.Max(maxX - minX, maxY - minY) : 0;
            result.SpaceCount = result.Spaces.Count;
            result.Summary = DescribeAggregate(result);
            return result;
        }

        /// <summary>分类结果的一行摘要(界面状态栏用)。</summary>
        public static string DescribeClassified(PublicAreaClassification c)
        {
            if (c == null) return "";
            var sb = new StringBuilder();
            sb.Append("模型空间:站厅 ").Append(c.Hall.Count).Append(" 个");
            if (c.HallPublicAreaOnly) sb.Append("(已按名称「公共区」筛掉同层其他 ").Append(c.HallExcluded.Count).Append(" 个)");
            sb.Append("、站台 ").Append(c.Platform.Count).Append(" 个");
            if (c.PlatformPublicAreaOnly) sb.Append("(已筛掉 ").Append(c.PlatformExcluded.Count).Append(" 个)");
            sb.Append("、未识别 ").Append(c.Unclassified.Count).Append(" 个");
            if (c.SkippedUnplaced > 0) sb.Append(";另有未放置空间 ").Append(c.SkippedUnplaced).Append(" 个已跳过");
            sb.Append("。");
            return sb.ToString();
        }

        /// <summary>聚合结果的一行摘要(界面状态栏用)。</summary>
        public static string DescribeAggregate(SpaceAggregate a)
        {
            if (a == null) return "";
            string label = a.Target == PublicAreaTarget.Hall ? "站厅" : "站台";
            var sb = new StringBuilder();
            sb.Append(label).Append(" ").Append(a.SpaceCount).Append(" 个空间:合计 ")
              .Append(Fmt(a.AreaM2)).Append(" m²");
            if (a.HeightM > 0)
            {
                sb.Append(",层高 ").Append(Fmt(a.HeightM)).Append(" m");
                if (Math.Abs(a.HeightMaxM - a.HeightMinM) > 0.05)
                {
                    sb.Append("(").Append(Fmt(a.HeightMinM)).Append("~").Append(Fmt(a.HeightMaxM)).Append(")");
                }
            }
            else
            {
                sb.Append(",未取到层高");
            }

            if (a.LengthM > 0) sb.Append(",长度(包围盒长边估算)").Append(Fmt(a.LengthM)).Append(" m");
            if (a.HeightMissingCount > 0) sb.Append(";有 ").Append(a.HeightMissingCount).Append(" 个空间缺高度,已按其余空间加权");
            if (a.SkippedCount > 0) sb.Append(";跳过未放置空间 ").Append(a.SkippedCount).Append(" 个");
            sb.Append("。");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ 内部

        /// <summary>
        /// 名称含"公共区"时只保留这些空间,其余移入 excluded;返回是否发生了筛选。
        /// 只看<strong>名称</strong>:标高/编号里带"公共区"不足以说明该空间本身是公共区。
        /// </summary>
        private static bool NarrowToPublicArea(List<SpaceSnapshot> list, List<SpaceSnapshot> excluded)
        {
            if (list.Count == 0) return false;

            var keep = new List<SpaceSnapshot>();
            var drop = new List<SpaceSnapshot>();
            foreach (var s in list)
            {
                bool isPublicArea = (s.Name ?? "").IndexOf(PublicAreaKeyword, StringComparison.Ordinal) >= 0;
                if (isPublicArea) keep.Add(s); else drop.Add(s);
            }

            // 一个"公共区"都没有时不做筛选(把同层全部空间交给用户核对,并在摘要里说明)
            if (keep.Count == 0) return false;

            list.Clear();
            list.AddRange(keep);
            excluded.AddRange(drop);
            return drop.Count > 0;
        }

        private static bool ContainsAny(string text, string[] keywords)
        {
            foreach (var k in keywords)
            {
                // net48 没有 string.Contains(string, StringComparison),用 IndexOf
                if (text.IndexOf(k, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        private static void SortByLevelThenNumber(List<SpaceSnapshot> list)
        {
            list.Sort((a, b) =>
            {
                int c = string.Compare(a.LevelName, b.LevelName, StringComparison.Ordinal);
                return c != 0 ? c : string.Compare(a.Number, b.Number, StringComparison.Ordinal);
            });
        }

        private static string Fmt(double v)
        {
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>按目标侧聚合出的公共区几何(对应 D55/D56 面积、C13 层高、C14 长度)。</summary>
    public sealed class SpaceAggregate
    {
        public PublicAreaTarget Target { get; set; }

        /// <summary>面积合计 m²。</summary>
        public double AreaM2 { get; set; }

        /// <summary>面积加权平均层高 m(0 = 未取到)。</summary>
        public double HeightM { get; set; }

        public double HeightMinM { get; set; }
        public double HeightMaxM { get; set; }

        /// <summary>公共区长度 m(所选空间水平包围盒长边;0 = 未取到)。</summary>
        public double LengthM { get; set; }

        public int SpaceCount { get; set; }
        public int SkippedCount { get; set; }
        public int HeightMissingCount { get; set; }

        public List<SpaceSnapshot> Spaces { get; } = new List<SpaceSnapshot>();

        /// <summary>界面状态栏文案。</summary>
        public string Summary { get; set; } = "";
    }

    /// <summary>全模型空间的分类结果(自动识别用)。</summary>
    public sealed class PublicAreaClassification
    {
        /// <summary>识别为站厅且参与合计的空间。</summary>
        public List<SpaceSnapshot> Hall { get; } = new List<SpaceSnapshot>();

        /// <summary>识别为站台且参与合计的空间。</summary>
        public List<SpaceSnapshot> Platform { get; } = new List<SpaceSnapshot>();

        /// <summary>两侧关键词都没匹配上的空间(不参与合计,界面列出待人工指定)。</summary>
        public List<SpaceSnapshot> Unclassified { get; } = new List<SpaceSnapshot>();

        /// <summary>匹配了站厅关键词、但被"公共区"口径筛掉的空间(如付费区、风道)。</summary>
        public List<SpaceSnapshot> HallExcluded { get; } = new List<SpaceSnapshot>();

        /// <summary>匹配了站台关键词、但被"公共区"口径筛掉的空间。</summary>
        public List<SpaceSnapshot> PlatformExcluded { get; } = new List<SpaceSnapshot>();

        /// <summary>站厅是否按"公共区"关键词做了筛选。</summary>
        public bool HallPublicAreaOnly { get; set; }

        /// <summary>站台是否按"公共区"关键词做了筛选。</summary>
        public bool PlatformPublicAreaOnly { get; set; }

        /// <summary>被跳过的未放置空间数。</summary>
        public int SkippedUnplaced { get; set; }

        /// <summary>界面状态栏文案。</summary>
        public string Summary { get; set; } = "";
    }
}
