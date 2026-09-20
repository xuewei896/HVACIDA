using System;
using System.IO;
using System.Text;
using Autodesk.Revit.UI;
using HVACIDA.Core.Models;

namespace HVACIDA.Revit.Services
{
    /// <summary>
    /// **当前 Revit 上下文快照**(照参考文档 2.2:每次请求都动态拼进 system prompt,
    /// 让模型知道"用户在哪个文件、哪个视图、选了什么",不瞎编)。
    /// </summary>
    internal static class RevitAiContext
    {
        /// <summary>拼一段给模型看的上下文(读不到的部分如实写"未打开文档",不编)。</summary>
        public static string BuildSnapshot(UIApplication uiApp)
        {
            var sb = new StringBuilder();
            try
            {
                if (uiApp == null)
                {
                    sb.AppendLine("Revit 未就绪(UIApplication 为空)");
                    return sb.ToString();
                }

                sb.AppendLine("Revit 版本:" + uiApp.Application.VersionNumber);

                UIDocument uidoc = uiApp.ActiveUIDocument;
                if (uidoc == null || uidoc.Document == null)
                {
                    sb.AppendLine("当前没有打开文档");
                }
                else
                {
                    var doc = uidoc.Document;
                    sb.AppendLine("文档:" + (string.IsNullOrEmpty(doc.Title) ? "(未命名)" : doc.Title));
                    sb.AppendLine("文档是否已保存:" + (string.IsNullOrEmpty(doc.PathName) ? "否(未保存)" : "是"));
                    sb.AppendLine("当前视图:" + (uidoc.ActiveView == null ? "(未知)" : uidoc.ActiveView.Name));
                    if (uidoc.ActiveView != null)
                    {
                        sb.AppendLine("视图类型:" + uidoc.ActiveView.ViewType);
                        if (uidoc.ActiveView.Scale > 0) sb.AppendLine("比例:1:" + uidoc.ActiveView.Scale);
                    }
                    int selected = 0;
                    try
                    {
                        var ids = uidoc.Selection.GetElementIds();
                        selected = ids == null ? 0 : ids.Count;
                    }
                    catch
                    {
                        selected = -1;
                    }
                    sb.AppendLine("当前选择构件数:" + (selected < 0 ? "(读不到)" : selected.ToString()));
                }

                sb.AppendLine("已注册的 AI 命令数:" + Core.Services.AiCommandBus.RegisteredNames().Count);
                sb.AppendLine("「操作 Revit」开关:" + (Core.Services.AiCommandBus.OperateRevitEnabled ? "已开启(可执行命令)" : "已关闭(只能聊天,命令不会执行)"));
            }
            catch (Exception ex)
            {
                sb.AppendLine("上下文读取失败:" + ex.Message + "(按" + "无上下文" + "继续)");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 当前**工作区**标识(参考文档 2.6:Revit 版本 + Windows 用户 + 项目路径哈希;
        /// 聊天记录与 API key 都按它隔离)。未保存文档用 <c>unsaved-</c> 前缀。
        /// </summary>
        public static AiWorkspaceScope BuildScope(UIApplication uiApp)
        {
            string version = "unknown";
            string project = "";
            bool unsaved = true;
            try
            {
                if (uiApp != null)
                {
                    version = uiApp.Application.VersionNumber;
                    UIDocument uidoc = uiApp.ActiveUIDocument;
                    if (uidoc != null && uidoc.Document != null)
                    {
                        project = uidoc.Document.PathName;
                        if (string.IsNullOrEmpty(project)) project = uidoc.Document.Title;
                        unsaved = string.IsNullOrEmpty(uidoc.Document.PathName);
                    }
                }
            }
            catch
            {
                // 读不到就按"未保存 + 无项目"处理:只是隔离键更粗,不会串数据
            }

            return new AiWorkspaceScope
            {
                RevitVersion = version,
                UserName = SafeUserName(),
                ProjectPath = project,
                IsUnsaved = unsaved
            };
        }

        private static string SafeUserName()
        {
            try { return Environment.UserName ?? "unknown"; }
            catch { return "unknown"; }
        }

        /// <summary>当前文档所在目录(界面显示用;没有就返回空)。</summary>
        public static string ProjectDirectory(UIApplication uiApp)
        {
            try
            {
                UIDocument uidoc = uiApp == null ? null : uiApp.ActiveUIDocument;
                if (uidoc == null || uidoc.Document == null) return "";
                string path = uidoc.Document.PathName;
                return string.IsNullOrEmpty(path) ? "" : Path.GetDirectoryName(path);
            }
            catch
            {
                return "";
            }
        }
    }
}
