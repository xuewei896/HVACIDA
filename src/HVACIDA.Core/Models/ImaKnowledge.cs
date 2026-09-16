using System;
using System.Collections.Generic;

namespace HVACIDA.Core.Models
{
    /// <summary>
    /// **ima 在线知识库接入设置**(腾讯 ima 的开放接口,纯 HTTP + JSON,不引任何第三方库)。
    /// <para>
    /// ⚠ 关键事实(必须先让用户看见):**分享链接里的 shareId 不能当接口凭证**。
    /// ima 的开放接口(<c>https://ima.qq.com/openapi/wiki/v1/*</c>)要求三个东西:
    /// ① <see cref="ClientId"/>、② <see cref="ApiKey"/>(来自 ima 开放平台申请的凭证)、
    /// ③ <see cref="KnowledgeBaseId"/> —— 知识库的**内部 ID**。
    /// shareId 只用于在 ima 网页端打开分享页,既不能鉴权、也不能定位知识库,
    /// 所以本插件**不把 shareId 当凭证用**,只提供「打开分享页」和「让你把凭证填进来」两条路。
    /// </para>
    /// <para>
    /// 未填凭证时插件**不假装能查**:会直说"没配 ima 凭证,只能用本地知识库",与本仓库
    /// 「没算过就不摆结果」的口径一致。
    /// </para>
    /// </summary>
    public class ImaKnowledgeSettings
    {
        /// <summary>是否启用 ima 在线知识库(默认关:没配凭证就别发网络请求)。</summary>
        public bool Enabled { get; set; }

        /// <summary>ima 开放平台 Client ID(请求头 <c>ima-openapi-clientid</c>)。</summary>
        public string ClientId { get; set; } = "";

        /// <summary>ima 开放平台 API Key(请求头 <c>ima-openapi-apikey</c>)。**本机明文保存,勿外发**。</summary>
        public string ApiKey { get; set; } = "";

        /// <summary>知识库 ID(接口参数 <c>knowledge_base_id</c>;根目录 folder_id 等于它)。</summary>
        public string KnowledgeBaseId { get; set; } = "";

        /// <summary>ima 分享链接或 shareId(**仅供"在浏览器里打开"用,不参与鉴权**)。</summary>
        public string ShareId { get; set; } = "";

        /// <summary>单次检索返回条数上限(1-50)。</summary>
        public int Limit { get; set; } = 10;

        /// <summary>接口地址(默认官方地址;留空即用默认。私有化/代理时可改,也便于自检里断网演练)。</summary>
        public string Endpoint { get; set; } = "";

        /// <summary>是否已具备发请求的三个必要条件(Enabled 由调用方另外判断)。</summary>
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(ClientId) &&
            !string.IsNullOrWhiteSpace(ApiKey) &&
            !string.IsNullOrWhiteSpace(KnowledgeBaseId);

        /// <summary>缺什么(给界面直接显示,不猜)。</summary>
        public string MissingCredentialText()
        {
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(ClientId)) missing.Add("Client ID");
            if (string.IsNullOrWhiteSpace(ApiKey)) missing.Add("API Key");
            if (string.IsNullOrWhiteSpace(KnowledgeBaseId)) missing.Add("知识库 ID");
            if (missing.Count == 0) return "凭证齐全。";
            return "还缺:" + string.Join("、", missing.ToArray()) + "。";
        }

        /// <summary>给用户看的凭证说明(界面/说明文件共用同一段话)。</summary>
        public static string CredentialHelp =>
            "ima 在线知识库需要「Client ID + API Key + 知识库 ID」三样(在 ima 开放平台申请凭证,知识库 ID 取自知识库本身);" +
            "分享链接里的 shareId 只是网页分享标识,**不能当接口凭证**,插件不会拿它去鉴权。" +
            "检索返回的是命中条目的标题与片段(highlight_content),不是整篇原文 —— 引用请回 ima 打开原文核对。";

        /// <summary>复制一份(避免界面直接改到已保存的对象)。</summary>
        public ImaKnowledgeSettings Clone()
        {
            return new ImaKnowledgeSettings
            {
                Enabled = Enabled,
                ClientId = ClientId ?? "",
                ApiKey = ApiKey ?? "",
                KnowledgeBaseId = KnowledgeBaseId ?? "",
                ShareId = ShareId ?? "",
                Limit = Limit,
                Endpoint = Endpoint ?? ""
            };
        }
    }

    /// <summary>ima 检索命中的一条知识条目(接口返回 <c>info_list</c> 的一项)。</summary>
    public class ImaKnowledgeHit
    {
        /// <summary>媒体 ID(ima 内部的条目 ID)。</summary>
        public string MediaId { get; set; } = "";

        /// <summary>条目标题(通常就是文件名)。</summary>
        public string Title { get; set; } = "";

        /// <summary>所属文件夹 ID。</summary>
        public string ParentFolderId { get; set; } = "";

        /// <summary>命中片段(接口字段 highlight_content;**不是**全文)。</summary>
        public string Highlight { get; set; } = "";

        /// <summary>出处文本(界面显示用)。</summary>
        public string SourceText =>
            "出处:ima 知识库(在线检索)" +
            (string.IsNullOrEmpty(MediaId) ? "" : ";media_id=" + MediaId) +
            (string.IsNullOrEmpty(ParentFolderId) ? "" : ";文件夹=" + ParentFolderId);
    }

    /// <summary>
    /// **一次 ima 检索/连通性测试的结果**。失败**不抛异常**给界面,而是把原因写进
    /// <see cref="ErrorMessage"/> / <see cref="Note"/>,界面照实显示。
    /// </summary>
    public class ImaKnowledgeSearchResult
    {
        /// <summary>是否成功(网络通 + retcode=0)。</summary>
        public bool Success { get; set; }

        /// <summary>本次检索的问题。</summary>
        public string Query { get; set; } = "";

        /// <summary>ima 返回的 retcode(0 = 成功;网络没通时为 -1)。</summary>
        public int RetCode { get; set; } = -1;

        /// <summary>失败原因(ima 的 errmsg 原样透传,附上插件补的处置建议)。</summary>
        public string ErrorMessage { get; set; } = "";

        /// <summary>命中条目。</summary>
        public List<ImaKnowledgeHit> Hits { get; set; } = new List<ImaKnowledgeHit>();

        /// <summary>是否还有下一页(接口字段 is_end 取反)。</summary>
        public bool HasMore { get; set; }

        /// <summary>提示语(给用户看的一句话;成功与失败都有)。</summary>
        public string Note { get; set; } = "";

        /// <summary>这次是连通性测试(get_knowledge_base)还是检索(search_knowledge)。</summary>
        public bool IsConnectionTest { get; set; }

        /// <summary>命中条数。</summary>
        public int Count => Hits.Count;

        /// <summary>构造一条失败结果(不抛异常)。</summary>
        public static ImaKnowledgeSearchResult Fail(string query, int retCode, string errorMessage, string note)
        {
            return new ImaKnowledgeSearchResult
            {
                Success = false,
                Query = query ?? "",
                RetCode = retCode,
                ErrorMessage = errorMessage ?? "",
                Note = note ?? ""
            };
        }
    }
}
