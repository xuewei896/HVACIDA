using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.UI.ViewModels
{
    /// <summary>聊天记录的一行(可绑定;**流式回答时正文会不断变化**,所以需要 INPC)。</summary>
    public class AiChatLine : ViewModelBase
    {
        private string _text;
        private bool _streaming;

        public AiChatLine(bool isUser, string text)
        {
            IsUser = isUser;
            _text = text ?? "";
        }

        /// <summary>是否用户说的话(界面靠它换样式/对齐)。</summary>
        public bool IsUser { get; }

        /// <summary>是否助手(与 <see cref="IsUser"/> 互斥,便于绑定)。</summary>
        public bool IsAssistant => !IsUser;

        /// <summary>正文(流式时逐段追加)。</summary>
        public string Text
        {
            get => _text;
            set => Set(ref _text, value ?? "");
        }

        /// <summary>这条是否还在流式接收(界面可显示光标/忙碌)。</summary>
        public bool Streaming
        {
            get => _streaming;
            set => Set(ref _streaming, value);
        }

        /// <summary>追加一段(流式回调)。</summary>
        public void Append(string piece)
        {
            Text = _text + (piece ?? "");
        }
    }

    /// <summary>命令活动日志的一行(界面表格)。</summary>
    public class AiToolLogRow
    {
        public AiToolLogRow(string name, string state, long elapsedMs)
        {
            Name = name ?? "";
            State = state ?? "";
            ElapsedMs = elapsedMs;
        }

        public string Name { get; }
        public string State { get; }
        public long ElapsedMs { get; }
        public string ElapsedText => ElapsedMs + " ms";
    }

    /// <summary>
    /// **AI 助手面板 ViewModel**(承《如何将AI大模型(DeepSeek)接入Revit中》的聊天面板)。
    /// <para>
    /// UI 层**不引用 Revit**:上下文快照与工作区标识由 Revit 层通过
    /// <see cref="ContextSnapshotProvider"/> / <see cref="ScopeProvider"/> 注入
    /// (没注入时按"无上下文 / 无工作区"处理,功能照常但隔离键更粗)。
    /// </para>
    /// <para>
    /// 三条界面纪律:① 回答是**草稿**,流式接收后要对着工具返回的数据核对;
    /// ② **「操作 Revit」默认关**(关掉时模型看不到工具、命令不会执行);
    /// ③ 发送前后都写明**发了什么**(问题 + 依据/工具数据),不藏着。
    /// </para>
    /// </summary>
    public class AiAssistantViewModel : ViewModelBase
    {
        // ---- Revit 层注入的两个钩子(UI 层不引用 Revit) ----
        /// <summary>上下文快照提供者(由 Revit 层设置)。</summary>
        public static Func<string> ContextSnapshotProvider { get; set; }

        /// <summary>工作区标识提供者(由 Revit 层设置;用于隔离 API key 与聊天记录)。</summary>
        public static Func<AiWorkspaceScope> ScopeProvider { get; set; }

        private readonly Dispatcher _dispatcher;
        private readonly string _settingsPath;

        private AiChatSettings _settings = new AiChatSettings();
        private AiWorkspaceScope _scope = new AiWorkspaceScope();
        private string _question = "";
        private string _status = "";
        private string _note = "";
        private string _lastError = "";
        private bool _busy;
        private AiProviderPreset _selectedProvider;
        private string _apiKeyText = "";
        private string _endpointText = "";
        private string _modelText = "";
        private string _scopeText = "";
        private bool _operateRevit;

        public AiAssistantViewModel()
            : this(null)
        {
        }

        public AiAssistantViewModel(string settingsPath)
        {
            _settingsPath = string.IsNullOrEmpty(settingsPath) ? AiSettingsStore.DefaultPath : settingsPath;
            _dispatcher = System.Windows.Application.Current != null
                ? System.Windows.Application.Current.Dispatcher
                : Dispatcher.CurrentDispatcher;

            SendCommand = new RelayCommand(SendAsync);
            SaveCommand = new RelayCommand(Save);
            ClearCommand = new RelayCommand(Clear);
            OpenKeyPageCommand = new RelayCommand(OpenKeyPage);
            ClearLogCommand = new RelayCommand(() => { ToolLog.Clear(); AiCommandBus.ClearLog(); });

            Reload();
        }

        /// <summary>聊天记录。</summary>
        public ObservableCollection<AiChatLine> Lines { get; } = new ObservableCollection<AiChatLine>();

        /// <summary>命令活动日志(模型调了哪条命令、成功还是失败、耗时)。</summary>
        public ObservableCollection<AiToolLogRow> ToolLog { get; } = new ObservableCollection<AiToolLogRow>();

        /// <summary>服务预设(DeepSeek / 通义 / 智谱 / Kimi / OpenAI / 自定义)。</summary>
        public IList<AiProviderPreset> Providers => AiProviderPresets.All;

        /// <summary>选中的服务预设。</summary>
        public AiProviderPreset SelectedProvider
        {
            get => _selectedProvider;
            set
            {
                if (!Set(ref _selectedProvider, value) || value == null) return;
                _settings.Provider = value.Key;
                if (AiProviderPresets.IsBuiltIn(value.Key))
                {
                    // 选了内置预设就按预设填地址与模型名(自定义预设保留用户填的值)
                    _settings.Endpoint = value.Endpoint;
                    _settings.Model = value.Model;
                    EndpointText = value.Endpoint;
                    ModelText = value.Model;
                }
                OnPropertyChanged(nameof(KeyPageUrl));
            }
        }

        /// <summary>该服务的密钥申请页(界面按钮用;自定义服务没有)。</summary>
        public string KeyPageUrl => _selectedProvider == null ? "" : _selectedProvider.KeyPage;

        /// <summary>是否启用 AI 助手。</summary>
        public bool Enabled
        {
            get => _settings.Enabled;
            set { if (_settings.Enabled == value) return; _settings.Enabled = value; OnPropertyChanged(nameof(Enabled)); RaiseSummary(); }
        }

        /// <summary>API key(按工作区加密保存)。</summary>
        public string ApiKeyText
        {
            get => _apiKeyText;
            set { if (Set(ref _apiKeyText, value ?? "")) RaiseSummary(); }
        }

        /// <summary>接口地址(OpenAI 兼容 base)。</summary>
        public string EndpointText
        {
            get => _endpointText;
            set => Set(ref _endpointText, value ?? "");
        }

        /// <summary>模型名。</summary>
        public string ModelText
        {
            get => _modelText;
            set => Set(ref _modelText, value ?? "");
        }

        /// <summary>「操作 Revit」总开关(**默认关**):关掉时模型看不到工具、命令也不会执行。</summary>
        public bool OperateRevit
        {
            get => _operateRevit;
            set
            {
                if (!Set(ref _operateRevit, value)) return;
                AiCommandBus.OperateRevitEnabled = value;
                OnPropertyChanged(nameof(OperateRevitText));
                OnPropertyChanged(nameof(ToolCountText));
                Status = value
                    ? "已开启「操作 Revit」:模型可以调用工具读取工程数据(命令返回的工程数据会随对话发给模型服务方)。"
                    : "已关闭「操作 Revit」:模型看不到工具,只能聊天与查知识库;工程数据不会出网。";
            }
        }

        /// <summary>开关的一句话状态。</summary>
        public string OperateRevitText => _operateRevit ? "已开启(可执行读取命令)" : "已关闭(只聊天,不动模型)";

        /// <summary>当前可用命令数(界面显示"模型能看到几条工具")。</summary>
        public string ToolCountText
        {
            get
            {
                if (!_operateRevit) return "模型可见命令:0 条(开关已关)";
                if (!AiCommandBus.IsReady) return "模型可见命令:0 条(命令集尚未加载,Revit 完全初始化后自动加载)";
                return "模型可见命令:" + AiCommandBus.ToolsForModel().Count + " 条";
            }
        }

        /// <summary>提问内容。</summary>
        public string Question
        {
            get => _question;
            set => Set(ref _question, value ?? "");
        }

        /// <summary>状态行。</summary>
        public string Status
        {
            get => _status;
            private set => Set(ref _status, value);
        }

        /// <summary>提示(送了什么、token 用量、依据几条)。</summary>
        public string Note
        {
            get => _note;
            private set => Set(ref _note, value);
        }

        /// <summary>失败原因(成功时为空,红字显示)。</summary>
        public string LastError
        {
            get => _lastError;
            private set => Set(ref _lastError, value);
        }

        /// <summary>工作区说明(界面显示按什么隔离聊天记录与密钥)。</summary>
        public string ScopeText
        {
            get => _scopeText;
            private set => Set(ref _scopeText, value);
        }

        /// <summary>一句话状态(标题栏/开关区显示)。</summary>
        public string Summary
        {
            get
            {
                if (!_settings.Enabled) return "未启用";
                if (string.IsNullOrWhiteSpace(_apiKeyText)) return "已启用但缺 API key";
                return AiProviderPresets.Find(_settings.Provider) == null
                    ? "已启用(自定义服务)"
                    : "已启用(" + AiProviderPresets.Find(_settings.Provider).DisplayName + " · " + _modelText + ")";
            }
        }

        /// <summary>正在请求模型。</summary>
        public bool Busy
        {
            get => _busy;
            private set
            {
                if (!Set(ref _busy, value)) return;
                OnPropertyChanged(nameof(CanSend));
            }
        }

        /// <summary>发送按钮可用性。</summary>
        public bool CanSend => !_busy;

        /// <summary>是否有聊天记录(没聊过就不摆空区)。</summary>
        public bool HasLines => Lines.Count > 0;

        /// <summary>是否有命令日志(没调用过命令就不摆空表)。</summary>
        public bool HasToolLog => ToolLog.Count > 0;

        public ICommand SendCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand OpenKeyPageCommand { get; }
        public ICommand ClearLogCommand { get; }

        /// <summary>重新读设置与工作区(打开面板 / 切换文档后调用)。</summary>
        public void Reload()
        {
            _scope = ScopeProvider == null ? new AiWorkspaceScope() : (ScopeProvider() ?? new AiWorkspaceScope());
            string scopeId = ScopeProvider == null ? "" : _scope.Id;
            string note;
            _settings = AiSettingsStore.Load(_settingsPath, scopeId, out note);

            _apiKeyText = _settings.ApiKey ?? "";
            _endpointText = _settings.Endpoint ?? "";
            _modelText = _settings.Model ?? "";
            _selectedProvider = AiProviderPresets.Find(_settings.Provider) ?? AiProviderPresets.Find("deepseek");
            if (_selectedProvider != null && string.IsNullOrEmpty(_settings.Provider)) _settings.Provider = _selectedProvider.Key;
            if (string.IsNullOrEmpty(_endpointText) && _selectedProvider != null) _endpointText = _selectedProvider.Endpoint;
            if (string.IsNullOrEmpty(_modelText) && _selectedProvider != null) _modelText = _selectedProvider.Model;

            _operateRevit = AiCommandBus.OperateRevitEnabled;
            ScopeText = ScopeProvider == null
                ? "工作区:未接入 Revit 上下文(自检/离线模式;密钥不按工作区隔离)"
                : "工作区:" + _scope.Id + (_scope.IsUnsaved ? "(文档未保存)" : "");

            OnPropertyChanged(nameof(Enabled));
            OnPropertyChanged(nameof(ApiKeyText));
            OnPropertyChanged(nameof(EndpointText));
            OnPropertyChanged(nameof(ModelText));
            OnPropertyChanged(nameof(SelectedProvider));
            OnPropertyChanged(nameof(OperateRevit));
            OnPropertyChanged(nameof(OperateRevitText));
            OnPropertyChanged(nameof(ToolCountText));
            OnPropertyChanged(nameof(Summary));
            Note = note;
            Status = AiCommandBus.IsReady
                ? "命令集已就绪:" + AiCommandBus.HostDescription
                : "命令集尚未加载(Revit 完全初始化后自动加载);现在可以聊天与查知识库。";
        }

        /// <summary>保存设置(API key 按工作区加密;不发请求)。</summary>
        public void Save()
        {
            try
            {
                _settings.ApiKey = _apiKeyText ?? "";
                _settings.Endpoint = _endpointText ?? "";
                _settings.Model = _modelText ?? "";
                string scopeId = ScopeProvider == null ? "" : _scope.Id;
                string path = AiSettingsStore.Save(_settings, _settingsPath, scopeId);
                Status = "AI 助手设置已保存(" + path + ");" + ApiKeyVault.LastNote;
                RaiseSummary();
            }
            catch (Exception ex)
            {
                Status = "保存 AI 助手设置失败:" + ex.Message;
            }
        }

        /// <summary>清空聊天记录(不删设置、不删密钥)。</summary>
        public void Clear()
        {
            Lines.Clear();
            ToolLog.Clear();
            OnPropertyChanged(nameof(HasLines));
            OnPropertyChanged(nameof(HasToolLog));
            Note = "";
            LastError = "";
            Status = "已清空聊天记录(设置与密钥保留)。";
        }

        /// <summary>在浏览器打开当前服务的密钥申请页。</summary>
        public void OpenKeyPage()
        {
            string url = KeyPageUrl;
            if (string.IsNullOrEmpty(url))
            {
                Status = "当前是自定义服务,没有固定的密钥申请页 —— 请按服务方文档申请,并把接口地址填在「接口地址」里。";
                return;
            }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                Status = "已在浏览器打开密钥申请页:" + url;
            }
            catch (Exception ex)
            {
                Status = "打开浏览器失败:" + ex.Message + "(可手动访问 " + url + ")";
            }
        }

        /// <summary>
        /// **发一条消息**:本地检索依据 → (可选)带工具清单请求模型 → 流式接收 → 执行工具 → 循环 → 显示草稿。
        /// 全程在后台线程发请求,只有界面更新回到 UI 线程。
        /// </summary>
        private async void SendAsync()
        {
            if (_busy) return;
            string query = (_question ?? "").Trim();
            if (query.Length == 0)
            {
                Status = "请先输入问题。";
                return;
            }
            if (!_settings.Enabled)
            {
                Status = "AI 助手未启用 —— 在下面勾选「启用 AI 助手」并保存(或直接用「AI问答 → 规范知识库」查本地库)。";
                return;
            }
            if (string.IsNullOrWhiteSpace(_apiKeyText))
            {
                Status = "还没有填 API key —— 点【申请 API key】拿到后填进下面并保存。";
                return;
            }

            _settings.ApiKey = _apiKeyText;
            _settings.Endpoint = _endpointText;
            _settings.Model = _modelText;
            var settings = _settings.Clone();
            settings.Enabled = true;

            LastError = "";
            AddLine(new AiChatLine(true, query));
            var answer = new AiChatLine(false, "");
            answer.Streaming = true;
            AddLine(answer);
            Question = "";

            Busy = true;
            Status = "正在请求 " + DeepSeekClient.ResolveModel(settings) + "…";
            Note = AiCommandBus.OperateRevitEnabled
                ? AiChatClient.PrivacyNoteWithTools
                : "本次发送:你的问题 + 插件检索到的依据文本;模型看不到工具,不会执行任何命令。";

            try
            {
                string context = ContextSnapshotProvider == null ? "" : (ContextSnapshotProvider() ?? "");
                var result = await System.Threading.Tasks.Task.Run(() => AiChatClient.Send(
                    query, settings, null, context, true,
                    piece => OnUi(() => answer.Append(piece)),
                    text => OnUi(() => Status = text),
                    call => OnUi(() =>
                    {
                        ToolLog.Add(new AiToolLogRow(call.Name,
                            call.ResultJson != null && call.ResultJson.StartsWith("{\"error", StringComparison.Ordinal) ? "失败" : "完成",
                            call.ElapsedMs));
                        OnPropertyChanged(nameof(HasToolLog));
                    }),
                    null));

                answer.Streaming = false;
                if (result.Success)
                {
                    if (string.IsNullOrEmpty(answer.Text)) answer.Text = result.Content ?? "";
                    Status = "已生成草稿(" + result.Model + "," + result.ElapsedMs + " ms;" + result.UsageText +
                             ";命令 " + result.ToolCallCount + " 条)—— **请对着数据与依据核对后再用**。";
                    Note = result.Note;
                    if (result.HitRoundLimit)
                    {
                        LastError = "已达到工具调用轮次上限(" + AiChatClient.MaxToolRounds + " 轮),回答可能不完整。";
                    }
                }
                else
                {
                    answer.Text = "(没有回答)";
                    LastError = result.ErrorMessage;
                    Status = "本次没有模型回答:" + result.ErrorMessage;
                    Note = result.Note;
                }
            }
            catch (Exception ex)
            {
                answer.Streaming = false;
                if (string.IsNullOrEmpty(answer.Text)) answer.Text = "(发送失败)";
                LastError = ex.Message;
                Status = "发送失败:" + ex.Message;
            }
            finally
            {
                Busy = false;
                OnPropertyChanged(nameof(ToolCountText));
            }
        }

        private void AddLine(AiChatLine line)
        {
            Lines.Add(line);
            OnPropertyChanged(nameof(HasLines));
        }

        private void OnUi(Action action)
        {
            try
            {
                if (_dispatcher == null || _dispatcher.CheckAccess()) action();
                else _dispatcher.BeginInvoke(action);
            }
            catch
            {
                // 界面已关闭时忽略回调:不影响请求本身的结论
            }
        }

        private void RaiseSummary()
        {
            OnPropertyChanged(nameof(Summary));
        }
    }
}
