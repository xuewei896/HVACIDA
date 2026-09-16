using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using HVACIDA.Core.Services;

namespace HVACIDA.Revit.Services
{
    /// <summary>
    /// **有限范围的自动标注**(需求 2.6):在平面视图里给**空间**添加「名称 + 编号」标注。
    /// <para>
    /// 口径(与 Core 的 <see cref="AutoTagResult"/> 一致,界面上如实显示):
    /// ① 只处理**平面视图**(楼层平面 / 天花平面)且非样板;
    /// ② 视图里**已有空间标注就整个视图跳过** —— 不重复堆标注;
    /// ③ 视图里没有空间也跳过;
    /// ④ 单个空间标注创建失败**逐条计数**,不静默;
    /// ⑤ **范围外**:风管/水管尺寸与设备编号的自动标注不做(需要规则库与位置算法)。
    /// </para>
    /// <para>标注属于文档修改,需事务上下文(本类不开事务,由命令层包起来)。</para>
    /// </summary>
    internal static class RevitAutoTagger
    {
        /// <summary>批量标注各平面视图里的空间(返回逐视图结果)。</summary>
        public static AutoTagResult TagSpaces(Document doc, AutoTagResult result)
        {
            if (result == null) result = new AutoTagResult();
            if (doc == null) return result;

            var views = new List<View>();
            var collector = new FilteredElementCollector(doc).OfClass(typeof(View)).WhereElementIsNotElementType();
            foreach (var element in collector)
            {
                var view = element as View;
                if (view == null || view.IsTemplate) continue;
                if (view.ViewType != ViewType.FloorPlan && view.ViewType != ViewType.CeilingPlan) continue;
                views.Add(view);
            }

            foreach (var view in views)
            {
                var row = new AutoTagViewResult { ViewName = view.Name ?? "" };
                try
                {
                    var spaces = new List<Space>();
                    var spaceCollector = new FilteredElementCollector(doc, view.Id)
                        .OfClass(typeof(SpatialElement));
                    foreach (var element in spaceCollector)
                    {
                        var space = element as Space;
                        if (space != null && space.Location != null) spaces.Add(space);
                    }

                    if (spaces.Count == 0)
                    {
                        row.Skipped = 0;
                        row.Message = "该视图没有空间(或空间未放置),跳过";
                        result.Views.Add(row);
                        continue;
                    }

                    if (HasSpaceTags(doc, view))
                    {
                        row.Skipped = spaces.Count;
                        row.Message = "已有空间标注,整个视图跳过(不重复堆标注)";
                        result.Views.Add(row);
                        continue;
                    }

                    foreach (var space in spaces)
                    {
                        try
                        {
                            var point = (space.Location as LocationPoint).Point;
                            IndependentTag.Create(doc, view.Id, new Reference(space), false,
                                TagMode.TM_ADDBY_CATEGORY, TagOrientation.Horizontal, point);
                            row.Added++;
                        }
                        catch
                        {
                            row.Failed++;
                        }
                    }

                    row.Message = "新增 " + row.Added + " 个空间标注" +
                                  (row.Failed > 0 ? ",失败 " + row.Failed + " 个(通常是没有合适位置)" : "");
                }
                catch (Exception ex)
                {
                    row.Message = "该视图标注失败: " + ex.Message;
                    row.Failed++;
                }

                result.Views.Add(row);
            }

            foreach (var row in result.Views)
            {
                result.AddedTotal += row.Added;
                result.SkippedTotal += row.Skipped;
                result.FailedTotal += row.Failed;
            }
            return result;
        }

        /// <summary>视图里是否已经有"标在空间上的"标注。</summary>
        private static bool HasSpaceTags(Document doc, View view)
        {
            try
            {
                var collector = new FilteredElementCollector(doc, view.Id).OfClass(typeof(IndependentTag));
                foreach (var element in collector)
                {
                    var tag = element as IndependentTag;
                    if (tag == null) continue;
                    var tagged = doc.GetElement(tag.TaggedLocalElementId);
                    if (tagged is Space) return true;
                }
            }
            catch
            {
                // 读不到标注情况按"没有标注"处理:宁可多标一次也不漏标(失败了会逐条计数)
            }
            return false;
        }
    }
}
