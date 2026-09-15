using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>
    /// XML 文件仓库实现(骨架期落盘方案)。
    /// 文件:project.xml(工程信息)/ large-system.xml(大系统输入)/ large-smoke.xml(排烟计算参数)/ small-systems.xml(小系统工程,多系统)。
    /// TODO(存储):后续切换 SQLite(System.Data.SQLite 或 Microsoft.Data.Sqlite),保留本实现用于迁移/测试。
    /// </summary>
    public class XmlProjectRepository : IDataRepository
    {
        public XmlProjectRepository()
            : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HVACIDA"))
        {
        }

        public XmlProjectRepository(string storageDirectory)
        {
            StorageDirectory = storageDirectory;
        }

        public string StorageDirectory { get; }

        private string ProjectFilePath => Path.Combine(StorageDirectory, "project.xml");

        private string LargeSystemFilePath => Path.Combine(StorageDirectory, "large-system.xml");

        private string LargeSmokeFilePath => Path.Combine(StorageDirectory, "large-smoke.xml");

        private string SmallSystemsFilePath => Path.Combine(StorageDirectory, "small-systems.xml");

        /// <summary>旧版单系统文件(仅用于自动迁移)。</summary>
        private string LegacySmallSystemFilePath => Path.Combine(StorageDirectory, "small-system.xml");

        public ProjectInfoModel LoadProject() => Load(ProjectFilePath, () => new ProjectInfoModel());

        public void SaveProject(ProjectInfoModel project)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            Save(ProjectFilePath, project);
        }

        public LargeSystemInput LoadLargeSystem() => Load(LargeSystemFilePath, () => new LargeSystemInput());

        public void SaveLargeSystem(LargeSystemInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            Save(LargeSystemFilePath, input);
        }

        public LargeSmokeInput LoadLargeSmoke() => Load(LargeSmokeFilePath, () => new LargeSmokeInput());

        public void SaveLargeSmoke(LargeSmokeInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            Save(LargeSmokeFilePath, input);
        }

        /// <summary>
        /// 读取小系统工程(多系统)。首次读取时若只有旧的单系统文件,则**自动迁移**:
        /// 读入那一套系统写进容器并落盘 small-systems.xml,旧文件保留不动(可人工回退)。
        /// </summary>
        public SmallSystemProject LoadSmallSystems()
        {
            if (File.Exists(SmallSystemsFilePath))
            {
                return Load(SmallSystemsFilePath, () => new SmallSystemProject());
            }

            var project = new SmallSystemProject();
            if (File.Exists(LegacySmallSystemFilePath))
            {
                var legacy = Load(LegacySmallSystemFilePath, () => (SmallSystemInput)null);
                if (legacy != null && (legacy.Rooms.Count > 0 || !string.IsNullOrEmpty(legacy.SystemCode)))
                {
                    project.Systems.Add(legacy);
                    try { Save(SmallSystemsFilePath, project); } catch { /* 迁移落盘失败不阻断读取 */ }
                }
            }
            return project;
        }

        public void SaveSmallSystems(SmallSystemProject project)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            Save(SmallSystemsFilePath, project);
        }

        /// <summary>兼容旧接口:取容器里第一套系统(没有则返回默认实例)。</summary>
        public SmallSystemInput LoadSmallSystem()
        {
            var project = LoadSmallSystems();
            return project.Systems.Count > 0 ? project.Systems[0] : new SmallSystemInput();
        }

        /// <summary>兼容旧接口:写入单系统(整体替换容器里"同类型同编号"的那一套)。</summary>
        public void SaveSmallSystem(SmallSystemInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var project = LoadSmallSystems();
            project.Upsert(input);
            SaveSmallSystems(project);
        }

        /// <summary>反序列化;文件缺失或损坏时回退默认(不阻断启动,由上层提示)。</summary>
        private static T Load<T>(string path, Func<T> fallback)
        {
            try
            {
                if (!File.Exists(path)) return fallback();
                var serializer = new XmlSerializer(typeof(T));
                using (var stream = File.OpenRead(path))
                {
                    var loaded = serializer.Deserialize(stream);
                    return loaded == null ? fallback() : (T)loaded;
                }
            }
            catch
            {
                return fallback();
            }
        }

        /// <summary>序列化落盘(UTF-8 无 BOM,缩进可读)。</summary>
        private void Save<T>(string path, T value)
        {
            Directory.CreateDirectory(StorageDirectory);
            var serializer = new XmlSerializer(typeof(T));
            var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
            using (var writer = XmlWriter.Create(path, settings))
            {
                serializer.Serialize(writer, value);
            }
        }
    }
}
