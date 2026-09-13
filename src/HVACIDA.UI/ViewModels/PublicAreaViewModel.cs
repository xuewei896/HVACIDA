using System.Windows.Input;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.UI.ViewModels
{
    /// <summary>
    /// 公共区参数窗 ViewModel(Ribbon「大系统 → 公共区参数」)。
    /// 只编辑大系统输入里的**公共区几何**与**高峰客流**两节,并通过同一 IDataRepository
    /// 与「负荷计算」窗共享数据(保存后负荷计算窗读到的是同一份)。
    /// </summary>
    public class PublicAreaViewModel : ViewModelBase
    {
        private readonly IDataRepository _repository;
        private LargeSystemInput _input;
        private string _status = "";

        public PublicAreaViewModel()
            : this(null)
        {
        }

        public PublicAreaViewModel(IDataRepository repository)
        {
            _repository = repository ?? new XmlProjectRepository();
            _input = _repository.LoadLargeSystem();
            SaveCommand = new RelayCommand(Save);
            ResetCommand = new RelayCommand(Reset);
        }

        /// <summary>大系统输入(绑定路径 Input.HallAreaM2 等)。</summary>
        public LargeSystemInput Input
        {
            get => _input;
            private set => Set(ref _input, value);
        }

        /// <summary>保存到仓库(负荷计算窗随后读取同一份)。</summary>
        public ICommand SaveCommand { get; }

        /// <summary>只恢复公共区几何与客流两节的默认值(其余各节不动)。</summary>
        public ICommand ResetCommand { get; }

        /// <summary>状态提示。</summary>
        public string Status
        {
            get => _status;
            private set => Set(ref _status, value);
        }

        private void Save()
        {
            try
            {
                _repository.SaveLargeSystem(Input);
                Status = "公共区参数已保存: " + _repository.StorageDirectory + "\\large-system.xml";
            }
            catch (System.Exception ex)
            {
                Status = "保存失败: " + ex.Message;
            }
        }

        private void Reset()
        {
            var d = new LargeSystemInput();

            // 二、车站几何
            Input.HallAreaM2 = d.HallAreaM2;
            Input.PlatformAreaM2 = d.PlatformAreaM2;
            Input.HallHeightM = d.HallHeightM;
            Input.HallLengthM = d.HallLengthM;

            // 三、高峰客流
            Input.UpLineBoardCount = d.UpLineBoardCount;
            Input.DownLineBoardCount = d.DownLineBoardCount;
            Input.UpLineAlightCount = d.UpLineAlightCount;
            Input.DownLineAlightCount = d.DownLineAlightCount;
            Input.TransferBoardCount = d.TransferBoardCount;
            Input.TransferAlightCount = d.TransferAlightCount;
            Input.HallBoardStayMin = d.HallBoardStayMin;
            Input.HallAlightStayMin = d.HallAlightStayMin;
            Input.HallTransferBoardStayMin = d.HallTransferBoardStayMin;
            Input.HallTransferAlightStayMin = d.HallTransferAlightStayMin;
            Input.PlatformBoardStayMin = d.PlatformBoardStayMin;
            Input.PlatformAlightStayMin = d.PlatformAlightStayMin;
            Input.PlatformTransferBoardStayMin = d.PlatformTransferBoardStayMin;
            Input.PlatformTransferAlightStayMin = d.PlatformTransferAlightStayMin;
            Input.ClusterFactor = d.ClusterFactor;
            Input.SuperPeakHourFactor = d.SuperPeakHourFactor;

            OnPropertyChanged(nameof(Input));
            Status = "已恢复公共区几何与客流默认值(其余各节不变),请点【保 存】。";
        }
    }
}
