using HVACIDA.Core.Models;

namespace HVACIDA.Core.Services
{
    /// <summary>大系统负荷计算服务接口(需求文档 2.2.3.1)。</summary>
    public interface ILargeSystemLoadCalculator
    {
        /// <summary>按输入参数执行大系统负荷/风量/排烟/选型计算。</summary>
        LargeSystemResult Calculate(LargeSystemInput input);
    }
}
