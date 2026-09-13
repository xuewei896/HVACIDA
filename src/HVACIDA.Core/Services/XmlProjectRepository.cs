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
    /// 文件:project.xml(工程信息)/ large-system.xml(大系统输入)/ small-system.xml(小系统输入)。
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

        private string SmallSystemFilePath => Path.Combine(StorageDirectory, "small-system.xml");

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

        public SmallSystemInput LoadSmallSystem() => Load(SmallSystemFilePath, () => new SmallSystemInput());

        public void SaveSmallSystem(SmallSystemInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            Save(SmallSystemFilePath, input);
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
