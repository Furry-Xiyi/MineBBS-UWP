using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Shapes;
using Newtonsoft.Json.Linq;

namespace MineBBS.Views
{
    public sealed partial class DetailPage : Page
    {
        private readonly HttpClient _httpClient = new HttpClient();
        private readonly CoreDispatcher _dispatcher;

        // 当前页面的上下文
        private string _currentUrl;
        private string _targetId;
        private string _detailType; // "resource" 或 "thread"

        // 认证信息
        private string _cookies = "";
        private string _xfToken = "";

        // API 基础配置
        private const string BaseApi = "https://mbapi.xyqaq.cn/api";

        public DetailPage()
        {
            this.InitializeComponent();
            _dispatcher = CoreWindow.GetForCurrentThread().Dispatcher;

            // 【关键修改】模拟更真实的浏览器请求头，防止 500 拦截
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/plain, */*");
            _httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.8");
            // 添加 Referer 往往能解决“不带ID”导致的拒绝访问
            _httpClient.DefaultRequestHeaders.Referrer = new Uri("https://www.minebbs.com/");
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is Tuple<string, string, string, string> paramWithAuth)
            {
                _currentUrl = paramWithAuth.Item1;
                _cookies = paramWithAuth.Item3;
                _xfToken = paramWithAuth.Item4;
            }
            else if (e.Parameter is Tuple<string, string> param)
            {
                _currentUrl = param.Item1;
            }
            else if (e.Parameter is string url)
            {
                _currentUrl = url;
            }

            System.Diagnostics.Debug.WriteLine($"[DetailPage] Navigated to: {_currentUrl}, Auth: {!string.IsNullOrEmpty(_cookies)}");
            await StartLoadingAsync();
        }

        private async Task StartLoadingAsync()
        {
            LoadingProgressRing.IsActive = true;
            LoadingProgressRing.Visibility = Visibility.Visible;
            ErrorPanel.Visibility = Visibility.Collapsed;
            ContentPanel.Children.Clear();

            try
            {
                if (string.IsNullOrEmpty(_currentUrl)) throw new Exception("URL为空");

                if (_currentUrl.Contains("resources"))
                {
                    _detailType = "resource";
                    await LoadResourceFlowAsync();
                }
                else if (_currentUrl.Contains("threads"))
                {
                    _detailType = "thread";
                    await LoadThreadFlowAsync();
                }
                else
                {
                    throw new Exception("不支持的链接类型");
                }
            }
            catch (Exception ex)
            {
                ShowError($"加载失败: {ex.Message}");
            }
            finally
            {
                LoadingProgressRing.IsActive = false;
                LoadingProgressRing.Visibility = Visibility.Collapsed;
            }
        }

        #region ID 提取逻辑 (增强版)

        // 尝试从 URL 提取所有可能的 ID 组合
        private List<string> ExtractCandidateIds(string url, string segment)
        {
            var candidates = new List<string>();

            // 1. 尝试提取纯数字 ID (如 14080)
            // 匹配 /resources/xxx.14080/ 中的 14080
            var numericMatch = Regex.Match(url, @"\.(\d+)/?$");
            if (numericMatch.Success)
            {
                candidates.Add(numericMatch.Groups[1].Value);
            }
            else
            {
                // 尝试匹配 /threads/12345/ 这种纯数字格式
                var simpleNumMatch = Regex.Match(url, $@"/{segment}/(\d+)/?$");
                if (simpleNumMatch.Success)
                {
                    candidates.Add(simpleNumMatch.Groups[1].Value);
                }
            }

            // 2. 尝试提取完整 Slug (如 quedp-rpg-x.14080)
            // 有些 API 代理需要完整的 slug 才能工作
            var slugMatch = Regex.Match(url, $@"/{segment}/([^/]+)/?");
            if (slugMatch.Success)
            {
                var val = slugMatch.Groups[1].Value;
                if (!candidates.Contains(val)) candidates.Add(val);
            }

            return candidates;
        }

        #endregion

        #region API请求封装 (修复了500崩溃问题)

        private async Task<JObject> ApiGet(string endpoint)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseApi}{endpoint}");
                if (!string.IsNullOrEmpty(_cookies)) request.Headers.Add("X-Cookies", _cookies);

                var response = await _httpClient.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();

                System.Diagnostics.Debug.WriteLine($"[API] GET {endpoint} : {(int)response.StatusCode}");

                // 【关键修复】如果服务器返回 500/403，打印具体内容而不是直接崩溃
                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[API Error Content]: {content}");
                    // 如果是 500，返回 null 让上层逻辑决定是否重试其他 ID
                    return null;
                }

                if (string.IsNullOrEmpty(content)) return null;

                // 尝试解析
                try
                {
                    return JObject.Parse(content);
                }
                catch
                {
                    System.Diagnostics.Debug.WriteLine($"[API] 非 JSON 响应: {content.Substring(0, Math.Min(content.Length, 100))}...");
                    return null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API Exception] {ex.Message}");
                return null;
            }
        }

        #endregion

        #region 资源加载流程

        private async Task LoadResourceFlowAsync()
        {
            var candidates = ExtractCandidateIds(_currentUrl, "resources");
            JObject resJson = null;
            string successId = null;

            // 轮询尝试 ID (先试数字，再试 Slug)
            foreach (var id in candidates)
            {
                System.Diagnostics.Debug.WriteLine($"[Try ID] 尝试资源 ID: {id}");
                resJson = await ApiGet($"/resources/{id}");
                if (resJson != null && resJson["success"]?.Value<bool>() == true)
                {
                    successId = id;
                    _targetId = id;
                    break;
                }
            }

            if (resJson == null || resJson["success"]?.Value<bool>() != true)
            {
                throw new Exception($"无法获取资源详情 (尝试了 ID: {string.Join(",", candidates)})。可能是需要登录或 API 故障。");
            }

            var data = resJson["data"];
            var basic = data["basic"];

            // 获取附属信息
            var tasks = new List<Task<JObject>>
            {
                ApiGet($"/resources/{successId}/stats"),
                ApiGet($"/resources/{successId}/updates"),
                ApiGet($"/resources/{successId}/history")
            };

            string discussionId = data["threadId"]?.ToString() ?? successId;

            var results = await Task.WhenAll(tasks);
            var stats = results[0]?["data"];
            var updates = results[1]?["data"]?["updates"] as JArray;
            var history = results[2]?["data"] as JArray;

            await _dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                BuildResourceHeader(basic);
                BuildResourceStats(stats, data["sidebar"]);
                BuildResourceDescription(data["description"]?.ToString());

                if (data["customFields"] is JObject fields && fields.HasValues)
                    BuildCustomFields(fields);

                if (updates != null && updates.Count > 0)
                    BuildUpdatesList(updates);

                await LoadDiscussionSectionAsync(discussionId);
            });
        }

        private async Task LoadDiscussionSectionAsync(string threadId, int page = 1)
        {
            var discussJson = await ApiGet($"/resources/discussions/{threadId}?page={page}");

            await _dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (page == 1)
                {
                    ContentPanel.Children.Add(new TextBlock
                    {
                        Text = "💬 讨论与评价",
                        FontSize = 20,
                        FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                        Margin = new Thickness(12, 24, 12, 12)
                    });
                }

                if (discussJson != null && discussJson["success"]?.Value<bool>() == true)
                {
                    var posts = discussJson["posts"] as JArray;
                    if (posts != null)
                    {
                        foreach (var post in posts) ContentPanel.Children.Add(CreatePostCard(post));
                    }
                }
                else
                {
                    ContentPanel.Children.Add(new TextBlock { Text = "暂无评论。", Foreground = new SolidColorBrush(Colors.Gray), Margin = new Thickness(16) });
                }
            });
        }

        #endregion

        #region 帖子加载流程

        private async Task LoadThreadFlowAsync()
        {
            var candidates = ExtractCandidateIds(_currentUrl, "threads");
            JObject json = null;

            foreach (var id in candidates)
            {
                System.Diagnostics.Debug.WriteLine($"[Try ID] 尝试帖子 ID: {id}");
                json = await ApiGet($"/threads/{id}");
                if (json != null && json["success"]?.Value<bool>() == true)
                {
                    _targetId = id;
                    break;
                }
            }

            if (json == null || json["success"]?.Value<bool>() != true)
            {
                throw new Exception("无法获取帖子详情，请检查是否需要登录。");
            }

            var data = json["data"];
            var thread = data["thread"];
            var posts = data["posts"] as JArray;
            var poll = data["poll"];

            await _dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                BuildThreadHeader(thread);
                if (poll != null && poll.HasValues) BuildPollUI(poll);
                if (posts != null)
                {
                    foreach (var post in posts) ContentPanel.Children.Add(CreatePostCard(post));
                }
            });
        }

        #endregion

        #region UI 构建 (保持原样，略微优化错误处理)

        private void BuildResourceHeader(JToken basic)
        {
            if (basic == null) return;
            var grid = new Grid
            {
                Padding = new Thickness(16),
                Background = new SolidColorBrush(Color.FromArgb(20, 0, 0, 0)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(12)
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var iconUrl = basic["icon"]?.ToString();
            if (!string.IsNullOrEmpty(iconUrl) && !iconUrl.StartsWith("http")) iconUrl = "https://www.minebbs.com" + iconUrl;

            var img = new Image { Width = 64, Height = 64, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Top };
            if (!string.IsNullOrEmpty(iconUrl)) img.Source = new BitmapImage(new Uri(iconUrl));
            else img.Source = new BitmapImage(new Uri("ms-appx:///Assets/StoreLogo.png"));

            grid.Children.Add(img);

            var stack = new StackPanel();
            Grid.SetColumn(stack, 1);

            stack.Children.Add(new TextBlock
            {
                Text = basic["title"]?.ToString(),
                FontSize = 20,
                FontWeight = Windows.UI.Text.FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap
            });

            stack.Children.Add(new TextBlock
            {
                Text = $"版本: {basic["version"]}",
                FontSize = 14,
                Foreground = new SolidColorBrush(Colors.Gray),
                Margin = new Thickness(0, 4, 0, 0)
            });

            stack.Children.Add(new TextBlock
            {
                Text = $"作者: {basic["author"]?["name"]}",
                FontSize = 14,
                Foreground = new SolidColorBrush(Colors.Teal),
                Margin = new Thickness(0, 2, 0, 0)
            });

            if (basic["labels"] is JArray labels)
            {
                var labelStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
                foreach (var label in labels)
                {
                    var labelText = label is JObject ? label["text"]?.ToString() : label.ToString();
                    var border = new Border
                    {
                        Background = new SolidColorBrush(Colors.Orange),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(6, 2, 6, 2),
                        Margin = new Thickness(0, 0, 8, 0)
                    };
                    border.Child = new TextBlock { Text = labelText, FontSize = 12, Foreground = new SolidColorBrush(Colors.White) };
                    labelStack.Children.Add(border);
                }
                stack.Children.Add(labelStack);
            }

            grid.Children.Add(stack);
            ContentPanel.Children.Add(grid);
        }

        private void BuildResourceStats(JToken stats, JToken sidebar)
        {
            var statGrid = new Grid { Margin = new Thickness(12, 0, 12, 12) };
            for (int i = 0; i < 4; i++) statGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            string dl = stats?["downloads"]?.ToString() ?? sidebar?["downloads"]?.ToString() ?? "-";
            string views = stats?["views"]?.ToString() ?? sidebar?["views"]?.ToString() ?? "-";
            string rating = sidebar?["rating"]?["stars"]?.ToString() ?? "-";

            AddStatItem(statGrid, 0, "📥 下载", dl);
            AddStatItem(statGrid, 1, "👁 浏览", views);
            AddStatItem(statGrid, 2, "⭐ 评分", rating);
            AddStatItem(statGrid, 3, "📅 更新", stats?["lastUpdateText"]?.ToString() ?? "未知");

            ContentPanel.Children.Add(statGrid);
        }

        private void AddStatItem(Grid grid, int col, string label, string value)
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(new TextBlock { Text = value, FontSize = 16, FontWeight = Windows.UI.Text.FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center });
            stack.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = new SolidColorBrush(Colors.Gray), HorizontalAlignment = HorizontalAlignment.Center });
            Grid.SetColumn(stack, col);
            grid.Children.Add(stack);
        }

        private void BuildResourceDescription(string html)
        {
            if (string.IsNullOrEmpty(html)) return;
            var container = new StackPanel { Margin = new Thickness(12) };
            container.Children.Add(new TextBlock
            {
                Text = "📝 介绍",
                FontSize = 18,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });
            container.Children.Add(CreateAutoHeightWebView(html));
            ContentPanel.Children.Add(container);
        }

        private void BuildCustomFields(JObject fields)
        {
            var panel = new StackPanel { Margin = new Thickness(12), Background = new SolidColorBrush(Color.FromArgb(10, 0, 0, 0)), Padding = new Thickness(8) };
            foreach (var prop in fields.Properties())
            {
                var valObj = prop.Value;
                string label = valObj["label"]?.ToString() ?? prop.Name;
                string value = valObj["value"] is JArray arr ? string.Join(", ", arr) : valObj["value"]?.ToString();

                var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                sp.Children.Add(new TextBlock { Text = label + ": ", FontWeight = Windows.UI.Text.FontWeights.Bold, Width = 100 });
                sp.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap });
                panel.Children.Add(sp);
            }
            ContentPanel.Children.Add(panel);
        }

        private void BuildUpdatesList(JArray updates)
        {
            var header = new TextBlock { Text = "更新日志", Margin = new Thickness(12, 12, 12, 4), FontWeight = Windows.UI.Text.FontWeights.Bold };
            ContentPanel.Children.Add(header);
            var listStack = new StackPanel { Margin = new Thickness(12) };

            foreach (var up in updates.Take(5))
            {
                var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var title = new TextBlock { Text = up["title"]?.ToString(), FontWeight = Windows.UI.Text.FontWeights.SemiBold };
                var ver = new TextBlock { Text = up["version"]?.ToString(), Foreground = new SolidColorBrush(Colors.Gray), HorizontalAlignment = HorizontalAlignment.Right };

                Grid.SetColumn(ver, 1);
                row.Children.Add(title);
                row.Children.Add(ver);
                listStack.Children.Add(row);
            }
            ContentPanel.Children.Add(listStack);
        }

        private void BuildThreadHeader(JToken thread)
        {
            if (thread == null) return;
            var stack = new StackPanel { Margin = new Thickness(12), Padding = new Thickness(12), Background = new SolidColorBrush(Color.FromArgb(20, 0, 0, 0)) };

            var prefix = thread["prefix"]?["text"]?.ToString();
            string titleText = thread["title"]?.ToString();

            if (!string.IsNullOrEmpty(prefix))
            {
                var border = new Border { Background = new SolidColorBrush(Colors.DodgerBlue), CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 2, 4, 2), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 4) };
                border.Child = new TextBlock { Text = prefix, Foreground = new SolidColorBrush(Colors.White), FontSize = 12 };
                stack.Children.Add(border);
            }

            stack.Children.Add(new TextBlock { Text = titleText, FontSize = 22, FontWeight = Windows.UI.Text.FontWeights.Bold, TextWrapping = TextWrapping.Wrap });

            if (thread["breadcrumbs"] is JArray crumbs && crumbs.Count > 0)
            {
                var crumbText = string.Join(" > ", crumbs.Select(c => c["text"]));
                stack.Children.Add(new TextBlock { Text = crumbText, FontSize = 12, Foreground = new SolidColorBrush(Colors.Gray), Margin = new Thickness(0, 4, 0, 0) });
            }

            ContentPanel.Children.Add(stack);
        }

        private UIElement CreatePostCard(JToken post)
        {
            var card = new Grid
            {
                Margin = new Thickness(12, 6, 12, 6),
                Padding = new Thickness(12),
                Background = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0)),
                BorderThickness = new Thickness(1)
            };

            if (Application.Current.RequestedTheme == ApplicationTheme.Dark)
            {
                card.Background = new SolidColorBrush(Color.FromArgb(255, 32, 32, 32));
                card.BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
            }

            card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var userGrid = new Grid();
            userGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            userGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var author = post["author"];
            string avatarUrl = author?["avatar"]?.ToString();
            if (!string.IsNullOrEmpty(avatarUrl) && !avatarUrl.StartsWith("http")) avatarUrl = "https://www.minebbs.com" + avatarUrl;

            var avatar = new Ellipse { Width = 36, Height = 36, Margin = new Thickness(0, 0, 10, 0) };
            if (!string.IsNullOrEmpty(avatarUrl)) avatar.Fill = new ImageBrush { ImageSource = new BitmapImage(new Uri(avatarUrl)) };
            else avatar.Fill = new SolidColorBrush(Colors.Gray);

            userGrid.Children.Add(avatar);

            var nameStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(nameStack, 1);

            var nameBlock = new TextBlock { Text = author?["username"]?.ToString() ?? author?["name"]?.ToString() ?? "Guest", FontWeight = Windows.UI.Text.FontWeights.Bold };
            nameStack.Children.Add(nameBlock);

            string userTitle = author?["title"]?.ToString() ?? author?["userTitle"]?.ToString();
            if (!string.IsNullOrEmpty(userTitle))
            {
                nameStack.Children.Add(new TextBlock { Text = userTitle, FontSize = 10, Foreground = new SolidColorBrush(Colors.Gray) });
            }

            userGrid.Children.Add(nameStack);
            card.Children.Add(userGrid);

            string html = post["content"]?["html"]?.ToString() ?? post["content"]?.ToString();
            if (!string.IsNullOrEmpty(html))
            {
                var contentWebView = CreateAutoHeightWebView(html);
                Grid.SetRow(contentWebView, 1);
                card.Children.Add(contentWebView);
            }

            var footerStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            Grid.SetRow(footerStack, 2);

            string dateStr = post["date"]?["text"]?.ToString() ?? post["createdAt"]?["display"]?.ToString();
            footerStack.Children.Add(new TextBlock { Text = dateStr, FontSize = 12, Foreground = new SolidColorBrush(Colors.Gray) });

            var reactions = post["reactions"];
            if (reactions != null)
            {
                int count = 0;
                if (reactions["total"] != null) count = reactions["total"].Value<int>();
                else if (reactions["summary"] is JArray arr) count = arr.Sum(r => r["count"]?.Value<int>() ?? 0);

                if (count > 0)
                {
                    footerStack.Children.Add(new TextBlock { Text = $" · 👍 {count}", FontSize = 12, Foreground = new SolidColorBrush(Colors.Gray), Margin = new Thickness(8, 0, 0, 0) });
                }
            }

            card.Children.Add(footerStack);
            return card;
        }

        private void BuildPollUI(JToken poll)
        {
            var card = new StackPanel { Margin = new Thickness(12), Background = new SolidColorBrush(Color.FromArgb(10, 0, 0, 255)), Padding = new Thickness(12) };
            card.Children.Add(new TextBlock { Text = "📊 投票: " + poll["title"]?.ToString(), FontWeight = Windows.UI.Text.FontWeights.Bold });

            if (poll["responses"] is JArray responses)
            {
                foreach (var resp in responses)
                {
                    var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    grid.Children.Add(new TextBlock { Text = resp["response"]?.ToString() });

                    var percent = resp["percentage"]?.ToString();
                    var pText = new TextBlock { Text = percent, FontWeight = Windows.UI.Text.FontWeights.Bold };
                    Grid.SetColumn(pText, 1);
                    grid.Children.Add(pText);

                    card.Children.Add(grid);
                }
            }
            ContentPanel.Children.Add(card);
        }

        private WebView CreateAutoHeightWebView(string htmlContent)
        {
            string css = @"<style>body{font-family:'Segoe UI',sans-serif;font-size:14px;margin:0;padding:0;overflow-wrap:break-word;} 
                           img{max-width:100%;height:auto;} a{color:#0078D7;text-decoration:none;} 
                           blockquote{background:#f0f0f0;border-left:4px solid #ccc;margin:10px 0;padding:5px 10px;}</style>";

            if (Application.Current.RequestedTheme == ApplicationTheme.Dark)
            {
                css += "<style>body{color:#E0E0E0;background-color:transparent;} blockquote{background:#333;border-color:#555;} a{color:#4CC2FF;}</style>";
            }

            string fullHtml = $@"<!DOCTYPE html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width, initial-scale=1'>{css}</head>
                                 <body>{htmlContent}</body></html>";

            var webView = new WebView
            {
                Height = 250,
                Margin = new Thickness(0, 8, 0, 8)
            };

            webView.NavigateToString(fullHtml);
            return webView;
        }

        private void ShowError(string msg)
        {
            _dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                ErrorTextBlock.Text = msg;
                ErrorPanel.Visibility = Visibility.Visible;
            });
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }

        #endregion
    }
}
