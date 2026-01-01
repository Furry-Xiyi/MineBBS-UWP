using System.Collections.ObjectModel;

namespace MineBBS.Models
{
    /// <summary>
    /// 轮播图模型
    /// </summary>
    public class BannerModel
    {
        public string ImageUrl { get; set; }
        public string Link { get; set; }
    }

    /// <summary>
    /// 公告模型
    /// </summary>
    public class NoticeModel
    {
        public string Title { get; set; }
        public string Link { get; set; }
    }

    /// <summary>
    /// 推荐内容模型
    /// </summary>
    public class FeaturedModel
    {
        public string Title { get; set; }
        public string AuthorName { get; set; }
        public string PublishTime { get; set; }
        public string Summary { get; set; }
        public string AuthorAvatar { get; set; }
        public string Link { get; set; }
    }

    /// <summary>
    /// 论坛板块模型
    /// </summary>
    public class ForumModel
    {
        public string ForumName { get; set; }
        public string ForumDesc { get; set; }
        public int TopicCount { get; set; }
        public int MsgCount { get; set; }
        public string LatestTopicTitle { get; set; }
        public string IconClass { get; set; }
        public string Link { get; set; }
    }

    /// <summary>
    /// 论坛分区模型
    /// </summary>
    public class ForumCategoryModel
    {
        public string CategoryName { get; set; }
        public ObservableCollection<ForumModel> Forums { get; set; } = new ObservableCollection<ForumModel>();
    }

    /// <summary>
    /// 主题模型
    /// </summary>
    public class TopicModel
    {
        public string Title { get; set; }
        public string AuthorName { get; set; }
        public string PublishTime { get; set; }
        public int ReplyCount { get; set; }
        public int ViewCount { get; set; }
        public string Link { get; set; }
    }

    /// <summary>
    /// 统计信息模型
    /// </summary>
    public class StatisticsModel
    {
        public string OnlineUsers { get; set; }
        public string TotalTopics { get; set; }
        public string TotalMessages { get; set; }
        public string TotalMembers { get; set; }
    }
}