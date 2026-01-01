using HtmlAgilityPack;
using MineBBS.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Shapes;

namespace MineBBS.Views
{
    public sealed partial class HomePage : Page, MineBBS.Interfaces.ISearchable
    {
        private readonly HttpClient _httpClient = new HttpClient();
        private readonly CoreDispatcher _dispatcher;
        private string _currentFormHash = ""; // 用于签到
        private bool _isLoggedIn = false;

        public HomePage()
        {
            this.InitializeComponent();
            _dispatcher = CoreWindow.GetForCurrentThread().Dispatcher;

            // 配置HTTP请求头
            _httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Accept.TryParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

            Loaded += async (s, e) => await LoadMineBBSDataAsync();

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
            timer.Tick += async (s, e) => await LoadMineBBSDataAsync();
            timer.Start();
        }

        // 搜索方法 - 供MainPage调用
        public void PerformSearch(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return;

            // 跳转到搜索页面
            var searchUrl = $"https://www.minebbs.com/search/?q={Uri.EscapeDataString(query)}";
            Frame.Navigate(typeof(WebViewPage), Tuple.Create(searchUrl, "搜索"));
        }

        private async Task LoadMineBBSDataAsync()
        {
            await _dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                LoadingProgressRing.IsActive = true;
                LoadingProgressRing.Visibility = Visibility.Visible;
                ErrorPanel.Visibility = Visibility.Collapsed;
                MainScrollViewer.Visibility = Visibility.Collapsed;
                ContentPanel.Children.Clear();
            });

