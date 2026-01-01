using HtmlAgilityPack;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Shapes;

namespace MineBBS.Views
{
    public sealed partial class HomePage : Page
    {
        private const string BASE_URL = "https://www.minebbs.com/";
        private HtmlDocument _htmlDoc;
        private FlipView _mainCarousel;
        private FlipView _featuredCarousel;
        private int _currentCarouselItemsPerPage = 1;
        private int _currentFeaturedItemsPerPage = 2;
        private StackPanel _rightPanel;
        private bool _isCheckingIn = false;

        private Grid _checkInCard;
        private FontIcon _checkInIcon;
        private TextBlock _checkInStatusText;
        private Button _checkInButton;
        private TextBlock _checkInCountText;
        private GridView _friendLinksGridView;
        private GridView _partnerLinksGridView;
        private GridView _sponsorGridView;
        private List<NewContentItem> _jsThreads = new List<NewContentItem>();
        private List<NewContentItem> _jsResources = new List<NewContentItem>();
        private List<NewContentItem> _jsReplies = new List<NewContentItem>();
        private List<OnlineMember> _jsOnlineMembers = new List<OnlineMember>();
        private List<TrendingItem> _jsTrending = new List<TrendingItem>();
        private ForumStats _jsStats = new ForumStats();

        public HomePage()
        {
            this.InitializeComponent();
            this.SizeChanged += HomePage_SizeChanged;
        }

        private bool IsUserLoggedIn()
        {
            // 检查是否有保存的用户数据和Cookie
            var userData = ApplicationData.Current.LocalSettings.Values["UserData"] as string;
            var cookie = ApplicationData.Current.LocalSettings.Values["UserCookie"] as string;
            return !string.IsNullOrEmpty(userData) && !string.IsNullOrEmpty(cookie);
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // 如果已经加载过，直接显示
            if (_htmlDoc != null && MainScrollViewer.Visibility == Visibility.Visible)
            {
                return;
            }

            await LoadPageData();
        }

        private TaskCompletionSource<string> _htmlTaskCompletion;

        private async Task LoadPageData()
        {
            try
            {
                LoadingProgressRing.IsActive = true;
                ErrorPanel.Visibility = Visibility.Collapsed;

                // 使用 WebView2 获取 HTML
                _htmlTaskCompletion = new TaskCompletionSource<string>();
                HiddenWebView.Source = new Uri(BASE_URL);
                var html = await _htmlTaskCompletion.Task;

                _htmlDoc = new HtmlDocument();
                _htmlDoc.LoadHtml(html);

                // 渲染页面内容
                await RenderTopCarousel();
                await RenderFeaturedContent();
                await RenderForumCategories();

                // 显示主内容
                LoadingProgressRing.IsActive = false;
                MainScrollViewer.Visibility = Visibility.Visible;
                RefreshButton.Visibility = Visibility.Visible;

                // 最后创建广告占位并初始化位置
                CreateAdPlaceholder();
            }
            catch (Exception ex)
            {
                LoadingProgressRing.IsActive = false;
                ErrorPanel.Visibility = Visibility.Visible;
                ErrorTextBlock.Text = $"加载失败: {ex.Message}\n\n请检查网络连接后重试。";
            }
        }

        private void UpdateRightPanelPosition()
        {
            // 安全检查
            if (_rightPanel == null || ContentPanel == null) return;

            var leftStack = ContentPanel.Children.OfType<StackPanel>().FirstOrDefault();
            if (leftStack == null) return;

            double windowWidth = Window.Current.Bounds.Width;
            bool isSingleColumn = windowWidth < 1000;

            // 从所有可能的位置移除
            try
            {
                if (ContentPanel.Children.Contains(_rightPanel))
                    ContentPanel.Children.Remove(_rightPanel);
                if (leftStack.Children.Contains(_rightPanel))
                    leftStack.Children.Remove(_rightPanel);
            }
            catch { }

            // 根据模式决定位置
            try
            {
                if (isSingleColumn)
                {
                    _rightPanel.Margin = new Thickness(0, 16, 0, 0);
                    _rightPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
                    leftStack.Children.Add(_rightPanel);
                    Grid.SetColumnSpan(leftStack, 2);
                }
                else
                {
                    _rightPanel.Margin = new Thickness(0);
                    _rightPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
                    _rightPanel.VerticalAlignment = VerticalAlignment.Top;
                    Grid.SetColumn(_rightPanel, 1);
                    ContentPanel.Children.Add(_rightPanel);
                    Grid.SetColumnSpan(leftStack, 1);
                }
            }
            catch { }
        }

        private async void CreateAdPlaceholder()
        {
            if (_rightPanel != null) return;

            _rightPanel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Spacing = 16
            };

            // 🔍 使用JavaScript注入获取动态数据
            await InjectAndFetchData();

            // 🔍 调试：检查解析结果
            System.Diagnostics.Debug.WriteLine($"解析结果:");
            System.Diagnostics.Debug.WriteLine($"  新主题: {_jsThreads.Count} 条");
            System.Diagnostics.Debug.WriteLine($"  在线会员: {_jsOnlineMembers.Count} 人");
            System.Diagnostics.Debug.WriteLine($"  论坛统计: 主题{_jsStats.ThreadCount}, 在线{_jsStats.OnlineTotal}");

            // 渲染所有右侧内容
            RenderAdBanners();
            RenderCheckInPanel();
            RenderGoldStats();
            RenderNewContentTabs();
            RenderOnlineMembers();
            RenderTrendingContent();
            RenderForumStats();

            try
            {
                UpdateRightPanelPosition();
            }
            catch
            {
                Grid.SetColumn(_rightPanel, 1);
                _rightPanel.Margin = new Thickness(0);
                if (!ContentPanel.Children.Contains(_rightPanel))
                {
                    ContentPanel.Children.Add(_rightPanel);
                }
            }
        }

