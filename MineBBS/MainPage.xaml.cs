using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using System.Text.Json;
using Windows.ApplicationModel.Core;
using Windows.Storage;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Shapes;

namespace MineBBS
{
    public sealed partial class MainPage : Page
    {
        private const string USER_DATA_KEY = "UserData";
        private const string COOKIE_KEY = "UserCookie";
        private bool _isLoggedIn = false;
        private UserData _currentUser;

        public MainPage()
        {
            this.InitializeComponent();

            // 设置自定义标题栏
            var coreTitleBar = CoreApplication.GetCurrentView().TitleBar;
            coreTitleBar.ExtendViewIntoTitleBar = true;

            var titleBar = ApplicationView.GetForCurrentView().TitleBar;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

            Window.Current.SetTitleBar(DragRegion);

            // 加载用户数据
            LoadUserData();

            // 默认加载首页
            if (NavView.MenuItems.Count > 0)
            {
                NavView.SelectedItem = NavView.MenuItems[0];
                NavigateToPage("HomePage");
            }
        }

        #region 导航
        private void NavView_ItemInvoked(Microsoft.UI.Xaml.Controls.NavigationView sender,
                                         Microsoft.UI.Xaml.Controls.NavigationViewItemInvokedEventArgs args)
        {
            if (args.IsSettingsInvoked)
            {
                NavigateToPage("SettingsPage");
            }
            else
            {
                string tag = (args.InvokedItemContainer as Microsoft.UI.Xaml.Controls.NavigationViewItem)?.Tag?.ToString();
                if (!string.IsNullOrEmpty(tag))
                {
                    NavigateToPage(tag);
                }
            }
        }

        private void NavigateToPage(string pageTag)
        {
            string pageName = $"MineBBS.Views.{pageTag}";
            Type pageType = Type.GetType(pageName + ", MineBBS");

            if (pageType != null && ContentFrame.CurrentSourcePageType != pageType)
            {
                ContentFrame.Navigate(pageType);
            }
        }

        private void NavView_BackRequested(Microsoft.UI.Xaml.Controls.NavigationView sender,
                                           Microsoft.UI.Xaml.Controls.NavigationViewBackRequestedEventArgs args)
        {
            if (ContentFrame.CanGoBack)
            {
                ContentFrame.GoBack();
            }
        }

        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            NavView.IsBackEnabled = ContentFrame.CanGoBack;

            if (e.SourcePageType == typeof(Views.SettingsPage))
            {
                NavView.SelectedItem = (Microsoft.UI.Xaml.Controls.NavigationViewItem)NavView.SettingsItem;
            }
            else
            {
                var tag = e.SourcePageType.Name;
                var item = NavView.MenuItems
                    .OfType<Microsoft.UI.Xaml.Controls.NavigationViewItem>()
                    .FirstOrDefault(i => i.Tag?.ToString() == tag);

                if (item != null)
                {
                    NavView.SelectedItem = item;
                }
            }
        }
        #endregion

        #region 搜索
        private void AppTitleBar_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 根据窗口宽度切换搜索框和按钮
            double width = Window.Current.Bounds.Width;

