using System.Collections.Generic;
using System.Linq;
using HVACIDA.Core.Services;

namespace HVACIDA.UI.ViewModels
{
    /// <summary>信息窗中的一节(标题 + 若干行)。</summary>
    public sealed class InfoSection
    {
        public InfoSection(string heading, IEnumerable<string> lines)
        {
            Heading = heading;
            Lines = (lines ?? Enumerable.Empty<string>()).ToList();
        }

        public string Heading { get; }

        public IList<string> Lines { get; }
    }

    /// <summary>
    /// 通用信息窗 ViewModel:承载「待实现说明」「操作指南」「帮助」三类只读内容,
    /// 内容全部来自 <see cref="ModuleCatalog"/>,避免与 Ribbon 按钮文字两头维护。
    /// </summary>
    public class InfoViewModel : ViewModelBase
    {
        private InfoViewModel(string title, string intro, string statusText, IEnumerable<InfoSection> sections)
        {
            Title = title;
            Intro = intro;
            StatusText = statusText;
            Sections = (sections ?? Enumerable.Empty<InfoSection>()).ToList();
        }

        /// <summary>窗口标题。</summary>
        public string Title { get; }

        /// <summary>顶部提示条文字。</summary>
        public string Intro { get; }

        /// <summary>状态徽标文字(如「待实现」)。</summary>
        public string StatusText { get; }

        /// <summary>内容分节。</summary>
        public IList<InfoSection> Sections { get; }

        /// <summary>未实现模块说明(状态徽标 + 已定口径 + 待补项)。</summary>
        public static InfoViewModel ForModule(ModuleInfo module)
        {
            var sections = new List<InfoSection>();
            if (module.Notes.Count > 0)
            {
                sections.Add(new InfoSection("计算要点 / 已定口径 / 待补项", module.Notes));
            }
            sections.Add(new InfoSection("实现状态", new[]
            {
                module.StatusText + " —— 本窗口只说明口径,不产生假数据。",
                "所属 Ribbon 面板:" + module.Panel
            }));

            return new InfoViewModel(
                module.TitleOneLine + " — HVACIDA",
                "「" + module.TitleOneLine + "」" + module.StatusText + "。以下为本模块已确定的口径与待补事项(需求文档口径)。",
                module.StatusText,
                sections);
        }

        /// <summary>操作指南。</summary>
        public static InfoViewModel Guide()
        {
            return new InfoViewModel(
                "操作指南 — HVACIDA",
                "推荐按 ①→⑦ 顺序操作;每一步都可以取消,不会改动模型。数值口径见《大系统负荷计算公式.docx》与需求文档。",
                "操作指南",
                new[]
                {
                    new InfoSection("推荐流程", ModuleCatalog.GuideSteps),
                    new InfoSection("常见注意点", new[]
                    {
                        "高峰客流必须手工输入 —— 插件不会从模型推断客流量。",
                        "站厅与站台送风温度必须一致,由站厅送风温度统一确定。",
                        "排烟按计算风量(面积 × 60),选型再乘 1.2。",
                        "未实现模块会给出说明窗口,不会给出假结果。"
                    })
                });
        }

        /// <summary>帮助 / 关于。</summary>
        public static InfoViewModel Help()
        {
            return new InfoViewModel(
                "帮助 — 关于 HVACIDA",
                "版本信息、排错信息与常用操作。反馈问题时请附上「排错信息」中的路径与日志。",
                "帮助",
                new[]
                {
                    new InfoSection("版本信息", ModuleCatalog.HelpVersionLines),
                    new InfoSection("排错信息(反馈问题时请附上)", ModuleCatalog.HelpDiagnosticLines),
                    new InfoSection("常用操作与快捷键", ModuleCatalog.HelpShortcutLines)
                });
        }
    }
}
