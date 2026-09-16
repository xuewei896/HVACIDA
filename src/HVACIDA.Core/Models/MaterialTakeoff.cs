using System;
using System.Collections.Generic;

namespace HVACIDA.Core.Models
{
    /// <summary>材料表统计的构件类别(需求 2.5;与 Revit 类别一一对应,便于回模型核对)。</summary>
    public enum MaterialCategory
    {
        Duct = 0,
        DuctFitting = 1,
        DuctAccessory = 2,
        DuctTerminal = 3,
        Pipe = 4,
        PipeFitting = 5,
        PipeAccessory = 6,
        MechanicalEquipment = 7,
        PlumbingFixture = 8,
        Insulation = 9,
        Other = 99
    }

    /// <summary>
    /// 一条**从模型读到的**构件(材料表统计的原始行)。
    /// <para>
    /// 计量口径:数量按"该类别在模型里的主计量属性"取 —— 风管/水管取**长度 m**、保温取**面积 m²**、
    /// 设备/管件/附件/末端取**件数**;单位不同者**不合并求和**(见 <see cref="Services.MaterialTakeoffService"/>),
    /// 不做"把米和个加在一起"这种没有意义的合计。
    /// </para>
    /// </summary>
    public class MaterialItem
    {
        /// <summary>类别。</summary>
        public MaterialCategory Category { get; set; } = MaterialCategory.Other;

        /// <summary>类别中文名(界面与材料表用)。</summary>
        public string CategoryName { get; set; } = "";

        /// <summary>族名称(模型来源)。</summary>
        public string FamilyName { get; set; } = "";

        /// <summary>类型名称(模型来源;材料表按"族 + 类型"归并)。</summary>
        public string TypeName { get; set; } = "";

        /// <summary>计量单位(m / m² / m³ / 个)。</summary>
        public string Unit { get; set; } = "个";

        /// <summary>数量(长度 m / 面积 m² / 体积 m³ / 个数)。</summary>
        public double Quantity { get; set; } = 1.0;

        /// <summary>件数(该类型下有几个构件)。</summary>
        public double Count { get; set; } = 1.0;

        /// <summary>Revit 元素 Id(0 = 非模型行)。</summary>
        public int ElementId { get; set; }

        /// <summary>所属系统名(模型有系统时填,便于按系统核对)。</summary>
        public string SystemName { get; set; } = "";

        /// <summary>读数说明(取的是哪个属性/为什么按此计量)。</summary>
        public string Note { get; set; } = "";
    }

    /// <summary>材料表的一行(**按 类别 + 族 + 类型 + 单位 归并**后的合计)。</summary>
    public class MaterialTakeoffRow
    {
        public MaterialCategory Category { get; set; } = MaterialCategory.Other;
        public string CategoryName { get; set; } = "";
        public string FamilyName { get; set; } = "";
        public string TypeName { get; set; } = "";
        public string Unit { get; set; } = "个";

        /// <summary>合计数量(单位见 <see cref="Unit"/>)。</summary>
        public double TotalQuantity { get; set; }

        /// <summary>合计件数。</summary>
        public double TotalCount { get; set; }

        /// <summary>该类型的备注(口径/待补)。</summary>
        public string Note { get; set; } = "";
    }

    /// <summary>类别小计。</summary>
    public class MaterialCategoryTotal
    {
        public MaterialCategory Category { get; set; } = MaterialCategory.Other;
        public string CategoryName { get; set; } = "";
        public string Unit { get; set; } = "";

        /// <summary>该类别合计数量(同类同单位才求和)。</summary>
        public double TotalQuantity { get; set; }

        /// <summary>该类别件数合计。</summary>
        public double TotalCount { get; set; }

        /// <summary>该类别下的类型数。</summary>
        public int TypeCount { get; set; }
    }

    /// <summary>
    /// **材料表统计结果**(需求 2.5):逐类型明细 + 类别小计 + 口径说明。
    /// 纯 Core(不碰 Revit),可被自检直接断言;界面表格、计算书与 Excel 都由它渲染。
    /// </summary>
    public class MaterialTakeoffResult
    {
        /// <summary>逐类型明细(按类别、再按数量从大到小)。</summary>
        public List<MaterialTakeoffRow> Rows { get; set; } = new List<MaterialTakeoffRow>();

        /// <summary>类别小计。</summary>
        public List<MaterialCategoryTotal> CategoryTotals { get; set; } = new List<MaterialCategoryTotal>();

        /// <summary>原始构件条数(从模型读到的元素数)。</summary>
        public int ItemCount { get; set; }

        /// <summary>构件总件数(含按长度计量者的件数)。</summary>
        public double TotalCount { get; set; }

        /// <summary>口径说明(界面与计算书都显示)。</summary>
        public string Note { get; set; } = "";

        /// <summary>待补 / 局限。</summary>
        public string PendingNote { get; set; } = "";

        /// <summary>数据来源说明(读了哪些类别、多少构件)。</summary>
        public string SourceNote { get; set; } = "";

        /// <summary>是否有可展示的行。</summary>
        public bool HasRows => Rows.Count > 0;
    }
}
