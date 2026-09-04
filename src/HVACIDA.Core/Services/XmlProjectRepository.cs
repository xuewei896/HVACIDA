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
    /// TODO(存储):后续切换 SQLite(System.Data.SQLite 或 Microsoft.Data.Sqlite),保留本实现用于迁移/测试。
    /// </summary>
    public class XmlProjectRepository : IDataRepository
    {
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(ProjectInfoModel));

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

        public ProjectInfoModel LoadProject()
        {
            try
            {
                if (!File.Exists(ProjectFilePath)) return new ProjectInfoModel();
                using (var stream = File.OpenRead(ProjectFilePath))
                {
                    var loaded = Serializer.Deserialize(stream) as ProjectInfoModel;
                    return loaded ?? new ProjectInfoModel();
                }
            }
            catch
            {
                // 损坏文件不阻断启动,回退默认并交由上层提示。
                return new ProjectInfoModel();
            }
        }

        public void SaveProject(ProjectInfoModel project)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            Directory.CreateDirectory(StorageDirectory);
            var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
            using (var writer = XmlWriter.Create(ProjectFilePath, settings))
            {
                Serializer.Serialize(writer, project);
            }
        }
    }
}
