using System;
using System.Collections.Generic;
using System.Windows.Input;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.UI.ViewModels
{
    /// <summary>
    /// 公共区参数窗 ViewModel(Ribbon「大系统 → 公共区参数」)。
    /// <list type="bullet">
    ///   <item>编辑大系统输入里的<strong>公共区几何</strong>(D55/D56/C13/C14)与<strong>高峰客流</strong>两节,
    ///         通过同一 IDataRepository 与「负荷计算」窗共享数据;</item>
    ///   <item>几何可由 Revit 空间自动取值:模型空间由命令层(<c>ShowPublicAreaCommand</c>)预先读好并注入,
    ///         本 ViewModel 只做纯 Core 的分类/聚合,不引用 Revit API;</item>
    ///   <item>「拾取站厅/站台空间」需要 Revit 选择交互,按 <see cref="PendingPickTarget"/> 交回命令层执行
    ///         (窗口关闭 → 命令层拾取 → 用同一 ViewModel 重新开窗),原因见
    ///         <see cref="RequestPick"/> 注释。</item>
    /// </list>
    /// </summary>
    public class PublicAreaViewModel : ViewModelBase
    {
        private readonly LargeSystemInputService _service;
        private readonly IList<SpaceSnapshot> _modelSpaces;
        private readonly bool _pickAvailable;

        private readonly List<SpaceSnapshot> _pickedHall = new List<SpaceSnapshot>();
        private readonly List<SpaceSnapshot> _pickedPlatform = new List<SpaceSnapshot>();
        private readonly HashSet<int> _manuallyClaimed = new HashSet<int>();

        private LargeSystemInput _input;
        private string _status = "";
        private string _modelNote = "";
        private IList<SpaceRow> _spaceRows = new List<SpaceRow>();
        private bool _geometryFromModel;
        private bool _manualGeometry;
        private PublicAreaTarget? _pendingPickTarget;

        public PublicAreaViewModel()
            : this(null, null, null, false)
        {
        }

        public PublicAreaViewModel(IDataRepository repository)
            : this(repository, null, null, false)
        {
        }

        /// <param name="repository">数据仓库(为空则用 %AppData%\HVACIDA)。</param>
        /// <param name="modelSpaces">命令层从 Revit 模型读到的空间快照(可为空 = 无模型)。</param>
        /// <param name="modelNote">模型读取情况说明(如"共 42 个空间")。</param>
        /// <param name="pickAvailable">是否存在可拾取的活动文档(仅 Revit 内为 true)。</param>
        public PublicAreaViewModel(IDataRepository repository, IList<SpaceSnapshot> modelSpaces, string modelNote, bool pickAvailable)
        {
            _service = new LargeSystemInputService(repository);
            _modelSpaces = modelSpaces ?? new List<SpaceSnapshot>();
            _pickAvailable = pickAvailable;
            _input = _service.Load();

            SaveCommand = new RelayCommand(Save);
            ResetCommand = new RelayCommand(Reset);
            AutoDetectCommand = new RelayCommand(AutoDetect, () => ModelDataAvailable);

            _modelNote = modelNote ?? "";
            RebuildRows();
            if (string.IsNullOrEmpty(_modelNote)) ModelNote = BuildModelNote();
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

        /// <summary>按模型空间名称自动识别站厅/站台并填入(D55/D56/C13/C14)。</summary>
        public ICommand AutoDetectCommand { get; }

        /// <summary>模型里是否有可用的已放置空间(决定【自动识别】是否可用)。</summary>
        public bool ModelDataAvailable => _modelSpaces.Count > 0;

        /// <summary>是否有可拾取的活动文档(决定两个【拾取…】是否可用)。</summary>
        public bool PickAvailable => _pickAvailable;

        /// <summary>几何是否已由模型取值(取值后由只读底色转为可手改,见 UI设计规范 §2.5)。</summary>
        public bool GeometryFromModel => _geometryFromModel;

        /// <summary>用户是否显式选择了手工输入几何。</summary>
        public bool ManualGeometry
        {
            get => _manualGeometry;
            set
            {
                if (!Set(ref _manualGeometry, value)) return;
                OnPropertyChanged(nameof(IsGeometryLocked));
                Status = value
                    ? "已切换为手工输入几何;点【自动识别】或【拾取…】可随时改回模型取值。"
                    : "已回到模型取值模式:请用【自动识别】或【拾取…】填入几何。";
            }
        }

        /// <summary>几何字段是否锁定为只读(模型可用、尚未取到值、且未选手工输入)。</summary>
        public bool IsGeometryLocked => _pickAvailable && !_geometryFromModel && !_manualGeometry;

        /// <summary>模型读取情况说明。</summary>
        public string ModelNote
        {
            get => _modelNote;
            private set => Set(ref _modelNote, value);
        }

        /// <summary>模型取值明细(站厅/站台/未分类)。</summary>
        public IList<SpaceRow> SpaceRows
        {
            get => _spaceRows;
            private set => Set(ref _spaceRows, value);
        }

        /// <summary>状态提示。</summary>
        public string Status
        {
            get => _status;
            private set => Set(ref _status, value);
        }

        /// <summary>
        /// 待执行的拾取目标(窗口关闭后由命令层读取)。
        /// <para>
        /// 为什么不在这里直接调 Revit 拾取:WPF <c>ShowDialog()</c> 会在 Win32 层禁用 Owner(Revit 主窗),
        /// 模态期间模型根本点不动,<c>Hide()</c> 也不会恢复 Owner —— 必须让模态循环真正结束(<c>Close()</c>),
        /// 由 <c>IExternalCommand.Execute</c> 在无模态窗的环境里调 <c>Selection.PickObjects</c>,
        /// 再用同一个 ViewModel 重新开窗(用户已填内容不丢)。
        /// </para>
        /// </summary>
        public PublicAreaTarget? PendingPickTarget => _pendingPickTarget;

        /// <summary>记录拾取目标(窗口随后关闭,由命令层执行拾取)。</summary>
        public void RequestPick(PublicAreaTarget target)
        {
            _pendingPickTarget = target;
        }

        /// <summary>命令层拾取完成后清空标记。</summary>
        public void ClearPendingPick()
        {
            _pendingPickTarget = null;
        }

        /// <summary>由命令层写入状态提示(拾取取消/失败等窗口外发生的情况)。</summary>
        public void SetStatus(string text)
        {
            Status = text ?? "";
        }

        /// <summary>
        /// 命令层拾取到的空间回填(目标已知,不做名称推断)。
        /// </summary>
        public void ApplyPick(PublicAreaTarget target, IList<SpaceSnapshot> spaces, string note)
        {
            if (spaces == null || spaces.Count == 0)
            {
                Status = "已取消拾取(未选择任何空间),几何保持原值。";
                return;
            }

            var aggregate = PublicAreaAggregator.Aggregate(spaces, target);
            if (aggregate.SpaceCount == 0)
            {
                Status = "所选空间均未放置(面积为 0),几何保持原值;请检查模型中的空间是否已放置。";
                return;
            }

            var picked = aggregate.Spaces;
            var claim = target == PublicAreaTarget.Hall ? _pickedHall : _pickedPlatform;
            claim.Clear();
            claim.AddRange(picked);
            foreach (var s in picked) _manuallyClaimed.Add(s.ElementId);

            ApplyAggregate(aggregate);
            RebuildRows();
            ModelNote = BuildModelNote();

            Status = "已拾取并填入 " + aggregate.Summary +
                     (string.IsNullOrEmpty(note) ? "" : "(" + note + ")") +
                     " 核对后点【确 定】保存。";
        }

        // ------------------------------------------------------------------ 内部

        /// <summary>按名称自动识别全模型空间并填入两侧几何。</summary>
        private void AutoDetect()
        {
            if (!ModelDataAvailable)
            {
                Status = "当前文档没有可用的空间(Space),无法自动识别;请改用【拾取…】或手工输入。";
                return;
            }

            var classification = PublicAreaAggregator.Classify(_modelSpaces);
            if (classification.Hall.Count == 0 && classification.Platform.Count == 0)
            {
                ModelNote = classification.Summary + " " + _modelSpaces.Count +
                            " 个空间名称里都没有「站厅/站台」关键词 —— 请用【拾取站厅空间…】手工指定,或勾选【手工输入几何参数】。";
                Status = "未能按名称识别出站厅/站台空间,几何保持原值。";
                return;
            }

            var notes = new List<string>();
            if (classification.Hall.Count > 0)
            {
                var hall = PublicAreaAggregator.Aggregate(classification.Hall, PublicAreaTarget.Hall);
                ApplyAggregate(hall);
                notes.Add(hall.Summary);
            }

            if (classification.Platform.Count > 0)
            {
                var platform = PublicAreaAggregator.Aggregate(classification.Platform, PublicAreaTarget.Platform);
                ApplyAggregate(platform);
                notes.Add(platform.Summary);
            }

            RebuildRows();
            ModelNote = BuildModelNote();
            Status = "已按模型空间自动识别:" + string.Join(" ", notes.ToArray()) +
                     (classification.Unclassified.Count > 0
                         ? " 有 " + classification.Unclassified.Count + " 个空间未识别,未计入合计(见明细)。"
                         : "") +
                     " 核对后点【确 定】保存。";
        }

        /// <summary>把聚合结果写进输入(D55/D56/C13/C14);长度为站厅专用(C14)。</summary>
        private void ApplyAggregate(SpaceAggregate aggregate)
        {
            if (aggregate.Target == PublicAreaTarget.Hall)
            {
                if (aggregate.AreaM2 > 0) Input.HallAreaM2 = Round(aggregate.AreaM2);
                if (aggregate.HeightM > 0) Input.HallHeightM = Round(aggregate.HeightM);
                if (aggregate.LengthM > 0) Input.HallLengthM = Round(aggregate.LengthM);
            }
            else
            {
                if (aggregate.AreaM2 > 0) Input.PlatformAreaM2 = Round(aggregate.AreaM2);
            }

            _geometryFromModel = true;
            OnPropertyChanged(nameof(Input));
            OnPropertyChanged(nameof(GeometryFromModel));
            OnPropertyChanged(nameof(IsGeometryLocked));
        }

        /// <summary>重建明细列表:手工拾取优先于自动识别;未分类的排除已手工认领的元素。</summary>
        private void RebuildRows()
        {
            var classification = PublicAreaAggregator.Classify(_modelSpaces);
            var rows = new List<SpaceRow>();

            var hall = _pickedHall.Count > 0 ? _pickedHall : classification.Hall;
            var platform = _pickedPlatform.Count > 0 ? _pickedPlatform : classification.Platform;
            string hallCategory = _pickedHall.Count > 0 ? "站厅(手动拾取)" : "站厅(自动识别)";
            string platformCategory = _pickedPlatform.Count > 0 ? "站台(手动拾取)" : "站台(自动识别)";

            foreach (var s in hall) rows.Add(SpaceRow.From(s, hallCategory));
            foreach (var s in platform) rows.Add(SpaceRow.From(s, platformCategory));

            // 被"公共区"口径筛掉的同侧空间必须列出来,否则"合计是不是漏了付费区"无法核对
            foreach (var s in classification.HallExcluded) rows.Add(SpaceRow.From(s, "站厅·非公共区(不计入)"));
            foreach (var s in classification.PlatformExcluded) rows.Add(SpaceRow.From(s, "站台·非公共区(不计入)"));

            foreach (var s in classification.Unclassified)
            {
                if (_manuallyClaimed.Contains(s.ElementId)) continue;
                rows.Add(SpaceRow.From(s, "未识别(不计入)"));
            }

            SpaceRows = rows;
        }

        /// <summary>模型概况文案(未由命令层给出时按分类结果兜底)。</summary>
        private string BuildModelNote()
        {
            if (_modelSpaces.Count == 0)
            {
                return _pickAvailable
                    ? "当前文档中没有空间(Space):请在建筑模型或链接模型中放置空间,或勾选【手工输入几何参数】直接填写。"
                    : "未在 Revit 模型中打开文档,本次无法从模型取值。";
            }

            var classification = PublicAreaAggregator.Classify(_modelSpaces);
            return classification.Summary + " 层高取空间「体积/面积」的有效高度,长度按所选空间水平包围盒长边估算,均可手工覆盖。";
        }

        private static double Round(double v)
        {
            return Math.Round(v, 2);
        }

        private void Save()
        {
            TrySave();
        }

        /// <summary>
        /// 保存到仓库;返回是否成功。窗口的【确 定】按"保存并关闭"实现(UI设计规范 §4.2 要点),
        /// 保存失败时必须<strong>不关窗</strong>,否则用户看不到失败原因。
        /// </summary>
        public bool TrySave()
        {
            try
            {
                _service.Save(Input);
                Status = "公共区参数已保存: " + _service.StorageDirectory +
                         "\\large-system.xml(「负荷计算」「计算结果」窗均按此计算)";
                return true;
            }
            catch (Exception ex)
            {
                Status = "保存失败: " + ex.Message;
                return false;
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

            _pickedHall.Clear();
            _pickedPlatform.Clear();
            _manuallyClaimed.Clear();
            _geometryFromModel = false;
            _manualGeometry = false;

            OnPropertyChanged(nameof(Input));
            OnPropertyChanged(nameof(GeometryFromModel));
            OnPropertyChanged(nameof(ManualGeometry));
            OnPropertyChanged(nameof(IsGeometryLocked));
            RebuildRows();

            Status = "已恢复公共区几何与客流默认值(其余各节不变),请点【确 定】。";
        }
    }
}
