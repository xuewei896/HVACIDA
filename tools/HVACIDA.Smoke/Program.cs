using System;
using System.Text;
using HVACIDA.Core.Models;
using HVACIDA.Core.Services;

namespace HVACIDA.Smoke
{
    /// <summary>
    /// 大系统负荷计算数值自测:
    /// 1) 默认参数(客流 0)不应崩溃、无负数;
    /// 2) 典型客流场景输出应自洽(送风量>0、制冷量>0、排烟=面积×60 等)。
    /// 待拿到 Excel 样例数据后,在此追加"输入→期望输出"断言用例逐格比对。
    /// </summary>
    internal static class Program
    {
        private static void Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            var calculator = new LargeSystemLoadCalculator();

            RunScenario(calculator, "场景1:默认参数(客流=0)", new LargeSystemInput());

            var busy = new LargeSystemInput
            {
                OutdoorWetBulbC = 27.9,
                HallDesignTempC = 30,
                PlatformDesignTempC = 28,
                UpLineBoardCount = 6000,
                DownLineBoardCount = 6500,
                UpLineAlightCount = 5800,
                DownLineAlightCount = 6200,
                TransferBoardCount = 3000,
                TransferAlightCount = 2800,
                HallAreaM2 = 1500,
                PlatformAreaM2 = 1200
            };
            RunScenario(calculator, "场景2:典型高峰客流", busy);

            Console.WriteLine("Smoke 完成(数值正确性待 Excel 样例逐格核对)。");
        }

        private static void RunScenario(ILargeSystemLoadCalculator calculator, string title, LargeSystemInput input)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine(title);
            Console.WriteLine("==================================================");
            LargeSystemResult r = calculator.Calculate(input);
            Console.WriteLine(ResultFormatter.FormatLarge(input, r));

            Console.WriteLine("-- 自检 --");
            Console.WriteLine("送风量>=0: " + (r.TotalSupplyFlowM3H >= 0));
            Console.WriteLine("制冷量>=0: " + (r.TotalCoolingKw >= 0));
            Console.WriteLine("排烟=面积×60: 站厅 " + (r.HallSmokeFlowM3H == input.HallAreaM2 * 60) +
                              ",站台 " + (r.PlatformSmokeFlowM3H == input.PlatformAreaM2 * 60));
            Console.WriteLine();
        }
    }
}
