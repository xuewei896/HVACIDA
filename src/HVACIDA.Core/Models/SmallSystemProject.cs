using System;
using System.Collections.Generic;

namespace HVACIDA.Core.Models
{
    /// <summary>
    /// 小系统工程的**多系统容器**(需求 2.2.3.2:小系统按系统类型/编号分系统管理,
    /// "小系统 → 计算结果"要汇总全站所有小系统)。
    /// <para>
    /// 存储为 <c>small-systems.xml</c>;单个系统沿用 <see cref="SmallSystemInput"/>。
    /// 旧版单系统文件 <c>small-system.xml</c> 会在首次读取时**自动迁移**为本容器(见 XmlProjectRepository)。
    /// </para>
    /// </summary>
    [Serializable]
    public class SmallSystemProject
    {
        /// <summary>全部小系统(按录入顺序;同一系统类型可有多套,如 AHU-A101 / AHU-A201)。</summary>
        public List<SmallSystemInput> Systems { get; set; } = new List<SmallSystemInput>();

        /// <summary>
        /// 按"系统类型 + 系统编号"查找;<paramref name="systemCode"/> 为空时按类型找第一个。
        /// </summary>
        public SmallSystemInput Find(SmallSystemType type, string systemCode)
        {
            foreach (var s in Systems)
            {
                if (s.SystemType != type) continue;
                if (string.IsNullOrEmpty(systemCode)) return s;
                if (string.Equals(s.SystemCode ?? "", systemCode, StringComparison.Ordinal)) return s;
            }
            return null;
        }

        /// <summary>
        /// 新增或覆盖一个系统(同类型同编号视为同一套,整体替换;编号为空时按"同类型同编号(空)"匹配)。
        /// 返回被替换的旧系统(没有则为 null)。
        /// </summary>
        public SmallSystemInput Upsert(SmallSystemInput system)
        {
            if (system == null) throw new ArgumentNullException(nameof(system));

            for (int i = 0; i < Systems.Count; i++)
            {
                if (Systems[i].SystemType != system.SystemType) continue;
                if (!string.Equals(Systems[i].SystemCode ?? "", system.SystemCode ?? "", StringComparison.Ordinal)) continue;
                var replaced = Systems[i];
                Systems[i] = system;
                return replaced;
            }
            Systems.Add(system);
            return null;
        }

        /// <summary>移除一个系统,返回是否真的移除了。</summary>
        public bool Remove(SmallSystemType type, string systemCode)
        {
            for (int i = 0; i < Systems.Count; i++)
            {
                if (Systems[i].SystemType != type) continue;
                if (!string.Equals(Systems[i].SystemCode ?? "", systemCode ?? "", StringComparison.Ordinal)) continue;
                Systems.RemoveAt(i);
                return true;
            }
            return false;
        }
    }
}
