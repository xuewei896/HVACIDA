using System;
using System.Windows.Input;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.UI.ViewModels
{
    /// <summary>
    /// 项目信息 ViewModel(需求文档 2.1)。
    /// 2026-09-11 Ribbon 拆分后:「工程信息」窗与「气象参数」窗**共用本 ViewModel**(同一份
    /// project.xml),只是各自展示不同分节,避免两处数据分叉。
    /// </summary>
    public class ProjectInfoViewModel : ViewModelBase
    {
        private readonly IDataRepository _repository;
        private string _status = "";
        private string _weatherStatus = "";

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
            FetchWeatherCommand = new RelayCommand(FetchWeather);
            ResetWeatherCommand = new RelayCommand(ResetWeather);
        }

        /// <summary>工程数据根(绑定路径 Model.Basic.* / Model.Design.*)。</summary>
        public ProjectInfoModel Model { get; }

        /// <summary>设计阶段选项。</summary>
        public string[] StageOptions { get; }

        /// <summary>保存命令(写入 %AppData%\HVACIDA\project.xml;正式版写 .rvt 全局参数)。</summary>
        public ICommand SaveCommand { get; }

        /// <summary>从气象数据库获取典型计算参数(正式版接气象数据库,当前为内置典型值)。</summary>
        public ICommand FetchWeatherCommand { get; }

        /// <summary>恢复气象参数默认(内置典型值)。</summary>
        public ICommand ResetWeatherCommand { get; }

        /// <summary>存储目录(界面提示用)。</summary>
        public string StorageDirectory => _repository.StorageDirectory;

        /// <summary>工程信息窗保存结果提示。</summary>
        public string Status
        {
            get => _status;
            private set => Set(ref _status, value);
        }

        /// <summary>气象参数窗状态提示。</summary>
        public string WeatherStatus
        {
            get => _weatherStatus;
            private set => Set(ref _weatherStatus, value);
        }

        private void Save()
        {
            try
            {
                _repository.SaveProject(Model);
                Status = "已保存到: " + _repository.StorageDirectory;
                WeatherStatus = "已保存到: " + _repository.StorageDirectory;
            }
            catch (Exception ex)
            {
                Status = "保存失败: " + ex.Message;
                WeatherStatus = "保存失败: " + ex.Message;
            }
        }

        /// <summary>
        /// 取用气象数据库典型值(北京地区,与需求文档 2.1.2 示例一致)。
        /// TODO(气象库):接入气象数据库查询接口后改为按项目地点检索。
        /// </summary>
        private void FetchWeather()
        {
            ApplyTypicalWeather();
            WeatherStatus = "已从气象数据库获取典型计算参数,请核对后点【保 存】。";
        }

        private void ResetWeather()
        {
            ApplyTypicalWeather();
            WeatherStatus = "已恢复内置典型计算参数。";
        }

        private void ApplyTypicalWeather()
        {
            // 整体替换 Design 实例并通知一次,保证界面所有气象字段同步刷新
            // (Core 模型为纯数据类,不实现 INotifyPropertyChanged)。
            var d = new DesignConditionParams();

            d.LargeSystemOutdoor.SummerACDryBulbC = 31.0;
            d.LargeSystemOutdoor.SummerACWetBulbC = 25.0;
            d.LargeSystemOutdoor.SummerVentDryBulbC = 26.4;
            d.LargeSystemOutdoor.WinterVentDryBulbC = -3.6;
            d.LargeSystemOutdoor.WinterACDryBulbC = -7.6;

            d.SmallSystemOutdoor.SummerACDryBulbC = 31.0;
            d.SmallSystemOutdoor.SummerACWetBulbC = 25.0;
            d.SmallSystemOutdoor.SummerVentDryBulbC = 26.4;

            d.Common.AtmosphericPressureKPa = 102.17;
            d.Common.OutdoorRelativeHumidityPercent = 60.0;

            d.LargeSystemIndoor.HallDryBulbC = 29.0;
            d.LargeSystemIndoor.HallRelativeHumidityPercent = 60.0;
            d.LargeSystemIndoor.PlatformDryBulbC = 27.0;
            d.LargeSystemIndoor.PlatformRelativeHumidityPercent = 60.0;

            d.SmallSystemIndoor.ManagementRoomDryBulbC = 26.0;
            d.SmallSystemIndoor.WeakCurrentRoomDryBulbC = 26.0;
            d.SmallSystemIndoor.StrongCurrentRoomDryBulbC = 28.0;
            d.SmallSystemIndoor.IndoorWetBulbC = 20.0;
            d.SmallSystemIndoor.IndoorRelativeHumidityPercent = 60.0;

            Model.Design = d;
            OnPropertyChanged(nameof(Model));
        }
    }
}
