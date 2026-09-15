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

        /// <summary>
        /// 读取小系统工程(**多系统**;需求 2.2.3.2:全站多套小系统)。
        /// 若只有旧版单系统文件 small-system.xml,会自动迁移为本容器(见实现)。
        /// </summary>
        SmallSystemProject LoadSmallSystems();

        /// <summary>保存小系统工程(全部小系统)。</summary>
        void SaveSmallSystems(SmallSystemProject project);

        /// <summary>
        /// 读取小系统输入(单系统;**兼容入口**,语义 = 取容器里<strong>第一套</strong>系统,不是「刚保存的那套」)。
        /// 新代码请用 <see cref="LoadSmallSystems"/> + <see cref="SmallSystemProject.Find"/>。
        /// </summary>
        SmallSystemInput LoadSmallSystem();

        /// <summary>保存小系统输入(单系统;**兼容入口**,按"类型 + 编号"upsert 进小系统工程)。</summary>
        void SaveSmallSystem(SmallSystemInput input);

        /// <summary>
        /// 读取水力计算数据(需求 2.3 / 2.4):系数集 + 最近一次从模型读到的风系统 / 水系统输入。
        /// 结果不落盘 —— 「计算结果」窗按输入现算(与其它模块一致)。不存在时返回默认实例。
        /// </summary>
        HydraulicProject LoadHydraulic();

        /// <summary>保存水力计算数据(系数集与两次读取的管网输入)。</summary>
        void SaveHydraulic(HydraulicProject project);
    }
}
