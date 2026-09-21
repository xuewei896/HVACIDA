using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.UI.ViewModels
{
    /// <summary>
    /// 项目信息 ViewModel(需求文档 2.1)。
    /// 2026-09-11 Ribbon 拆分后:「工程信息」窗与「气象参数」窗**共用本 ViewModel**(同一份
    /// project.xml),只是各自展示不同分节,避免两处数据分叉。
    /// <para>
    /// 2026-09-15:项目地点的**省/市改为下拉**,数据源 <see cref="WeatherDatabase"/>
    /// (内嵌 GB 50736-2012 附录A,294 个台站);**选定城市即自动把该市室外气象参数写进项目**。
    /// </para>
    /// </summary>
    public class ProjectInfoViewModel : ViewModelBase
    {
        private readonly IDataRepository _repository;
        private readonly WeatherDatabase _weather;
        private string _status = "";
        private string _weatherStatus = "";
        private string _stationInfo = "";
        private string _weatherSummary = "";
        private string _selectedProvince = "";
        private string _selectedCity = "";
        private IList<string> _cities = new List<string>();
        private bool _loading;
        private bool _weatherFromDatabase;

        public ProjectInfoViewModel()
            : this(null)
        {
        }

        public ProjectInfoViewModel(IDataRepository repository)
            : this(repository, null)
        {
        }

        /// <param name="repository">数据仓库(为空则用 %AppData%\HVACIDA)。</param>
        /// <param name="weatherDatabase">气象库(为空则用内嵌默认库;自检可注入小样本)。</param>
        public ProjectInfoViewModel(IDataRepository repository, WeatherDatabase weatherDatabase)
        {
            _repository = repository ?? new XmlProjectRepository();
            _weather = weatherDatabase ?? WeatherDatabase.Default;
            Model = _repository.LoadProject();
            StageOptions = new[] { "初步设计", "施工图设计" };
            Provinces = _weather.Provinces.ToArray();

            SaveCommand = new RelayCommand(Save);
            FetchWeatherCommand = new RelayCommand(FetchWeather);
            ResetWeatherCommand = new RelayCommand(ResetWeather);

            // 回填已保存的省/市(不触发"选城市即回填",避免每次开窗都覆盖用户手改过的参数)
            _loading = true;
            SelectedProvince = Model.Basic.LocationProvince;
            SelectedCity = Model.Basic.LocationCity;
            _loading = false;

            RefreshStationInfo();
            if (IsKnownLocation)
            {
                WeatherStatus = "当前地点:" + Model.Basic.LocationProvince + " " + Model.Basic.LocationCity +
                                "。点【从气象数据库获取】可按当前地点回填室外气象参数。";
            }
        }

        /// <summary>工程数据根(绑定路径 Model.Basic.* / Model.Design.*)。</summary>
        public ProjectInfoModel Model { get; }

        /// <summary>设计阶段选项。</summary>
        public string[] StageOptions { get; }

        /// <summary>省级行政区下拉项(来自内嵌气象库,按标准顺序)。</summary>
        public string[] Provinces { get; }

        /// <summary>当前省下的城市下拉项。</summary>
        public IList<string> Cities
        {
            get => _cities;
            private set => Set(ref _cities, value);
        }

        /// <summary>选中的省;变化时刷新城市下拉(本身不改动气象参数)。</summary>
        public string SelectedProvince
        {
            get => _selectedProvince;
            set
            {
                if (!Set(ref _selectedProvince, value)) return;

                Model.Basic.LocationProvince = value ?? "";
                Cities = _weather.CitiesOf(value).ToList();

                // 换省后原城市不属于新省 → 清空,等用户重新选
                if (!string.IsNullOrEmpty(_selectedCity) && !Cities.Contains(_selectedCity))
                {
                    _selectedCity = "";
                    Model.Basic.LocationCity = "";
                    OnPropertyChanged(nameof(SelectedCity));
                }

                OnPropertyChanged(nameof(HasCities));
                OnPropertyChanged(nameof(SelectedCityHint));
                RefreshStationInfo();
                if (!_loading) Status = "已选择「" + value + "」,请继续选择城市(选定城市将自动回填气象参数)。";
            }
        }

        /// <summary>城市下拉是否有内容(绑定用)。</summary>
        public bool HasCities => Cities != null && Cities.Count > 0;

        /// <summary>未选省时给下拉的提示文案。</summary>
        public string SelectedCityHint => HasCities ? "" : "请先选择省 / 直辖市 / 自治区";

        /// <summary>
        /// 选中的城市。**选定即自动把该市室外气象参数写入项目**(需求:选择市后自动把该市气象参数输入到项目中)。
        /// 数据源 GB 50736-2012 附录A;标准未记录的项保持原值并给出告警,绝不猜值。
        /// </summary>
        public string SelectedCity
        {
            get => _selectedCity;
            set
            {
                if (!Set(ref _selectedCity, value)) return;

                Model.Basic.LocationCity = value ?? "";
                RefreshStationInfo();
                if (_loading || string.IsNullOrEmpty(value)) return;

                var result = ApplySelectedCityWeather();
                Status = result == null
                    ? "未在气象库中找到「" + value + "」。"
                    : result.Note + " 已写入本工程,点【保 存】落盘。";
                WeatherStatus = Status;
            }
        }

        /// <summary>当前台站信息行(台站名/编号/经纬度/海拔/统计年份)。</summary>
        public string StationInfo
        {
            get => _stationInfo;
            private set
            {
                if (Set(ref _stationInfo, value)) OnPropertyChanged(nameof(StationInfoVisibility));
            }
        }

        /// <summary>选定城市后将回填的气象参数摘要(界面核对用)。</summary>
        public string WeatherSummary
        {
            get => _weatherSummary;
            private set => Set(ref _weatherSummary, value);
        }

        /// <summary>台站信息框是否显示(未选到台站时折叠)。</summary>
        public System.Windows.Visibility StationInfoVisibility =>
            string.IsNullOrEmpty(_stationInfo)
                ? System.Windows.Visibility.Collapsed
                : System.Windows.Visibility.Visible;

        /// <summary>气象参数是否来自数据库(界面标注"来自 GB 50736-2012 附录A")。</summary>
        public bool WeatherFromDatabase
        {
            get => _weatherFromDatabase;
            private set => Set(ref _weatherFromDatabase, value);
        }

        /// <summary>保存命令(写入 %AppData%\HVACIDA\project.xml;正式版写 .rvt 全局参数)。</summary>
        public ICommand SaveCommand { get; }

        /// <summary>按当前选择的地点从气象数据库回填室外参数。</summary>
        public ICommand FetchWeatherCommand { get; }

        /// <summary>恢复气象参数出厂默认值(<see cref="DesignConditionParams"/> 构造值)。</summary>
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

        private bool IsKnownLocation =>
            !string.IsNullOrEmpty(_selectedProvince) && !string.IsNullOrEmpty(_selectedCity);

        private void RefreshStationInfo()
        {
            var station = _weather.Find(_selectedProvince, _selectedCity);
            if (station == null)
            {
                StationInfo = "";
                WeatherSummary = "";
                return;
            }

            StationInfo = station.StationInfoText() +
                          " · 数据源 GB 50736-2012 附录A(库内共 " + _weather.All.Count + " 个台站)";
            WeatherSummary =
                "夏季空调 干球/湿球 " + T(station.SummerAcDryBulbC) + " / " + T(station.SummerAcWetBulbC) + " ℃" +
                " · 夏季通风 " + T(station.SummerVentDryBulbC) + " ℃" +
                " · 冬季空调 " + T(station.WinterAcOutdoorC) + " ℃" +
                " · 冬季通风 " + T(station.WinterVentOutdoorC) + " ℃" +
                " · 夏季大气压力 " + P(station.SummerAtmPressureHpa) + " kPa" +
                " · 夏季通风相对湿度 " + T(station.SummerVentRhPct) + " %";
        }

        private static string T(double? v)
        {
            return v.HasValue ? v.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "—";
        }

        private static string P(double? hpa)
        {
            return hpa.HasValue
                ? (hpa.Value / 10.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                : "—";
        }

        /// <summary>把当前选中城市的气象参数写进 <see cref="ProjectInfoModel.Design"/>。</summary>
        private WeatherApplyResult ApplySelectedCityWeather()
        {
            var station = _weather.Find(_selectedProvince, _selectedCity);
            if (station == null)
            {
                WeatherFromDatabase = false;
                return null;
            }

            var result = WeatherDatabase.Apply(station, Model.Design);
            WeatherFromDatabase = true;
            OnPropertyChanged(nameof(Model));      // Core 模型是纯数据类,整体通知一次刷新所有字段
            return result;
        }

        /// <summary>
        /// 保存到仓库;返回是否成功。窗口【确 定】按"保存并关闭"实现,
        /// 保存失败时必须<strong>不关窗</strong>,否则用户看不到失败原因。
        /// </summary>
        public bool TrySave()
        {
            try
            {
                _repository.SaveProject(Model);
                Status = "已保存到: " + _repository.StorageDirectory;
                WeatherStatus = "已保存到: " + _repository.StorageDirectory;
                return true;
            }
            catch (Exception ex)
            {
                Status = "保存失败: " + ex.Message;
                WeatherStatus = "保存失败: " + ex.Message;
                return false;
            }
        }

        private void Save()
        {
            TrySave();
        }

        /// <summary>按当前地点从气象数据库回填(未选地点时提示去哪里选)。</summary>
        private void FetchWeather()
        {
            if (!IsKnownLocation)
            {
                WeatherStatus = "请先在 Ribbon「项目信息 → 工程信息」里选择项目地点(省 / 市),再回来取参数。";
                return;
            }

            var result = ApplySelectedCityWeather();
            if (result == null)
            {
                WeatherStatus = "未在气象库中找到「" + _selectedProvince + " " + _selectedCity + "」。";
                return;
            }

            WeatherStatus = result.Note + " 已回填:" + result.FilledText + "。请核对后点【保 存】。";
        }

        private void ResetWeather()
        {
            Model.Design = new DesignConditionParams();
            WeatherFromDatabase = false;
            OnPropertyChanged(nameof(Model));
            WeatherStatus = "已恢复出厂默认值(未使用气象库);请按项目所在地核对,或回「工程信息」重选城市。";
        }
    }
}
