using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace MineBBS.Views
{
    public sealed partial class WebViewPage : Page
    {
        private string _optionText = "完整版本";
        private string _shareUrl = string.Empty;

        public WebViewPage()
        {
            InitializeComponent();
        }

        // 从 DownloadPage 导航过来时，传入 Tuple<string url, string option>
        protected override async void OnNavigatedTo(Windows.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is Tuple<string, string> param)
            {
                _shareUrl = param.Item1 ?? string.Empty;
                _optionText = string.IsNullOrWhiteSpace(param.Item2) ? "完整版本" : param.Item2;

                DownloadOverlay.Visibility = Visibility.Visible;
                await InitializeWebViewAsync(_shareUrl);
            }
        }

        private async Task InitializeWebViewAsync(string url)
        {
            await DownloadWebView.EnsureCoreWebView2Async();
            var core = DownloadWebView.CoreWebView2;

            // 新窗口仍然承载到内容区（复刻你的 NewWindowRequested）
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

                    // 把新窗口上下文交给站点
                    e.NewWindow = newWebView.CoreWebView2;
                    e.Handled = true;

                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        DownloadOverlay.Visibility = Visibility.Visible;

                        if (DownloadContainer is Panel panel)
                        {
                            // 只保留一个子窗口（和你原来一致）
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

            // 复刻你的日志输出
            core.NavigationStarting += (s, args) =>
            {
                System.Diagnostics.Debug.WriteLine($"[WebView2] NavigationStarting -> {args.Uri}");
            };
            core.NavigationCompleted += async (s, args) =>
            {
                System.Diagnostics.Debug.WriteLine($"[WebView2] NavigationCompleted -> Success={args.IsSuccess}, Uri={DownloadWebView.Source}");
                if (args.IsSuccess)
                {
                    await TriggerDownloadOptionAsync(_optionText);
                }
            };

            // 导航到详情页
            if (!string.IsNullOrWhiteSpace(url))
            {
                DownloadWebView.Source = new Uri(url);
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
  if (optionText === '完整版本') {
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

  log('clicked option: ' + optionText);
  return 'clicked';
})();
";
            var result = await DownloadWebView.ExecuteScriptAsync(script);
            System.Diagnostics.Debug.WriteLine($"[WebView2] Trigger result: {result}");
        }
    }
}