        // 新增：通过JavaScript注入获取数据
        private async Task InjectAndFetchData()
        {
            var script = @"
        (function() {
            var data = {
                threads: [],
                resources: [],
                replies: [],
                onlineMembers: [],
                trending: [],
                stats: {}
            };

            // 获取新主题
            var threadWidget = document.querySelector('[data-widget-key=""thread""]');
            if (threadWidget) {
                var threadItems = threadWidget.querySelectorAll('.block-row');
                threadItems.forEach(function(item, index) {
                    if (index >= 5) return;
                    var link = item.querySelector('.contentRow-main > a');
                    var label = item.querySelector('.label');
                    var author = item.querySelector('.listInline--bullet li:nth-child(1)');
                    var time = item.querySelector('time');
                    var replies = item.querySelector('.listInline--bullet li:nth-child(3)');
                    var forum = item.querySelector('.contentRow-minor--hideLinks a');
                    
                    if (link) {
                        data.threads.push({
                            title: link.textContent.trim(),
                            url: link.getAttribute('href'),
                            label: label ? label.textContent.trim() : '',
                            labelClass: label ? label.className : '',
                            author: author ? author.textContent.trim() : '',
                            time: time ? time.getAttribute('data-short') : '',
                            replies: replies ? replies.textContent.trim() : '',
                            forum: forum ? forum.textContent.trim() : ''
                        });
                    }
                });
            }

            // 获取在线会员
            var onlineWidget = document.querySelector('[data-widget-key=""onlineuser""]');
            if (onlineWidget) {
                var memberLinks = onlineWidget.querySelectorAll('.listInline--comma a');
                memberLinks.forEach(function(link, index) {
                    if (index >= 20) return;
                    data.onlineMembers.push({
                        username: link.textContent.trim(),
                        url: link.getAttribute('href'),
                        userId: link.getAttribute('data-user-id')
                    });
                });

                var footer = onlineWidget.querySelector('.block-footer-counter');
                if (footer) {
                    data.stats.onlineText = footer.textContent.trim();
                }
            }

            // 获取论坛统计
            var statsWidget = document.querySelector('[data-widget-key=""forum_statistics""]');
            if (statsWidget) {
                var dls = statsWidget.querySelectorAll('dl.pairs');
                dls.forEach(function(dl) {
                    var dt = dl.querySelector('dt');
                    var dd = dl.querySelector('dd');
                    if (dt && dd) {
                        var key = dt.textContent.trim();
                        if (key === '主题') {
                            data.stats.threads = dd.textContent.trim();
                        } else if (key === '消息') {
                            data.stats.messages = dd.textContent.trim();
                        } else if (key === '用户') {
                            data.stats.users = dd.textContent.trim();
                        } else if (key === '最新用户') {
                            var link = dd.querySelector('a');
                            if (link) {
                                data.stats.latestUser = link.textContent.trim();
                                data.stats.latestUserUrl = link.getAttribute('href');
                            }
                        }
                    }
                });
            }

            return JSON.stringify(data);
        })();
    ";

            try
            {
                var result = await HiddenWebView.CoreWebView2.ExecuteScriptAsync(script);
                if (!string.IsNullOrEmpty(result) && result != "null")
                {
                    // 去掉外层引号
                    if (result.StartsWith("\""))
                        result = result.Substring(1, result.Length - 2);

                    // 保存到字段中供解析方法使用
                    await ParseFromJavaScriptData(result);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JavaScript注入错误: {ex.Message}");
            }
        }
        private async Task ParseFromJavaScriptData(string jsonData)
        {
            try
            {
                // 处理转义字符
                jsonData = jsonData.Replace("\\\"", "\"")
                                   .Replace("\\n", "")
                                   .Replace("\\r", "")
                                   .Replace("\\\\", "\\");

                System.Diagnostics.Debug.WriteLine($"收到的JSON数据长度: {jsonData.Length}");
                System.Diagnostics.Debug.WriteLine($"JSON前100字符: {jsonData.Substring(0, Math.Min(100, jsonData.Length))}");

                // 使用简单的JSON解析（因为UWP可能没有System.Text.Json）
                dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonData);

                // 解析新主题
                if (data.threads != null)
                {
                    _jsThreads.Clear();
                    foreach (var thread in data.threads)
                    {
                        _jsThreads.Add(new NewContentItem
                        {
                            Title = thread.title?.ToString() ?? "",
                            Url = thread.url?.ToString() ?? "",
                            Label = thread.label?.ToString() ?? "",
                            LabelColor = GetLabelColor(thread.labelClass?.ToString() ?? ""),
                            Author = thread.author?.ToString() ?? "",
                            Time = thread.time?.ToString() ?? "",
                            Forum = thread.forum?.ToString() ?? "",
                            ReplyCount = ParseReplyCount(thread.replies?.ToString() ?? "")
                        });
                    }
                    System.Diagnostics.Debug.WriteLine($"解析到 {_jsThreads.Count} 个新主题");
                }

                // 解析在线会员
                if (data.onlineMembers != null)
                {
                    _jsOnlineMembers.Clear();
                    foreach (var member in data.onlineMembers)
                    {
                        _jsOnlineMembers.Add(new OnlineMember
                        {
                            Username = member.username?.ToString() ?? "",
                            Url = member.url?.ToString() ?? "",
                            UserId = int.TryParse(member.userId?.ToString() ?? "0", out int id) ? id : 0
                        });
                    }
                    System.Diagnostics.Debug.WriteLine($"解析到 {_jsOnlineMembers.Count} 个在线会员");
                }

                // 解析论坛统计
                if (data.stats != null)
                {
                    _jsStats = new ForumStats();

                    if (data.stats.threads != null)
                    {
                        var threadsText = data.stats.threads.ToString().Replace(",", "");
                        if (int.TryParse(threadsText, out int threads))
                            _jsStats.ThreadCount = threads;
                    }

                    if (data.stats.messages != null)
                    {
                        var messagesText = data.stats.messages.ToString().Replace(",", "");
                        if (int.TryParse(messagesText, out int messages))
                            _jsStats.MessageCount = messages;
                    }

                    if (data.stats.users != null)
                    {
                        var usersText = data.stats.users.ToString().Replace(",", "");
                        if (int.TryParse(usersText, out int users))
                            _jsStats.UserCount = users;
                    }

                    if (data.stats.latestUser != null)
                    {
                        _jsStats.LatestUser = data.stats.latestUser.ToString();
                        _jsStats.LatestUserUrl = data.stats.latestUserUrl?.ToString() ?? "";
                    }

                    // 解析在线统计
                    if (data.stats.onlineText != null)
                    {
                        var onlineText = data.stats.onlineText.ToString();
                        var match = System.Text.RegularExpressions.Regex.Match(onlineText, @"在线：(\d+)\s*（用户：(\d+),\s*游客：(\d+)）");
                        if (match.Success)
                        {
                            if (int.TryParse(match.Groups[1].Value, out int total))
                                _jsStats.OnlineTotal = total;
                            if (int.TryParse(match.Groups[2].Value, out int members))
                                _jsStats.OnlineMembers = members;
                            if (int.TryParse(match.Groups[3].Value, out int guests))
                                _jsStats.OnlineGuests = guests;
                        }
                    }

                    System.Diagnostics.Debug.WriteLine($"解析到论坛统计: 主题{_jsStats.ThreadCount}, 在线{_jsStats.OnlineTotal}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ParseFromJavaScriptData 错误: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"堆栈: {ex.StackTrace}");
            }
        }

        // 辅助方法：从CSS类获取标签颜色
        private string GetLabelColor(string labelClass)
        {
            if (labelClass.Contains("yellow"))
                return "Gold";
            else if (labelClass.Contains("skyBlue"))
                return "SkyBlue";
            else if (labelClass.Contains("primary"))
                return "DodgerBlue";
            else
                return "Gray";
        }

        // 辅助方法：解析回复数
        private int ParseReplyCount(string replyText)
        {
            if (string.IsNullOrEmpty(replyText))
                return 0;

            var match = System.Text.RegularExpressions.Regex.Match(replyText, @"(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int count))
                return count;

            return 0;
        }
        #region 右侧面板解析

        private List<AdBanner> ParseAdBanners()
        {
            var banners = new List<AdBanner>();

            // 只解析data-position="swiper_1"和"swiper_2"的广告（赢泰云和林风云）
            var adNodes = _htmlDoc.DocumentNode.SelectNodes("//div[@data-position='swiper_1' or @data-position='swiper_2']//a");

            if (adNodes != null)
            {
                foreach (var node in adNodes)
                {
                    var imgNode = node.SelectSingleNode(".//img");
                    if (imgNode != null)
                    {
                        var imgSrc = imgNode.GetAttributeValue("src", "");
                        if (!string.IsNullOrEmpty(imgSrc))
                        {
                            if (imgSrc.StartsWith("./"))
                                imgSrc = BASE_URL + imgSrc.Substring(2);
                            else if (imgSrc.StartsWith("/"))
                                imgSrc = BASE_URL + imgSrc.TrimStart('/');

                            banners.Add(new AdBanner
                            {
                                ImageUrl = imgSrc,
                                LinkUrl = node.GetAttributeValue("href", ""),
                                Title = imgNode.GetAttributeValue("alt", "")
                            });
                        }
                    }
                }
            }

            return banners;
        }

        // 新增：解析旋律云横幅广告
        private AdBanner ParseBottomBanner()
        {
            var bannerNode = _htmlDoc.DocumentNode.SelectSingleNode("//div[@data-position='container_content_below']//a");

            if (bannerNode != null)
            {
                var imgNode = bannerNode.SelectSingleNode(".//img");
                if (imgNode != null)
                {
                    var imgSrc = imgNode.GetAttributeValue("src", "");
                    if (!string.IsNullOrEmpty(imgSrc))
                    {
                        if (imgSrc.StartsWith("./"))
                            imgSrc = BASE_URL + imgSrc.Substring(2);
                        else if (imgSrc.StartsWith("/"))
                            imgSrc = BASE_URL + imgSrc.TrimStart('/');

                        return new AdBanner
                        {
                            ImageUrl = imgSrc,
                            LinkUrl = bannerNode.GetAttributeValue("href", ""),
                            Title = imgNode.GetAttributeValue("alt", "")
                        };
                    }
                }
            }

            return null;
        }

        private List<SponsorLink> ParseFriendLinks()
        {
            var links = new List<SponsorLink>();
            var friendLinkNodes = _htmlDoc.DocumentNode.SelectNodes("//div[@data-widget-key='friend_link']//a");

            if (friendLinkNodes != null)
            {
                foreach (var node in friendLinkNodes)
                {
                    var imgNode = node.SelectSingleNode(".//img");
                    if (imgNode != null)
                    {
                        var imgSrc = imgNode.GetAttributeValue("src", "");
                        if (imgSrc.StartsWith("./"))
                            imgSrc = BASE_URL + imgSrc.Substring(2);
                        else if (imgSrc.StartsWith("/"))
                            imgSrc = BASE_URL + imgSrc.TrimStart('/');

                        links.Add(new SponsorLink
                        {
                            Title = node.GetAttributeValue("title", "") ?? imgNode.GetAttributeValue("alt", ""),
                            Url = node.GetAttributeValue("href", ""),
                            ImageUrl = imgSrc
                        });
                    }
                }
            }

            return links;
        }

        private List<SponsorLink> ParsePartnerLinks()
        {
            var links = new List<SponsorLink>();
            var partnerLinkNodes = _htmlDoc.DocumentNode.SelectNodes("//div[@data-widget-key='partner_list']//a");

            if (partnerLinkNodes != null)
            {
                foreach (var node in partnerLinkNodes)
                {
                    var imgNode = node.SelectSingleNode(".//img");
                    if (imgNode != null)
                    {
                        var imgSrc = imgNode.GetAttributeValue("src", "");
                        if (imgSrc.StartsWith("./"))
                            imgSrc = BASE_URL + imgSrc.Substring(2);
                        else if (imgSrc.StartsWith("/"))
                            imgSrc = BASE_URL + imgSrc.TrimStart('/');

                        links.Add(new SponsorLink
                        {
                            Title = node.GetAttributeValue("title", "") ?? imgNode.GetAttributeValue("alt", ""),
                            Url = node.GetAttributeValue("href", ""),
                            ImageUrl = imgSrc
                        });
                    }
                }
            }

            return links;
        }

        private CheckInInfo ParseCheckInInfo()
        {
            var info = new CheckInInfo
            {
                IsCheckedIn = false,
                TodayCount = 0,
                TotalDays = 0,
                MonthlyReward = 0
            };

            // 查找签到信息区域
            var checkInWidget = _htmlDoc.DocumentNode.SelectSingleNode("//div[@data-widget-key='daily_sign']");
            if (checkInWidget != null)
            {
                // 检查是否已签到 - 查找"今日签到已完成"或成功图标
                var signedMessage = checkInWidget.SelectSingleNode(".//div[@class='mjc-signed-message']");
                info.IsCheckedIn = signedMessage != null;

                if (info.IsCheckedIn)
                {
                    // 获取今日签到人数 - 从"今日已有 XXX 人签到"中提取
                    var todayCountNode = checkInWidget.SelectSingleNode(".//p[@class='mjc-sub-message']");
                    if (todayCountNode != null)
                    {
                        var text = todayCountNode.InnerText;
                        var match = System.Text.RegularExpressions.Regex.Match(text, @"(\d+)");
                        if (match.Success)
                        {
                            int tempCount;
                            if (int.TryParse(match.Groups[1].Value, out tempCount))
                            {
                                info.TodayCount = tempCount;
                            }
                        }
                    }
                }

                // 获取金粒统计信息
                var statCard = checkInWidget.SelectSingleNode(".//div[@class='mjc-stat-card']");
                if (statCard != null)
                {
                    // 获取签到次数
                    var statItems = statCard.SelectNodes(".//li");
                    if (statItems != null && statItems.Count >= 1)
                    {
                        // 第一个li是签到次数
                        var daysValue = statItems[0].SelectSingleNode(".//span[@class='mjc-stat-value']");
                        if (daysValue != null)
                        {
                            int tempDays;
                            if (int.TryParse(daysValue.InnerText.Trim(), out tempDays))
                            {
                                info.TotalDays = tempDays;
                            }
                        }

                        // 第二个li是本月奖励
                        if (statItems.Count >= 2)
                        {
                            var rewardValue = statItems[1].SelectSingleNode(".//span[@class='mjc-stat-value']");
                            if (rewardValue != null)
                            {
                                // 提取数字（去掉"金粒"文字）
                                var text = rewardValue.InnerText.Trim();
                                var match = System.Text.RegularExpressions.Regex.Match(text, @"(\d+)");
                                if (match.Success)
                                {
                                    int tempReward;
                                    if (int.TryParse(match.Groups[1].Value, out tempReward))
                                    {
                                        info.MonthlyReward = tempReward;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return info;
        }

        // 解析新主题
        private List<NewContentItem> ParseNewThreads()
        {
            // 优先使用JavaScript获取的数据
            if (_jsThreads != null && _jsThreads.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"使用JavaScript数据: {_jsThreads.Count} 个新主题");
                return _jsThreads;
            }

            System.Diagnostics.Debug.WriteLine("JavaScript数据为空，尝试HTML解析");

            // 回退到HTML解析（原有代码）
            var items = new List<NewContentItem>();
            var threadNodes = _htmlDoc.DocumentNode.SelectNodes("//div[@data-widget-key='thread']//li[@class='block-row']");

            if (threadNodes == null)
            {
                System.Diagnostics.Debug.WriteLine("HTML解析也失败：未找到thread节点");
                return items;
            }

            foreach (var node in threadNodes.Take(5))
            {
                var titleNode = node.SelectSingleNode(".//div[@class='contentRow-main']//a");
                var authorNode = node.SelectSingleNode(".//ul[@class='listInline listInline--bullet']/li[1]");
                var timeNode = node.SelectSingleNode(".//time");
                var replyNode = node.SelectSingleNode(".//ul[@class='listInline listInline--bullet']/li[3]");
                var forumNode = node.SelectSingleNode(".//div[@class='contentRow-minor contentRow-minor--hideLinks']//a");
                var labelNode = node.SelectSingleNode(".//span[@class='label']");

                if (titleNode != null)
                {
                    var item = new NewContentItem
                    {
                        Title = System.Net.WebUtility.HtmlDecode(titleNode.InnerText.Trim()),
                        Url = titleNode.GetAttributeValue("href", ""),
                        Author = authorNode?.InnerText.Trim() ?? "",
                        Time = timeNode?.GetAttributeValue("data-short", "") ?? "",
                        Forum = forumNode?.InnerText.Trim() ?? "",
                        Label = labelNode?.InnerText.Trim() ?? "",
                        LabelColor = labelNode?.GetAttributeValue("class", "").Contains("yellow") == true ? "Gold" :
                                    labelNode?.GetAttributeValue("class", "").Contains("skyBlue") == true ? "SkyBlue" : "Gray"
                    };

                    var replyText = replyNode?.InnerText.Trim() ?? "";
                    if (replyText.Contains("回复"))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(replyText, @"(\d+)");
                        if (match.Success && int.TryParse(match.Groups[1].Value, out int replies))
                        {
                            item.ReplyCount = replies;
                        }
                    }

                    items.Add(item);
                }
            }

            System.Diagnostics.Debug.WriteLine($"HTML解析到 {items.Count} 个新主题");
            return items;
        }

        // 解析新资源
        private List<NewContentItem> ParseNewResources()
        {
            var items = new List<NewContentItem>();
            var resourceNodes = _htmlDoc.DocumentNode.SelectNodes("//div[@data-widget-key='resource']//li[@class='block-row']");

            if (resourceNodes == null) return items;

            foreach (var node in resourceNodes.Take(5))
            {
                var titleNode = node.SelectSingleNode(".//div[@class='contentRow-main']//a");
                var descNode = node.SelectSingleNode(".//div[@class='contentRow-lesser']");
                var authorNode = node.SelectSingleNode(".//ul[@class='listInline listInline--bullet']/li[1]");
                var timeNode = node.SelectSingleNode(".//time");
                var avatarNode = node.SelectSingleNode(".//img");
                var labelNode = node.SelectSingleNode(".//span[contains(@class,'resTag')]");

                if (titleNode != null)
                {
                    var avatarSrc = avatarNode?.GetAttributeValue("src", "");
                    if (!string.IsNullOrEmpty(avatarSrc) && avatarSrc.StartsWith("./"))
                        avatarSrc = BASE_URL + avatarSrc.Substring(2);

                    items.Add(new NewContentItem
                    {
                        Title = System.Net.WebUtility.HtmlDecode(titleNode.InnerText.Trim()),
                        Url = titleNode.GetAttributeValue("href", ""),
                        Author = authorNode?.InnerText.Trim() ?? "",
                        Time = timeNode?.GetAttributeValue("data-short", "") ?? "",
                        Description = descNode?.InnerText.Trim() ?? "",
                        AvatarUrl = avatarSrc ?? "",
                        Label = labelNode?.InnerText.Trim() ?? ""
                    });
                }
            }

            return items;
        }

        // 解析新回复
        private List<NewContentItem> ParseNewReplies()
        {
            var items = new List<NewContentItem>();
            var replyNodes = _htmlDoc.DocumentNode.SelectNodes("//div[@data-widget-key='post']//li[@class='block-row']");

            if (replyNodes == null) return items;

            foreach (var node in replyNodes.Take(5))
            {
                var titleNode = node.SelectSingleNode(".//div[@class='contentRow-main']//a");
                var authorNode = node.SelectSingleNode(".//ul[@class='listInline listInline--bullet']/li[1]");
                var timeNode = node.SelectSingleNode(".//time");
                var forumNode = node.SelectSingleNode(".//div[@class='contentRow-minor contentRow-minor--hideLinks']//a");

                if (titleNode != null)
                {
                    items.Add(new NewContentItem
                    {
                        Title = System.Net.WebUtility.HtmlDecode(titleNode.InnerText.Trim()),
                        Url = titleNode.GetAttributeValue("href", ""),
                        Author = authorNode?.InnerText.Trim() ?? "",
                        Time = timeNode?.GetAttributeValue("data-short", "") ?? "",
                        Forum = forumNode?.InnerText.Trim() ?? ""
                    });
                }
            }

            return items;
        }

        // 解析在线会员
        private List<OnlineMember> ParseOnlineMembers()
        {
            // 优先使用JavaScript获取的数据
            if (_jsOnlineMembers != null && _jsOnlineMembers.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"使用JavaScript数据: {_jsOnlineMembers.Count} 个在线会员");
                return _jsOnlineMembers;
            }

            System.Diagnostics.Debug.WriteLine("JavaScript数据为空，尝试HTML解析");

            // 回退到HTML解析（原有代码）
            var members = new List<OnlineMember>();
            var memberNodes = _htmlDoc.DocumentNode.SelectNodes("//div[@data-widget-key='online_list']//ul[@class='listInline listInline--comma']/li/a");

            if (memberNodes == null)
            {
                // 尝试备用选择器
                memberNodes = _htmlDoc.DocumentNode.SelectNodes("//div[@data-widget-key='onlineuser']//ul[@class='listInline listInline--comma']/li/a");
            }

            if (memberNodes == null)
            {
                System.Diagnostics.Debug.WriteLine("HTML解析也失败：未找到在线会员节点");
                return members;
            }

            foreach (var node in memberNodes.Take(20))
            {
                var userId = node.GetAttributeValue("data-user-id", "");
                members.Add(new OnlineMember
                {
                    Username = node.InnerText.Trim(),
                    Url = node.GetAttributeValue("href", ""),
                    UserId = int.TryParse(userId, out int id) ? id : 0
                });
            }

            System.Diagnostics.Debug.WriteLine($"HTML解析到 {members.Count} 个在线会员");
            return members;
        }

        // 解析热门内容
        private List<TrendingItem> ParseTrendingContent()
        {
            var items = new List<TrendingItem>();
            var trendingNodes = _htmlDoc.DocumentNode.SelectNodes("//div[@data-widget-key='forum_overview_trending_content']//li[@class='block-row']");

            if (trendingNodes == null) return items;

            foreach (var node in trendingNodes.Take(5))
            {
                var titleNode = node.SelectSingleNode(".//div[@class='contentRow-main']//a");
                var authorNode = node.SelectSingleNode(".//ul[@class='listInline listInline--bullet']/li[1]");
                var timeNode = node.SelectSingleNode(".//time");
                var ratingNode = node.SelectSingleNode(".//span[@class='ratingStars']");
                var thumbNode = node.SelectSingleNode(".//img");

                if (titleNode != null)
                {
                    var thumbSrc = thumbNode?.GetAttributeValue("src", "");
                    if (!string.IsNullOrEmpty(thumbSrc) && thumbSrc.StartsWith("./"))
                        thumbSrc = BASE_URL + thumbSrc.Substring(2);

                    var rating = 0.0;
                    if (ratingNode != null)
                    {
                        var titleAttr = ratingNode.GetAttributeValue("title", "");
                        if (double.TryParse(titleAttr.Split(' ')[0], out var r))
                        {
                            rating = r;
                        }
                    }

                    items.Add(new TrendingItem
                    {
                        Title = System.Net.WebUtility.HtmlDecode(titleNode.InnerText.Trim()),
                        Url = titleNode.GetAttributeValue("href", ""),
                        Author = authorNode?.InnerText.Trim() ?? "",
                        UpdateTime = timeNode?.InnerText.Trim() ?? "",
                        Rating = rating,
                        ThumbnailUrl = thumbSrc ?? ""
                    });
                }
            }

            return items;
        }

        // 解析论坛统计
        private ForumStats ParseForumStats()
        {
            // 优先使用JavaScript获取的数据
            if (_jsStats != null && _jsStats.ThreadCount > 0)
            {
                System.Diagnostics.Debug.WriteLine($"使用JavaScript数据: 主题{_jsStats.ThreadCount}, 在线{_jsStats.OnlineTotal}");
                return _jsStats;
            }

            System.Diagnostics.Debug.WriteLine("JavaScript数据为空，尝试HTML解析");

            // 回退到HTML解析（原有代码保持不变）
            var stats = new ForumStats();

            var statsWidget = _htmlDoc.DocumentNode.SelectSingleNode("//div[@data-widget-key='forum_statistics']");
            if (statsWidget != null)
            {
                var threadsDl = statsWidget.SelectSingleNode(".//dl[@class='pairs pairs--justified count--threads']/dd");
                var messagesDl = statsWidget.SelectSingleNode(".//dl[@class='pairs pairs--justified count--messages']/dd");
                var usersDl = statsWidget.SelectSingleNode(".//dl[@class='pairs pairs--justified count--users']/dd");
                var latestUserNode = statsWidget.SelectSingleNode(".//dl[@class='pairs pairs--justified'][dt[text()='最新用户']]//a");

                if (threadsDl != null)
                {
                    var text = threadsDl.InnerText.Trim().Replace(",", "");
                    if (int.TryParse(text, out int threads))
                        stats.ThreadCount = threads;
                }

                if (messagesDl != null)
                {
                    var text = messagesDl.InnerText.Trim().Replace(",", "");
                    if (int.TryParse(text, out int messages))
                        stats.MessageCount = messages;
                }

                if (usersDl != null)
                {
                    var text = usersDl.InnerText.Trim().Replace(",", "");
                    if (int.TryParse(text, out int users))
                        stats.UserCount = users;
                }

                if (latestUserNode != null)
                {
                    stats.LatestUser = latestUserNode.InnerText.Trim();
                    stats.LatestUserUrl = latestUserNode.GetAttributeValue("href", "");
                }
            }

            // 解析在线统计（尝试多个可能的选择器）
            var onlineWidget = _htmlDoc.DocumentNode.SelectSingleNode("//div[@data-widget-key='online_list']");
            if (onlineWidget == null)
            {
                onlineWidget = _htmlDoc.DocumentNode.SelectSingleNode("//div[@data-widget-key='onlineuser']");
            }

            if (onlineWidget != null)
            {
                var footerText = onlineWidget.SelectSingleNode(".//span[@class='block-footer-counter']")?.InnerText ?? "";
                var match = System.Text.RegularExpressions.Regex.Match(footerText, @"在线：(\d+)\s*（用户：(\d+),\s*游客：(\d+)）");
                if (match.Success)
                {
                    if (int.TryParse(match.Groups[1].Value, out int total))
                        stats.OnlineTotal = total;
                    if (int.TryParse(match.Groups[2].Value, out int members))
                        stats.OnlineMembers = members;
                    if (int.TryParse(match.Groups[3].Value, out int guests))
                        stats.OnlineGuests = guests;
                }
            }

            System.Diagnostics.Debug.WriteLine($"HTML解析到论坛统计: 主题{stats.ThreadCount}, 在线{stats.OnlineTotal}");
            return stats;
        }

        private List<SponsorLink> ParseSponsors()
        {
            var sponsors = new List<SponsorLink>();

            // 友情链接
            var friendLinks = _htmlDoc.DocumentNode.SelectNodes("//div[@data-widget-key='friend_link']//a");
            if (friendLinks != null)
            {
                foreach (var node in friendLinks)
                {
                    var imgNode = node.SelectSingleNode(".//img");
                    if (imgNode != null)
                    {
                        var imgSrc = imgNode.GetAttributeValue("src", "");
                        if (imgSrc.StartsWith("./"))
                            imgSrc = BASE_URL + imgSrc.Substring(2);
                        else if (imgSrc.StartsWith("/"))
                            imgSrc = BASE_URL + imgSrc.TrimStart('/');

                        sponsors.Add(new SponsorLink
                        {
                            Title = node.GetAttributeValue("title", "") ?? imgNode.GetAttributeValue("alt", ""),
                            Url = node.GetAttributeValue("href", ""),
                            ImageUrl = imgSrc
                        });
                    }
                }
            }

            // 合作伙伴
            var partnerLinks = _htmlDoc.DocumentNode.SelectNodes("//div[@data-widget-key='partner_list']//a");
            if (partnerLinks != null)
            {
                foreach (var node in partnerLinks)
                {
                    var imgNode = node.SelectSingleNode(".//img");
                    if (imgNode != null)
                    {
                        var imgSrc = imgNode.GetAttributeValue("src", "");
                        if (imgSrc.StartsWith("./"))
                            imgSrc = BASE_URL + imgSrc.Substring(2);
                        else if (imgSrc.StartsWith("/"))
                            imgSrc = BASE_URL + imgSrc.TrimStart('/');

                        sponsors.Add(new SponsorLink
                        {
                            Title = node.GetAttributeValue("title", "") ?? imgNode.GetAttributeValue("alt", ""),
                            Url = node.GetAttributeValue("href", ""),
                            ImageUrl = imgSrc
                        });
                    }
                }
            }

            return sponsors;
        }
        #endregion

        #region 右侧面板渲染

        private void RenderAdBanners()
        {
            var banners = ParseAdBanners();

            foreach (var banner in banners)
            {
                var image = new Image
                {
                    Source = new BitmapImage(new Uri(banner.ImageUrl)),
                    Stretch = Stretch.UniformToFill,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                var border = new Border
                {
                    Child = image,
                    CornerRadius = new CornerRadius(8),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                var button = new Button
                {
                    Content = border,
                    Background = null,
                    BorderBrush = null,
                    Padding = new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Tag = banner.LinkUrl
                };
                button.Click += AdBanner_Click;

                // 动态设置广告图片高度
                button.SizeChanged += (sender, e) =>
                {
                    double width = e.NewSize.Width;
                    if (width > 0)
                    {
                        double aspectRatio = 279.97 / 89.59;
                        double height = width / aspectRatio;
                        image.Height = height;
                    }
                };

                _rightPanel.Children.Add(button);
            }
        }

        private void RenderCheckInPanel()
        {
            // 检查登录状态
            if (!IsUserLoggedIn()) return;

            var checkInInfo = ParseCheckInInfo();

            _checkInCard = new Grid
            {
                Background = (Brush)Application.Current.Resources["SystemControlAcrylicElementBrush"],
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var stack = new StackPanel
            {
                Spacing = 12
            };

            // 标题
            var title = new TextBlock
            {
                Text = "每日签到",
                FontSize = 18,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(title);

            // 图标和状态
            var statusStack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 8
            };

            _checkInIcon = new FontIcon
            {
                Glyph = checkInInfo.IsCheckedIn ? "\uE73E" : "\uE823", // Checked : Clock
                FontSize = 32
            };
            statusStack.Children.Add(_checkInIcon);

            _checkInStatusText = new TextBlock
            {
                Text = checkInInfo.IsCheckedIn ? "今日签到已完成" : "今日尚未签到",
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            statusStack.Children.Add(_checkInStatusText);

            stack.Children.Add(statusStack);

            // 签到按钮（未签到时显示）
            _checkInButton = new Button
            {
                Content = "签到",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Style = (Style)Application.Current.Resources["AccentButtonStyle"],
                Visibility = checkInInfo.IsCheckedIn ? Visibility.Collapsed : Visibility.Visible
            };
            _checkInButton.Click += CheckInButton_Click;
            stack.Children.Add(_checkInButton);

            // 今日签到人数（已签到时显示）
            _checkInCountText = new TextBlock
            {
                Text = $"今日有 {checkInInfo.TodayCount} 人签到",
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.Gray),
                HorizontalAlignment = HorizontalAlignment.Center,
                Visibility = checkInInfo.IsCheckedIn ? Visibility.Visible : Visibility.Collapsed
            };
            stack.Children.Add(_checkInCountText);

            _checkInCard.Children.Add(stack);
            _rightPanel.Children.Add(_checkInCard);
        }

        private void RenderGoldStats()
        {
            // 检查登录状态
            if (!IsUserLoggedIn()) return;

            var checkInInfo = ParseCheckInInfo();

            var card = new Grid
            {
                Background = (Brush)Application.Current.Resources["SystemControlAcrylicElementBrush"],
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var stack = new StackPanel
            {
                Spacing = 12
            };

            // 标题
            var title = new TextBlock
            {
                Text = "金粒",
                FontSize = 18,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(title);

            // 签到次数
            var daysStack = new StackPanel
            {
                Spacing = 4
            };
            var daysLabel = new TextBlock
            {
                Text = "签到次数",
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.Gray),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var daysValue = new TextBlock
            {
                Text = $"{checkInInfo.TotalDays} 天",
                FontSize = 20,
                FontWeight = Windows.UI.Text.FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            daysStack.Children.Add(daysLabel);
            daysStack.Children.Add(daysValue);
            stack.Children.Add(daysStack);

            // 本月奖励
            var rewardStack = new StackPanel
            {
                Spacing = 4
            };
            var rewardLabel = new TextBlock
            {
                Text = "本月奖励",
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.Gray),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var rewardValue = new TextBlock
            {
                Text = $"{checkInInfo.MonthlyReward} 金粒",
                FontSize = 20,
                FontWeight = Windows.UI.Text.FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            rewardStack.Children.Add(rewardLabel);
            rewardStack.Children.Add(rewardValue);
            stack.Children.Add(rewardStack);

            card.Children.Add(stack);
            _rightPanel.Children.Add(card);
        }

        // 渲染新内容Tab面板
        private void RenderNewContentTabs()
        {
            var card = new Grid
            {
                Background = (Brush)Application.Current.Resources["SystemControlAcrylicElementBrush"],
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var mainStack = new StackPanel();

            // Tab导航栏
            var tabNav = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
                Padding = new Thickness(0),
                Height = 48
            };

            var tabStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var tabs = new[] { "新主题", "新资源", "新回复" };
            var tabButtons = new List<Button>();

            for (int i = 0; i < tabs.Length; i++)
            {
                var tabButton = new Button
                {
                    Content = tabs[i],
                    Background = null,
                    BorderBrush = null,
                    Foreground = i == 0 ? new SolidColorBrush((Color)Application.Current.Resources["SystemAccentColor"]) :
                                          new SolidColorBrush(Colors.Gray),
                    FontWeight = i == 0 ? Windows.UI.Text.FontWeights.SemiBold : Windows.UI.Text.FontWeights.Normal,
                    Padding = new Thickness(16, 8, 16, 8),
                    Tag = i
                };
                tabButtons.Add(tabButton);
                tabStack.Children.Add(tabButton);
            }

            tabNav.Children.Add(tabStack);
            mainStack.Children.Add(tabNav);

            // Tab内容容器
            var contentGrid = new Grid
            {
                Padding = new Thickness(12)
            };

            var threadsPanel = CreateNewThreadsPanel();
            var resourcesPanel = CreateNewResourcesPanel();
            var repliesPanel = CreateNewRepliesPanel();

            threadsPanel.Visibility = Visibility.Visible;
            resourcesPanel.Visibility = Visibility.Collapsed;
            repliesPanel.Visibility = Visibility.Collapsed;

            contentGrid.Children.Add(threadsPanel);
            contentGrid.Children.Add(resourcesPanel);
            contentGrid.Children.Add(repliesPanel);

            mainStack.Children.Add(contentGrid);
            card.Children.Add(mainStack);

            // Tab切换事件
            for (int i = 0; i < tabButtons.Count; i++)
            {
                var button = tabButtons[i];
                var index = i;
                button.Click += (s, e) =>
                {
                    // 更新按钮样式
                    foreach (var btn in tabButtons)
                    {
                        var btnIndex = (int)btn.Tag;
                        btn.Foreground = btnIndex == index ?
                            new SolidColorBrush((Color)Application.Current.Resources["SystemAccentColor"]) :
                            new SolidColorBrush(Colors.Gray);
                        btn.FontWeight = btnIndex == index ?
                            Windows.UI.Text.FontWeights.SemiBold :
                            Windows.UI.Text.FontWeights.Normal;
                    }

                    // 切换面板
                    threadsPanel.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
                    resourcesPanel.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
                    repliesPanel.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
                };
            }

            _rightPanel.Children.Add(card);
        }

        private StackPanel CreateNewThreadsPanel()
        {
            var panel = new StackPanel { Spacing = 8 };
            var threads = ParseNewThreads();

            foreach (var thread in threads)
            {
                var item = new StackPanel { Spacing = 4 };

                var titleStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

                if (!string.IsNullOrEmpty(thread.Label))
                {
                    var label = new Border
                    {
                        Background = new SolidColorBrush(thread.LabelColor == "Gold" ? Colors.Gold :
                                                         thread.LabelColor == "SkyBlue" ? Colors.SkyBlue : Colors.Gray),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(6, 2, 6, 2)
                    };
                    label.Child = new TextBlock
                    {
                        Text = thread.Label,
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Colors.White)
                    };
                    titleStack.Children.Add(label);
                }

                var titleLink = new HyperlinkButton
                {
                    Content = thread.Title,
                    FontSize = 13,
                    Padding = new Thickness(0),
                    Tag = thread.Url
                };
                titleLink.Click += (s, e) => NavigateToUrl((s as HyperlinkButton)?.Tag as string);
                titleStack.Children.Add(titleLink);

                item.Children.Add(titleStack);

                var infoText = new TextBlock
                {
                    Text = $"{thread.Author} · {thread.Time} · 回复: {thread.ReplyCount} · {thread.Forum}",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Colors.Gray)
                };
                item.Children.Add(infoText);

                var separator = new Rectangle
                {
                    Height = 1,
                    Fill = new SolidColorBrush(Color.FromArgb(30, 128, 128, 128)),
                    Margin = new Thickness(0, 4, 0, 0)
                };
                item.Children.Add(separator);

                panel.Children.Add(item);
            }

            return panel;
        }

        private StackPanel CreateNewResourcesPanel()
        {
            var panel = new StackPanel { Spacing = 8 };
            var resources = ParseNewResources();

            foreach (var resource in resources)
            {
                var item = new Grid();
                item.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                item.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                item.ColumnSpacing = 8;

                if (!string.IsNullOrEmpty(resource.AvatarUrl))
                {
                    var avatar = new Image
                    {
                        Source = new BitmapImage(new Uri(resource.AvatarUrl)),
                        Width = 40,
                        Height = 40,
                        Stretch = Stretch.UniformToFill
                    };
                    var border = new Border
                    {
                        Child = avatar,
                        CornerRadius = new CornerRadius(4),
                        Width = 40,
                        Height = 40
                    };
                    Grid.SetColumn(border, 0);
                    item.Children.Add(border);
                }

                var contentStack = new StackPanel { Spacing = 4 };
                Grid.SetColumn(contentStack, 1);

                var titleLink = new HyperlinkButton
                {
                    Content = resource.Title,
                    FontSize = 13,
                    Padding = new Thickness(0),
                    Tag = resource.Url
                };
                titleLink.Click += (s, e) => NavigateToUrl((s as HyperlinkButton)?.Tag as string);
                contentStack.Children.Add(titleLink);

                if (!string.IsNullOrEmpty(resource.Description))
                {
                    var desc = new TextBlock
                    {
                        Text = resource.Description,
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Colors.Gray),
                        MaxLines = 2,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    contentStack.Children.Add(desc);
                }

                var infoText = new TextBlock
                {
                    Text = $"{resource.Author} · {resource.Time}",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Colors.Gray)
                };
                contentStack.Children.Add(infoText);

                item.Children.Add(contentStack);

                var separator = new Rectangle
                {
                    Height = 1,
                    Fill = new SolidColorBrush(Color.FromArgb(30, 128, 128, 128)),
                    Margin = new Thickness(0, 8, 0, 0)
                };

                var wrapper = new StackPanel { Spacing = 8 };
                wrapper.Children.Add(item);
                wrapper.Children.Add(separator);

                panel.Children.Add(wrapper);
            }

            return panel;
        }

        private StackPanel CreateNewRepliesPanel()
        {
            var panel = new StackPanel { Spacing = 8 };
            var replies = ParseNewReplies();

            foreach (var reply in replies)
            {
                var item = new StackPanel { Spacing = 4 };

                var titleLink = new HyperlinkButton
                {
                    Content = reply.Title,
                    FontSize = 13,
                    Padding = new Thickness(0),
                    Tag = reply.Url
                };
                titleLink.Click += (s, e) => NavigateToUrl((s as HyperlinkButton)?.Tag as string);
                item.Children.Add(titleLink);

                var infoText = new TextBlock
                {
                    Text = $"{reply.Author} · {reply.Time} · {reply.Forum}",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Colors.Gray)
                };
                item.Children.Add(infoText);

                var separator = new Rectangle
                {
                    Height = 1,
                    Fill = new SolidColorBrush(Color.FromArgb(30, 128, 128, 128)),
                    Margin = new Thickness(0, 4, 0, 0)
                };
                item.Children.Add(separator);

                panel.Children.Add(item);
            }

            return panel;
        }

        // 渲染在线会员
        private void RenderOnlineMembers()
        {
            try
            {
                var members = ParseOnlineMembers();
                var stats = ParseForumStats();

                System.Diagnostics.Debug.WriteLine($"RenderOnlineMembers: 会员数={members.Count}, 在线总数={stats.OnlineTotal}");

                if (members.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("在线会员列表为空，跳过渲染");
                    return;
                }

                var card = new Grid
                {
                    Background = (Brush)Application.Current.Resources["SystemControlAcrylicElementBrush"],
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(16),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                var stack = new StackPanel { Spacing = 8 };

                var title = new TextBlock
                {
                    Text = "在线会员",
                    FontSize = 16,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold
                };
                stack.Children.Add(title);

                var memberWrap = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12
                };

                for (int i = 0; i < members.Count; i++)
                {
                    var member = members[i];
                    var link = new Hyperlink();
                    link.Inlines.Add(new Run { Text = member.Username });
                    link.Click += (s, e) => NavigateToUrl(member.Url);

                    memberWrap.Inlines.Add(link);

                    if (i < members.Count - 1)
                    {
                        memberWrap.Inlines.Add(new Run { Text = ", " });
                    }
                }

                stack.Children.Add(memberWrap);

                var footer = new TextBlock
                {
                    Text = $"在线：{stats.OnlineTotal} （用户：{stats.OnlineMembers}, 游客：{stats.OnlineGuests}）",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Colors.Gray),
                    Margin = new Thickness(0, 8, 0, 0)
                };
                stack.Children.Add(footer);

                card.Children.Add(stack);
                _rightPanel.Children.Add(card);

                System.Diagnostics.Debug.WriteLine("在线会员卡片已添加到右侧面板");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RenderOnlineMembers 错误: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"堆栈: {ex.StackTrace}");
            }
        }

        // 渲染热门内容
        private void RenderTrendingContent()
        {
            try
            {
                var items = ParseTrendingContent();

                System.Diagnostics.Debug.WriteLine($"RenderTrendingContent: 热门内容数={items.Count}");

                if (items.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("热门内容列表为空，跳过渲染");
                    return;
                }

                var card = new Grid
                {
                    Background = (Brush)Application.Current.Resources["SystemControlAcrylicElementBrush"],
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(16),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                var stack = new StackPanel { Spacing = 8 };

                var title = new TextBlock
                {
                    Text = "热门内容",
                    FontSize = 16,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 8)
                };
                stack.Children.Add(title);

                foreach (var item in items)
                {
                    var itemGrid = new Grid();
                    itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    itemGrid.ColumnSpacing = 8;

                    if (!string.IsNullOrEmpty(item.ThumbnailUrl))
                    {
                        var thumb = new Image
                        {
                            Source = new BitmapImage(new Uri(item.ThumbnailUrl)),
                            Width = 48,
                            Height = 48,
                            Stretch = Stretch.UniformToFill
                        };
                        var border = new Border
                        {
                            Child = thumb,
                            CornerRadius = new CornerRadius(4),
                            Width = 48,
                            Height = 48
                        };
                        Grid.SetColumn(border, 0);
                        itemGrid.Children.Add(border);
                    }

                    var contentStack = new StackPanel { Spacing = 4 };
                    Grid.SetColumn(contentStack, 1);

                    var titleLink = new HyperlinkButton
                    {
                        Content = item.Title,
                        FontSize = 13,
                        Padding = new Thickness(0),
                        Tag = item.Url
                    };
                    titleLink.Click += (s, e) => NavigateToUrl((s as HyperlinkButton)?.Tag as string);
                    contentStack.Children.Add(titleLink);

                    var infoText = new TextBlock
                    {
                        Text = $"{item.Author} · {item.UpdateTime}",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Colors.Gray)
                    };
                    contentStack.Children.Add(infoText);

                    if (item.Rating > 0)
                    {
                        var ratingText = new TextBlock
                        {
                            Text = $"⭐ {item.Rating:F2}",
                            FontSize = 11,
                            Foreground = new SolidColorBrush(Colors.Gold)
                        };
                        contentStack.Children.Add(ratingText);
                    }

                    itemGrid.Children.Add(contentStack);

                    var separator = new Rectangle
                    {
                        Height = 1,
                        Fill = new SolidColorBrush(Color.FromArgb(30, 128, 128, 128)),
                        Margin = new Thickness(0, 8, 0, 0)
                    };

                    var wrapper = new StackPanel { Spacing = 8 };
                    wrapper.Children.Add(itemGrid);
                    wrapper.Children.Add(separator);

                    stack.Children.Add(wrapper);
                }

                card.Children.Add(stack);
                _rightPanel.Children.Add(card);

                System.Diagnostics.Debug.WriteLine("热门内容卡片已添加到右侧面板");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RenderTrendingContent 错误: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"堆栈: {ex.StackTrace}");
            }
        }

        // 渲染论坛统计
        private void RenderForumStats()
        {
            var stats = ParseForumStats();

            var card = new Grid
            {
                Background = (Brush)Application.Current.Resources["SystemControlAcrylicElementBrush"],
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var stack = new StackPanel { Spacing = 8 };

            var title = new TextBlock
            {
                Text = "论坛统计",
                FontSize = 16,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };
            stack.Children.Add(title);

            // 主题
            var threadsGrid = new Grid();
            threadsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            threadsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var threadsLabel = new TextBlock { Text = "主题", FontSize = 13 };
            var threadsValue = new TextBlock { Text = stats.ThreadCount.ToString("N0"), FontSize = 13, FontWeight = Windows.UI.Text.FontWeights.SemiBold };
            Grid.SetColumn(threadsLabel, 0);
            Grid.SetColumn(threadsValue, 1);
            threadsGrid.Children.Add(threadsLabel);
            threadsGrid.Children.Add(threadsValue);
            stack.Children.Add(threadsGrid);

            // 消息
            var messagesGrid = new Grid();
            messagesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            messagesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var messagesLabel = new TextBlock { Text = "消息", FontSize = 13 };
            var messagesValue = new TextBlock { Text = stats.MessageCount.ToString("N0"), FontSize = 13, FontWeight = Windows.UI.Text.FontWeights.SemiBold };
            Grid.SetColumn(messagesLabel, 0);
            Grid.SetColumn(messagesValue, 1);
            messagesGrid.Children.Add(messagesLabel);
            messagesGrid.Children.Add(messagesValue);
            stack.Children.Add(messagesGrid);

            // 用户
            var usersGrid = new Grid();
            usersGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            usersGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var usersLabel = new TextBlock { Text = "用户", FontSize = 13 };
            var usersValue = new TextBlock { Text = stats.UserCount.ToString("N0"), FontSize = 13, FontWeight = Windows.UI.Text.FontWeights.SemiBold };
            Grid.SetColumn(usersLabel, 0);
            Grid.SetColumn(usersValue, 1);
            usersGrid.Children.Add(usersLabel);
            usersGrid.Children.Add(usersValue);
            stack.Children.Add(usersGrid);

            // 最新用户
            if (!string.IsNullOrEmpty(stats.LatestUser))
            {
                var latestUserGrid = new Grid();
                latestUserGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                latestUserGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var latestLabel = new TextBlock { Text = "最新用户", FontSize = 13 };
                var latestLink = new HyperlinkButton
                {
                    Content = stats.LatestUser,
                    FontSize = 13,
                    Padding = new Thickness(0),
                    Tag = stats.LatestUserUrl
                };
                latestLink.Click += (s, e) => NavigateToUrl((s as HyperlinkButton)?.Tag as string);

                Grid.SetColumn(latestLabel, 0);
                Grid.SetColumn(latestLink, 1);
                latestUserGrid.Children.Add(latestLabel);
                latestUserGrid.Children.Add(latestLink);
                stack.Children.Add(latestUserGrid);
            }

            card.Children.Add(stack);
            _rightPanel.Children.Add(card);
        }

        private void SponsorCard_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_sponsorGridView?.ItemsPanelRoot is ItemsWrapGrid wrapGrid)
            {
                double totalWidth = e.NewSize.Width - 32; // 减去Padding
                double minItemWidth = 140; // 每个最小宽度
                int columns = Math.Max(1, (int)(totalWidth / minItemWidth));

                double finalWidth = totalWidth / columns;

                wrapGrid.ItemWidth = finalWidth;
                wrapGrid.ItemHeight = 60;
            }
        }
        #endregion

        #region 右侧面板事件

        private void AdBanner_Click(object sender, RoutedEventArgs e)
        {
            var url = (sender as Button)?.Tag as string;
            if (!string.IsNullOrEmpty(url))
            {
                NavigateToUrl(url);
            }
        }

        private async void CheckInButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isCheckingIn) return;

            _isCheckingIn = true;
            _checkInButton.IsEnabled = false;

            try
            {
                // 执行签到
                await PerformCheckIn();

                // 更新UI
                _checkInIcon.Glyph = "\uE73E"; // Checked icon
                _checkInStatusText.Text = "今日签到已完成";
                _checkInButton.Visibility = Visibility.Collapsed;

                // 重新获取签到信息并更新计数
                var checkInInfo = ParseCheckInInfo();
                _checkInCountText.Text = $"今日有 {checkInInfo.TodayCount} 人签到";
                _checkInCountText.Visibility = Visibility.Visible;

                // 刷新金粒统计
                RefreshGoldStats();
            }
            catch (Exception ex)
            {
                var dialog = new ContentDialog
                {
                    Title = "签到失败",
                    Content = $"签到时发生错误：{ex.Message}",
                    CloseButtonText = "确定"
                };
                await dialog.ShowAsync();

                _checkInButton.IsEnabled = true;
            }
            finally
            {
                _isCheckingIn = false;
            }
        }

        private async Task PerformCheckIn()
        {
            // 首先检查页面上是否已经显示已签到
            var checkInWidget = _htmlDoc.DocumentNode.SelectSingleNode("//div[@data-widget-key='daily_sign']");
            if (checkInWidget != null)
            {
                var signedMessage = checkInWidget.SelectSingleNode(".//div[@class='mjc-signed-message']");
                if (signedMessage != null)
                {
                    // 已经签到过了
                    throw new Exception("今日已签到，无需重复签到");
                }
            }

            // 使用HiddenWebView执行签到
            var script = @"
        (function() {
            try {
                // 查找签到表单中的按钮
                var signInBtn = document.querySelector('.mjc-signin-form .button--cta, form[action*=""sign""] button, button.js-signInButton');
                if (signInBtn) {
                    signInBtn.click();
                    return 'success';
                }
                
                // 尝试直接提交表单
                var signInForm = document.querySelector('.mjc-signin-form form, form[action*=""sign""]');
                if (signInForm) {
                    signInForm.submit();
                    return 'success';
                }
                
                return 'button_not_found';
            } catch(e) {
                return 'error: ' + e.message;
            }
        })();
    ";

            var result = await HiddenWebView.CoreWebView2.ExecuteScriptAsync(script);

            if (result.Contains("success"))
            {
                // 等待页面更新
                await Task.Delay(2000);

                // 重新获取HTML
                var html = await HiddenWebView.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML;");
                if (!string.IsNullOrEmpty(html))
                {
                    html = html.Substring(1, html.Length - 2)
                               .Replace("\\u003C", "<")
                               .Replace("\\\"", "\"")
                               .Replace("\\n", "")
                               .Replace("\\r", "")
                               .Replace("\\\\", "\\");

                    _htmlDoc.LoadHtml(html);
                }
            }
            else
            {
                throw new Exception("签到失败：未找到签到按钮。可能您已经签到过了，或页面结构已改变。");
            }
        }

        private void RefreshGoldStats()
        {
            var checkInInfo = ParseCheckInInfo();

            // 找到金粒统计卡片并更新
            foreach (var child in _rightPanel.Children)
            {
                if (child is Grid grid && grid.Children.Count > 0 && grid.Children[0] is StackPanel stack)
                {
                    if (stack.Children.Count > 0 && stack.Children[0] is TextBlock title && title.Text == "金粒")
                    {
                        // 更新签到次数
                        if (stack.Children.Count > 1 && stack.Children[1] is StackPanel daysStack
                            && daysStack.Children.Count > 1 && daysStack.Children[1] is TextBlock daysValue)
                        {
                            daysValue.Text = $"{checkInInfo.TotalDays} 天";
                        }

                        // 更新本月奖励
                        if (stack.Children.Count > 2 && stack.Children[2] is StackPanel rewardStack
                            && rewardStack.Children.Count > 1 && rewardStack.Children[1] is TextBlock rewardValue)
                        {
                            rewardValue.Text = $"{checkInInfo.MonthlyReward} 金粒";
                        }
                        break;
                    }
                }
            }
        }

        private void SponsorLink_Click(object sender, RoutedEventArgs e)
        {
            string url = null;

            if (sender is Button button)
                url = button.Tag as string;
            else if (sender is HyperlinkButton hyperlink)
                url = hyperlink.Tag as string;

            if (!string.IsNullOrEmpty(url))
            {
                NavigateToUrl(url);
            }
        }

        #endregion

        private async void HiddenWebView_NavigationCompleted(WebView2 sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs args)
        {
            try
            {
                // 确保导航成功
                if (!args.IsSuccess)
                {
                    _htmlTaskCompletion?.TrySetException(new Exception($"导航失败: {args.WebErrorStatus}"));
                    return;
                }

                // ⚠️ 关键：等待JavaScript执行完成，让动态内容加载
                await Task.Delay(3000); // 等待3秒让所有widget加载

                // 可选：执行JavaScript检查内容是否加载完成
                var checkScript = @"
            (function() {
                var threadWidget = document.querySelector('[data-widget-key=""thread""]');
                var onlineWidget = document.querySelector('[data-widget-key=""onlineuser""]');
                var statsWidget = document.querySelector('[data-widget-key=""forum_statistics""]');
                
                if (threadWidget && onlineWidget && statsWidget) {
                    var threadItems = threadWidget.querySelectorAll('.block-row').length;
                    var onlineUsers = onlineWidget.querySelectorAll('.listInline--comma li').length;
                    
                    return JSON.stringify({
                        ready: true,
                        threadCount: threadItems,
                        onlineCount: onlineUsers
                    });
                }
                return JSON.stringify({ready: false});
            })();
        ";

                var checkResult = await sender.ExecuteScriptAsync(checkScript);
                System.Diagnostics.Debug.WriteLine($"内容加载检查: {checkResult}");

                // 获取网页 HTML
                var html = await sender.ExecuteScriptAsync("document.documentElement.outerHTML;");

                // 去掉首尾双引号，并处理常见转义字符
                if (!string.IsNullOrEmpty(html) && html.Length >= 2 && html.StartsWith("\"") && html.EndsWith("\""))
                {
                    html = html.Substring(1, html.Length - 2)
                               .Replace("\\u003C", "<")
                               .Replace("\\\"", "\"")
                               .Replace("\\n", "")
                               .Replace("\\r", "")
                               .Replace("\\\\", "\\");
                }

                // 返回 HTML
                _htmlTaskCompletion?.TrySetResult(html);
            }
            catch (Exception ex)
            {
                _htmlTaskCompletion?.TrySetException(ex);
            }
        }

        #region 顶部轮播图
        private async Task RenderTopCarousel()
        {
            var carouselItems = ParseTopCarousel();
            if (carouselItems.Count == 0) return;

            TopCarouselContainer.Children.Clear();

            var carouselHeight = Window.Current.Bounds.Height / 3;

            _mainCarousel = new FlipView
            {
                HorizontalAlignment = HorizontalAlignment.Stretch, // 改为Stretch，不要Center
                VerticalAlignment = VerticalAlignment.Center,
                Height = carouselHeight
            };

            UpdateCarouselGrouping(carouselItems);

            var carouselGrid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch // 容器也要Stretch
            };
            carouselGrid.Children.Add(_mainCarousel);

            MainPage.AttachIndicators(_mainCarousel, carouselGrid, bottomMargin: 16, intervalSeconds: 5);

            TopCarouselContainer.Children.Add(carouselGrid);
        }

        private void UpdateCarouselGrouping(List<CarouselItem> items)
        {
            if (_mainCarousel == null) return;

            _mainCarousel.Items.Clear();

            foreach (var item in items)
            {
                var container = CreateSingleCarouselItem(item);
                _mainCarousel.Items.Add(container);
            }
        }

        private Grid CreateSingleCarouselItem(CarouselItem item)
        {
            var maxHeight = Window.Current.Bounds.Height / 3;

            var image = new Image
            {
                Source = new BitmapImage(new Uri(item.ImageUrl)),
                Stretch = Stretch.Uniform,
                MaxHeight = maxHeight,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var containerGrid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                Height = maxHeight,
                Background = null // 去掉背景
            };

            var border = new Border
            {
                CornerRadius = new CornerRadius(12),
                Child = image,
                Background = null, // 去掉背景
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var button = new Button
            {
                Content = border,
                Background = null, // 去掉背景
                BorderBrush = null,
                Padding = new Thickness(0),
                Tag = item.LinkUrl,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = (VerticalAlignment)HorizontalAlignment.Stretch
            };
            button.Click += CarouselButton_Click;

            containerGrid.Children.Add(button);
            return containerGrid;
        }

        private List<CarouselItem> ParseTopCarousel()
        {
            var items = new List<CarouselItem>();
            var seenImages = new HashSet<string>();

            var imgNodes = _htmlDoc.DocumentNode
                .SelectNodes("//div[@data-widget-key='forum_slide']//img");

            if (imgNodes == null) return items;

            foreach (var img in imgNodes)
            {
                try
                {
                    var imgSrc = img.GetAttributeValue("src", "");

                    if (string.IsNullOrWhiteSpace(imgSrc))
                        continue;

                    if (imgSrc.StartsWith("./"))
                        imgSrc = BASE_URL + imgSrc.Substring(2);
                    else if (imgSrc.StartsWith("/"))
                        imgSrc = BASE_URL + imgSrc.TrimStart('/');

                    // 去重
                    if (seenImages.Contains(imgSrc))
                        continue;

                    var linkNode = img.Ancestors("a").FirstOrDefault();
                    if (linkNode == null)
                        continue;

                    var linkUrl = linkNode.GetAttributeValue("href", "");

                    if (string.IsNullOrWhiteSpace(linkUrl))
                        continue;

                    seenImages.Add(imgSrc);

                    items.Add(new CarouselItem
                    {
                        ImageUrl = imgSrc,
                        LinkUrl = linkUrl,
                        ApplyUrl = ""
                    });
                }
                catch
                {
                }
            }

            return items;
        }
        #endregion

        #region 推荐内容
        private async Task RenderFeaturedContent()
        {
            var featuredItems = ParseFeaturedContent();
            if (featuredItems.Count == 0) return;

            FeaturedCarouselContainer.Children.Clear();

            var carouselHeight = 115;

            _featuredCarousel = new FlipView
            {
                HorizontalAlignment = HorizontalAlignment.Stretch, // 改为Stretch
                VerticalAlignment = VerticalAlignment.Top,
                Height = carouselHeight
            };

            UpdateFeaturedGrouping(featuredItems);

            var carouselGrid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch, // 容器也要Stretch
                VerticalAlignment = VerticalAlignment.Center,
            };
            carouselGrid.Children.Add(_featuredCarousel);

            MainPage.AttachIndicators(_featuredCarousel, carouselGrid, bottomMargin: 8, intervalSeconds: 8);

            FeaturedCarouselContainer.Children.Add(carouselGrid);
        }

        private void UpdateFeaturedGrouping(List<FeaturedItem> items)
        {
            if (_featuredCarousel == null) return;

            _featuredCarousel.Items.Clear();

            // 根据窗口宽度决定显示1个还是2个
            double windowWidth = Window.Current.Bounds.Width;
            bool isSingleColumn = windowWidth < 1000;

            _currentFeaturedItemsPerPage = isSingleColumn ? 1 : 2;

            for (int i = 0; i < items.Count; i += _currentFeaturedItemsPerPage)
            {
                var pageItems = items.Skip(i).Take(_currentFeaturedItemsPerPage).ToList();
                var container = CreateFeaturedPageContainer(pageItems);
                _featuredCarousel.Items.Add(container);
            }

            // 每次更新分页后重新调整广告位置（仅当广告已创建时）
            if (_rightPanel != null)
            {
                try
                {
                    UpdateAdPosition();
                }
                catch
                {
                    // 忽略广告位置更新错误
                }
            }
        }

        private Grid CreateFeaturedPageContainer(List<FeaturedItem> items)
        {
            var grid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch, // 改回Stretch
                VerticalAlignment = VerticalAlignment.Center, // 改为居中，不是Top
                MinHeight = 150 // 设置最小高度与FlipView一致
            };

            grid.ColumnDefinitions.Clear();
            for (int i = 0; i < items.Count; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }
            grid.ColumnSpacing = 12;

            for (int i = 0; i < items.Count; i++)
            {
                var card = CreateFeaturedItemCard(items[i]);
                Grid.SetColumn(card, i);
                grid.Children.Add(card);
            }

            return grid;
        }

        private Grid CreateFeaturedItemCard(FeaturedItem item)
        {
            var card = new Grid
            {
                Style = this.Resources["CardStyle"] as Style,
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch, // 填充父容器
                MinWidth = 300,
                MaxHeight = 140 // 稍微降低一点
            };

            card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 头像固定大小
            var image = new Image
            {
                Source = new BitmapImage(new Uri(item.AvatarUrl)),
                Width = 80,
                Height = 80,
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };

            card.Children.Add(image);

            var contentStack = new StackPanel
            {
                Margin = new Thickness(88, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };

            var titleBlock = new TextBlock
            {
                Text = item.Title,
                FontSize = 16,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            contentStack.Children.Add(titleBlock);

            if (!string.IsNullOrWhiteSpace(item.Description))
            {
                contentStack.Children.Add(new TextBlock
                {
                    Text = item.Description,
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Colors.Gray),
                    TextWrapping = TextWrapping.Wrap,
                    MaxLines = 2,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }

            Grid.SetRow(contentStack, 0);
            card.Children.Add(contentStack);

            var button = new Button
            {
                Background = null,
                BorderBrush = null,
                Padding = new Thickness(0),
                Content = card,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Tag = item.Url
            };
            button.Click += FeaturedItemButton_Click;

            var wrapper = new Grid();
            wrapper.Children.Add(button);

            return wrapper;
        }

        private void RenderFriendLinks()
        {
            var links = ParseFriendLinks();
            if (links.Count == 0) return;

            var card = new Grid
            {
                Background = (Brush)Application.Current.Resources["SystemControlAcrylicElementBrush"],
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 16)
            };

            var title = new TextBlock
            {
                Text = "友情链接",
                FontSize = 16,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 12)
            };

            _friendLinksGridView = new GridView
            {
                IsItemClickEnabled = false,
                SelectionMode = ListViewSelectionMode.None,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            _friendLinksGridView.ItemsPanel = (ItemsPanelTemplate)
                Windows.UI.Xaml.Markup.XamlReader.Load(
                @"<ItemsPanelTemplate 
            xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
            <ItemsWrapGrid Orientation='Horizontal'/>
          </ItemsPanelTemplate>");

            foreach (var link in links)
            {
                if (!string.IsNullOrEmpty(link.ImageUrl))
                {
                    var image = new Image
                    {
                        Source = new BitmapImage(new Uri(link.ImageUrl)),
                        Stretch = Stretch.Uniform,
                        Height = 33
                    };

                    var button = new Button
                    {
                        Content = image,
                        Background = null,
                        BorderBrush = null,
                        Padding = new Thickness(4),
                        Tag = link.Url
                    };

                    ToolTipService.SetToolTip(button, link.Title);
                    button.Click += SponsorLink_Click;
                    _friendLinksGridView.Items.Add(button);
                }
            }

            var container = new StackPanel { Spacing = 8 };
            container.Children.Add(title);
            container.Children.Add(_friendLinksGridView);
            card.Children.Add(container);

            ForumCategoriesSection.Children.Add(card);
            card.SizeChanged += FriendLinksCard_SizeChanged;
        }

        private void RenderPartnerLinks()
        {
            var links = ParsePartnerLinks();
            if (links.Count == 0) return;

            var card = new Grid
            {
                Background = (Brush)Application.Current.Resources["SystemControlAcrylicElementBrush"],
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 16)
            };

            var title = new TextBlock
            {
                Text = "合作伙伴",
                FontSize = 16,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 12)
            };

            _partnerLinksGridView = new GridView
            {
                IsItemClickEnabled = false,
                SelectionMode = ListViewSelectionMode.None,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            _partnerLinksGridView.ItemsPanel = (ItemsPanelTemplate)
                Windows.UI.Xaml.Markup.XamlReader.Load(
                @"<ItemsPanelTemplate 
            xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
            <ItemsWrapGrid Orientation='Horizontal'/>
          </ItemsPanelTemplate>");

            foreach (var link in links)
            {
                if (!string.IsNullOrEmpty(link.ImageUrl))
                {
                    var image = new Image
                    {
                        Source = new BitmapImage(new Uri(link.ImageUrl)),
                        Stretch = Stretch.Uniform,
                        Height = 33
                    };

                    var button = new Button
                    {
                        Content = image,
                        Background = null,
                        BorderBrush = null,
                        Padding = new Thickness(4),
                        Tag = link.Url
                    };

                    ToolTipService.SetToolTip(button, link.Title);
                    button.Click += SponsorLink_Click;
                    _partnerLinksGridView.Items.Add(button);
                }
            }

            var container = new StackPanel { Spacing = 8 };
            container.Children.Add(title);
            container.Children.Add(_partnerLinksGridView);
            card.Children.Add(container);

            ForumCategoriesSection.Children.Add(card);
            card.SizeChanged += PartnerLinksCard_SizeChanged;
        }

        private void PartnerLinksCard_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_partnerLinksGridView?.ItemsPanelRoot is ItemsWrapGrid wrapGrid)
            {
                double totalWidth = e.NewSize.Width - 32;
                double minItemWidth = 120;
                int columns = Math.Max(1, (int)(totalWidth / minItemWidth));
                double finalWidth = totalWidth / columns;
                wrapGrid.ItemWidth = finalWidth;
                wrapGrid.ItemHeight = 50;
            }
        }

        // 渲染底部横幅广告
        private void RenderBottomBanner()
        {
            var banner = ParseBottomBanner();
            if (banner == null) return;

            var image = new Image
            {
                Source = new BitmapImage(new Uri(banner.ImageUrl)),
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var border = new Border
            {
                Child = image,
                CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 16)
            };

            var button = new Button
            {
                Content = border,
                Background = null,
                BorderBrush = null,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Tag = banner.LinkUrl
            };
            button.Click += AdBanner_Click;

            // 动态设置高度（按813.94 × 90.44的比例）
            button.SizeChanged += (sender, e) =>
            {
                double width = e.NewSize.Width;
                if (width > 0)
                {
                    double aspectRatio = 813.94 / 90.44;
                    double height = width / aspectRatio;
                    image.Height = height;
                }
            };

            ForumCategoriesSection.Children.Add(button);
        }

        private void FriendLinksCard_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_friendLinksGridView?.ItemsPanelRoot is ItemsWrapGrid wrapGrid)
            {
                double totalWidth = e.NewSize.Width - 32;
                double minItemWidth = 120;
                int columns = Math.Max(1, (int)(totalWidth / minItemWidth));
                double finalWidth = totalWidth / columns;
                wrapGrid.ItemWidth = finalWidth;
                wrapGrid.ItemHeight = 50;
            }
        }

