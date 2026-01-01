using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.Web.Http.Headers;
// 用于普通 API 请求/JSON 的类型
using NetHttpClient = System.Net.Http.HttpClient;
using NetHttpClientHandler = System.Net.Http.HttpClientHandler;
using NetHttpMethod = System.Net.Http.HttpMethod;
using NetHttpRequestMessage = System.Net.Http.HttpRequestMessage;
using NetHttpResponseMessage = System.Net.Http.HttpResponseMessage;
// 用于 UWP WebView 导航的类型
using WinHttpClient = Windows.Web.Http.HttpClient;
using WinHttpHeaders = Windows.Web.Http.Headers;
using WinHttpMethod = Windows.Web.Http.HttpMethod;
using WinHttpRequestMessage = Windows.Web.Http.HttpRequestMessage;

namespace MineBBS.Views
{
    public sealed partial class DownloadPage : Page
    {
        private const string ApiBase = "https://api.mc.minebbs.com/api/v1/versions/";
        private int currentPage = 1;
        private int totalPages = 1;

        public DownloadPage()
        {
            this.InitializeComponent();
            this.Loaded += DownloadPage_Loaded;
        }


        private async void DownloadPage_Loaded(object sender, RoutedEventArgs e)
        {
            this.Loaded -= DownloadPage_Loaded;
            await LoadDataAsync();
        }

        private async Task LoadDataAsync(bool append = false)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                StatusText.Text = "正在获取版本数据…";
            });

            try
            {
                var newVersions = await FetchVersionsAsync(currentPage);

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    if (!append)
                    {
                        VersionList.Items.Clear();
                    }

                    foreach (var version in newVersions)
                    {
                        VersionList.Items.Add(version);
                    }

                    StatusText.Text = $"已加载 {VersionList.Items.Count} 个版本";
                    LoadMoreButton.Visibility = currentPage < totalPages ? Visibility.Visible : Visibility.Collapsed;
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    StatusText.Text = $"获取失败: {ex.Message}";
                });
            }
        }
        private async Task<string> GetJsonAsync(string url)
        {
            using (var http = new NetHttpClient())
            {
                return await http.GetStringAsync(url);
            }
        }
        private async Task<List<MinecraftVersion>> FetchVersionsAsync(int page)
        {
            var url = $"{ApiBase}?page={page}&pageSize=10&platform=3&environment=1";

            using (var http = new HttpClient())
            {
                var json = await GetJsonAsync($"{ApiBase}?page={page}&pageSize=10&platform=3&environment=1");
                var obj = JObject.Parse(json);

                totalPages = obj["totalPages"]?.ToObject<int>() ?? 1;

                var arr = obj["data"] as JArray;
                if (arr == null || arr.Count == 0)
                    throw new Exception("未找到版本数据");

                var list = new List<MinecraftVersion>();

                foreach (var item in arr)
                {
                    try
                    {
                        var version = new MinecraftVersion
                        {
                            Version = item["version"]?.ToString() ?? "未知版本",
                            Downloads = item["total_download_count"]?.ToString() ?? "0",
                            Description = item["log"]?.ToString()
                                          ?? item["abstract"]?.ToString()
                                          ?? "暂无更新说明"
                        };

                        // 时间戳转日期
                        var timeToken = item["time"];
                        if (timeToken != null && long.TryParse(timeToken.ToString(), out long unixTime))
                        {
                            version.Date = DateTimeOffset.FromUnixTimeSeconds(unixTime)
                                           .ToLocalTime()
                                           .ToString("yyyy-MM-dd");
                        }

                        // 文件大小
                        var sizeToken = item["size"];
                        if (sizeToken != null && long.TryParse(sizeToken.ToString(), out long sizeBytes))
                        {
                            var sizeMB = sizeBytes / 1024.0 / 1024.0;
                            version.Size = $"{sizeMB:F0} MB";
                        }

                        // 下载链接处理
                        var links = item["downloadlinks"] as JArray;
                        if (links != null && links.Count > 0)
                        {
                            string Normalize(string raw) =>
                                raw.StartsWith("//") ? "https:" + raw : raw;

                            version.DownloadUrl = Normalize(links[0].ToString());

                            if (links.Count > 1)
                                version.MirrorUrl = Normalize(links[1].ToString());
                        }

                        // 分享链接用版本详情页
                        var idToken = item["id"];
                        if (idToken != null)
                        {
                            version.ShareUrl = $"https://mc.minebbs.com/version/{idToken}";
                        }

                        list.Add(version);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"解析版本失败: {ex.Message}");
                        continue;
                    }
                }

                return list;
            }
        }

        private async void DetailButton_Click(object sender, RoutedEventArgs e)
        {
            var url = (sender as Button)?.Tag as string;
            if (!string.IsNullOrEmpty(url) && Uri.IsWellFormedUriString(url, UriKind.Absolute))
                await Launcher.LaunchUriAsync(new Uri(url));
        }

        private void ShareButton_Click(object sender, RoutedEventArgs e)
        {
            var url = (sender as Button)?.Tag as string;
            if (!string.IsNullOrEmpty(url) && Uri.IsWellFormedUriString(url, UriKind.Absolute))
            {
                var dataPackage = new DataPackage();
                dataPackage.SetText(url);
                Clipboard.SetContent(dataPackage);

                // 可选：提示用户已复制
                StatusText.Text = "分享链接已复制到剪贴板";
            }
        }

        private void DownloadPrimary_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuFlyoutItem;
            var version = menuItem?.DataContext as MinecraftVersion;
            if (version == null) return;

            Frame.Navigate(typeof(WebViewPage), Tuple.Create(version.ShareUrl, "完整版本"));
        }

        private void DownloadMirror_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuFlyoutItem;
            var version = menuItem?.DataContext as MinecraftVersion;
            if (version == null) return;

            Frame.Navigate(typeof(WebViewPage), Tuple.Create(version.ShareUrl, "备用通道"));
        }

        private async void LoadMoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentPage < totalPages)
            {
                currentPage++;
                await LoadDataAsync(append: true);
            }
        }
    }
}