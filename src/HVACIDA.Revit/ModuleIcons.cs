using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HVACIDA.Revit
{
    /// <summary>
    /// Ribbon 按钮图标:读取**内嵌**在 HVACIDA.Revit.dll 里的 PNG(16×16 / 32×32),
    /// 由 tools/HVACIDA.IconGen 用矢量几何生成(见 docs/UI设计规范.md §4.0 图标规范)。
    ///
    /// 为什么内嵌而不是散落在输出目录:
    ///   .addin 只登记 DLL 一个文件,内嵌资源不会因为拷贝遗漏而"按钮没图标"。
    /// </summary>
    public static class ModuleIcons
    {
        /// <summary>Ribbon 大按钮图标边长(Revit 推荐 32×32,高 DPI 下更清晰)。</summary>
        public const int LargeSize = 32;

        /// <summary>Ribbon 小按钮 / 折叠面板下拉图标边长。</summary>
        public const int SmallSize = 16;

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, ImageSource> Cache = new Dictionary<string, ImageSource>();
        private static readonly Lazy<string[]> ResourceNames = new Lazy<string[]>(
            () => typeof(ModuleIcons).Assembly.GetManifestResourceNames());

        /// <summary>取图标;不存在返回 null。返回的 ImageSource 已 Freeze,可跨线程使用。</summary>
        public static ImageSource Get(string moduleKey, int size)
        {
            if (string.IsNullOrEmpty(moduleKey)) return null;

            string cacheKey = moduleKey + "_" + size;
            lock (Gate)
            {
                ImageSource cached;
                if (Cache.TryGetValue(cacheKey, out cached)) return cached;

                ImageSource image = Load(moduleKey, size);
                Cache[cacheKey] = image;
                return image;
            }
        }

        /// <summary>该模块的 16/32 图标是否齐全(启动自检用)。</summary>
        public static bool HasBoth(string moduleKey)
        {
            return Get(moduleKey, SmallSize) != null && Get(moduleKey, LargeSize) != null;
        }

        /// <summary>缺失图标的模块键(供启动时一次性报错,避免一张张找)。</summary>
        public static IList<string> FindMissing(IEnumerable<string> moduleKeys)
        {
            return moduleKeys.Where(k => !HasBoth(k)).ToList();
        }

        private static ImageSource Load(string moduleKey, int size)
        {
            string suffix = ".Icons." + moduleKey + "_" + size + ".png";
            string name = ResourceNames.Value.FirstOrDefault(n =>
                n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            if (name == null) return null;

            using (Stream stream = typeof(ModuleIcons).Assembly.GetManifestResourceStream(name))
            {
                if (stream == null) return null;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;   // 读完即缓存,不依赖流存活
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
        }
    }
}
