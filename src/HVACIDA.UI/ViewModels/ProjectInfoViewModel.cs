using System.Windows.Input;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.UI.ViewModels
{
    /// <summary>项目信息窗 ViewModel(需求文档 2.1:工程基本信息 + 气象参数)。</summary>
    public class ProjectInfoViewModel : ViewModelBase
    {
        private readonly IDataRepository _repository;
        private string _status = "";

        public ProjectInfoViewModel()
            : this(null)
        {
        }

        public ProjectInfoViewModel(IDataRepository repository)
        {
            _repository = repository ?? new XmlProjectRepository();
            Model = _repository.LoadProject();
            StageOptions = new[] { "初步设计", "施工图设计" };
            SaveCommand = new RelayCommand(Save);
        }

        /// <summary>工程数据根(绑定路径 Model.Basic.* / Model.Design.*)。</summary>
        public ProjectInfoModel Model { get; }

        /// <summary>设计阶段选项。</summary>
        public string[] StageOptions { get; }

        /// <summary>保存命令。</summary>
        public ICommand SaveCommand { get; }

        /// <summary>保存结果提示。</summary>
        public string Status
        {
            get => _status;
            private set => Set(ref _status, value);
        }

        private void Save()
        {
            try
            {
                _repository.SaveProject(Model);
                Status = "已保存到: " + _repository.StorageDirectory;
            }
            catch (System.Exception ex)
            {
                Status = "保存失败: " + ex.Message;
            }
        }
    }
}