            if (width >= 690)
            {
                SearchBox.Visibility = Visibility.Visible;
                SearchIconButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                SearchBox.Visibility = Visibility.Collapsed;
                SearchIconButton.Visibility = Visibility.Visible;
            }
        }

        private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (!string.IsNullOrWhiteSpace(args.QueryText))
            {
                PerformSearch(args.QueryText);
            }
        }

        private void FlyoutSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (!string.IsNullOrWhiteSpace(args.QueryText))
            {
                SearchFlyout.Hide();
                PerformSearch(args.QueryText);
            }
        }

        private void FlyoutSearchBox_Loaded(object sender, RoutedEventArgs e)
        {
            // Flyout打开后自动聚焦到搜索框
            FlyoutSearchBox.Focus(FocusState.Programmatic);
        }

        private void PerformSearch(string query)
        {
            if (ContentFrame.Content is MineBBS.Interfaces.ISearchable searchable)
            {
                searchable.PerformSearch(query);
            }
            else
            {
                var searchUrl = $"https://www.minebbs.com/search/?q={Uri.EscapeDataString(query)}";
                ContentFrame.Navigate(typeof(Views.WebViewPage), Tuple.Create(searchUrl, "搜索结果"));
            }
        }
        #endregion

        #region 用户登录/退出
        private async void LoginMenuItem_Click(object sender, RoutedEventArgs e)
        {
            UserFlyout.Hide();

            var loginUrl = "https://www.minebbs.com/login/";

            var dialog = new ContentDialog
            {
                Title = "登录 MineBBS",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close
            };

            var webView = new Microsoft.UI.Xaml.Controls.WebView2
            {
                Height = 500,
                Width = 450
            };

            dialog.Content = webView;

            try
            {
                await webView.EnsureCoreWebView2Async();

                // 在导航前先清除旧的Cookie（重要！）
                try
                {
                    var oldCookies = await webView.CoreWebView2.CookieManager.GetCookiesAsync("https://www.minebbs.com");
                    foreach (var cookie in oldCookies)
                    {
                        webView.CoreWebView2.CookieManager.DeleteCookie(cookie);
                    }
                    System.Diagnostics.Debug.WriteLine($"清除了 {oldCookies.Count} 个旧Cookie");
                }
                catch (Exception clearEx)
                {
                    System.Diagnostics.Debug.WriteLine($"清除旧Cookie失败：{clearEx.Message}");
                }

                webView.CoreWebView2.Navigate(loginUrl);

                // 监听导航完成，检测是否登录成功
                webView.CoreWebView2.NavigationCompleted += async (s, navArgs) =>
                {
                    if (navArgs.IsSuccess)
                    {
                        var currentUrl = webView.Source.ToString();

                        if (currentUrl.Contains("/members/") ||
                            (currentUrl == "https://www.minebbs.com/" && !currentUrl.Contains("login")))
                        {
                            var cookies = await webView.CoreWebView2.CookieManager.GetCookiesAsync("https://www.minebbs.com/");
                            if (cookies.Count > 0)
                            {
                                var cookieString = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
                                ApplicationData.Current.LocalSettings.Values[COOKIE_KEY] = cookieString;

                                try
                                {
                                    var script = @"
                                (function() {
                                    try {
                                        var avatarImg = document.querySelector('.p-navgroup-link--user .avatar img, .p-nav-opposite .avatar img');
                                        var nameElem = document.querySelector('.p-navgroup-link--user .p-navgroup-linkText, .p-nav-opposite .username');
                                        
                                        if (avatarImg && nameElem) {
                                            return JSON.stringify({
                                                avatar: avatarImg.src,
                                                name: nameElem.textContent.trim(),
                                                email: ''
                                            });
                                        }
                                        return null;
                                    } catch (e) {
                                        return null;
                                    }
                                })();
                            ";

                                    var result = await webView.CoreWebView2.ExecuteScriptAsync(script);
                                    if (!string.IsNullOrEmpty(result) && result != "null" && result != "\"null\"")
                                    {
                                        result = result.Trim('"').Replace("\\\"", "\"");

                                        var jsonDoc = System.Text.Json.JsonDocument.Parse(result);
                                        var root = jsonDoc.RootElement;

                                        var name = root.TryGetProperty("name", out var nameElem) ? nameElem.GetString() : "";
                                        var avatar = root.TryGetProperty("avatar", out var avatarElem) ? avatarElem.GetString() : "";
                                        var email = root.TryGetProperty("email", out var emailElem) ? emailElem.GetString() : "";

                                        var simpleJson = $"{{\"name\":\"{name}\",\"avatar\":\"{avatar}\",\"email\":\"{email}\"}}";
                                        ApplicationData.Current.LocalSettings.Values[USER_DATA_KEY] = simpleJson;

                                        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                                        {
                                            LoadUserData();
                                            dialog.Hide();
                                        });
                                    }
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"获取用户信息失败：{ex.Message}");
                                }
                            }
                        }
                    }
                };

                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                var errorDialog = new ContentDialog
                {
                    Title = "登录失败",
                    Content = $"无法打开登录页面：{ex.Message}",
                    CloseButtonText = "确定"
                };
                await errorDialog.ShowAsync();
            }
        }

        private void ProfileMenuItem_Click(object sender, RoutedEventArgs e)
        {
            UserFlyout.Hide();
            ContentFrame.Navigate(typeof(Views.WebViewPage), Tuple.Create("https://www.minebbs.com/account/", "个人资料"));
        }

        private void MessagesMenuItem_Click(object sender, RoutedEventArgs e)
        {
            UserFlyout.Hide();
            ContentFrame.Navigate(typeof(Views.WebViewPage), Tuple.Create("https://www.minebbs.com/conversations/", "私信"));
        }

        private void NotificationsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            UserFlyout.Hide();
            ContentFrame.Navigate(typeof(Views.WebViewPage), Tuple.Create("https://www.minebbs.com/account/alerts", "通知"));
        }

        private void ApplyCarouselButton_Click(object sender, RoutedEventArgs e)
        {
            UserFlyout.Hide();
            ContentFrame.Navigate(typeof(Views.WebViewPage), Tuple.Create("https://www.minebbs.com/threads/minebbs.43513/", "申请轮播图"));
        }

        private async void LogoutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            UserFlyout.Hide();

            var confirmDialog = new ContentDialog
            {
                Title = "确认退出",
                Content = "确定要退出登录吗？",
                PrimaryButtonText = "退出",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close
            };

            var result = await confirmDialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                try
                {
                    // 方法1：创建临时WebView2清除Cookie
                    var tempWebView = new Microsoft.UI.Xaml.Controls.WebView2();
                    await tempWebView.EnsureCoreWebView2Async();

                    // 清除MineBBS网站的所有Cookie
                    var cookies = await tempWebView.CoreWebView2.CookieManager.GetCookiesAsync("https://www.minebbs.com");
                    foreach (var cookie in cookies)
                    {
                        tempWebView.CoreWebView2.CookieManager.DeleteCookie(cookie);
                    }

                    System.Diagnostics.Debug.WriteLine($"已清除 {cookies.Count} 个Cookie");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"清除Cookie失败：{ex.Message}");
                }

                // 清除应用保存的数据
                ApplicationData.Current.LocalSettings.Values.Remove(USER_DATA_KEY);
                ApplicationData.Current.LocalSettings.Values.Remove(COOKIE_KEY);

                // 更新UI
                _isLoggedIn = false;
                _currentUser = null;
                UpdateUserUI();
            }
        }

        private void LoadUserData()
        {
            var userData = ApplicationData.Current.LocalSettings.Values[USER_DATA_KEY] as string;
            var cookie = ApplicationData.Current.LocalSettings.Values[COOKIE_KEY] as string;

            if (!string.IsNullOrEmpty(userData) && !string.IsNullOrEmpty(cookie))
            {
                try
                {
                    // 手动解析JSON，不使用反射
                    _currentUser = ParseUserDataManually(userData);
                    _isLoggedIn = _currentUser != null;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"解析用户数据失败：{ex.Message}");
                    _isLoggedIn = false;
                    _currentUser = null;
                }
            }
            else
            {
                _isLoggedIn = false;
                _currentUser = null;
            }

            UpdateUserUI();
        }

        // 添加手动解析JSON的方法
        private UserData ParseUserDataManually(string json)
        {
            try
            {
                var userData = new UserData();

                // 简单的JSON手动解析
                json = json.Trim();
                if (json.StartsWith("{") && json.EndsWith("}"))
                {
                    json = json.Substring(1, json.Length - 2); // 移除 { }

                    var pairs = json.Split(',');
                    foreach (var pair in pairs)
                    {
                        var colonIndex = pair.IndexOf(':');
                        if (colonIndex > 0)
                        {
                            var key = pair.Substring(0, colonIndex).Trim().Trim('"');
                            var value = pair.Substring(colonIndex + 1).Trim().Trim('"');

                            switch (key.ToLower())
                            {
                                case "name":
                                    userData.Name = value;
                                    break;
                                case "avatar":
                                    userData.Avatar = value;
                                    break;
                                case "email":
                                    userData.Email = value;
                                    break;
                            }
                        }
                    }
                }

                return userData;
            }
            catch
            {
                return null;
            }
        }

        private void UpdateUserUI()
        {
            if (_isLoggedIn && _currentUser != null)
            {
                // 更新用户名
                UserPicture.DisplayName = _currentUser.Name ?? "用户";
                FlyoutUserPicture.DisplayName = _currentUser.Name ?? "用户";
                FlyoutUserNameText.Text = _currentUser.Name ?? "用户";
                FlyoutUserEmailText.Text = string.IsNullOrEmpty(_currentUser.Email) ? "MineBBS 用户" : _currentUser.Email;

                // 只加载外层头像按钮的头像，Flyout内的头像在Flyout打开时加载
                if (!string.IsNullOrEmpty(_currentUser.Avatar))
                {
                    try
                    {
                        var bitmap = new BitmapImage(new Uri(_currentUser.Avatar));
                        UserPicture.ProfilePicture = bitmap;
                        // 不在这里设置 FlyoutUserPicture
                        System.Diagnostics.Debug.WriteLine($"头像按钮加载成功: {_currentUser.Avatar}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"头像按钮加载失败: {ex.Message}");
                        UserPicture.ProfilePicture = null;
                    }
                }
                else
                {
                    UserPicture.ProfilePicture = null;
                }

                // 显示已登录状态的菜单项
                LoginButton.Visibility = Visibility.Collapsed;
                ProfileButton.Visibility = Visibility.Visible;
                MessagesButton.Visibility = Visibility.Visible;
                NotificationsButton.Visibility = Visibility.Visible;
                UserSeparator.Visibility = Visibility.Visible;
                CarouselSeparator.Visibility = Visibility.Visible;
                ApplyCarouselButton.Visibility = Visibility.Visible;
                UserMenuSeparator.Visibility = Visibility.Visible;
                LogoutButton.Visibility = Visibility.Visible;
            }
            else
            {
                // 显示未登录状态
                UserPicture.DisplayName = "访客";
                UserPicture.ProfilePicture = null;
                FlyoutUserPicture.DisplayName = "访客";
                FlyoutUserPicture.ProfilePicture = null;
                FlyoutUserNameText.Text = "访客";
                FlyoutUserEmailText.Text = "点击登录";

                LoginButton.Visibility = Visibility.Visible;
                ProfileButton.Visibility = Visibility.Collapsed;
                MessagesButton.Visibility = Visibility.Collapsed;
                NotificationsButton.Visibility = Visibility.Collapsed;
                UserSeparator.Visibility = Visibility.Visible;
                CarouselSeparator.Visibility = Visibility.Collapsed;
                ApplyCarouselButton.Visibility = Visibility.Collapsed;
                UserMenuSeparator.Visibility = Visibility.Collapsed;
                LogoutButton.Visibility = Visibility.Collapsed;
            }
        }
        private void SearchIconButton_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            AnimatedIcon.SetState(SearchAnimatedIcon, "PointerOver");
        }

        private void SearchIconButton_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            AnimatedIcon.SetState(SearchAnimatedIcon, "Normal");
        }

        private void SearchIconButton_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            AnimatedIcon.SetState(SearchAnimatedIcon, "Pressed");
        }

        private void SearchIconButton_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            AnimatedIcon.SetState(SearchAnimatedIcon, "PointerOver");
        }

        private void UserFlyout_Opened(object sender, object e)
        {
            // Flyout打开时，确保头像正确显示
            if (_isLoggedIn && _currentUser != null && !string.IsNullOrEmpty(_currentUser.Avatar))
            {
                try
                {
                    // 如果已经有头像，直接使用
                    if (UserPicture.ProfilePicture != null)
                    {
                        FlyoutUserPicture.ProfilePicture = UserPicture.ProfilePicture;
                    }
                    else
                    {
                        // 否则重新加载
                        var bitmap = new BitmapImage(new Uri(_currentUser.Avatar));
                        FlyoutUserPicture.ProfilePicture = bitmap;
                    }
                    System.Diagnostics.Debug.WriteLine("Flyout头像已更新");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Flyout头像加载失败: {ex.Message}");
                }
            }
        }
        #endregion

        #region 轮播图指示器（静态方法供HomePage使用）
        public static void AttachIndicators(FlipView flipView, Grid parentGrid, int bottomMargin = 12, int intervalSeconds = 5)
        {
            var indicatorPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, bottomMargin)
            };

            // 动态创建指示器
            void UpdateIndicators()
            {
                indicatorPanel.Children.Clear();

                for (int i = 0; i < flipView.Items.Count; i++)
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

                if (indicatorPanel.Children.Count > 0 && flipView.SelectedIndex >= 0 && flipView.SelectedIndex < indicatorPanel.Children.Count)
                {
                    ((Ellipse)indicatorPanel.Children[flipView.SelectedIndex]).Fill =
                        (Brush)Application.Current.Resources["SystemControlHighlightAccentBrush"];
                }
            }

            // 初始化指示器
            UpdateIndicators();

            // 监听Items变化（如果FlipView的Items改变了，需要重新创建指示器）
            flipView.Items.VectorChanged += (s, e) => UpdateIndicators();

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

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(intervalSeconds) };
            timer.Tick += (s, e) =>
            {
                if (flipView.Items.Count > 0)
                {
                    int nextIndex = (flipView.SelectedIndex + 1) % flipView.Items.Count;
                    flipView.SelectedIndex = nextIndex;
                }
            };
            timer.Start();

            parentGrid.Children.Add(indicatorPanel);
        }
        #endregion
        private void UserAvatarButton_Click(object sender, RoutedEventArgs e)
        {
            FlyoutBase.ShowAttachedFlyout((FrameworkElement)sender);
        }

        #region 用户数据模型
        private class UserData
        {
            public string Name { get; set; }
            public string Avatar { get; set; }
            public string Email { get; set; }
        }
        #endregion
    }
}