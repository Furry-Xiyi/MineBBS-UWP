namespace MineBBS.Interfaces
{
    /// <summary>
    /// 可搜索页面接口
    /// </summary>
    public interface ISearchable
    {
        /// <summary>
        /// 执行搜索
        /// </summary>
        /// <param name="query">搜索关键词</param>
        void PerformSearch(string query);
    }
}