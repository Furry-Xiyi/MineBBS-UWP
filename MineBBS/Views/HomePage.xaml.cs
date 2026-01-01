using HtmlAgilityPack;
using MineBBS.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.ApplicationModel.Contacts;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Shapes;

namespace MineBBS.Views
{
    public sealed partial class HomePage : Page
    {
        private readonly HttpClient _httpClient = new HttpClient();
        private readonly CoreDispatcher _dispatcher;

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

                // 1. 解析公告
                ParseNotices(htmlDoc, notices);

                // 2. 解析轮播图
                ParseBanners(htmlDoc, banners);

                // 3. 解析推荐内容
                ParseFeaturedContent(htmlDoc, featuredContents);

                // 4. 解析论坛板块分区
                ParseForumCategories(htmlDoc, forumCategories);

                // 5. 解析最新主题
                ParseLatestTopics(htmlDoc, latestTopics);

                // 6. 解析统计数据
                ParseStatistics(htmlDoc, stats);

                System.Diagnostics.Debug.WriteLine($"数据解析完成：轮播{banners.Count}, 公告{notices.Count}, 推荐{featuredContents.Count}, 分区{forumCategories.Count}, 最新{latestTopics.Count}");

                // 7. 构建UI
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
                    noticeStack.Children.Add(new TextBlock
                    {
                        Text = notice.Title,
                        FontSize = 13,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 133, 100, 4)),
                        Margin = new Thickness(0, 0, 16, 0),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
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
                    flipView.Items.Add(image);
                }

