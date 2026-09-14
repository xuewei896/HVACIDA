using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;

namespace HVACIDA.IconGen
{
    /// <summary>
    /// HVACIDA Ribbon 图标生成器(System.Drawing 矢量绘制 → 内嵌 PNG)。
    ///
    /// 为什么用生成器而不是手绘 PNG:
    ///   22 个图标风格统一、可复现、可改;改一行代码就能重出全套,不需要设计源文件。
    ///
    /// 设计规范(与 docs/UI设计规范.md §4.0 配套):
    ///   设计坐标系 32×32,四周留 2px;以 8 倍超采样绘制后缩放,
    ///   输出 32×32(大按钮)与 16×16(小按钮)两套 PNG;
    ///   配色:深蓝主色 + 浅蓝底 + 少量强调色(负荷=琥珀、排烟=红、已实现=绿)。
    ///
    /// 用法:
    ///   HVACIDA.IconGen.exe --out src\HVACIDA.Revit\Resources\Icons
    ///   HVACIDA.IconGen.exe --out <dir> --preview          # 同时打印 ASCII 预览,便于无图形界面时自检
    ///   HVACIDA.IconGen.exe --out <dir> --only eng-info
    /// </summary>
    internal static class Program
    {
        private const int Design = 32;      // 设计坐标系边长
        private const int SuperSample = 8;  // 超采样倍数
        private static readonly int[] OutputSizes = { 32, 16 };

        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            string outDir = Path.Combine("src", "HVACIDA.Revit", "Resources", "Icons");
            bool preview = false;
            string only = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--out":
                        if (i + 1 < args.Length) outDir = args[++i];
                        break;
                    case "--preview":
                        preview = true;
                        break;
                    case "--only":
                        if (i + 1 < args.Length) only = args[++i];
                        break;
                }
            }

            Directory.CreateDirectory(outDir);

            var keys = Icons.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            if (only != null)
            {
                keys = keys.Where(k => k == only).ToList();
                if (keys.Count == 0) { Console.WriteLine("没有这个图标键: " + only); return 2; }
            }

            int written = 0;
            foreach (string key in keys)
            {
                using (var master = RenderMaster(Icons[key]))
                {
                    foreach (int size in OutputSizes)
                    {
                        string file = Path.Combine(outDir, key + "_" + size + ".png");
                        using (var scaled = Downscale(master, size))
                        {
                            scaled.Save(file, ImageFormat.Png);
                            written++;
                        }
                    }

                    if (preview)
                    {
                        Console.WriteLine();
                        Console.WriteLine("== " + key + " ==");
                        PrintAscii(master);
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine("已生成 " + written + " 个 PNG(" + keys.Count + " 个图标 × " + OutputSizes.Length + " 尺寸)→ " + outDir);
            return 0;
        }

        // =====================================================================
        // 渲染
        // =====================================================================
        private static Bitmap RenderMaster(Action<Ctx> draw)
        {
            var bmp = new Bitmap(Design * SuperSample, Design * SuperSample, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(Color.Transparent);
                g.ScaleTransform(SuperSample, SuperSample);
                var ctx = new Ctx(g);
                draw(ctx);
            }
            return bmp;
        }

        private static Bitmap Downscale(Bitmap master, int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.Clear(Color.Transparent);
                g.DrawImage(master, new Rectangle(0, 0, size, size), new Rectangle(0, 0, master.Width, master.Height), GraphicsUnit.Pixel);
            }
            return bmp;
        }

        /// <summary>
        /// 把图标打印成 ASCII(1 像素 1 字符,按"合成到白底后的亮度"分级):
        /// '@' 描边/深色,'+' 中色,'.' 浅色填充,' ' 透明 —— 便于在没有图形界面的环境里自检形状。
        /// </summary>
        private static void PrintAscii(Bitmap master)
        {
            const string ramp = "@%#*+=-:. ";   // 深 → 浅
            using (var small = Downscale(master, 32))
            {
                var sb = new StringBuilder();
                sb.AppendLine("   " + string.Concat(Enumerable.Range(0, 32).Select(i => (i % 10).ToString())));
                for (int y = 0; y < 32; y++)
                {
                    sb.Append(y.ToString("00") + " ");
                    for (int x = 0; x < 32; x++)
                    {
                        Color c = small.GetPixel(x, y);
                        if (c.A < 20) { sb.Append(' '); continue; }
                        double a = c.A / 255.0;
                        // 合成到白底
                        double lum = (c.R * 0.299 + c.G * 0.587 + c.B * 0.114) * a + 255 * (1 - a);
                        // ramp 是"深 → 浅",所以亮度越高索引越大
                        int idx = (int)Math.Round(lum / 255.0 * (ramp.Length - 1));
                        sb.Append(ramp[Math.Max(0, Math.Min(ramp.Length - 1, idx))]);
                    }
                    sb.AppendLine();
                }
                Console.Write(sb.ToString());
            }
        }

        // =====================================================================
        // 绘图上下文(全部使用 32×32 设计坐标,不写文字,避免依赖字体)
        // =====================================================================
        private sealed class Ctx
        {
            internal static readonly Color Line = Color.FromArgb(0x23, 0x48, 0x6E);     // 主色:深蓝
            internal static readonly Color Accent = Color.FromArgb(0x2E, 0x8B, 0xD8);   // 强调:亮蓝
            internal static readonly Color Soft = Color.FromArgb(0xE8, 0xF1, 0xFA);     // 浅蓝底
            internal static readonly Color Warm = Color.FromArgb(0xC8, 0x80, 0x2B);     // 负荷/热
            internal static readonly Color Red = Color.FromArgb(0xC0, 0x39, 0x2B);      // 排烟/火
            internal static readonly Color Green = Color.FromArgb(0x3E, 0x8E, 0x41);    // 已实现/通过
            internal static readonly Color Gray = Color.FromArgb(0x8A, 0x8A, 0x8A);     // 次要

            private readonly Graphics _g;
            internal Ctx(Graphics g) { _g = g; }

            internal Pen P(Color c, float w) => new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            internal SolidBrush B(Color c) => new SolidBrush(c);

            internal void Line2(Color c, float w, float x1, float y1, float x2, float y2)
            {
                using (var p = P(c, w)) _g.DrawLine(p, x1, y1, x2, y2);
            }

            /// <summary>折线(粗线画风管/管道用,圆角接头)。</summary>
            internal void Polyline(Color c, float w, params PointF[] pts)
            {
                using (var p = P(c, w))
                {
                    p.LineJoin = LineJoin.Round;
                    _g.DrawLines(p, pts);
                }
            }

            internal void Rect(Color stroke, float w, float x, float y, float rw, float rh, Color? fill = null, float radius = 0)
            {
                using (var path = RoundRect(x, y, rw, rh, radius))
                {
                    if (fill.HasValue) using (var b = B(fill.Value)) _g.FillPath(b, path);
                    using (var p = P(stroke, w)) _g.DrawPath(p, path);
                }
            }

            internal void Fill(Color c, float x, float y, float rw, float rh, float radius = 0)
            {
                using (var path = RoundRect(x, y, rw, rh, radius))
                using (var b = B(c)) _g.FillPath(b, path);
            }

            internal void Ellipse(Color stroke, float w, float cx, float cy, float r, Color? fill = null)
            {
                var rect = new RectangleF(cx - r, cy - r, r * 2, r * 2);
                if (fill.HasValue) using (var b = B(fill.Value)) _g.FillEllipse(b, rect);
                using (var p = P(stroke, w)) _g.DrawEllipse(p, rect);
            }

            internal void Pie(Color stroke, float w, float cx, float cy, float r, float start, float sweep)
            {
                var rect = new RectangleF(cx - r, cy - r, r * 2, r * 2);
                using (var p = P(stroke, w)) _g.DrawArc(p, rect, start, sweep);
            }

            internal void Poly(Color stroke, float w, Color? fill, params PointF[] pts)
            {
                if (fill.HasValue) using (var b = B(fill.Value)) _g.FillPolygon(b, pts);
                using (var p = P(stroke, w)) _g.DrawPolygon(p, pts);
            }

            /// <summary>箭头(默认向右):(x,y) 为箭头尖端。</summary>
            internal void Arrow(Color c, float x, float y, float len, float dirDeg = 0, float head = 4.4f, float w = 1.6f)
            {
                double rad = dirDeg * Math.PI / 180.0;
                float bx = x - (float)(Math.Cos(rad) * len);
                float by = y - (float)(Math.Sin(rad) * len);
                Line2(c, w, bx, by, x, y);

                double left = rad + Math.PI * 0.78, right = rad - Math.PI * 0.78;
                Poly(c, w, c,
                    new PointF(x, y),
                    new PointF(x + (float)(Math.Cos(left) * head), y + (float)(Math.Sin(left) * head)),
                    new PointF(x + (float)(Math.Cos(right) * head), y + (float)(Math.Sin(right) * head)));
            }

            internal void Wave(Color c, float w, float x1, float x2, float y, float amp, float phase = 0)
            {
                var pts = new List<PointF>();
                int steps = 16;
                for (int i = 0; i <= steps; i++)
                {
                    float t = (float)i / steps;
                    float x = x1 + (x2 - x1) * t;
                    float yy = y + (float)(Math.Sin((t * 2 * Math.PI) + phase) * amp);
                    pts.Add(new PointF(x, yy));
                }
                using (var p = P(c, w)) _g.DrawLines(p, pts.ToArray());
            }

            /// <summary>风机:圆 + 三片叶片。</summary>
            internal void Fan(Color c, float w, float cx, float cy, float r)
            {
                Ellipse(c, w, cx, cy, r);
                for (int i = 0; i < 3; i++)
                {
                    double a = (-90 + i * 120) * Math.PI / 180.0;
                    float px = cx + (float)(Math.Cos(a) * r * 0.72);
                    float py = cy + (float)(Math.Sin(a) * r * 0.72);
                    Line2(c, w * 0.9f, cx, cy, px, py);
                }
                Ellipse(c, w * 0.8f, cx, cy, r * 0.18f, c);
            }

            /// <summary>柱状图:数据以 0..1 高度给出。</summary>
            internal void Bars(Color c, float w, float x, float y, float totalW, float totalH, params float[] heights)
            {
                Line2(c, w, x, y + totalH, x + totalW, y + totalH);
                float gap = 1.6f;
                float bw = (totalW - gap * (heights.Length - 1)) / heights.Length;
                for (int i = 0; i < heights.Length; i++)
                {
                    float h = Math.Max(1.6f, heights[i] * totalH);
                    float bx = x + i * (bw + gap);
                    Fill(i == heights.Length - 1 ? Accent : c, bx, y + totalH - h, bw, h, 0.6f);
                }
            }

            internal static GraphicsPath RoundRect(float x, float y, float w, float h, float r)
            {
                var path = new GraphicsPath();
                if (r <= 0.01f) { path.AddRectangle(new RectangleF(x, y, w, h)); return path; }
                float d = r * 2;
                path.AddArc(x, y, d, d, 180, 90);
                path.AddArc(x + w - d, y, d, d, 270, 90);
                path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
                path.AddArc(x, y + h - d, d, d, 90, 90);
                path.CloseFigure();
                return path;
            }
        }

        // =====================================================================
        // 22 个图标的几何定义(键与 ModuleCatalog 的 Key 一致)
        // =====================================================================
        private static readonly Dictionary<string, Action<Ctx>> Icons = new Dictionary<string, Action<Ctx>>
        {
            // ---------- 1. 项目信息 ----------
            ["eng-info"] = g =>
            {
                // 写字板:标题条 + 夹子 + 文本行
                g.Rect(Ctx.Line, 1.5f, 6, 4, 20, 25, Ctx.Soft, 2.5f);
                g.Fill(Ctx.Accent, 6, 4, 20, 5.5f, 2.5f);
                g.Fill(Color.White, 12.5f, 2.2f, 7, 3.6f, 1.4f);
                g.Rect(Ctx.Line, 1.1f, 12.5f, 2.2f, 7, 3.6f, null, 1.4f);
                g.Line2(Ctx.Line, 1.3f, 9.5f, 14, 22.5f, 14);
                g.Line2(Ctx.Line, 1.3f, 9.5f, 18.5f, 22.5f, 18.5f);
                g.Line2(Ctx.Line, 1.3f, 9.5f, 23, 18, 23);
            },
            ["weather"] = g =>
            {
                // 太阳(左上)+ 云(右下,只描边)+ 雨线:三者分开,16px 下也分得清
                g.Ellipse(Ctx.Warm, 1.6f, 9, 9, 3.8f, Color.FromArgb(0xFD, 0xF3, 0xE0));
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4.0;
                    g.Line2(Ctx.Warm, 1.4f,
                        9 + (float)(Math.Cos(a) * 5.3), 9 + (float)(Math.Sin(a) * 5.3),
                        9 + (float)(Math.Cos(a) * 7.0), 9 + (float)(Math.Sin(a) * 7.0));
                }
                // 云:两段圆弧 + 底边(不填充,避免糊成一团)
                g.Pie(Ctx.Line, 1.7f, 18.5f, 18.5f, 4.6f, 180, 180);
                g.Pie(Ctx.Line, 1.7f, 24.5f, 19.6f, 3.6f, 180, 180);
                g.Line2(Ctx.Line, 1.7f, 13.9f, 18.5f, 28.1f, 18.5f);
                g.Line2(Ctx.Line, 1.7f, 13.9f, 18.5f, 13.9f, 21.5f);
                g.Line2(Ctx.Line, 1.7f, 28.1f, 18.5f, 28.1f, 23.2f);
                // 雨:两条斜线
                g.Line2(Ctx.Accent, 1.6f, 17, 23.6f, 15, 28.4f);
                g.Line2(Ctx.Accent, 1.6f, 24, 23.6f, 22, 28.4f);
            },

            // ---------- 2. 大系统 ----------
            ["public-area"] = g =>
            {
                // 站厅/站台两层公共区 + 人形(去掉尺寸标注,16px 下更干净)
                g.Rect(Ctx.Line, 1.7f, 3, 7, 26, 6, Ctx.Soft, 1.6f);
                g.Rect(Ctx.Line, 1.7f, 3, 19, 26, 6, Ctx.Soft, 1.6f);
                g.Ellipse(Ctx.Accent, 1.3f, 16, 15.6f, 2.0f, Ctx.Accent);
                g.Line2(Ctx.Accent, 1.7f, 16, 17.6f, 16, 18.4f);
                g.Line2(Ctx.Accent, 1.4f, 13.5f, 18.2f, 18.5f, 18.2f);
            },
            ["large-load"] = g =>
            {
                // 计算器:显示屏 + 键盘点阵
                g.Rect(Ctx.Line, 1.5f, 5, 3.5f, 22, 25, Ctx.Soft, 2.5f);
                g.Fill(Ctx.Accent, 7.5f, 6.2f, 17, 6, 1.2f);
                for (int r = 0; r < 3; r++)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        g.Fill(r == 2 && c == 2 ? Ctx.Warm : Ctx.Line, 7.6f + c * 5.6f, 15 + r * 4.2f, 4.2f, 3f, 0.8f);
                    }
                }
            },
            ["large-smoke"] = g =>
            {
                // 风管 + 上升烟气 + 上箭头
                g.Rect(Ctx.Line, 1.5f, 3, 22, 26, 6, Ctx.Soft, 1.5f);
                g.Line2(Ctx.Line, 1.2f, 8, 22, 8, 28);
                g.Line2(Ctx.Line, 1.2f, 16, 22, 16, 28);
                g.Line2(Ctx.Line, 1.2f, 24, 22, 24, 28);
                g.Wave(Ctx.Red, 1.5f, 7, 25, 18, 1.4f, 0.6f);
                g.Wave(Ctx.Red, 1.5f, 7, 25, 13.5f, 1.4f, 2.2f);
                g.Arrow(Ctx.Red, 16, 3.5f, 7.5f, 90, 4.6f, 1.8f);
            },
            ["large-result"] = g =>
            {
                // 柱状图 + 基线 + 峰值强调
                g.Bars(Ctx.Line, 1.5f, 5, 6, 22, 20, 0.42f, 0.66f, 0.86f, 1.0f);
                g.Line2(Ctx.Gray, 1.0f, 5, 27.5f, 27, 27.5f);
            },

            // ---------- 3. 小系统 ----------
            ["small-allair"] = g =>
            {
                // 机组箱体 + 送风(上)/回风(下)箭头
                g.Rect(Ctx.Line, 1.7f, 4, 6, 24, 20, Ctx.Soft, 2.0f);
                g.Line2(Ctx.Gray, 1.0f, 6, 16, 26, 16);
                g.Arrow(Ctx.Accent, 26, 11, 17, 0, 3.4f, 1.8f);
                g.Arrow(Ctx.Green, 6, 21, 17, 180, 3.4f, 1.8f);
            },
            ["small-vrf"] = g =>
            {
                // 室外机(格栅)+ 两台室内机 + 冷媒管
                g.Rect(Ctx.Line, 1.7f, 3, 17, 11, 11, Ctx.Soft, 1.6f);
                for (int i = 0; i < 3; i++) g.Line2(Ctx.Line, 1.3f, 5, 19.6f + i * 3.2f, 12, 19.6f + i * 3.2f);
                g.Rect(Ctx.Line, 1.5f, 18, 5.5f, 11, 6, Ctx.Soft, 1.4f);
                g.Rect(Ctx.Line, 1.5f, 18, 20, 11, 6, Ctx.Soft, 1.4f);
                g.Line2(Ctx.Accent, 1.6f, 8.5f, 17, 8.5f, 8.5f);
                g.Line2(Ctx.Accent, 1.6f, 8.5f, 8.5f, 23.5f, 8.5f);
                g.Line2(Ctx.Accent, 1.6f, 23.5f, 8.5f, 23.5f, 20);
                g.Ellipse(Ctx.Accent, 1.1f, 23.5f, 8.5f, 1.6f, Color.White);
                g.Ellipse(Ctx.Accent, 1.1f, 23.5f, 20f, 1.6f, Color.White);
            },
            ["small-exhaust"] = g =>
            {
                // 风机(左)+ 排风格栅(右)+ 排出箭头
                g.Fan(Ctx.Line, 1.6f, 10.5f, 15, 6.2f);
                g.Rect(Ctx.Line, 1.6f, 19, 10, 8, 10, Ctx.Soft, 1.4f);
                for (int i = 0; i < 3; i++) g.Line2(Ctx.Line, 1.2f, 20.4f, 12.6f + i * 2.6f, 25.6f, 12.6f + i * 2.6f);
                g.Arrow(Ctx.Accent, 30, 15, 7.5f, 0, 3.6f, 1.8f);
            },
            ["small-sesmoke"] = g =>
            {
                // 共用风管(横向)+ 送风↓(蓝)/排烟↑(红)+ 阀
                g.Rect(Ctx.Line, 1.7f, 3, 13, 26, 7, Ctx.Soft, 1.5f);
                g.Poly(Ctx.Line, 1.4f, Color.White, new PointF(13.4f, 14), new PointF(13.4f, 19), new PointF(18.6f, 16.5f));
                g.Poly(Ctx.Line, 1.4f, Color.White, new PointF(18.6f, 14), new PointF(18.6f, 19), new PointF(13.4f, 16.5f));
                g.Arrow(Ctx.Accent, 8, 29, 7, 90, 3.6f, 1.7f);
                g.Arrow(Ctx.Red, 16, 3, 7, 270, 3.6f, 1.7f);
                g.Arrow(Ctx.Red, 25, 3, 7, 270, 3.6f, 1.7f);
            },
            ["small-press"] = g =>
            {
                // 楼梯 + 加压上箭头
                g.Line2(Ctx.Line, 1.6f, 13, 28, 29, 28);
                g.Rect(Ctx.Line, 1.5f, 13, 22, 6, 6, Color.White, 0.8f);
                g.Rect(Ctx.Line, 1.5f, 19, 16, 6, 12, Color.White, 0.8f);
                g.Rect(Ctx.Line, 1.5f, 25, 10, 4, 18, Ctx.Soft, 0.8f);
                g.Arrow(Ctx.Accent, 6.5f, 4.5f, 20, 270, 4.4f, 2.0f);
            },
            ["small-smoke"] = g =>
            {
                // 顶排烟管(含风机)+ 上升烟气 + 排烟箭头
                g.Rect(Ctx.Line, 1.7f, 3, 4, 26, 7, Ctx.Soft, 1.5f);
                g.Fan(Ctx.Line, 1.3f, 24.5f, 7.5f, 2.8f);
                for (int i = 0; i < 3; i++) g.Line2(Ctx.Gray, 1.1f, 5.5f + i * 3.4f, 5.5f, 5.5f + i * 3.4f, 9.5f);
                g.Wave(Ctx.Red, 1.7f, 6, 26, 17, 1.6f, 0.4f);
                g.Wave(Ctx.Red, 1.7f, 6, 26, 23, 1.6f, 2.0f);
                g.Arrow(Ctx.Red, 16, 11.5f, 3.6f, 270, 3.2f, 1.7f);
            },
            ["small-result"] = g =>
            {
                // 房间轮廓内的结果柱状图
                g.Rect(Ctx.Line, 1.5f, 3, 4.5f, 26, 24, Color.White, 1.8f);
                g.Bars(Ctx.Line, 1.4f, 6.5f, 8, 19, 17, 0.45f, 0.75f, 1.0f);
            },

            // ---------- 4. 水力计算 ----------
            ["hyd-air"] = g =>
            {
                // L 形风管(粗线弯头)+ 管中心线 + 流向箭头
                g.Polyline(Ctx.Line, 5.2f, new PointF(7, 27), new PointF(7, 12.5f), new PointF(18, 12.5f));
                g.Polyline(Color.White, 1.5f, new PointF(7, 25), new PointF(7, 12.5f), new PointF(16.5f, 12.5f));
                g.Arrow(Ctx.Accent, 28.5f, 12.5f, 8, 0, 3.6f, 1.8f);
                g.Line2(Ctx.Line, 1.4f, 6, 22, 12, 22);
                g.Line2(Ctx.Line, 1.4f, 6, 28, 12, 28);
            },
            ["hyd-water"] = g =>
            {
                // 管道 + 蝶阀 + 水滴(右上,避免与管道重叠)
                g.Line2(Ctx.Line, 1.8f, 3, 11, 29, 11);
                g.Line2(Ctx.Line, 1.8f, 3, 17, 29, 17);
                g.Line2(Ctx.Accent, 2.6f, 3, 14, 29, 14);
                g.Poly(Ctx.Line, 1.5f, Color.White, new PointF(11, 6), new PointF(11, 22), new PointF(21, 14));
                g.Poly(Ctx.Line, 1.5f, Color.White, new PointF(21, 6), new PointF(21, 22), new PointF(11, 14));
                g.Poly(Ctx.Accent, 1.3f, Ctx.Accent,
                    new PointF(16, 19.6f), new PointF(13.2f, 23.5f), new PointF(18.8f, 23.5f));
                g.Fill(Ctx.Accent, 13.6f, 23.2f, 4.8f, 4.4f, 2.2f);
                g.Line2(Ctx.Line, 1.2f, 16, 20.2f, 16, 22);
            },
            ["hyd-result"] = g =>
            {
                // 压力表(少刻度)+ 管段
                g.Line2(Ctx.Line, 1.8f, 3, 26, 29, 26);
                g.Ellipse(Ctx.Line, 1.9f, 16, 13.5f, 9.2f, Ctx.Soft);
                for (int i = 0; i < 4; i++)
                {
                    double a = (205 + i * 43) * Math.PI / 180.0;
                    g.Line2(Ctx.Gray, 1.2f, 16 + (float)(Math.Cos(a) * 7.4), 13.5f + (float)(Math.Sin(a) * 7.4),
                        16 + (float)(Math.Cos(a) * 5.8), 13.5f + (float)(Math.Sin(a) * 5.8));
                }
                g.Line2(Ctx.Accent, 2.0f, 16, 13.5f, 21.5f, 7.5f);
                g.Ellipse(Ctx.Accent, 1.1f, 16, 13.5f, 1.4f, Ctx.Accent);
            },

            // ---------- 5. 出图 ----------
            ["schedule"] = g =>
            {
                // 明细表:表头 + 行列线
                g.Rect(Ctx.Line, 1.5f, 4, 5, 24, 22, Color.White, 1.5f);
                g.Fill(Ctx.Accent, 4.7f, 5.7f, 22.6f, 5.4f, 1.0f);
                for (int i = 0; i < 3; i++) g.Line2(Ctx.Line, 1.1f, 4, 15 + i * 4, 28, 15 + i * 4);
                g.Line2(Ctx.Line, 1.1f, 12, 11, 12, 27);
                g.Line2(Ctx.Line, 1.1f, 20, 11, 20, 27);
            },
            ["titleblock"] = g =>
            {
                // 图框 + 内框 + 标题栏
                g.Rect(Ctx.Line, 1.5f, 3, 4, 26, 24, Color.White, 1.0f);
                g.Rect(Ctx.Line, 1.1f, 5, 6, 22, 20, null, 0.6f);
                g.Rect(Ctx.Line, 1.2f, 16, 18, 11, 8, Ctx.Soft, 0.8f);
                g.Line2(Ctx.Line, 1.0f, 16, 21.5f, 27, 21.5f);
                g.Line2(Ctx.Line, 1.0f, 21.5f, 18, 21.5f, 26);
                g.Line2(Ctx.Accent, 1.2f, 7, 9, 15, 9);
                g.Line2(Ctx.Accent, 1.2f, 7, 12, 12, 12);
            },

            // ---------- 6. AI问答 ----------
            ["guide"] = g =>
            {
                // 步骤清单:三条 [序号方块 + 说明线](比"书页"在 32px 下更清楚)
                g.Rect(Ctx.Line, 1.6f, 3.5f, 4.5f, 25, 23, Color.White, 1.6f);
                for (int i = 0; i < 3; i++)
                {
                    float y = 8 + i * 6.4f;
                    g.Fill(i == 0 ? Ctx.Green : Ctx.Accent, 6.5f, y, 5, 5, 1.0f);
                    g.Line2(Color.White, 1.2f, 7.6f, y + 2.5f, 8.6f, y + 3.6f);
                    g.Line2(Color.White, 1.2f, 8.6f, y + 3.6f, 10.4f, y + 1.4f);
                    g.Line2(Ctx.Gray, 1.5f, 14, y + 2.5f, 25.5f, y + 2.5f);
                }
            },
            ["knowledge"] = g =>
            {
                // 规范书 + 放大镜(放大镜缩小并移到右下角外沿,不与书页重叠)
                g.Rect(Ctx.Line, 1.6f, 3, 5, 19, 22, Ctx.Soft, 1.6f);
                g.Line2(Ctx.Line, 1.6f, 7, 5, 7, 27);
                for (int i = 0; i < 3; i++) g.Line2(Ctx.Gray, 1.2f, 9.5f, 11 + i * 4.6f, 20, 11 + i * 4.6f);
                g.Ellipse(Ctx.Accent, 1.9f, 21.5f, 20, 6.0f, Color.FromArgb(0xF2, 0xF8, 0xFD));
                g.Line2(Ctx.Accent, 2.6f, 25.7f, 24.2f, 29.5f, 28.4f);
            },

            // ---------- 7. 产品支持 ----------
            ["feedback"] = g =>
            {
                // 对话气泡(尾巴短)+ 感叹号
                g.Fill(Ctx.Soft, 3, 6, 26, 17, 3.0f);
                g.Rect(Ctx.Line, 1.7f, 3, 6, 26, 17, null, 3.0f);
                g.Poly(Ctx.Line, 1.7f, Ctx.Soft, new PointF(9, 22.6f), new PointF(10.5f, 28.5f), new PointF(15.5f, 22.4f));
                g.Fill(Ctx.Warm, 14.7f, 8.8f, 2.6f, 7.0f, 1.3f);
                g.Ellipse(Ctx.Warm, 1.1f, 16, 19.4f, 1.7f, Ctx.Warm);
            },
            ["help"] = g =>
            {
                // 圆圈 + 几何问号(不依赖字体)
                g.Ellipse(Ctx.Accent, 1.9f, 16, 16, 12, Ctx.Soft);
                g.Pie(Ctx.Line, 2.3f, 16, 12.4f, 4.6f, 195, 250);
                g.Line2(Ctx.Line, 2.3f, 16.4f, 13.6f, 16.4f, 18.4f);
                g.Ellipse(Ctx.Line, 1.1f, 16.4f, 22.6f, 1.7f, Ctx.Line);
            }
        };
    }
}
