namespace HVACIDA.Core.Services
{
    /// <summary>
    /// 计算书/报告生成服务(需求文档 2.2.4、2.5 导出、4.1 Excel/PDF 接口的文本先行版)。
    /// TODO(导出):后续实现 Excel(EPPlus/OpenXML 评估中)与 PDF。
    /// </summary>
    public interface ICalculationReportGenerator
    {
        /// <summary>生成文本报告文件并返回完整路径。</summary>
        /// <param name="title">报告标题(用于文件名与首行)</param>
        /// <param name="content">多行正文(UTF-8)</param>
        string SaveTextReport(string title, string content);
    }
}