                // 指示器面板（叠加在 FlipView 内部底部居中）
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
                        Fill = (Brush)Application.Current.Resources["SystemControlForegroundBaseLowBrush"], // 默认灰色
                        Margin = new Thickness(4, 0, 4, 0)
                    };
                    indicatorPanel.Children.Add(dot);
                }

                // 初始默认第一个点高亮
                ((Ellipse)indicatorPanel.Children[0]).Fill =
                    (Brush)Application.Current.Resources["SystemControlHighlightAccentBrush"];

                // 绑定 FlipView.SelectionChanged 更新点颜色
                flipView.SelectionChanged += (s, e) =>
                {
                    for (int i = 0; i < indicatorPanel.Children.Count; i++)
                    {
                        var dot = (Ellipse)indicatorPanel.Children[i];
                        dot.Fill = (Brush)Application.Current.Resources[
                            i == flipView.SelectedIndex
                            ? "SystemControlHighlightAccentBrush" // 高亮色
                            : "SystemControlForegroundBaseLowBrush" // 灰色
                        ];
                    }
                };

                // 自动轮播
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

                // 把 FlipView 和指示器叠加在同一个 Grid
                flipViewGrid.Children.Add(flipView);
                flipViewGrid.Children.Add(indicatorPanel);

                ContentPanel.Children.Add(flipViewGrid);
            }

            // 推荐内容
            if (featuredContents.Count > 0)
            {
                ContentPanel.Children.Add(new TextBlock
                {
                    Text = "📌 推荐内容",
                    FontSize = 18,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    Margin = new Thickness(12, 16, 12, 8)
                });

                foreach (var featured in featuredContents)
                {
                    var grid = new Grid
                    {
                        Margin = new Thickness(12, 8, 12, 0),
                        Padding = new Thickness(12),
                        Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"]
                    };
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    var avatar = new Border
                    {
                        Width = 48,
                        Height = 48,
                        CornerRadius = new CornerRadius(24)
                    };
                    avatar.Child = new Image
                    {
                        Source = new BitmapImage(new Uri(featured.AuthorAvatar)),
                        Stretch = Stretch.UniformToFill
                    };
                    Grid.SetColumn(avatar, 0);
                    grid.Children.Add(avatar);

                    var infoStack = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
                    infoStack.Children.Add(new TextBlock
                    {
                        Text = featured.Title,
                        FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                        FontSize = 15,
                        TextWrapping = TextWrapping.Wrap,
                        MaxLines = 2,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                    infoStack.Children.Add(new TextBlock
                    {
                        Text = featured.Summary,
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Colors.Gray),
                        Margin = new Thickness(0, 4, 0, 0),
                        MaxLines = 2,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                    var metaStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
                    metaStack.Children.Add(new TextBlock
                    {
                        Text = featured.AuthorName,
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 43, 90, 60))
                    });
                    metaStack.Children.Add(new TextBlock { Text = " · ", FontSize = 11, Foreground = new SolidColorBrush(Colors.Gray) });
                    metaStack.Children.Add(new TextBlock
                    {
                        Text = featured.PublishTime,
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Colors.Gray)
                    });
                    infoStack.Children.Add(metaStack);

                    Grid.SetColumn(infoStack, 1);
                    grid.Children.Add(infoStack);

                    ContentPanel.Children.Add(grid);
                }
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
                    var forumGrid = new Grid
                    {
                        Padding = new Thickness(12),
                        Margin = new Thickness(12, 4, 12, 0),
                        Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"]
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
                    statsStack.Children.Add(new TextBlock { Text = " 主题", FontSize = 11, Foreground = new SolidColorBrush(Colors.Gray), Margin = new Thickness(2, 0, 12, 0) });
                    statsStack.Children.Add(new TextBlock { Text = "📝 ", FontSize = 11 });
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
                    var topicGrid = new Grid
                    {
                        Padding = new Thickness(12, 10, 12, 10),
                        Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                        Margin = new Thickness(12, 4, 12, 0)
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
                    metaStack.Children.Add(new TextBlock { Text = " · ", FontSize = 11, Foreground = new SolidColorBrush(Colors.Gray) });
                    metaStack.Children.Add(new TextBlock { Text = topic.PublishTime, FontSize = 11, Foreground = new SolidColorBrush(Colors.Gray) });
                    metaStack.Children.Add(new TextBlock { Text = " · ", FontSize = 11, Foreground = new SolidColorBrush(Colors.Gray), Margin = new Thickness(8, 0, 0, 0) });
                    metaStack.Children.Add(new TextBlock { Text = "💬 ", FontSize = 11 });
                    metaStack.Children.Add(new TextBlock { Text = topic.ReplyCount.ToString(), FontSize = 11, Foreground = new SolidColorBrush(Colors.Gray) });
                    metaStack.Children.Add(new TextBlock { Text = " · 👁 ", FontSize = 11, Margin = new Thickness(8, 0, 0, 0) });
                    metaStack.Children.Add(new TextBlock { Text = topic.ViewCount.ToString(), FontSize = 11, Foreground = new SolidColorBrush(Colors.Gray) });
                    topicStack.Children.Add(metaStack);
                    topicGrid.Children.Add(topicStack);
                    ContentPanel.Children.Add(topicGrid);
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
                var bannerNodes = htmlDoc.DocumentNode.SelectNodes("//div[@data-widget-key='forum_slide']//div[contains(@class, 'swiper-slide')]//img[not(contains(@src, 'apply_button'))]");
                if (bannerNodes != null)
                {
                    foreach (var node in bannerNodes)
                    {
                        var imgUrl = node.GetAttributeValue("src", "");
                        if (!string.IsNullOrEmpty(imgUrl))
                        {
                            if (imgUrl.StartsWith("/"))
                                imgUrl = "https://www.minebbs.com" + imgUrl;
                            banners.Add(new BannerModel { ImageUrl = imgUrl });
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
                                AuthorAvatar = avatarNode?.GetAttributeValue("src", "") ?? "https://www.minebbs.com/data/avatars/default/0.png"
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
                                        LatestTopicTitle = latestTopicNode?.InnerText.Trim() ?? ""
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
                                ViewCount = int.TryParse(viewCountNode?.InnerText.Trim(), out var v) ? v : 0
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