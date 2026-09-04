using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>小系统负荷/通风计算服务接口(需求文档 2.2.3.2)。</summary>
    public interface ISmallSystemLoadCalculator
    {
        /// <summary>按输入参数执行小系统负荷/通风量/新风量计算。</summary>
        SmallSystemResult Calculate(SmallSystemInput input);
    }
}
