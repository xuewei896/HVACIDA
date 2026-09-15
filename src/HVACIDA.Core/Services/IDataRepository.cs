using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// 项目数据仓库(需求文档 4.2 数据接口)。
    /// 当前以 XML 落盘实现;后续按同一接口换 SQLite(需求:数据库 SQLite)。
    /// </summary>
    public interface IDataRepository
    {
        /// <summary>数据根目录(默认 %AppData%\HVACIDA)。</summary>
        string StorageDirectory { get; }

        /// <summary>读取工程信息(不存在时返回默认实例)。</summary>
        ProjectInfoModel LoadProject();

        /// <summary>保存工程信息。</summary>
        void SaveProject(ProjectInfoModel project);

        /// <summary>
        /// 读取大系统输入(Ribbon「大系统 → 公共区参数 / 负荷计算」共用同一份数据)。
        /// 不存在时返回默认实例。
        /// </summary>
        LargeSystemInput LoadLargeSystem();

        /// <summary>保存大系统输入(公共区参数窗与负荷计算窗都写这里)。</summary>
        void SaveLargeSystem(LargeSystemInput input);

        /// <summary>
        /// 读取大系统排烟计算参数(需求 2.2.3.1;"排烟计算"窗与"计算结果"窗共用)。
        /// 不含公共区面积 —— 面积取自 <see cref="LargeSystemInput"/>(D55/D56)。
        /// </summary>
        LargeSmokeInput LoadLargeSmoke();

        /// <summary>保存大系统排烟计算参数。</summary>
        void SaveLargeSmoke(LargeSmokeInput input);

        /// <summary>读取小系统输入(不存在时返回默认实例)。</summary>
        SmallSystemInput LoadSmallSystem();

        /// <summary>保存小系统输入。</summary>
        void SaveSmallSystem(SmallSystemInput input);
    }
}