        private List<FeaturedItem> ParseFeaturedContent()
        {
            var items = new List<FeaturedItem>();
            var carouselItems = _htmlDoc.DocumentNode.SelectNodes("//div[@data-widget-key='featured_content']//li[@class='carousel-container f-carousel__slide']");

            if (carouselItems == null) return items;

            foreach (var node in carouselItems.Take(20)) // 限制最多20个
            {
                var titleNode = node.SelectSingleNode(".//h4[@class='contentRow-title']//a");
                var avatarNode = node.SelectSingleNode(".//img");
                var descNode = node.SelectSingleNode(".//div[@class='contentRow-lesser']");
                var authorNode = node.SelectSingleNode(".//a[@class='username' or contains(@class, 'username ')]");
                var timeNode = node.SelectSingleNode(".//time");
                var ratingNode = node.SelectSingleNode(".//span[@class='ratingStars']");

                if (titleNode != null)
                {
                    var avatarSrc = avatarNode?.GetAttributeValue("src", "");
                    if (string.IsNullOrEmpty(avatarSrc))
                    {
                        avatarSrc = "ms-appx:///Assets/default-avatar.png";
                    }
                    else if (avatarSrc.StartsWith("/"))
                    {
                        avatarSrc = BASE_URL + avatarSrc.TrimStart('/');
                    }

                    var rating = 0.0;
                    if (ratingNode != null)
                    {
                        var titleAttr = ratingNode.GetAttributeValue("title", "");
                        if (double.TryParse(titleAttr.Split(' ')[0], out var r))
                        {
                            rating = r;
                        }
                    }

                    items.Add(new FeaturedItem
                    {
                        Title = titleNode.InnerText.Trim(),
                        Url = titleNode.GetAttributeValue("href", ""),
                        AvatarUrl = avatarSrc,
                        Description = descNode?.InnerText.Trim() ?? "",
                        Author = authorNode?.InnerText.Trim() ?? "",
                        UpdateTime = timeNode?.InnerText.Trim() ?? "",
                        Rating = rating
                    });
                }
            }

            return items;
        }
        #endregion

