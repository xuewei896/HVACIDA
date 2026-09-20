using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
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

        // ---- ima 在线知识库(需求 2.7 扩展):凭证只落本机,未启用就用本地知识库 ----
        private ImaKnowledgeSettings _imaSettings = new ImaKnowledgeSettings();
        private string _imaStatus = "";
        private string _imaNote = "";
        private string _imaLimitText = "10";
        private bool _imaPanelExpanded = true;
        private string _clauseNote = "";

        // ---- AI 问答(DeepSeek,需求 2.7「接在线大模型」):检索增强 + 依据清单,默认关 ----
        private AiChatSettings _aiSettings = new AiChatSettings();
        private string _aiStatus = "";
        private string _aiNote = "";
        private string _aiError = "";
        private string _aiAnswer = "";
        private bool _aiBusy;
        private bool _aiPanelExpanded;
        private string _aiModelText = "";
        private string _aiEndpointText = "";
        private string _aiMaxTokensText = "";
        private string _aiContextLimitText = "";
        private AiCitation _selectedAiCitation;

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
            _entries = KnowledgeBase.All;               // 访问即自动载入条文目录(见 KnowledgeBase.EnsureImportedClausesLoaded)

            AskCommand = new RelayCommand(Ask);
            ResetCommand = new RelayCommand(Reset);
            ExportExcelCommand = new RelayCommand(ExportExcel);
            ReloadClausesCommand = new RelayCommand(ReloadClauses);
            CategoryCommand = new RelayCommand(() => ApplyCategory(PendingCategory));
            OpenClauseFolderCommand = new RelayCommand(OpenClauseFolder);
            SaveImaCommand = new RelayCommand(SaveImaSettings);
            TestImaCommand = new RelayCommand(TestImaConnection);
            OpenImaShareCommand = new RelayCommand(OpenImaShare);
            SearchImaCommand = new RelayCommand(SearchImaOnly);
            SaveAiCommand = new RelayCommand(SaveAiSettings);
            AskAiCommand = new RelayCommand(AskAiAsync);
            TestAiCommand = new RelayCommand(TestAiConnection);
            OpenAiKeyPageCommand = new RelayCommand(OpenAiKeyPage);

            LoadImaSettings();
            LoadAiSettings();
            RefreshClauseNote();

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

        /// <summary>重新导入标准条文电子版(读 %AppData%\\HVACIDA\\规范条文 目录)。</summary>
        public ICommand ReloadClausesCommand { get; }

        /// <summary>条文目录(界面显示,便于用户把文件放进去)。</summary>
        public string ClauseDirectory => ClauseDocumentReader.DefaultDirectory;

        /// <summary>条文原文导入状态(载入了多少条 / 哪些文件没解析成功)。</summary>
        public string ClauseNote
        {
            get => _clauseNote;
            private set => Set(ref _clauseNote, value);
        }

        /// <summary>在资源管理器里打开条文目录(放文件用;目录不存在就先建)。</summary>
        public ICommand OpenClauseFolderCommand { get; }

        // ==================================================================
        // ima 在线知识库(腾讯 ima 开放接口)
        //
        // ⚠ 只有「Client ID + API Key + 知识库 ID」三样齐全**且**用户勾选启用,
        //   才会在提问时发网络请求;否则只用本地知识库,并在界面上直说没配。
        //   shareId / 分享链接只用于「在浏览器打开分享页」,**不当凭证用**。
        // ==================================================================

        /// <summary>ima 凭证说明(界面原样显示,避免用户以为 shareId 能当钥匙)。</summary>
        public string ImaCredentialHelp => ImaKnowledgeSettings.CredentialHelp;

        /// <summary>ima 设置文件路径(明文保存 API Key,界面要提醒)。</summary>
        public string ImaSettingsPath => ImaSettingsStore.DefaultPath;

        /// <summary>是否启用 ima 在线知识库(默认关)。</summary>
        public bool ImaEnabled
        {
            get => _imaSettings.Enabled;
            set
            {
                if (_imaSettings.Enabled == value) return;
                _imaSettings.Enabled = value;
                OnPropertyChanged(nameof(ImaEnabled));
            }
        }

        /// <summary>ima 开放平台 Client ID。</summary>
        public string ImaClientId
        {
            get => _imaSettings.ClientId;
            set
            {
                if (_imaSettings.ClientId == value) return;
                _imaSettings.ClientId = value ?? "";
                OnPropertyChanged(nameof(ImaClientId));
            }
        }

        /// <summary>ima 开放平台 API Key(本机明文保存)。</summary>
        public string ImaApiKey
        {
            get => _imaSettings.ApiKey;
            set
            {
                if (_imaSettings.ApiKey == value) return;
                _imaSettings.ApiKey = value ?? "";
                OnPropertyChanged(nameof(ImaApiKey));
            }
        }

        /// <summary>知识库 ID(接口参数 knowledge_base_id;不是 shareId)。</summary>
        public string ImaKnowledgeBaseId
        {
            get => _imaSettings.KnowledgeBaseId;
            set
            {
                if (_imaSettings.KnowledgeBaseId == value) return;
                _imaSettings.KnowledgeBaseId = value ?? "";
                OnPropertyChanged(nameof(ImaKnowledgeBaseId));
            }
        }

        /// <summary>ima 分享链接(只用于打开网页;shareId 单独一个值无法打开,插件不猜地址)。</summary>
        public string ImaShareLink
        {
            get => _imaSettings.ShareId;
            set
            {
                if (_imaSettings.ShareId == value) return;
                _imaSettings.ShareId = value ?? "";
                OnPropertyChanged(nameof(ImaShareLink));
            }
        }

        /// <summary>单次最多显示的命中条数(接口没有条数参数,只在本地截取)。</summary>
        public string ImaLimitText
        {
            get => _imaLimitText;
            set => Set(ref _imaLimitText, value ?? "");
        }

        /// <summary>ima 设置 / 调用状态(界面顶部显示)。</summary>
        public string ImaStatus
        {
            get => _imaStatus;
            private set { if (Set(ref _imaStatus, value)) OnPropertyChanged(nameof(ImaSummary)); }
        }

        /// <summary>ima 一句话状态(收起的面板标题里也要能看见,否则用户不知道有没有启用)。</summary>
        public string ImaSummary
        {
            get
            {
                if (!_imaSettings.Enabled && !_imaSettings.IsConfigured) return "未启用(只用本地知识库)";
                if (!_imaSettings.Enabled) return "凭证已填但未勾选启用";
                if (!_imaSettings.IsConfigured) return "已勾选启用,但凭证不全 —— " + _imaSettings.MissingCredentialText();
                return "已启用(知识库 ID " + _imaSettings.KnowledgeBaseId + ")";
            }
        }

        /// <summary>本次 ima 检索的提示(命中多少条 / 为什么没命中)。</summary>
        public string ImaNote
        {
            get => _imaNote;
            private set => Set(ref _imaNote, value);
        }

        /// <summary>ima 命中条目(在线检索结果;**片段**不是全文)。</summary>
        public ObservableCollection<ImaKnowledgeHit> ImaHits { get; } = new ObservableCollection<ImaKnowledgeHit>();

        /// <summary>是否显示 ima 命中表(没查过就不摆空表)。</summary>
        public bool HasImaHits => ImaHits.Count > 0;

        /// <summary>ima 设置面板是否展开(首次使用、还没配好时默认展开,配好后收起少占地方)。</summary>
        public bool ImaPanelExpanded
        {
            get => _imaPanelExpanded;
            set => Set(ref _imaPanelExpanded, value);
        }

        /// <summary>保存 ima 设置(只落本机 ima.xml,不发请求)。</summary>
        public ICommand SaveImaCommand { get; }

        /// <summary>测试 ima 连接(拉一次知识库信息,分清"凭证不对"还是"网络不通")。</summary>
        public ICommand TestImaCommand { get; }

        /// <summary>在浏览器里打开 ima 分享页(需要完整 http(s) 链接)。</summary>
        public ICommand OpenImaShareCommand { get; }

        /// <summary>单独用 ima 知识库检索一次(不影响本地检索结果)。</summary>
        public ICommand SearchImaCommand { get; }

        // ==================================================================
        // AI 问答(DeepSeek)—— 需求 2.7「接在线大模型」
        //
        // 口径:不是让模型凭记忆答工程问题,而是**检索增强** ——
        //   ① 先用本地知识库检索出**依据**;② 依据 + 问题发给 DeepSeek;
        //   ③ 系统提示写死「不编条文号/数值、资料不足要明说、结尾列依据」;
        //   ④ 回答与**依据清单**一起显示,用户核对的是依据。
        // 隐私:只发「问题 + 依据文本」,不发 Revit 模型数据与工程输入。
        // ==================================================================

        /// <summary>AI 凭证 / 隐私说明(界面原样显示)。</summary>
        public string AiCredentialHelp => AiChatSettings.CredentialHelp;

        /// <summary>AI 设置文件路径(明文保存 API key,界面要提醒)。</summary>
        public string AiSettingsPath => AiSettingsStore.DefaultPath;

        /// <summary>是否启用 AI 问答(默认关)。</summary>
        public bool AiEnabled
        {
            get => _aiSettings.Enabled;
            set
            {
                if (_aiSettings.Enabled == value) return;
                _aiSettings.Enabled = value;
                OnPropertyChanged(nameof(AiEnabled));
                OnPropertyChanged(nameof(AiSummary));
            }
        }

        /// <summary>DeepSeek API key(本机明文保存)。</summary>
        public string AiApiKey
        {
            get => _aiSettings.ApiKey;
            set
            {
                if (_aiSettings.ApiKey == value) return;
                _aiSettings.ApiKey = value ?? "";
                OnPropertyChanged(nameof(AiApiKey));
                OnPropertyChanged(nameof(AiSummary));
            }
        }

        /// <summary>模型名(留空用官方文档默认;界面提示当前实际会用哪个)。</summary>
        public string AiModelText
        {
            get => _aiModelText;
            set => Set(ref _aiModelText, value ?? "");
        }

        /// <summary>接口地址(留空用官方 https://api.deepseek.com)。</summary>
        public string AiEndpointText
        {
            get => _aiEndpointText;
            set => Set(ref _aiEndpointText, value ?? "");
        }

        /// <summary>单次回答最大生成 token。</summary>
        public string AiMaxTokensText
        {
            get => _aiMaxTokensText;
            set => Set(ref _aiMaxTokensText, value ?? "");
        }

        /// <summary>送入模型的依据条数上限。</summary>
        public string AiContextLimitText
        {
            get => _aiContextLimitText;
            set => Set(ref _aiContextLimitText, value ?? "");
        }

        /// <summary>当前实际会用的模型名与地址(收起面板时也看得见)。</summary>
        public string AiSummary
        {
            get
            {
                if (!_aiSettings.Enabled && !_aiSettings.IsConfigured) return "未启用(只用本地知识库)";
                if (!_aiSettings.Enabled) return "已填 API key 但未勾选启用";
                if (!_aiSettings.IsConfigured) return "已勾选启用,但" + _aiSettings.MissingCredentialText();
                return "已启用(" + DeepSeekClient.ResolveModel(_aiSettings) + ")";
            }
        }

        /// <summary>AI 设置 / 调用状态。</summary>
        public string AiStatus
        {
            get => _aiStatus;
            private set
            {
                if (Set(ref _aiStatus, value)) OnPropertyChanged(nameof(AiSummary));
            }
        }

        /// <summary>本次 AI 问答的提示(送了几条依据、token 用量、发往哪里)。</summary>
        public string AiNote
        {
            get => _aiNote;
            private set => Set(ref _aiNote, value);
        }

        /// <summary>失败原因(成功时为空;红字显示)。</summary>
        public string AiError
        {
            get => _aiError;
            private set
            {
                if (Set(ref _aiError, value)) OnPropertyChanged(nameof(HasAiResult));
            }
        }

        /// <summary>模型回答正文(**草稿**,必须对着依据核对)。</summary>
        public string AiAnswerText
        {
            get => _aiAnswer;
            private set
            {
                if (Set(ref _aiAnswer, value)) OnPropertyChanged(nameof(HasAiResult));
            }
        }

        /// <summary>正在请求模型(界面禁用按钮、显示"正在请求")。</summary>
        public bool AiBusy
        {
            get => _aiBusy;
            private set
            {
                if (!Set(ref _aiBusy, value)) return;
                OnPropertyChanged(nameof(AiCanAsk));
            }
        }

        /// <summary>按钮可用性(忙碌时禁用,避免重复发请求)。</summary>
        public bool AiCanAsk => !_aiBusy;

        /// <summary>AI 面板是否展开(AI 是主路径之外的可选增强,默认收起)。</summary>
        public bool AiPanelExpanded
        {
            get => _aiPanelExpanded;
            set => Set(ref _aiPanelExpanded, value);
        }

        /// <summary>本次回答用到的依据(来自本地检索;界面逐条列出便于核对)。</summary>
        public ObservableCollection<AiCitation> AiCitations { get; } = new ObservableCollection<AiCitation>();

        /// <summary>是否显示依据清单(没问过就不摆空表)。</summary>
        public bool HasAiCitations => AiCitations.Count > 0;

        /// <summary>是否显示 AI 回答区(没问过就不摆空区)。</summary>
        public bool HasAiResult =>
            !string.IsNullOrEmpty(_aiAnswer) || !string.IsNullOrEmpty(_aiError) || AiCitations.Count > 0;

        /// <summary>选中的依据(下方显示它的正文,便于逐条核对)。</summary>
        public AiCitation SelectedAiCitation
        {
            get => _selectedAiCitation;
            set
            {
                if (!Set(ref _selectedAiCitation, value)) return;
                OnPropertyChanged(nameof(AiCitationText));
            }
        }

        /// <summary>选中依据的正文(没选时给提示,不摆空白)。</summary>
        public string AiCitationText => _selectedAiCitation == null
            ? "(选中上面一条依据,这里显示它的正文 —— 模型回答对不对,就看它跟依据对不对得上)"
            : _selectedAiCitation.Text;

        /// <summary>保存 AI 设置(只落本机 ai.xml,不发请求)。</summary>
        public ICommand SaveAiCommand { get; }

        /// <summary>AI 回答:检索依据 → 发给 DeepSeek → 显示回答 + 依据(异步,不卡界面)。</summary>
        public ICommand AskAiCommand { get; }

        /// <summary>测试 AI 连接(发一条最小请求,会消耗极少量 token)。</summary>
        public ICommand TestAiCommand { get; }

        /// <summary>打开 DeepSeek 开放平台申请/查看 API key。</summary>
        public ICommand OpenAiKeyPageCommand { get; }

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

                // 启用了 ima 在线知识库才发网络请求;没启用/没配凭证时这里什么都不做(界面已在上面写明)
                if (_imaSettings.Enabled)
                {
                    var ima = ImaOpenApiClient.Search(_imaSettings, query);
                    ApplyImaResult(ima, true);
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

        /// <summary>重新导入条文目录下的标准条文电子版,并刷新条目列表。</summary>
        private void ReloadClauses()
        {
            try
            {
                var result = KnowledgeBase.ReloadImportedClauses(null);
                PendingCategory = "规范条文";
                ApplyCategory("规范条文");
                Status = result.Note + (result.Skipped.Count > 0 ? " 跳过:" + string.Join(";", result.Skipped.ToArray()) : "") +
                         "  条文目录:" + ClauseDirectory;
                RefreshClauseNote();
                OnPropertyChanged(nameof(ClauseDirectory));
            }
            catch (Exception ex)
            {
                Status = "导入标准条文失败: " + ex.Message;
            }
        }

        /// <summary>刷新「条文原文」状态行(载入条数 + 目录 + 跳过原因)。</summary>
        private void RefreshClauseNote()
        {
            int count = KnowledgeBase.ImportedClauseCount;
            ClauseNote = (count > 0
                    ? "条文原文:已载入 " + count + " 条(打开本窗时自动读取该目录)。"
                    : "条文原文:该目录里还没有可用条文 —— 把标准条文电子版(txt/md/csv/docx)放进去,再点【重新导入条文】。") +
                "  " + KnowledgeBase.ImportNote;
        }

        /// <summary>在资源管理器里打开条文目录(**推荐主路径**:放条文原文的地方)。</summary>
        private void OpenClauseFolder()
        {
            try
            {
                string directory = ClauseDirectory;
                if (!System.IO.Directory.Exists(directory)) System.IO.Directory.CreateDirectory(directory);
                Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
                Status = "已打开条文目录:" + directory + "(把标准条文电子版放进去,再点【重新导入条文】)";
            }
            catch (Exception ex)
            {
                Status = "打开条文目录失败:" + ex.Message + "(可手动在资源管理器地址栏粘贴:" + ClauseDirectory + ")";
            }
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

        // ==================================================================
        // ima 在线知识库:设置读写 / 连通性测试 / 打开分享页 / 单独检索
        // ==================================================================

        /// <summary>打开知识库窗时读一次本机 ima 设置(读失败不影响本地知识库)。</summary>
        private void LoadImaSettings()
        {
            string note;
            _imaSettings = ImaSettingsStore.Load(ImaSettingsStore.DefaultPath, out note);
            _imaLimitText = _imaSettings.Limit.ToString(CultureInfo.InvariantCulture);
            ImaStatus = _imaSettings.Enabled || _imaSettings.IsConfigured
                ? ImaSettingsStore.Summary(_imaSettings)
                : "ima 在线知识库:未启用 —— 只用本地知识库。" + ImaKnowledgeSettings.CredentialHelp;
            ImaNote = note;
            // ima 是**辅助**路径(只给片段):默认收起,靠标题上的一句话状态让用户知道有没有启用
            _imaPanelExpanded = false;

            OnPropertyChanged(nameof(ImaEnabled));
            OnPropertyChanged(nameof(ImaClientId));
            OnPropertyChanged(nameof(ImaApiKey));
            OnPropertyChanged(nameof(ImaKnowledgeBaseId));
            OnPropertyChanged(nameof(ImaShareLink));
            OnPropertyChanged(nameof(ImaLimitText));
            OnPropertyChanged(nameof(ImaPanelExpanded));
        }

        /// <summary>保存设置(把界面上的值收进设置对象后落盘;本方法不发网络请求)。</summary>
        private void SaveImaSettings()
        {
            try
            {
                int limit;
                if (!int.TryParse(_imaLimitText, NumberStyles.Integer, CultureInfo.InvariantCulture, out limit) || limit <= 0)
                {
                    limit = 10;
                }
                if (limit > 50) limit = 50;         // 界面填 9999 也不至于把表格拖死
                _imaSettings.Limit = limit;
                _imaLimitText = limit.ToString(CultureInfo.InvariantCulture);
                OnPropertyChanged(nameof(ImaLimitText));

                string path = ImaSettingsStore.Save(_imaSettings);
                ImaStatus = "ima 设置已保存(" + path + "):" + ImaSettingsStore.Summary(_imaSettings);
                ImaNote = "已保存。" + ImaKnowledgeSettings.CredentialHelp;
            }
            catch (Exception ex)
            {
                ImaStatus = "保存 ima 设置失败:" + ex.Message;
            }
        }

        /// <summary>测试连接:只拉一次知识库信息,用来分清"凭证/权限不对"还是"网络不通"。</summary>
        private void TestImaConnection()
        {
            try
            {
                SaveImaSettings();                  // 先落盘,免得"改了凭证没保存"被当成插件不生效
                if (!_imaSettings.IsConfigured)
                {
                    ImaStatus = "ima 凭证不全,没法测试 —— " + _imaSettings.MissingCredentialText();
                    return;
                }
                var result = ImaOpenApiClient.TestConnection(_imaSettings);
                ImaStatus = result.Success
                    ? "ima 连接测试:" + result.Note
                    : "ima 连接测试失败:" + result.ErrorMessage + " —— " + result.Note;
                ImaNote = result.Note;
            }
            catch (Exception ex)
            {
                ImaStatus = "测试 ima 连接失败:" + ex.Message;
            }
        }

        /// <summary>用默认浏览器打开 ima 分享页(只有完整 http(s) 链接才打开;shareId 单独一个值不猜地址)。</summary>
        private void OpenImaShare()
        {
            try
            {
                string url = ImaSettingsStore.ResolveShareUrl(_imaSettings.ShareId);
                if (url.Length == 0)
                {
                    ImaStatus = "没有打开分享页:请把 ima 的**完整分享链接**(https:// 开头)贴进「分享链接」框。" +
                                "只给 shareId 一个值的话,插件不知道对应的网址,不猜地址。";
                    return;
                }
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                ImaStatus = "已用默认浏览器打开 ima 分享页:" + url;
            }
            catch (Exception ex)
            {
                ImaStatus = "打开分享页失败:" + ex.Message + "(可以把链接复制到浏览器)";
            }
        }

        /// <summary>只用 ima 在线知识库检索一次(不动本地知识库的结果)。</summary>
        private void SearchImaOnly()
        {
            try
            {
                string query = (_question ?? "").Trim();
                if (query.Length == 0)
                {
                    ImaStatus = "请先输入问题,再点【用 ima 检索】。";
                    return;
                }
                var result = ImaOpenApiClient.Search(_imaSettings, query);
                ApplyImaResult(result, false);
            }
            catch (Exception ex)
            {
                ImaStatus = "ima 检索失败:" + ex.Message;
            }
        }

        /// <summary>把 ima 检索结果摆到界面上:命中就列标题与片段,失败就照实说(不冒充本地结果)。</summary>
        private void ApplyImaResult(ImaKnowledgeSearchResult result, bool appendToAnswer)
        {
            ImaHits.Clear();
            if (result.Success)
            {
                foreach (var hit in result.Hits) ImaHits.Add(hit);
            }
            OnPropertyChanged(nameof(HasImaHits));

            ImaNote = result.Success ? result.Note : ("ima 在线知识库没有给出结果:" + result.ErrorMessage);
            ImaStatus = result.Success
                ? "ima 在线知识库:" + result.Note
                : "ima 在线知识库:" + result.ErrorMessage + " —— " + result.Note + "(本次只用本地知识库的结果)";

            if (!appendToAnswer) return;

            string section;
            if (result.Success)
            {
                section = result.Count > 0
                    ? "\n\n—— ima 在线知识库(在线检索) ——\n" + ImaOpenApiClient.FormatHits(result.Hits) +
                      "注:" + result.Note
                    : "\n\n—— ima 在线知识库(在线检索) ——\n" + result.Note;
            }
            else
            {
                section = "\n\n—— ima 在线知识库(没有取到结果) ——\n" +
                          result.ErrorMessage + "。" + result.Note;
            }
            AnswerText = AnswerText + section;
            Lines.Add(new ChatLine(false, section.Trim()));
        }

        // ==================================================================
        // AI 问答(DeepSeek):设置读写 / 异步提问 / 连接测试 / 依据清单
        // ==================================================================

        /// <summary>打开知识库窗时读一次本机 AI 设置(读失败不影响本地知识库)。</summary>
        private void LoadAiSettings()
        {
            string note;
            _aiSettings = AiSettingsStore.Load(AiSettingsStore.DefaultPath, out note);
            _aiModelText = _aiSettings.Model ?? "";
            _aiEndpointText = _aiSettings.Endpoint ?? "";
            _aiMaxTokensText = _aiSettings.MaxTokens.ToString(CultureInfo.InvariantCulture);
            _aiContextLimitText = _aiSettings.ContextEntryLimit.ToString(CultureInfo.InvariantCulture);
            _aiPanelExpanded = false;                    // 可选增强:默认收起,标题上带一句话状态

            AiStatus = _aiSettings.Enabled || _aiSettings.IsConfigured
                ? AiSettingsStore.Summary(_aiSettings)
                : "AI 问答:未启用 —— 只用本地知识库。" + AiChatSettings.CredentialHelp;
            AiNote = note;

            OnPropertyChanged(nameof(AiEnabled));
            OnPropertyChanged(nameof(AiApiKey));
            OnPropertyChanged(nameof(AiModelText));
            OnPropertyChanged(nameof(AiEndpointText));
            OnPropertyChanged(nameof(AiMaxTokensText));
            OnPropertyChanged(nameof(AiContextLimitText));
            OnPropertyChanged(nameof(AiPanelExpanded));
        }

        /// <summary>把界面上的取值收进设置对象(不落盘、不发请求)。</summary>
        private void SyncAiSettingsFromUi()
        {
            int maxTokens;
            if (!int.TryParse(_aiMaxTokensText, NumberStyles.Integer, CultureInfo.InvariantCulture, out maxTokens) || maxTokens <= 0)
                maxTokens = 1024;
            if (maxTokens > 8192) maxTokens = 8192;

            int limit;
            if (!int.TryParse(_aiContextLimitText, NumberStyles.Integer, CultureInfo.InvariantCulture, out limit) || limit <= 0)
                limit = 5;
            if (limit > 20) limit = 20;

            _aiSettings.Model = (_aiModelText ?? "").Trim();
            _aiSettings.Endpoint = (_aiEndpointText ?? "").Trim();
            _aiSettings.MaxTokens = maxTokens;
            _aiSettings.ContextEntryLimit = limit;
            _aiMaxTokensText = maxTokens.ToString(CultureInfo.InvariantCulture);
            _aiContextLimitText = limit.ToString(CultureInfo.InvariantCulture);
            OnPropertyChanged(nameof(AiMaxTokensText));
            OnPropertyChanged(nameof(AiContextLimitText));
        }

        /// <summary>保存 AI 设置(只落本机 ai.xml)。</summary>
        private void SaveAiSettings()
        {
            try
            {
                SyncAiSettingsFromUi();
                string path = AiSettingsStore.Save(_aiSettings);
                AiStatus = "AI 设置已保存(" + path + "):" + AiSettingsStore.Summary(_aiSettings);
                AiNote = "已保存。" + AiChatSettings.CredentialHelp;
            }
            catch (Exception ex)
            {
                AiStatus = "保存 AI 设置失败:" + ex.Message;
            }
        }

        /// <summary>
        /// **AI 回答**:先本地检索依据,再把「问题 + 依据」发给 DeepSeek(在后台线程发,不卡界面)。
        /// 未启用 / 没填 Key 时**不发请求**,但照样把本地检索到的依据摆出来,并说清为什么没有模型回答。
        /// </summary>
        private async void AskAiAsync()
        {
            if (_aiBusy) return;
            try
            {
                string query = (_question ?? "").Trim();
                if (query.Length == 0)
                {
                    AiStatus = "请先输入问题,再点【AI 回答】。";
                    return;
                }

                SyncAiSettingsFromUi();
                var settings = _aiSettings.Clone();

                AiError = "";
                AiAnswerText = "";
                AiCitations.Clear();
                SelectedAiCitation = null;
                OnPropertyChanged(nameof(HasAiCitations));
                OnPropertyChanged(nameof(HasAiResult));

                if (!settings.Enabled || !settings.IsConfigured)
                {
                    // 不发请求:把本地依据与原因说清楚(依据清单对用户仍有价值)
                    var blocked = AiAnswerService.Ask(settings, query);
                    ApplyAiResult(blocked);
                    return;
                }

                AiBusy = true;
                AiStatus = "正在请求 DeepSeek(" + DeepSeekClient.ResolveModel(settings) + ")…… 会发送你的问题与检索到的依据文本。";
                AiNote = DeepSeekClient.PrivacyNote;

                var result = await System.Threading.Tasks.Task.Run(() => AiAnswerService.Ask(settings, query));
                ApplyAiResult(result);
            }
            catch (Exception ex)
            {
                AiError = "AI 问答失败:" + ex.Message;
                AiStatus = "AI 问答失败:" + ex.Message;
            }
            finally
            {
                AiBusy = false;
            }
        }

        /// <summary>把一次 AI 问答结果摆到界面上(成功给回答 + 依据;失败照实说并保留依据)。</summary>
        private void ApplyAiResult(AiChatResult result)
        {
            AiCitations.Clear();
            if (result.Citations != null)
            {
                foreach (var citation in result.Citations) AiCitations.Add(citation);
            }
            SelectedAiCitation = AiCitations.Count > 0 ? AiCitations[0] : null;
            OnPropertyChanged(nameof(HasAiCitations));
            OnPropertyChanged(nameof(HasAiResult));

            if (result.Success)
            {
                AiAnswerText = result.Content;
                AiError = "";
                AiStatus = "AI 回答已生成(" + result.Model + "," + result.ElapsedMs + " ms):" + result.UsageText +
                           ";依据 " + result.CitationCount + " 条 —— **请对着依据与标准原文核对后再用**。";
                AiNote = result.Note;
            }
            else
            {
                AiAnswerText = "";
                AiError = result.ErrorMessage;
                AiStatus = "本次没有模型回答:" + result.ErrorMessage;
                AiNote = result.Note + (result.CitationCount > 0
                    ? " 本地检索到的 " + result.CitationCount + " 条依据仍列在下面。"
                    : "");
            }
        }

        /// <summary>测试 AI 连接:发一条最小请求(会消耗极少量 token),用来分清"key/余额问题"还是"网络问题"。</summary>
        private void TestAiConnection()
        {
            try
            {
                SyncAiSettingsFromUi();
                var settings = _aiSettings.Clone();
                if (!settings.IsConfigured)
                {
                    AiStatus = "AI 凭证不全,没法测试 —— " + settings.MissingCredentialText();
                    return;
                }

                settings.Enabled = true;                 // 测试本身是显式动作,不受"是否勾选启用"阻挡
                settings.MaxTokens = 16;
                settings.Temperature = 0;
                var messages = new List<AiChatMessage>
                {
                    AiChatMessage.System("这是连通性测试。只回复四个字:连接正常。"),
                    AiChatMessage.User("连接正常吗?")
                };
                var result = DeepSeekClient.Ask(settings, messages, "连通性测试");
                AiError = result.Success ? "" : result.ErrorMessage;
                AiStatus = result.Success
                    ? "AI 连接测试:成功(" + result.Model + "," + result.ElapsedMs + " ms;" + result.UsageText +
                      ")。回答:" + result.Content.Replace("\r", " ").Replace("\n", " ")
                    : "AI 连接测试失败:" + result.ErrorMessage + " —— " + result.Note;
                AiNote = result.Note;
            }
            catch (Exception ex)
            {
                AiStatus = "测试 AI 连接失败:" + ex.Message;
            }
        }

        /// <summary>打开 DeepSeek 开放平台(申请 / 查看 API key)。</summary>
        private void OpenAiKeyPage()
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://platform.deepseek.com/api_keys") { UseShellExecute = true });
                AiStatus = "已在浏览器打开 DeepSeek 开放平台(platform.deepseek.com/api_keys),在那里申请 API key。";
            }
            catch (Exception ex)
            {
                AiStatus = "打开浏览器失败:" + ex.Message + "(可手动访问 https://platform.deepseek.com/api_keys)";
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
