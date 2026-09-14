using System.Globalization;
using HVACIDA.Core.Models;

namespace HVACIDA.UI.ViewModels
{
    /// <summary>
    /// 「模型取值明细」列表的一行。纯展示对象:列表整体替换,不需要 INotifyPropertyChanged。
    /// 数值统一在 Core 层换算为 m/m²,这里只做格式化。
    /// </summary>
    public class SpaceRow
    {
        /// <summary>分类(站厅/站台 + 自动识别或手动拾取,未分类的不参与合计)。</summary>
        public string Category { get; set; } = "";

        public string Number { get; set; } = "";

        public string Name { get; set; } = "";

        public string LevelName { get; set; } = "";

        public string AreaText { get; set; } = "";

        public string HeightText { get; set; } = "";

        /// <summary>来源标注(当前项目 / 链接模型)。</summary>
        public string SourceText { get; set; } = "";

        public static SpaceRow From(SpaceSnapshot space, string category)
        {
            if (space == null) return new SpaceRow { Category = category };
            return new SpaceRow
            {
                Category = category,
                Number = space.Number,
                Name = space.Name,
                LevelName = space.LevelName,
                AreaText = Num(space.AreaM2),
                HeightText = space.HeightM > 0 ? Num(space.HeightM) : "—",
                SourceText = space.FromLink ? "链接模型" : "当前项目"
            };
        }

        private static string Num(double v)
        {
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