        #region 论坛板块
        private async Task RenderForumCategories()
        {
            var categories = ParseForumCategories();
            ForumCategoriesSection.Children.Clear();

            foreach (var category in categories)
            {
                var categoryCard = new Grid
                {
                    Style = this.Resources["CardStyle"] as Style,
                    Margin = new Thickness(0, 0, 0, 16)
                };

                var stack = new StackPanel();

                // 分类标题
                var titleBlock = new TextBlock
                {
                    Text = category.Name,
                    FontSize = 20,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 16)
                };
                stack.Children.Add(titleBlock);

                // 子论坛/分类
                foreach (var forum in category.Forums)
                {
                    var forumButton = CreateForumButton(forum);
                    stack.Children.Add(forumButton);
                }

                categoryCard.Children.Add(stack);
                ForumCategoriesSection.Children.Add(categoryCard);
            }

            // 渲染友情链接、合作伙伴和底部横幅
            RenderFriendLinks();
            RenderPartnerLinks();
            RenderBottomBanner();
        }

        private Button CreateForumButton(ForumNode forum)
        {
            var button = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = null,
                BorderBrush = null,
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 8),
                Tag = forum.Url
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnSpacing = 12;
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 图标 - 使用ForumNode中的IconGlyph属性
            var icon = new FontIcon
            {
                Glyph = forum.IconGlyph, // 使用动态Icon
                FontSize = 20,
                Margin = new Thickness(0, 0, 12, 0)
            };
            Grid.SetColumn(icon, 0);

            // 内容
            var contentStack = new StackPanel();
            Grid.SetColumn(contentStack, 1);

            var titleBlock = new TextBlock
            {
                Text = forum.Title,
                FontSize = 15,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            contentStack.Children.Add(titleBlock);

            if (!string.IsNullOrWhiteSpace(forum.Description))
            {
                var descBlock = new TextBlock
                {
                    Text = forum.Description,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.Gray),
                    TextWrapping = TextWrapping.Wrap,
                    MaxLines = 2
                };
                contentStack.Children.Add(descBlock);
            }

            // 统计信息
            var statsStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 16
            };
            Grid.SetColumn(statsStack, 2);

            if (forum.ThreadCount > 0)
            {
                var threadsBlock = new TextBlock
                {
                    Text = $"主题: {forum.ThreadCount:N0}",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.Gray)
                };
                statsStack.Children.Add(threadsBlock);
            }

            if (forum.MessageCount > 0)
            {
                var messagesBlock = new TextBlock
                {
                    Text = $"消息: {forum.MessageCount:N0}",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.Gray)
                };
                statsStack.Children.Add(messagesBlock);
            }

            grid.Children.Add(icon);
            grid.Children.Add(contentStack);
            grid.Children.Add(statsStack);

            button.Content = grid;
            button.Click += ForumButton_Click;

            return button;
        }

        private List<ForumCategory> ParseForumCategories()
        {
            var categories = new List<ForumCategory>();
            var categoryNodes = _htmlDoc.DocumentNode.SelectNodes("//div[contains(@class, 'block--category')]");

            if (categoryNodes == null) return categories;

            foreach (var catNode in categoryNodes)
            {
                var categoryName = catNode.SelectSingleNode(".//h2[@class='block-header']//a")?.InnerText.Trim() ?? "未命名分类";

                var category = new ForumCategory
                {
                    Name = categoryName,
                    Forums = new List<ForumNode>()
                };

                // 解析论坛节点（node--forum）
                var forumNodes = catNode.SelectNodes(".//div[contains(@class, 'node--forum')]");
                if (forumNodes != null)
                {
                    foreach (var forumNode in forumNodes)
                    {
                        var titleNode = forumNode.SelectSingleNode(".//h3[@class='node-title']//a");
                        var descNode = forumNode.SelectSingleNode(".//div[@class='node-description']");
                        var statsNode = forumNode.SelectSingleNode(".//div[@class='node-stats']");

                        if (titleNode != null)
                        {
                            var forum = new ForumNode
                            {
                                Title = titleNode.InnerText.Trim(),
                                Url = titleNode.GetAttributeValue("href", ""),
                                Description = descNode?.InnerText.Trim() ?? "",
                                IconGlyph = GetIconForForum(titleNode.InnerText.Trim())
                            };

                            // 解析统计信息
                            if (statsNode != null)
                            {
                                var threadsDl = statsNode.SelectSingleNode(".//dl[dt[text()='主题数']]");
                                var messagesDl = statsNode.SelectSingleNode(".//dl[dt[text()='消息数']]");

                                if (threadsDl != null && int.TryParse(threadsDl.SelectSingleNode(".//dd")?.InnerText.Trim().Replace(",", ""), out var threads))
                                {
                                    forum.ThreadCount = threads;
                                }

                                if (messagesDl != null && int.TryParse(messagesDl.SelectSingleNode(".//dd")?.InnerText.Trim().Replace(",", ""), out var messages))
                                {
                                    forum.MessageCount = messages;
                                }
                            }

                            category.Forums.Add(forum);
                        }
                    }
                }

                // 解析分类节点（node--category），如创思艺海
                var categorySubNodes = catNode.SelectNodes(".//div[contains(@class, 'node--category') and contains(@class, 'node--depth2')]");
                if (categorySubNodes != null)
                {
                    foreach (var subNode in categorySubNodes)
                    {
                        var titleNode = subNode.SelectSingleNode(".//h3[@class='node-title']//a");
                        var descNode = subNode.SelectSingleNode(".//div[@class='node-description']");
                        var statsMetaNode = subNode.SelectSingleNode(".//div[@class='node-statsMeta']");

                        if (titleNode != null)
                        {
                            var forum = new ForumNode
                            {
                                Title = titleNode.InnerText.Trim(),
                                Url = titleNode.GetAttributeValue("href", ""),
                                Description = descNode?.InnerText.Trim() ?? "",
                                IconGlyph = GetIconForForum(titleNode.InnerText.Trim())
                            };

                            // 解析统计信息（分类节点使用不同的结构）
                            if (statsMetaNode != null)
                            {
                                var dls = statsMetaNode.SelectNodes(".//dl");
                                if (dls != null && dls.Count >= 2)
                                {
                                    var threadsText = dls[0].SelectSingleNode(".//dd")?.InnerText.Trim();
                                    var messagesText = dls[1].SelectSingleNode(".//dd")?.InnerText.Trim();

                                    if (!string.IsNullOrEmpty(threadsText))
                                    {
                                        // 处理"1.8K"这样的格式
                                        threadsText = threadsText.Replace("K", "00").Replace(".", "");
                                        if (int.TryParse(threadsText, out var threads))
                                        {
                                            forum.ThreadCount = threads;
                                        }
                                    }

                                    if (!string.IsNullOrEmpty(messagesText))
                                    {
                                        messagesText = messagesText.Replace("K", "00").Replace(".", "");
                                        if (int.TryParse(messagesText, out var messages))
                                        {
                                            forum.MessageCount = messages;
                                        }
                                    }
                                }
                            }

                            category.Forums.Add(forum);
                        }
                    }
                }

                if (category.Forums.Count > 0)
                {
                    categories.Add(category);
                }
            }

            return categories;
        }

        // 添加这个辅助方法，你可以在这里根据论坛标题返回不同的Icon
        private string GetIconForForum(string forumTitle)
        {
            if (forumTitle.Contains("新闻资讯"))
                return "\uE7BC";
            else if (forumTitle.Contains("游戏交流"))
                return "\xE8F2";
            else if (forumTitle.Contains("周边创作"))
                return "\uEE56";
            else if (forumTitle.Contains("基岩游戏资源"))
                return "\uE8EA";
            else if (forumTitle.Contains("Java游戏资源"))
                return "\uEC4E";
            else if (forumTitle.Contains("Bedrock Dedicated Server"))
                return "\uF404";
            else if (forumTitle.Contains("PocketMine"))
                return "\uF133";
            else if (forumTitle.Contains("Nukkit"))
                return "\uE759";
            else if (forumTitle.Contains("其他服务端"))
                return "\uE968";
            else if (forumTitle.Contains("游戏交流"))
                return "\xE8F2";
            else if (forumTitle.Contains("多人综合讨论"))
                return "\xE8F2";
            else if (forumTitle.Contains("服务器插件"))
                return "\xEA86";
            else if (forumTitle.Contains("服务端整合包"))
                return "\uF133";
            else if (forumTitle.Contains("JE-BE互通"))
                return "\uE748";
            else if (forumTitle.Contains("软件程序"))
                return "\uEB3B";
            else if (forumTitle.Contains("兴趣小组"))
                return "\xE716"; 
            else if (forumTitle.Contains("你问我答"))
                return "\xE9CE";
            else if (forumTitle.Contains("闲聊大厅"))
                return "\uE8BD";
            else if (forumTitle.Contains("服主直聘"))
                return "\uE779";
            else if (forumTitle.Contains("服务器宣传"))
                return "\uE990";
            else if (forumTitle.Contains("论坛公告"))
                return "\uE789";
            else if (forumTitle.Contains("综合申请"))
                return "\uE715";
            else if (forumTitle.Contains("站务议院"))
                return "\uE825";
            else if (forumTitle.Contains("举报投诉"))
                return "\uE7BA";
            else if (forumTitle.Contains("意见反馈"))
                return "\uE82F";
            else
                return "\xE74C";
        }
        #endregion

        private async void AdjustCarouselHeight()
        {
            if (_mainCarousel == null || ContentPanel == null)
                return;

            await Dispatcher.RunAsync(
                Windows.UI.Core.CoreDispatcherPriority.Low,
                () =>
                {
                    double width = ContentPanel.ActualWidth;

                    if (width <= 0)
                        return;

                    double aspectRatio = 0.25;
                    double calculatedHeight = width * aspectRatio;

                    double minHeight = 180;
                    double maxHeight = 400;

                    double finalHeight = Math.Max(minHeight, Math.Min(maxHeight, calculatedHeight));

                    _mainCarousel.Height = finalHeight;
                });
        }


        private void UpdateAdPosition()
        {
            // 安全检查
            if (_rightPanel == null || ContentPanel == null) return;

            // 获取左侧内容StackPanel
            var leftStack = ContentPanel.Children.OfType<StackPanel>().FirstOrDefault();
            if (leftStack == null)
            {
                // 如果找不到StackPanel，尝试直接添加到右侧列
                try
                {
                    if (!ContentPanel.Children.Contains(_rightPanel))
                    {
                        Grid.SetColumn(_rightPanel, 1);
                        _rightPanel.Margin = new Thickness(0);
                        ContentPanel.Children.Add(_rightPanel);
                    }
                }
                catch { }
                return;
            }

            // 计算窗口宽度
            double windowWidth = Window.Current.Bounds.Width;

            // 根据窗口宽度决定布局模式
            // 小于1000px时使用单列模式
            bool isSingleColumn = windowWidth < 1000;

            // 从所有可能的位置移除广告
            try
            {
                if (ContentPanel.Children.Contains(_rightPanel))
                {
                    ContentPanel.Children.Remove(_rightPanel);
                }
                if (leftStack.Children.Contains(_rightPanel))
                {
                    leftStack.Children.Remove(_rightPanel);
                }
            }
            catch { }

            // 根据模式决定广告位置
            try
            {
                if (isSingleColumn)
                {
                    // 单列模式 → 广告移动到左侧内容底部
                    _rightPanel.Margin = new Thickness(0, 16, 0, 0);
                    _rightPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
                    leftStack.Children.Add(_rightPanel);

                    // 让左侧内容占满整个宽度
                    Grid.SetColumnSpan(leftStack, 2);
                }
                else
                {
                    // 双列模式 → 广告在右侧列
                    _rightPanel.Margin = new Thickness(0);
                    _rightPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
                    _rightPanel.VerticalAlignment = VerticalAlignment.Top;
                    Grid.SetColumn(_rightPanel, 1);
                    ContentPanel.Children.Add(_rightPanel);

                    // 恢复左侧内容只占一列
                    Grid.SetColumnSpan(leftStack, 1);
                }
            }
            catch
            {
                // 如果放置失败，尝试默认位置
                try
                {
                    if (!ContentPanel.Children.Contains(_rightPanel))
                    {
                        Grid.SetColumn(_rightPanel, 1);
                        _rightPanel.Margin = new Thickness(0);
                        ContentPanel.Children.Add(_rightPanel);
                        Grid.SetColumnSpan(leftStack, 1);
                    }
                }
                catch { }
            }
        }

        #region 事件处理
        private void HomePage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            try
            {
                double windowWidth = Window.Current.Bounds.Width;
                bool isSingleColumn = windowWidth < 1000;

                // 动态调整边距和最大宽度
                if (isSingleColumn)
                {
                    // 单列模式：小边距，无最大宽度限制
                    ContentPanel.Margin = new Thickness(10, 0, 10, 40);
                    ContentPanel.MaxWidth = double.PositiveInfinity;
                    ContentPanel.HorizontalAlignment = HorizontalAlignment.Stretch;

                    // 调整列定义，取消MinWidth限制
                    if (ContentPanel.ColumnDefinitions.Count >= 1)
                    {
                        ContentPanel.ColumnDefinitions[0].MinWidth = 0; // 取消左列最小宽度
                    }
                }
                else
                {
                    // 双列模式：正常边距，1500最大宽度居中
                    ContentPanel.Margin = new Thickness(20, 0, 20, 40);
                    ContentPanel.MaxWidth = 1500;
                    ContentPanel.HorizontalAlignment = HorizontalAlignment.Center;

                    // 恢复列定义
                    if (ContentPanel.ColumnDefinitions.Count >= 1)
                    {
                        ContentPanel.ColumnDefinitions[0].MinWidth = 400; // 恢复左列最小宽度
                    }
                }

                // 1️⃣ 调整轮播图高度
                AdjustCarouselHeight();

                // 2️⃣ 根据窗口宽度重新计算并更新推荐内容分页
                if (_featuredCarousel != null && _htmlDoc != null)
                {
                    var featuredItems = ParseFeaturedContent();
                    if (featuredItems != null && featuredItems.Count > 0)
                    {
                        UpdateFeaturedGrouping(featuredItems);
                    }
                }
            }
            catch
            {
                // 忽略窗口大小改变期间的错误
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            _htmlDoc = null;
            _rightPanel = null;
            _friendLinksGridView = null;
            _partnerLinksGridView = null;
            ContentPanel.Children.Clear();
            MainScrollViewer.Visibility = Visibility.Collapsed;
            await LoadPageData();
        }

        private void CarouselButton_Click(object sender, RoutedEventArgs e)
        {
            var url = (sender as Button)?.Tag as string;
            if (!string.IsNullOrEmpty(url))
            {
                NavigateToUrl(url);
            }
        }

        private void ApplyCarouselButton_Click(object sender, RoutedEventArgs e)
        {
            var url = (sender as Button)?.Tag as string;
            if (!string.IsNullOrEmpty(url))
            {
                NavigateToUrl(url);
            }
        }

        private void FeaturedItemButton_Click(object sender, RoutedEventArgs e)
        {
            var url = (sender as Button)?.Tag as string;
            if (!string.IsNullOrEmpty(url))
            {
                NavigateToUrl(url);
            }
        }

        private void ForumButton_Click(object sender, RoutedEventArgs e)
        {
            var url = (sender as Button)?.Tag as string;
            if (!string.IsNullOrEmpty(url))
            {
                NavigateToUrl(url);
            }
        }

        private void ViewAllFeaturedButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateToUrl(BASE_URL + "featured/");
        }

        private void NavigateToUrl(string url)
        {
            // 如果是相对URL，转为绝对URL
            if (url.StartsWith("/"))
            {
                url = BASE_URL + url.TrimStart('/');
            }
            else if (!url.StartsWith("http"))
            {
                url = BASE_URL + url;
            }

            // 导航到WebViewPage
            Frame.Navigate(typeof(WebViewPage), Tuple.Create(url, "详情"));
        }
        #endregion
    }

    #region 数据模型
    public class CarouselItem
    {
        public string ImageUrl { get; set; }
        public string LinkUrl { get; set; }
        public string ApplyUrl { get; set; }
    }

    public class FeaturedItem
    {
        public string Title { get; set; }
        public string Url { get; set; }
        public string AvatarUrl { get; set; }
        public string Description { get; set; }
        public string Author { get; set; }
        public string UpdateTime { get; set; }
        public double Rating { get; set; }
    }

    public class ForumCategory
    {
        public string Name { get; set; }
        public List<ForumNode> Forums { get; set; }
    }

    public class ForumNode
    {
        public string Title { get; set; }
        public string Url { get; set; }
        public string Description { get; set; }
        public int ThreadCount { get; set; }
        public int MessageCount { get; set; }
        public string IconGlyph { get; set; } = "\uE8A5"; // 默认文档图标，你可以在ParseForumCategories中修改
    }


    public class AdBanner
    {
        public string ImageUrl { get; set; }
        public string LinkUrl { get; set; }
        public string Title { get; set; }
    }

    public class CheckInInfo
    {
        public bool IsCheckedIn { get; set; }
        public int TodayCount { get; set; }
        public int TotalDays { get; set; }
        public int MonthlyReward { get; set; }
    }

    public class SponsorLink
    {
        public string Title { get; set; }
        public string Url { get; set; }
        public string ImageUrl { get; set; }
    }

    public class NewContentItem
    {
        public string Title { get; set; }
        public string Url { get; set; }
        public string Author { get; set; }
        public string Time { get; set; }
        public int ReplyCount { get; set; }
        public string Forum { get; set; }
        public string AvatarUrl { get; set; }
        public string Label { get; set; }
        public string LabelColor { get; set; }
        public string Description { get; set; }
    }

    public class OnlineMember
    {
        public string Username { get; set; }
        public string Url { get; set; }
        public int UserId { get; set; }
    }

    public class TrendingItem
    {
        public string Title { get; set; }
        public string Url { get; set; }
        public string Author { get; set; }
        public string UpdateTime { get; set; }
        public double Rating { get; set; }
        public string ThumbnailUrl { get; set; }
    }

    public class ForumStats
    {
        public int ThreadCount { get; set; }
        public int MessageCount { get; set; }
        public int UserCount { get; set; }
        public string LatestUser { get; set; }
        public string LatestUserUrl { get; set; }
        public int OnlineTotal { get; set; }
        public int OnlineMembers { get; set; }
        public int OnlineGuests { get; set; }
    }
    #endregion
}