            try
            {
                var response = await _httpClient.GetStringAsync("https://www.minebbs.com/");
                System.Diagnostics.Debug.WriteLine("请求成功，HTML长度：" + response.Length);
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(response);

                var banners = new List<BannerModel>();
                var notices = new List<NoticeModel>();
                var featuredContents = new List<FeaturedModel>();
                var forumCategories = new List<ForumCategoryModel>();
                var latestTopics = new List<TopicModel>();
                var stats = new Dictionary<string, string>();

                // 解析各种数据
                ParseNotices(htmlDoc, notices);
                ParseBanners(htmlDoc, banners);
                ParseFeaturedContent(htmlDoc, featuredContents);
                ParseForumCategories(htmlDoc, forumCategories);
                ParseLatestTopics(htmlDoc, latestTopics);
                ParseStatistics(htmlDoc, stats);

                // 解析登录状态和签到信息
                ParseLoginStatus(htmlDoc);

                System.Diagnostics.Debug.WriteLine($"数据解析完成：轮播{banners.Count}, 公告{notices.Count}, 推荐{featuredContents.Count}, 分区{forumCategories.Count}, 最新{latestTopics.Count}");

                await _dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    BuildUI(banners, notices, featuredContents, forumCategories, latestTopics, stats);

                    LoadingProgressRing.IsActive = false;
                    LoadingProgressRing.Visibility = Visibility.Collapsed;
                    MainScrollViewer.Visibility = Visibility.Visible;
                });
            }
            catch (Exception ex)
            {
                var errorMsg = $"加载失败：{ex.Message}";
                if (ex.InnerException != null)
                    errorMsg += $"\n内部异常：{ex.InnerException.Message}";
                System.Diagnostics.Debug.WriteLine(errorMsg);

                await _dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    ErrorTextBlock.Text = errorMsg;
                    ErrorPanel.Visibility = Visibility.Visible;
                    LoadingProgressRing.IsActive = false;
                    LoadingProgressRing.Visibility = Visibility.Collapsed;
                });
            }
        }

        private void BuildUI(List<BannerModel> banners, List<NoticeModel> notices,
            List<FeaturedModel> featuredContents, List<ForumCategoryModel> forumCategories,
            List<TopicModel> latestTopics, Dictionary<string, string> stats)
        {
            ContentPanel.Children.Clear();

            // 用户登录/签到栏
            var userPanel = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 43, 90, 60)),
                Padding = new Thickness(16, 10, 16, 10)
            };
            userPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            userPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var userStack = new StackPanel { Orientation = Orientation.Horizontal };
            userStack.Children.Add(new TextBlock
            {
                Text = _isLoggedIn ? "👤 已登录" : "👤 未登录",
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(userStack, 0);
            userPanel.Children.Add(userStack);

            var actionStack = new StackPanel { Orientation = Orientation.Horizontal };

            if (!_isLoggedIn)
            {
                var loginButton = new Button
                {
                    Content = "登录",
                    Background = new SolidColorBrush(Color.FromArgb(255, 60, 120, 80)),
                    Foreground = new SolidColorBrush(Colors.White),
                    Margin = new Thickness(0, 0, 8, 0)
                };
                loginButton.Click += (s, e) => Frame.Navigate(typeof(WebViewPage), Tuple.Create("https://www.minebbs.com/login/", "登录"));
                actionStack.Children.Add(loginButton);
            }
            else
            {
                var signInButton = new Button
                {
                    Content = "每日签到",
                    Background = new SolidColorBrush(Color.FromArgb(255, 255, 193, 7)),
                    Foreground = new SolidColorBrush(Colors.Black),
                    Margin = new Thickness(0, 0, 8, 0)
                };
                signInButton.Click += async (s, e) => await PerformDailySignIn();
                actionStack.Children.Add(signInButton);
            }

            Grid.SetColumn(actionStack, 1);
            userPanel.Children.Add(actionStack);
            ContentPanel.Children.Add(userPanel);

            // 公告栏
            if (notices.Count > 0)
            {
                var noticeGrid = new Grid
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 255, 243, 205)),
                    Padding = new Thickness(16, 10, 16, 10)
                };
                var noticeStack = new StackPanel { Orientation = Orientation.Horizontal };
                noticeStack.Children.Add(new TextBlock
                {
                    Text = "📢 ",
                    FontSize = 16,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                });
                foreach (var notice in notices)
                {
                    var noticeText = new TextBlock
                    {
                        Text = notice.Title,
                        FontSize = 13,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 133, 100, 4)),
                        Margin = new Thickness(0, 0, 16, 0),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    noticeText.Tapped += (s, e) => NavigateToUrl(notice.Link);
                    noticeText.PointerEntered += (s, e) => Window.Current.CoreWindow.PointerCursor = new Windows.UI.Core.CoreCursor(Windows.UI.Core.CoreCursorType.Hand, 1);
                    noticeText.PointerExited += (s, e) => Window.Current.CoreWindow.PointerCursor = new Windows.UI.Core.CoreCursor(Windows.UI.Core.CoreCursorType.Arrow, 1);
                    noticeStack.Children.Add(noticeText);
                }
                noticeGrid.Children.Add(noticeStack);
                ContentPanel.Children.Add(noticeGrid);
            }

            // 轮播图
            if (banners.Count > 0)
            {
                var flipViewGrid = new Grid
                {
                    Height = 400,
                    Margin = new Thickness(12, 12, 12, 0)
                };

                var flipView = new FlipView
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };

                foreach (var banner in banners)
                {
                    var image = new Image
                    {
                        Source = new BitmapImage(new Uri(banner.ImageUrl)),
                        Stretch = Stretch.UniformToFill
                    };
                    image.Tapped += (s, e) => NavigateToUrl(banner.Link ?? "https://www.minebbs.com/");
                    flipView.Items.Add(image);
                }

                var indicatorPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 12)
                };

                for (int i = 0; i < banners.Count; i++)
                {
                    var dot = new Ellipse
                    {
                        Width = 8,
                        Height = 8,
                        Fill = (Brush)Application.Current.Resources["SystemControlForegroundBaseLowBrush"],
                        Margin = new Thickness(4, 0, 4, 0)
                    };
                    indicatorPanel.Children.Add(dot);
                }

                ((Ellipse)indicatorPanel.Children[0]).Fill =
                    (Brush)Application.Current.Resources["SystemControlHighlightAccentBrush"];

                flipView.SelectionChanged += (s, e) =>
                {
                    for (int i = 0; i < indicatorPanel.Children.Count; i++)
                    {
                        var dot = (Ellipse)indicatorPanel.Children[i];
                        dot.Fill = (Brush)Application.Current.Resources[
                            i == flipView.SelectedIndex
                            ? "SystemControlHighlightAccentBrush"
                            : "SystemControlForegroundBaseLowBrush"
                        ];
                    }
                };

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                timer.Tick += (s, e) =>
                {
                    if (flipView.Items.Count > 0)
                    {
                        int nextIndex = (flipView.SelectedIndex + 1) % flipView.Items.Count;
                        flipView.SelectedIndex = nextIndex;
                    }
                };
                timer.Start();

                flipViewGrid.Children.Add(flipView);
                flipViewGrid.Children.Add(indicatorPanel);
                ContentPanel.Children.Add(flipViewGrid);
            }

            // 推荐内容 - 轮播样式（两两一组）
            if (featuredContents.Count > 0)
            {
                ContentPanel.Children.Add(new TextBlock
                {
                    Text = "📌 推荐内容",
                    FontSize = 18,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    Margin = new Thickness(12, 16, 12, 8)
                });

                var featuredFlipView = new FlipView
                {
                    Height = 220,
                    Margin = new Thickness(12, 0, 12, 0)
                };

                // 两两一组
                for (int i = 0; i < featuredContents.Count; i += 2)
                {
                    var pairGrid = new Grid { Padding = new Thickness(8) };
                    pairGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    if (i + 1 < featuredContents.Count)
                    {
                        pairGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    }

                    // 第一个
                    var card1 = CreateFeaturedCard(featuredContents[i]);
                    Grid.SetColumn(card1, 0);
                    pairGrid.Children.Add(card1);

                    // 第二个（如果有）
                    if (i + 1 < featuredContents.Count)
                    {
                        var card2 = CreateFeaturedCard(featuredContents[i + 1]);
                        Grid.SetColumn(card2, 1);
                        pairGrid.Children.Add(card2);
                    }

                    featuredFlipView.Items.Add(pairGrid);
                }

                ContentPanel.Children.Add(featuredFlipView);
            }

            // 论坛板块分区
            foreach (var category in forumCategories)
            {
                var categoryHeader = new Grid
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 43, 90, 60)),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(12, 16, 12, 4)
                };
                categoryHeader.Children.Add(new TextBlock
                {
                    Text = category.CategoryName,
                    FontSize = 16,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Colors.White)
                });
                ContentPanel.Children.Add(categoryHeader);

                foreach (var forum in category.Forums)
                {
                    var forumGrid = CreateForumCard(forum);
                    ContentPanel.Children.Add(forumGrid);
                }
            }

            // 最新主题
            if (latestTopics.Count > 0)
            {
                ContentPanel.Children.Add(new TextBlock
                {
                    Text = "🆕 最新主题",
                    FontSize = 18,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    Margin = new Thickness(12, 16, 12, 8)
                });

                foreach (var topic in latestTopics)
                {
                    var topicCard = CreateTopicCard(topic);
                    ContentPanel.Children.Add(topicCard);
                }
            }

            // 统计信息
            var statsGrid = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 248, 249, 250)),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(12, 16, 12, 0)
            };
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var statItems = new[] {
                (stats.GetValueOrDefault("online", "0"), "在线"),
                (stats.GetValueOrDefault("topics", "0"), "主题"),
                (stats.GetValueOrDefault("messages", "0"), "消息"),
                (stats.GetValueOrDefault("members", "0"), "会员")
            };

            for (int i = 0; i < statItems.Length; i++)
            {
                var statStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                statStack.Children.Add(new TextBlock
                {
                    Text = statItems[i].Item1,
                    FontSize = 20,
                    FontWeight = Windows.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 43, 90, 60)),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                statStack.Children.Add(new TextBlock
                {
                    Text = statItems[i].Item2,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Colors.Gray),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 0)
                });
                Grid.SetColumn(statStack, i);
                statsGrid.Children.Add(statStack);
            }
            ContentPanel.Children.Add(statsGrid);

            // 底部信息
            var footerStack = new StackPanel
            {
                Margin = new Thickness(12, 16, 12, 0),
                Padding = new Thickness(12),
                Background = new SolidColorBrush(Color.FromArgb(255, 248, 249, 250))
            };
            footerStack.Children.Add(new TextBlock
            {
                Text = "MineBBS 我的世界中文论坛",
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.Gray),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            footerStack.Children.Add(new TextBlock
            {
                Text = "数据每30分钟自动刷新",
                FontSize = 10,
                Foreground = new SolidColorBrush(Colors.Gray),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            });
            ContentPanel.Children.Add(footerStack);
        }

        private Grid CreateFeaturedCard(FeaturedModel featured)
        {
            var card = new Grid
            {
                Margin = new Thickness(4),
                Padding = new Thickness(12),
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                CornerRadius = new CornerRadius(8)
            };
            card.Tapped += (s, e) => NavigateToUrl(featured.Link ?? "https://www.minebbs.com/");
            card.PointerEntered += (s, e) =>
            {
                card.Background = new SolidColorBrush(Color.FromArgb(255, 240, 240, 240));
                Window.Current.CoreWindow.PointerCursor = new Windows.UI.Core.CoreCursor(Windows.UI.Core.CoreCursorType.Hand, 1);
            };
            card.PointerExited += (s, e) =>
            {
                card.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
                Window.Current.CoreWindow.PointerCursor = new Windows.UI.Core.CoreCursor(Windows.UI.Core.CoreCursorType.Arrow, 1);
            };

            var stack = new StackPanel();

            var titleBlock = new TextBlock
            {
                Text = featured.Title,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            stack.Children.Add(titleBlock);

            var summaryBlock = new TextBlock
            {
                Text = featured.Summary,
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.Gray),
                Margin = new Thickness(0, 4, 0, 0),
                MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            stack.Children.Add(summaryBlock);

            var metaStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var avatar = new Ellipse
            {
                Width = 20,
                Height = 20,
                Fill = new ImageBrush { ImageSource = new BitmapImage(new Uri(featured.AuthorAvatar)) }
            };
            metaStack.Children.Add(avatar);
            metaStack.Children.Add(new TextBlock
            {
                Text = " " + featured.AuthorName,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 43, 90, 60)),
                VerticalAlignment = VerticalAlignment.Center
            });
            stack.Children.Add(metaStack);

            card.Children.Add(stack);
            return card;
        }

        private Grid CreateForumCard(ForumModel forum)
        {
            var forumGrid = new Grid
            {
                Padding = new Thickness(12),
                Margin = new Thickness(12, 4, 12, 0),
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"]
            };
            forumGrid.Tapped += (s, e) => NavigateToUrl(forum.Link ?? "https://www.minebbs.com/");
            forumGrid.PointerEntered += (s, e) =>
            {
                forumGrid.Background = new SolidColorBrush(Color.FromArgb(255, 240, 240, 240));
                Window.Current.CoreWindow.PointerCursor = new Windows.UI.Core.CoreCursor(Windows.UI.Core.CoreCursorType.Hand, 1);
            };
            forumGrid.PointerExited += (s, e) =>
            {
                forumGrid.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
                Window.Current.CoreWindow.PointerCursor = new Windows.UI.Core.CoreCursor(Windows.UI.Core.CoreCursorType.Arrow, 1);
            };

            forumGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            forumGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var forumStack = new StackPanel();
            forumStack.Children.Add(new TextBlock
            {
                Text = forum.ForumName,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                FontSize = 15
            });
            forumStack.Children.Add(new TextBlock
            {
                Text = forum.ForumDesc,
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.Gray),
                Margin = new Thickness(0, 4, 0, 0),
                MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var statsStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            statsStack.Children.Add(new TextBlock { Text = "💬 ", FontSize = 11 });
            statsStack.Children.Add(new TextBlock
            {
                Text = forum.TopicCount.ToString(),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 43, 90, 60))
            });
            statsStack.Children.Add(new TextBlock { Text = " 主题 · 📝 ", FontSize = 11, Foreground = new SolidColorBrush(Colors.Gray), Margin = new Thickness(2, 0, 2, 0) });
            statsStack.Children.Add(new TextBlock
            {
                Text = forum.MsgCount.ToString(),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 43, 90, 60))
            });
            statsStack.Children.Add(new TextBlock { Text = " 消息", FontSize = 11, Foreground = new SolidColorBrush(Colors.Gray), Margin = new Thickness(2, 0, 0, 0) });
            forumStack.Children.Add(statsStack);

            Grid.SetColumn(forumStack, 0);
            forumGrid.Children.Add(forumStack);

            if (!string.IsNullOrEmpty(forum.LatestTopicTitle))
            {
                var latestStack = new StackPanel
                {
                    Margin = new Thickness(12, 0, 0, 0),
                    MaxWidth = 150,
                    VerticalAlignment = VerticalAlignment.Center
                };
                latestStack.Children.Add(new TextBlock
                {
                    Text = "最新：",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Colors.Gray)
                });
                latestStack.Children.Add(new TextBlock
                {
                    Text = forum.LatestTopicTitle,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    MaxLines = 2,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 2, 0, 0)
                });
                Grid.SetColumn(latestStack, 1);
                forumGrid.Children.Add(latestStack);
            }

            return forumGrid;
        }

        private Grid CreateTopicCard(TopicModel topic)
        {
            var topicGrid = new Grid
            {
                Padding = new Thickness(12, 10, 12, 10),
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                Margin = new Thickness(12, 4, 12, 0)
            };
            topicGrid.Tapped += (s, e) => NavigateToUrl(topic.Link ?? "https://www.minebbs.com/");
            topicGrid.PointerEntered += (s, e) =>
            {
                topicGrid.Background = new SolidColorBrush(Color.FromArgb(255, 240, 240, 240));
                Window.Current.CoreWindow.PointerCursor = new Windows.UI.Core.CoreCursor(Windows.UI.Core.CoreCursorType.Hand, 1);
            };
            topicGrid.PointerExited += (s, e) =>
            {
                topicGrid.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
                Window.Current.CoreWindow.PointerCursor = new Windows.UI.Core.CoreCursor(Windows.UI.Core.CoreCursorType.Arrow, 1);
            };

            var topicStack = new StackPanel();
            topicStack.Children.Add(new TextBlock
            {
                Text = topic.Title,
                FontWeight = Windows.UI.Text.FontWeights.Medium,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var metaStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            metaStack.Children.Add(new TextBlock
            {
                Text = topic.AuthorName,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 43, 90, 60))
            });
            metaStack.Children.Add(new TextBlock { Text = " · " + topic.PublishTime + " · 💬 " + topic.ReplyCount + " · 👁 " + topic.ViewCount, FontSize = 11, Foreground = new SolidColorBrush(Colors.Gray) });
            topicStack.Children.Add(metaStack);
            topicGrid.Children.Add(topicStack);
            return topicGrid;
        }

        private void NavigateToUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            // 处理相对URL
            if (!url.StartsWith("http"))
            {
                url = "https://www.minebbs.com" + (url.StartsWith("/") ? url : "/" + url);
            }

            Frame.Navigate(typeof(WebViewPage), Tuple.Create(url, "详情"));
        }

        private async Task PerformDailySignIn()
        {
            try
            {
                // 这里需要实现签到逻辑
                // 由于签到需要登录态和formhash，建议通过WebView完成
                Frame.Navigate(typeof(WebViewPage), Tuple.Create("https://www.minebbs.com/plugin.php?id=dc_signin", "签到"));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"签到失败：{ex.Message}");
            }
        }

        private void ParseLoginStatus(HtmlDocument htmlDoc)
        {
            try
            {
                // 检查是否登录（通过查找用户信息节点）
                var userNode = htmlDoc.DocumentNode.SelectSingleNode("//a[contains(@class, 'p-navgroup-link--user')]");
                _isLoggedIn = userNode != null;

                // 获取formhash（用于签到等操作）
                var formHashNode = htmlDoc.DocumentNode.SelectSingleNode("//input[@name='formhash']");
                if (formHashNode != null)
                {
                    _currentFormHash = formHashNode.GetAttributeValue("value", "");
                }

                System.Diagnostics.Debug.WriteLine($"登录状态：{_isLoggedIn}, FormHash: {_currentFormHash}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"解析登录状态失败：{ex.Message}");
            }
        }

        // 以下是原有的解析方法（保持不变，但需要添加Link字段的提取）
        private void ParseNotices(HtmlDocument htmlDoc, List<NoticeModel> notices)
        {
            try
            {
                var noticeNodes = htmlDoc.DocumentNode.SelectNodes("//div[contains(@class, 'notice')]//a | //div[@class='p-body-header']//div[contains(@class, 'blockMessage')]//a");
                if (noticeNodes != null)
                {
                    foreach (var node in noticeNodes.Take(3))
                    {
                        var notice = new NoticeModel
                        {
                            Title = node.InnerText.Trim(),
                            Link = node.GetAttributeValue("href", "")
                        };
                        if (!string.IsNullOrEmpty(notice.Title))
                        {
                            notices.Add(notice);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"解析公告失败：{ex.Message}");
            }
        }

        private void ParseBanners(HtmlDocument htmlDoc, List<BannerModel> banners)
        {
            try
            {
                var bannerNodes = htmlDoc.DocumentNode.SelectNodes("//div[@data-widget-key='forum_slide']//div[contains(@class, 'swiper-slide')]//a");
                if (bannerNodes != null)
                {
                    foreach (var node in bannerNodes)
                    {
                        var imgNode = node.SelectSingleNode(".//img[not(contains(@src, 'apply_button'))]");
                        if (imgNode != null)
                        {
                            var imgUrl = imgNode.GetAttributeValue("src", "");
                            if (!string.IsNullOrEmpty(imgUrl))
                            {
                                if (imgUrl.StartsWith("/"))
                                    imgUrl = "https://www.minebbs.com" + imgUrl;
                                banners.Add(new BannerModel
                                {
                                    ImageUrl = imgUrl,
                                    Link = node.GetAttributeValue("href", "")
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"解析轮播图失败：{ex.Message}");
            }
        }

        private void ParseFeaturedContent(HtmlDocument htmlDoc, List<FeaturedModel> featuredContents)
        {
            try
            {
                var featuredNodes = htmlDoc.DocumentNode.SelectNodes("//div[@data-widget-key='featured_content']//div[contains(@class, 'carousel-item')]");
                if (featuredNodes != null)
                {
                    foreach (var node in featuredNodes)
                    {
                        var titleNode = node.SelectSingleNode(".//h4[contains(@class, 'contentRow-title')]/a");
                        var authorNode = node.SelectSingleNode(".//a[contains(@class, 'username')]");
                        var timeNode = node.SelectSingleNode(".//time[contains(@class, 'u-dt')]");
                        var summaryNode = node.SelectSingleNode(".//div[contains(@class, 'contentRow-lesser')]");
                        var avatarNode = node.SelectSingleNode(".//div[contains(@class, 'contentRow-figure')]//img");

                        if (titleNode != null && authorNode != null)
                        {
                            var featured = new FeaturedModel
                            {
                                Title = titleNode.InnerText.Trim(),
                                AuthorName = authorNode.InnerText.Trim(),
                                PublishTime = timeNode?.GetAttributeValue("data-date", timeNode?.InnerText.Trim() ?? ""),
                                Summary = summaryNode?.InnerText.Trim() ?? "",
                                AuthorAvatar = avatarNode?.GetAttributeValue("src", "") ?? "https://www.minebbs.com/data/avatars/default/0.png",
                                Link = titleNode.GetAttributeValue("href", "")
                            };

                            if (!string.IsNullOrEmpty(featured.AuthorAvatar) && featured.AuthorAvatar.StartsWith("/"))
                                featured.AuthorAvatar = "https://www.minebbs.com" + featured.AuthorAvatar;

                            featuredContents.Add(featured);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"解析推荐内容失败：{ex.Message}");
            }
        }

        private void ParseForumCategories(HtmlDocument htmlDoc, List<ForumCategoryModel> forumCategories)
        {
            try
            {
                var categoryNodes = htmlDoc.DocumentNode.SelectNodes("//div[contains(@class, 'block--category')]");
                if (categoryNodes != null)
                {
                    foreach (var categoryNode in categoryNodes)
                    {
                        var categoryTitleNode = categoryNode.SelectSingleNode(".//h2[contains(@class, 'block-header')]//a | .//h3[contains(@class, 'block-header')]//a");
                        var categoryName = categoryTitleNode?.InnerText.Trim() ?? "未命名分区";

                        var category = new ForumCategoryModel
                        {
                            CategoryName = categoryName,
                            Forums = new System.Collections.ObjectModel.ObservableCollection<ForumModel>()
                        };

                        var forumNodes = categoryNode.SelectNodes(".//div[contains(@class, 'node--forum')]");
                        if (forumNodes != null)
                        {
                            foreach (var node in forumNodes)
                            {
                                var nameNode = node.SelectSingleNode(".//h3[contains(@class, 'node-title')]/a");
                                var descNode = node.SelectSingleNode(".//div[contains(@class, 'node-description')]");
                                var topicCountNode = node.SelectSingleNode(".//dl[contains(@class, 'pairs')][1]/dd");
                                var msgCountNode = node.SelectSingleNode(".//dl[contains(@class, 'pairs')][2]/dd");
                                var latestTopicNode = node.SelectSingleNode(".//a[contains(@class, 'node-extra-title')]");

                                if (nameNode != null)
                                {
                                    var forum = new ForumModel
                                    {
                                        ForumName = nameNode.InnerText.Trim(),
                                        ForumDesc = descNode?.InnerText.Trim() ?? "",
                                        TopicCount = ParseKNumber(topicCountNode?.InnerText.Trim() ?? "0"),
                                        MsgCount = ParseKNumber(msgCountNode?.InnerText.Trim() ?? "0"),
                                        LatestTopicTitle = latestTopicNode?.InnerText.Trim() ?? "",
                                        Link = nameNode.GetAttributeValue("href", "")
                                    };

                                    category.Forums.Add(forum);
                                }
                            }
                        }

                        if (category.Forums.Count > 0)
                        {
                            forumCategories.Add(category);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"解析论坛分区失败：{ex.Message}");
            }
        }

        private void ParseLatestTopics(HtmlDocument htmlDoc, List<TopicModel> latestTopics)
        {
            try
            {
                var topicNodes = htmlDoc.DocumentNode.SelectNodes("//div[contains(@class, 'structItem--thread')]");
                if (topicNodes != null)
                {
                    foreach (var node in topicNodes.Take(10))
                    {
                        var titleNode = node.SelectSingleNode(".//div[contains(@class, 'structItem-title')]//a");
                        var authorNode = node.SelectSingleNode(".//a[contains(@class, 'username')]");
                        var timeNode = node.SelectSingleNode(".//time");
                        var replyCountNode = node.SelectSingleNode(".//dd[text()='回复']//preceding-sibling::dt");
                        var viewCountNode = node.SelectSingleNode(".//dd[text()='查看']//preceding-sibling::dt");

                        if (titleNode != null)
                        {
                            var topic = new TopicModel
                            {
                                Title = titleNode.InnerText.Trim(),
                                AuthorName = authorNode?.InnerText.Trim() ?? "",
                                PublishTime = timeNode?.GetAttributeValue("data-date", timeNode?.InnerText.Trim() ?? ""),
                                ReplyCount = int.TryParse(replyCountNode?.InnerText.Trim(), out var r) ? r : 0,
                                ViewCount = int.TryParse(viewCountNode?.InnerText.Trim(), out var v) ? v : 0,
                                Link = titleNode.GetAttributeValue("href", "")
                            };

                            latestTopics.Add(topic);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"解析最新主题失败：{ex.Message}");
            }
        }

        private void ParseStatistics(HtmlDocument htmlDoc, Dictionary<string, string> stats)
        {
            try
            {
                var statsNode = htmlDoc.DocumentNode.SelectSingleNode("//div[contains(@class, 'block-body')]//dl");
                if (statsNode != null)
                {
                    var onlineNode = statsNode.SelectSingleNode(".//dt[contains(text(), '在线')]//following-sibling::dd");
                    var topicsNode = statsNode.SelectSingleNode(".//dt[contains(text(), '主题')]//following-sibling::dd");
                    var messagesNode = statsNode.SelectSingleNode(".//dt[contains(text(), '消息')]//following-sibling::dd");
                    var membersNode = statsNode.SelectSingleNode(".//dt[contains(text(), '会员')]//following-sibling::dd");

                    stats["online"] = onlineNode?.InnerText.Trim() ?? "0";
                    stats["topics"] = topicsNode?.InnerText.Trim() ?? "0";
                    stats["messages"] = messagesNode?.InnerText.Trim() ?? "0";
                    stats["members"] = membersNode?.InnerText.Trim() ?? "0";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"解析统计信息失败：{ex.Message}");
            }
        }

        private int ParseKNumber(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            text = text.Trim().Replace(",", "").Replace(" ", "");
            if (text.Contains("K") || text.Contains("k"))
            {
                var numPart = text.Replace("K", "").Replace("k", "");
                if (double.TryParse(numPart, out var num))
                {
                    return (int)(num * 1000);
                }
            }
            int.TryParse(text, out var result);
            return result;
        }
    }
}