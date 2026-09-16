using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using HVACIDA.Core.Models;

namespace HVACIDA.Revit.Services
{
    /// <summary>
    /// 读**图纸清单**(需求 2.6):图纸编号 / 名称 / 图框族与类型 / 图幅 / 图框上放置的视图。
    /// 只读操作(不开事务);读不到的字段留空或给「—」,不编尺寸。
    /// </summary>
    internal static class RevitSheetReader
    {
        /// <summary>读全模型的图纸清单。</summary>
        public static IList<SheetItem> Read(Document doc, out string note)
        {
            var items = new List<SheetItem>();
            note = "";
            if (doc == null)
            {
                note = "没有活动文档,无法读取图纸清单。";
                return items;
            }

            int noTitleBlock = 0;
            int noOutline = 0;
            var collector = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).WhereElementIsNotElementType();
            foreach (var element in collector)
            {
                var sheet = element as ViewSheet;
                if (sheet == null) continue;

                var item = new SheetItem
                {
                    ElementId = sheet.Id.IntegerValue,
                    SheetNumber = sheet.SheetNumber ?? "",
                    SheetName = sheet.Name ?? ""
                };

                // 图框族 / 类型(2020 无 ViewSheet.GetTitleBlock,按类别在图纸范围内收集图框实例)
                try
                {
                    var titleBlock = FirstTitleBlock(doc, sheet);
                    if (titleBlock != null)
                    {
                        var type = doc.GetElement(titleBlock.GetTypeId()) as ElementType;
                        if (type != null)
                        {
                            item.TitleBlockFamily = type.FamilyName ?? "";
                            item.TitleBlockType = type.Name ?? "";
                        }
                    }
                    else
                    {
                        noTitleBlock++;
                    }
                }
                catch
                {
                    noTitleBlock++;
                }

                // 图幅(取图纸外框范围,英尺 → mm)
                try
                {
                    var outline = sheet.Outline;
                    if (outline != null)
                    {
                        double width = UnitUtils.ConvertFromInternalUnits(outline.Max.U - outline.Min.U, DisplayUnitType.DUT_MILLIMETERS);
                        double height = UnitUtils.ConvertFromInternalUnits(outline.Max.V - outline.Min.V, DisplayUnitType.DUT_MILLIMETERS);
                        if (width > 0 && height > 0)
                        {
                            item.WidthMm = width;
                            item.HeightMm = height;
                        }
                        else
                        {
                            noOutline++;
                        }
                    }
                    else
                    {
                        noOutline++;
                    }
                }
                catch
                {
                    noOutline++;
                }

                // 图框上放置的视图
                try
                {
                    foreach (var viewId in sheet.GetAllPlacedViews())
                    {
                        var view = doc.GetElement(viewId) as View;
                        if (view == null) continue;
                        var viewItem = new SheetViewItem
                        {
                            ViewName = view.Name ?? "",
                            ViewType = SafeViewType(view)
                        };
                        try { viewItem.Scale = view.Scale; } catch { }
                        item.Views.Add(viewItem);
                    }
                }
                catch
                {
                    item.Note = "读不到图框上的视图清单";
                }

                items.Add(item);
            }

            var parts = new List<string>();
            parts.Add("共读到 " + items.Count + " 张图纸");
            if (noTitleBlock > 0) parts.Add("有 " + noTitleBlock + " 张未读图框(未放置图框?)");
            if (noOutline > 0) parts.Add("有 " + noOutline + " 张读不到图幅外框");
            note = string.Join(",", parts.ToArray()) + "。";
            return items;
        }

        /// <summary>图纸上的第一个图框实例(没有返回 null)。</summary>
        private static FamilyInstance FirstTitleBlock(Document doc, ViewSheet sheet)
        {
            var collector = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .OfClass(typeof(FamilyInstance));
            foreach (var element in collector) return element as FamilyInstance;
            return null;
        }

        private static string SafeViewType(View view)
        {
            try
            {
                return view.ViewType.ToString();
            }
            catch
            {
                return "";
            }
        }
    }
}
