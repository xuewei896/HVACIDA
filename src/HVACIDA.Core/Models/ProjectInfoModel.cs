using System;

namespace HVACIDA.Core.Models
{
    /// <summary>
    /// 项目级数据根(基本信息 + 设计/气象参数)。UI 项目信息窗、Repository 均围绕本类型工作。
    /// </summary>
    [Serializable]
    public class ProjectInfoModel
    {
        public ProjectInfoModel()
        {
            Basic = new BasicProjectInfo();
            Design = new DesignConditionParams();
        }

        public BasicProjectInfo Basic { get; set; }

        public DesignConditionParams Design { get; set; }
    }

    /// <summary>工程基本信息(需求文档 2.1.1)。</summary>
    [Serializable]
    public class BasicProjectInfo
    {
        public string ProjectName { get; set; } = "";

        public string LocationProvince { get; set; } = "";

        public string LocationCity { get; set; } = "";

        public string LocationDistrict { get; set; } = "";

        /// <summary>车站名称(决策 B-4,2026-09-09:统一"车站名称",原"车站编号"不再使用)。</summary>
        public string StationName { get; set; } = "";

        /// <summary>设计阶段:初步设计 / 施工图设计(字符串便于 UI 下拉与序列化)。</summary>
        public string DesignStage { get; set; } = "初步设计";

        public string Remark { get; set; } = "";
    }
}
