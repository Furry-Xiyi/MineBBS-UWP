using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace MineBBS.Views
{
    public sealed partial class WebViewPage : Page
    {
        private string _optionText = "完整版本";
        private string _pageUrl = string.Empty;
        private string _pageTitle = "详情";

        public WebViewPage()
        {
            InitializeComponent();
        }

        private void WebViewPage_BackRequested(object sender, BackRequestedEventArgs e)
        {
            if (DownloadWebView.CanGoBack)
            {
                DownloadWebView.GoBack();
                e.Handled = true;
            }
            else if (Frame.CanGoBack)
            {
                Frame.GoBack();
                e.Handled = true;
            }
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is Tuple<string, string> param)
            {
                _pageUrl = param.Item1 ?? string.Empty;
                _pageTitle = string.IsNullOrWhiteSpace(param.Item2) ? "详情" : param.Item2;

                // 特殊处理：如果是下载页面，还需要optionText
                if (e.Parameter is Tuple<string, string, string> paramWithOption)
                {
                    _optionText = paramWithOption.Item3 ?? "完整版本";
                }

                DownloadOverlay.Visibility = Visibility.Visible;
                await InitializeWebViewAsync(_pageUrl);
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            // 清理WebView2
            if (DownloadWebView != null)
            {
                DownloadWebView.Close();
            }
        }

        private async Task InitializeWebViewAsync(string url)
        {
            try
            {
                await DownloadWebView.EnsureCoreWebView2Async();
                var core = DownloadWebView.CoreWebView2;

                // 设置User Agent
                core.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

                // 新窗口处理
                core.NewWindowRequested += async (s, e) =>
                {
                    var deferral = e.GetDeferral();
                    try
                    {
                        var newWebView = new WebView2
                        {
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            VerticalAlignment = VerticalAlignment.Stretch
                        };
                        await newWebView.EnsureCoreWebView2Async();

                        e.NewWindow = newWebView.CoreWebView2;
                        e.Handled = true;

                        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                        {
                            DownloadOverlay.Visibility = Visibility.Visible;

                            if (DownloadContainer is Panel panel)
                            {
                                var toRemove = panel.Children
                                    .OfType<WebView2>()
                                    .Where(v => v != DownloadWebView)
                                    .ToList();
                                foreach (var v in toRemove) panel.Children.Remove(v);

                                panel.Children.Add(newWebView);
                            }
                        });
                    }
                    finally
                    {
                        deferral.Complete();
                    }
                };

                // 导航事件
                core.NavigationStarting += (s, args) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[WebView2] NavigationStarting -> {args.Uri}");

                    // 显示加载指示器（如果你的XAML中有的话）
                    // LoadingRing.IsActive = true;
                };

                core.NavigationCompleted += async (s, args) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[WebView2] NavigationCompleted -> Success={args.IsSuccess}, Uri={DownloadWebView.Source}");

                    // 隐藏加载指示器
                    // LoadingRing.IsActive = false;

                    if (args.IsSuccess && _pageTitle == "下载")
                    {
                        // 只在下载页面触发自动点击
                        await TriggerDownloadOptionAsync(_optionText);
                    }
                };

                // 页面标题变化
                core.DocumentTitleChanged += (s, args) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[WebView2] Title changed: {core.DocumentTitle}");
                };

                // 导航到目标URL
                if (!string.IsNullOrWhiteSpace(url))
                {
                    DownloadWebView.Source = new Uri(url);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WebView2] 初始化失败: {ex.Message}");
            }
        }

        private async Task TriggerDownloadOptionAsync(string optionText)
        {
            string script = @"
(async function() {
  function sleep(ms) { return new Promise(r => setTimeout(r, ms)); }
  function log(msg) { console.log('[auto] ' + msg); }

  // 找下载按钮
  let btn = null;
  for (let i = 0; i < 20; i++) {
    btn = document.querySelector('.n-thing-header__extra button');
    if (btn) break;
    await sleep(300);
  }
  if (!btn) return 'btn-not-found';

  // hover 下载按钮
  btn.dispatchEvent(new MouseEvent('mouseenter', { bubbles: true }));
  btn.dispatchEvent(new MouseEvent('mouseover', { bubbles: true }));

  // 等菜单出现
  let menuRoot = null;
  for (let i = 0; i < 20; i++) {
    await sleep(300);
    menuRoot = document.querySelector('body > div.v-binder-follower-container .n-dropdown-menu');
    if (menuRoot) break;
  }
  if (!menuRoot) return 'menu-not-found';

  // 再延迟 0.8 秒，确保选项渲染出来
  await sleep(800);

  // 找目标选项容器
  let target = null;
  if ('" + optionText + @"' === '完整版本') {
    target = document.querySelector('body > div.v-binder-follower-container .n-dropdown-option:nth-child(1)');
  } else {
    target = document.querySelector('body > div.v-binder-follower-container .n-dropdown-option:nth-child(2)');
  }

  if (!target) return 'option-not-found';

  // 模拟完整点击流程
  target.dispatchEvent(new MouseEvent('mouseenter', { bubbles: true }));
  target.dispatchEvent(new MouseEvent('mouseover', { bubbles: true }));
  target.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
  target.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }));
  target.dispatchEvent(new MouseEvent('click', { bubbles: true }));

  log('clicked option: " + optionText + @"');
  return 'clicked';
})();
";
            try
            {
                var result = await DownloadWebView.ExecuteScriptAsync(script);
                System.Diagnostics.Debug.WriteLine($"[WebView2] Trigger result: {result}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WebView2] Script execution failed: {ex.Message}");
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            DownloadWebView?.Reload();
        }

        private async void OpenInBrowserButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_pageUrl))
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri(_pageUrl));
            }
            else if (DownloadWebView?.Source != null)
            {
                await Windows.System.Launcher.LaunchUriAsync(DownloadWebView.Source);
            }
        }

        private void DownloadWebView_NavigationStarting(WebView2 sender, CoreWebView2NavigationStartingEventArgs args)
        {
            LoadingPanel.Visibility = Visibility.Visible;
        }

        private void DownloadWebView_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;

            // 更新URL地址栏
            if (DownloadWebView?.Source != null)
            {
                UrlTextBox.Text = DownloadWebView.Source.ToString();
            }
        }
    }
}