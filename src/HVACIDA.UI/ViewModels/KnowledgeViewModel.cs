using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.UI.ViewModels
{
    /// <summary>
    /// AI问答 → **规范 / 口径知识库**窗 ViewModel(需求 2.7)。
    /// <para>
    /// 两条用法并存:① **提问**(关键词检索 → 答复 + 出处,答不出先说知识范围);
    /// ② **按分类浏览条目**(左侧列表 → 右侧正文 + 出处)。
    /// 内容全在 Core 的 <see cref="KnowledgeBase"/>,本类只做展示与命令。
    /// </para>
    /// <para>
    /// 保留原对话式界面所需的 <see cref="Lines"/> / <see cref="Question"/> / <see cref="Ask"/> 契约。
    /// </para>
    /// </summary>
    public class KnowledgeViewModel : ViewModelBase
    {
        private readonly ExcelReportGenerator _excel;

        private string _question = "";
        private string _status = "";
        private string _answerText = "";
        private string _scopeNote = "";
        private string _categoryFilter = "全部";
        private KnowledgeEntry _selectedEntry;
        private IList<KnowledgeEntry> _entries;

        public KnowledgeViewModel()
            : this(null)
        {
        }

        public KnowledgeViewModel(string reportsDirectory)
            : this(reportsDirectory, null)
        {
        }

        /// <summary>
        /// <paramref name="initialCategory"/> 用于「AI问答 → 操作指南」键:打开即定位到「操作步骤」(Revit 操作指南);
        /// 为空则显示全部。
        /// </summary>
        public KnowledgeViewModel(string reportsDirectory, string initialCategory)
        {
            _excel = new ExcelReportGenerator(reportsDirectory);
            _entries = KnowledgeBase.All;

            AskCommand = new RelayCommand(Ask);
            ResetCommand = new RelayCommand(Reset);
            ExportExcelCommand = new RelayCommand(ExportExcel);
            CategoryCommand = new RelayCommand(() => ApplyCategory(PendingCategory));

            Status = "共 " + _entries.Count + " 条条目(本项目已定口径 / 规范条文 / Revit 操作指南)。可以直接提问,也可以按分类浏览。";
            if (_entries.Count > 0) SelectedEntry = _entries[0];
            if (!string.IsNullOrEmpty(initialCategory))
            {
                PendingCategory = initialCategory;
                ApplyCategory(initialCategory);
            }
        }

        /// <summary>对话记录(用户问句 + 系统答复交替)。</summary>
        public ObservableCollection<ChatLine> Lines { get; } = new ObservableCollection<ChatLine>();

        /// <summary>示例问题(快捷按钮;取知识库里的「典型问法」)。</summary>
        public IList<string> SampleQuestions => KnowledgeBase.SampleQuestions();

        /// <summary>分类筛选选项。</summary>
        public IList<string> Categories => new List<string>
        {
            "全部", "已定口径", "规范条文", "规范依据", "操作步骤", "数据与存储", "待补与局限"
        };

        public string CategoryFilter
        {
            get => _categoryFilter;
            private set => Set(ref _categoryFilter, value);
        }

        /// <summary>当前筛选下的条目(左侧列表)。</summary>
        public IList<KnowledgeEntry> Entries
        {
            get => _entries;
            private set => Set(ref _entries, value);
        }

        /// <summary>选中的条目(右侧显示正文与出处)。</summary>
        public KnowledgeEntry SelectedEntry
        {
            get => _selectedEntry;
            set
            {
                if (!Set(ref _selectedEntry, value)) return;
                OnPropertyChanged(nameof(DetailTitle));
                OnPropertyChanged(nameof(DetailText));
                OnPropertyChanged(nameof(DetailSource));
                OnPropertyChanged(nameof(DetailCategory));
            }
        }

        public string DetailTitle => _selectedEntry == null ? "(未选中条目)" : _selectedEntry.Title;

        public string DetailCategory => _selectedEntry == null ? "" : _selectedEntry.CategoryName;

        public string DetailText => _selectedEntry == null ? "" : _selectedEntry.Answer;

        public string DetailSource => _selectedEntry == null ? "" : "出处:" + _selectedEntry.Source;

        /// <summary>提问内容。</summary>
        public string Question
        {
            get => _question;
            set => Set(ref _question, value);
        }

        /// <summary>本次答复(命中时的正文 + 出处)。</summary>
        public string AnswerText
        {
            get => _answerText;
            private set => Set(ref _answerText, value);
        }

        /// <summary>答不出时的知识范围说明(不编答案)。</summary>
        public string ScopeNote
        {
            get => _scopeNote;
            private set => Set(ref _scopeNote, value);
        }

        public string Status
        {
            get => _status;
            private set => Set(ref _status, value);
        }

        public ICommand AskCommand { get; }
        public ICommand ResetCommand { get; }
        public ICommand ExportExcelCommand { get; }

        /// <summary>切换分类(界面把选中项写进 <see cref="PendingCategory"/> 后执行本命令)。</summary>
        public ICommand CategoryCommand { get; }

        /// <summary>界面要切换到的分类(执行 <see cref="CategoryCommand"/> 前先赋值)。</summary>
        public string PendingCategory { get; set; } = "全部";

        /// <summary>提问:检索 → 答复 + 出处;答不出给「知识范围」说明并把最接近的条目列出来。</summary>
        public void Ask()
        {
            try
            {
                string query = (_question ?? "").Trim();
                if (query.Length == 0)
                {
                    Status = "请先输入问题,或点上面的示例问题。";
                    return;
                }

                Lines.Add(new ChatLine(true, query));
                var answer = KnowledgeBase.Answer(query);
                AnswerText = answer.AnswerText;
                ScopeNote = answer.ScopeNote;
                if (answer.HasAnswer)
                {
                    Lines.Add(new ChatLine(false, answer.AnswerText));
                    SelectedEntry = answer.Top;
                    Status = "命中 " + answer.Matches.Count + " 条:已显示最相关条目「" + answer.Top.Title +
                             "」(" + answer.Matches[0].HitText + "命中)。";
                }
                else
                {
                    Lines.Add(new ChatLine(false, answer.ScopeNote));
                    Status = "知识库范围内没有这个问题 —— 已给出覆盖范围与提问建议(不编答案)。";
                }
            }
            catch (Exception ex)
            {
                Status = "检索失败: " + ex.Message;
            }
        }

        private void Reset()
        {
            Lines.Clear();
            Question = "";
            AnswerText = "";
            ScopeNote = "";
            Status = "已清空问答记录。";
        }

        private void ApplyCategory(string category)
        {
            try
            {
                CategoryFilter = string.IsNullOrEmpty(category) ? "全部" : category;
                KnowledgeCategory parsed;
                if (CategoryFilter == "全部" || !TryParseCategory(CategoryFilter, out parsed))
                {
                    Entries = KnowledgeBase.All;
                }
                else
                {
                    Entries = KnowledgeBase.ByCategory(parsed);
                }
                SelectedEntry = _entries.Count > 0 ? _entries[0] : null;
                Status = "分类「" + CategoryFilter + "」:" + _entries.Count + " 条。";
            }
            catch (Exception ex)
            {
                Status = "筛选失败: " + ex.Message;
            }
        }

        private static bool TryParseCategory(string text, out KnowledgeCategory category)
        {
            category = KnowledgeCategory.Caliber;
            foreach (KnowledgeCategory value in Enum.GetValues(typeof(KnowledgeCategory)))
            {
                if (KnowledgeBase.CategoryName(value) == text)
                {
                    category = value;
                    return true;
                }
            }
            return false;
        }

        private void ExportExcel()
        {
            try
            {
                var workbook = KnowledgeBaseExcelExporter.Build(_entries);
                string path = _excel.SaveWorkbook("知识库条目", workbook);
                Status = "知识库已导出(" + _entries.Count + " 条、" + workbook.SheetCount + " 个工作表): " + path;
            }
            catch (Exception ex)
            {
                Status = "导出 Excel 失败: " + ex.Message;
            }
        }
    }

    /// <summary>对话记录的一行。</summary>
    public sealed class ChatLine
    {
        public ChatLine(bool isUser, string text)
        {
            IsUser = isUser;
            Text = text ?? "";
        }

        public bool IsUser { get; }

        public string Text { get; }
    }
}
