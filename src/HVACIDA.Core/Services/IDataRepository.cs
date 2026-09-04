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
    }
}
