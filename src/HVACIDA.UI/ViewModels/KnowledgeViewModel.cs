using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using HVACIDA.Core.Services;

namespace HVACIDA.UI.ViewModels
{
    /// <summary>对话一行。</summary>
    public sealed class ChatLine
    {
        public ChatLine(bool isUser, string text)
        {
            IsUser = isUser;
            Text = text;
        }

        public bool IsUser { get; }

        public string Text { get; }
    }

    /// <summary>
    /// 规范知识库问答 ViewModel(Ribbon「AI问答 → 规范知识库」)。
    /// 当前为本地规则应答(<see cref="DesignQaService"/>);正式版接 AI 服务与规范全文检索(需求 2.7 / 4.1)。
    /// </summary>
    public class KnowledgeViewModel : ViewModelBase
    {
        private readonly DesignQaService _qa = new DesignQaService();
        private string _question = "";
        private string _status = "";

        public KnowledgeViewModel()
        {
            Lines = new ObservableCollection<ChatLine>();
            AskCommand = new RelayCommand(Ask);
            ResetCommand = new RelayCommand(Reset);
            Lines.Add(new ChatLine(false,
                "你好,我是 HVACIDA 规范知识库(原型:本地规则应答)。" +
                "可以问我送风温差、排烟风量与选型、新风量、焓湿计算、客流口径、屏蔽门负荷、大小系统分工等问题。"));
        }

        /// <summary>对话内容。</summary>
        public ObservableCollection<ChatLine> Lines { get; }

        /// <summary>常用问题(界面按钮)。</summary>
        public IList<string> SampleQuestions => _qa.SampleQuestions;

        /// <summary>输入的问题。</summary>
        public string Question
        {
            get => _question;
            set => Set(ref _question, value);
        }

        public string Status
        {
            get => _status;
            private set => Set(ref _status, value);
        }

        public ICommand AskCommand { get; }

        public ICommand ResetCommand { get; }

        /// <summary>提问(供界面按钮或回车调用)。</summary>
        public void Ask()
        {
            string q = (Question ?? "").Trim();
            if (q.Length == 0)
            {
                Status = "请输入问题,或点上面的常用问题。";
                return;
            }

            Lines.Add(new ChatLine(true, q));
            Lines.Add(new ChatLine(false, _qa.Answer(q)));
            Question = "";
            Status = "已应答(本地规则库);未覆盖的问题会给出知识范围说明。";
        }

        private void Reset()
        {
            Lines.Clear();
            Lines.Add(new ChatLine(false, "已清空对话。可以继续提问,或点上面的常用问题。"));
            Status = "";
        }
    }
}
