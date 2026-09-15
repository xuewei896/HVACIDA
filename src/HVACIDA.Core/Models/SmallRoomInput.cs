using System;

namespace HVACIDA.Core.Models
{
    /// <summary>
    /// 小系统的<strong>单个房间</strong>输入(《小系统空调负荷、送排风、排烟计算公式.docx》:
    /// 全空气一次回风 / 多联机+新风 的 C27~P27、C64~K64;排风/送排风/排烟的房间行)。
    /// <para>
    /// 一个系统负责多个房间,故这些字段是**逐房间**的;系统级参数见 <see cref="SmallSystemInput"/>。
    /// 面积/层高/与土壤接触外墙长度/屋顶面积由模型空间取值(需求 2.2.3.2)。
    /// </para>
    /// </summary>
    [Serializable]
    public class SmallRoomInput
    {
        /// <summary>房间名称(输出表格第一列)。</summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// 房间类型(排风系统据此取默认换气次数:卫生间 20、气瓶间 4、环控机房 6、
        /// 电缆引入间 4、淋浴间 10、污水/废水泵房 4、其他 4)。空 = 其他。
        /// </summary>
        public string RoomType { get; set; } = "";

        /// <summary>房间面积 m²(C27 / C64)。</summary>
        public double AreaM2 { get; set; }

        /// <summary>房间高度 m(D27 / D64)。</summary>
        public double HeightM { get; set; }

        /// <summary>与土壤接触外墙长度 m(E27,由拾取墙体求和)。</summary>
        public double WallLengthM { get; set; }

        /// <summary>与土壤接触屋顶面积 m²(F27,默认与面积相同)。</summary>
        public double RoofAreaM2 { get; set; }

        /// <summary>房间设备冷负荷 W(G27 / E64)。</summary>
        public double EquipmentCoolingW { get; set; }

        /// <summary>房间预测人数(人)(I27 / G64)。</summary>
        public double Occupants { get; set; }

        /// <summary>房间换气次数 次/h(P27 / K64)。</summary>
        public double AirChangePerHour { get; set; }

        /// <summary>是否为防烟分区(排烟系统按分区行输入;用于表格分组显示)。</summary>
        public bool IsSmokeZone { get; set; }

        /// <summary>构造一个房间:屋顶面积默认等于面积(F27 默认 = C27)。</summary>
        public static SmallRoomInput Create(string name, double areaM2, double heightM)
        {
            return new SmallRoomInput
            {
                Name = name,
                AreaM2 = areaM2,
                HeightM = heightM,
                RoofAreaM2 = areaM2,
                EquipmentCoolingW = Utils.HvacConstants.SmallEquipmentCoolingW,
                AirChangePerHour = Utils.HvacConstants.SmallRoomAirChangePerHour
            };
        }

        /// <summary>面积×层高×换气次数 m³/h(排风/送排风系统与换气次数通风量都用它)。</summary>
        public double VolumeFlowByAchM3H => AreaM2 * HeightM * AirChangePerHour;
    }
}
