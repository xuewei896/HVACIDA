using System;
using System.Globalization;

namespace HVACIDA.Core.Models
{
    /// <summary>
    /// Revit 空间(<c>Autodesk.Revit.DB.Mechanical.Space</c>)在 Core 层的只读快照(需求 2.2.1 / 2.2.3.1)。
    /// <para>
    /// Core 不引用 Revit API,因此 Revit 侧读完模型后必须转成本类型再交给聚合/UI 层;
    /// <strong>单位已在本类型统一为 m / m² / m³</strong>(英尺换算在 <c>HVACIDA.Revit</c> 侧完成,不进 Core)。
    /// </para>
    /// </summary>
    public class SpaceSnapshot
    {
        /// <summary>Revit 元素 Id(链接模型的空间为该链接文档内的 Id,仅供追溯显示)。</summary>
        public int ElementId { get; set; }

        /// <summary>空间编号。</summary>
        public string Number { get; set; } = "";

        /// <summary>空间名称。</summary>
        public string Name { get; set; } = "";

        /// <summary>所在标高名称。</summary>
        public string LevelName { get; set; } = "";

        /// <summary>面积 m²(未放置的空间为 0)。</summary>
        public double AreaM2 { get; set; }

        /// <summary>体积 m³(未放置或未计算时为 0)。</summary>
        public double VolumeM3 { get; set; }

        /// <summary>层高 m(取 体积/面积 的有效平均高度;取不到时回退空间上限高度)。</summary>
        public double HeightM { get; set; }

        /// <summary>水平包围盒(模型坐标,m):用于按需求 C14 估算公共区长度。未取到时四值均为 0。</summary>
        public double MinXM { get; set; }
        public double MinYM { get; set; }
        public double MaxXM { get; set; }
        public double MaxYM { get; set; }

        /// <summary>是否已放置(有面积)。未放置的空间不参与任何合计,只计数提示。</summary>
        public bool IsPlaced => AreaM2 > 0;

        /// <summary>是否取到了水平包围盒。</summary>
        public bool HasExtent => MaxXM > MinXM || MaxYM > MinYM;

        /// <summary>是否来自链接模型(仅用于界面标注)。</summary>
        public bool FromLink { get; set; }

        /// <summary>用于界面明细的"名称/编号/标高"检索文本(小写,已合并)。</summary>
        public string MatchText
        {
            get
            {
                return ((Name ?? "") + " " + (Number ?? "") + " " + (LevelName ?? "")).ToLowerInvariant();
            }
        }

        public override string ToString()
        {
            return (string.IsNullOrEmpty(Number) ? "" : Number + " ") + Name +
                   "(" + LevelName + ")" + " " + AreaM2.ToString("0.##", CultureInfo.InvariantCulture) + " m²";
        }
    }
}
