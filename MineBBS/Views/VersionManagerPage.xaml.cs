using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Newtonsoft.Json;
using muxc = Microsoft.UI.Xaml.Controls;

namespace MineBBS.Views
{
    // 模型类放在 code-behind
    public class VersionEntry
    {
        public string Name { get; set; }
        public string RootPath { get; set; }
        public string JsonPath { get; set; }
        public string AccessToken { get; set; } // 新增：保存 FutureAccessList token
    }

    public class FileItem
    {
        public string DisplayName { get; set; }
        public string FullPath { get; set; }
        public string VersionTag { get; set; }
        public DateTimeOffset ModifiedTime { get; set; }
    }

    public sealed partial class VersionManagerPage : Page
    {
        private readonly List<VersionEntry> _versions = new List<VersionEntry>();
        private VersionEntry _currentVersion;
        private string _currentCategory;

        public VersionManagerPage()
        {
            this.InitializeComponent();
            _ = LoadVersionsAsync(); // 页面初始化时加载持久化的版本信息
        }

        // 导航事件
        private async void NavView_ItemInvoked(muxc.NavigationView sender, muxc.NavigationViewItemInvokedEventArgs args)
        {
            var tag = args.InvokedItemContainer?.Tag?.ToString();
            if (tag == "AddFolder")
            {
                await ImportFolder();
            }
            else if (args.InvokedItemContainer?.Tag is VersionEntry version)
            {
                _currentVersion = version;
                CurrentVersionTitle.Text = version.Name;

                // 用 StorageFolder 访问 RootPath（需要 FutureAccessList 授权）
                StorageFolder rootFolder = null;
                try
                {
                    rootFolder = await StorageFolder.GetFolderFromPathAsync(version.RootPath);
                }
                catch (Exception ex)
                {
                    // 如果直接从路径失败，尝试从 FutureAccessList 恢复（可选：你可以把 token 存起来）
                    System.Diagnostics.Debug.WriteLine($"GetFolderFromPathAsync失败: {ex.Message}");
                }

                var categories = new List<string>();
                foreach (var cat in new[] { "mods", "resourcepacks", "saves", "shaderpacks" })
                {
                    var catPath = Path.Combine(version.RootPath, cat);

                    // 优先用 StorageFolder.TryGetItemAsync 检查存在性
                    var exists = false;
                    if (rootFolder != null)
                    {
                        var item = await rootFolder.TryGetItemAsync(cat);
                        exists = item is StorageFolder;
                    }
                    else
                    {
                        // 兜底：如果授权失败，尝试 System.IO（某些桌面路径可用）
                        exists = Directory.Exists(catPath);
                    }

                    if (exists) categories.Add(cat);
                }

                CategoryList.Items.Clear();
                foreach (var cat in categories)
                {
                    CategoryList.Items.Add(new ListViewItem { Content = cat, Tag = cat });
                }

                CategoryPanel.Visibility = Visibility.Visible;
                FileScroll.Visibility = Visibility.Collapsed;
                NavView.IsBackButtonVisible = muxc.NavigationViewBackButtonVisible.Visible;
                NavView.IsBackEnabled = true;
            }
        }

        private void NavView_BackRequested(muxc.NavigationView sender, muxc.NavigationViewBackRequestedEventArgs args)
        {
            if (FileScroll.Visibility == Visibility.Visible)
            {
                // 从文件列表返回到分类列表
                FileScroll.Visibility = Visibility.Collapsed;
                CategoryPanel.Visibility = Visibility.Visible;
                NavView.IsBackButtonVisible = muxc.NavigationViewBackButtonVisible.Visible; // 保持显示
            }
            else if (CategoryPanel.Visibility == Visibility.Visible)
            {
                // 从分类列表返回到版本选择（顶层）
                CategoryPanel.Visibility = Visibility.Collapsed;
                NavView.IsBackButtonVisible = muxc.NavigationViewBackButtonVisible.Collapsed; // 顶层直接隐藏按钮
                NavView.SelectedItem = null;
            }
        }

        // 导入文件夹
        private async Task ImportFolder()
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            var folder = await picker.PickSingleFolderAsync();
            if (folder == null) return;

            var jsonFile = await FindVersionJson(folder);
            if (jsonFile == null)
            {
                var dialog = new ContentDialog
                {
                    Title = "导入失败",
                    Content = "未找到版本 JSON 文件，请选择正确的版本目录。",
                    CloseButtonText = "确定"
                };
                await dialog.ShowAsync();
                return;
            }

            var rootPath = Path.GetDirectoryName(jsonFile.Path);

            // 防止重复导入
            if (_versions.Any(v => v.RootPath == rootPath))
            {
                var dialog = new ContentDialog
                {
                    Title = "提示",
                    Content = "该版本文件夹已导入。",
                    CloseButtonText = "确定"
                };
                await dialog.ShowAsync();
                return;
            }

            // 保存访问令牌
            var token = StorageApplicationPermissions.FutureAccessList.Add(folder);

            var entry = new VersionEntry
            {
                Name = Path.GetFileName(rootPath),
                RootPath = rootPath,
                JsonPath = jsonFile.Path,
                AccessToken = token
            };
            _versions.Add(entry);

            var item = new muxc.NavigationViewItem
            {
                Content = entry.Name,
                Tag = entry
            };

            // 判断是否预览版（快照）
            bool isPreview = entry.Name.Contains("w") || entry.Name.Contains("snapshot", StringComparison.OrdinalIgnoreCase);

            // 设置不同的图标
            item.Icon = new BitmapIcon
            {
                UriSource = new Uri("ms-appx:///Assets/Download icon/" +
                                    (isPreview ? "minecraftPreviewIcon.ico" : "minecraftIcon.ico")),
                ShowAsMonochrome = false // 保持彩色显示
            };

            NavView.MenuItems.Add(item);

            await SaveVersionsAsync(); // 持久化保存

            var successDialog = new ContentDialog
            {
                Title = "导入成功",
                Content = $"已成功导入版本：{entry.Name}",
                CloseButtonText = "确定"
            };
            await successDialog.ShowAsync();
        }

        // 保存版本列表到本地 JSON
        private async Task SaveVersionsAsync()
        {
            var localFolder = ApplicationData.Current.LocalFolder;
            var file = await localFolder.CreateFileAsync("versions.json", CreationCollisionOption.ReplaceExisting);
            var json = JsonConvert.SerializeObject(_versions);
            await FileIO.WriteTextAsync(file, json);
        }

        // 启动时加载版本信息
        private async Task LoadVersionsAsync()
        {
            var localFolder = ApplicationData.Current.LocalFolder;
            var file = await localFolder.TryGetItemAsync("versions.json") as StorageFile;
            if (file == null) return;

            var json = await FileIO.ReadTextAsync(file);
            var list = JsonConvert.DeserializeObject<List<VersionEntry>>(json);
            if (list == null) return;

            _versions.Clear();
            _versions.AddRange(list);

            // 恢复 NavView 菜单
            foreach (var entry in _versions)
            {
                var item = new muxc.NavigationViewItem
                {
                    Content = entry.Name,
                    Tag = entry
                };

                // 判断是否预览版（快照）
                bool isPreview = entry.Name.Contains("w") || entry.Name.Contains("snapshot", StringComparison.OrdinalIgnoreCase);

                // 设置不同的图标
                item.Icon = new BitmapIcon
                {
                    UriSource = new Uri("ms-appx:///Assets/Download icon/" +
                                        (isPreview ? "minecraftPreviewIcon.ico" : "minecraftIcon.ico")),
                    ShowAsMonochrome = false // 保持彩色显示
                };

                NavView.MenuItems.Add(item);
            }
        }

        private async Task<StorageFile> FindVersionJson(StorageFolder root)
        {
            // 查当前目录
            var files = await root.GetFilesAsync();
            foreach (var f in files.Where(f => f.FileType.Equals(".json", StringComparison.OrdinalIgnoreCase)))
            {
                var text = await FileIO.ReadTextAsync(f);
                if (text.Contains("\"arguments\"") || text.Contains("\"id\""))
                    return f;
            }

            // 子目录按名字长度排序（长的优先）
            var subFolders = await root.GetFoldersAsync();
            foreach (var sub in subFolders.OrderByDescending(sf => sf.Name.Length))
            {
                var result = await FindVersionJson(sub);
                if (result != null)
                    return result;
            }

            return null;
        }

        // 分类选择 → 加载文件
        private async void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = CategoryList.SelectedItem as ListViewItem;
            _currentCategory = item?.Tag as string;
            if (string.IsNullOrEmpty(_currentCategory)) return;

            var path = Path.Combine(_currentVersion.RootPath, _currentCategory);
            var files = await LoadFileItems(path);

            PopulateFileExpanders(files);

            CategoryPanel.Visibility = Visibility.Collapsed;
            FileScroll.Visibility = Visibility.Visible;

            // 激活返回按钮
            NavView.IsBackButtonVisible = muxc.NavigationViewBackButtonVisible.Visible;
            NavView.IsBackEnabled = true;
        }

        private async Task<List<FileItem>> LoadFileItems(string folderPath)
        {
            var items = new List<FileItem>();
            try
            {
                var folder = await StorageFolder.GetFolderFromPathAsync(folderPath);
                var files = await folder.GetFilesAsync();
                foreach (var f in files)
                {
                    var folderName = Path.GetFileName(folderPath);

                    // mods 文件夹只显示 .jar
                    if (folderName.Equals("mods", StringComparison.OrdinalIgnoreCase) &&
                        !f.FileType.Equals(".jar", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // shaderpacks / resourcepacks 文件夹只显示 .zip
                    if ((folderName.Equals("shaderpacks", StringComparison.OrdinalIgnoreCase) ||
                         folderName.Equals("resourcepacks", StringComparison.OrdinalIgnoreCase)) &&
                        !f.FileType.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var props = await f.GetBasicPropertiesAsync();
                    items.Add(new FileItem
                    {
                        DisplayName = Path.GetFileNameWithoutExtension(f.Name),
                        FullPath = f.Path,
                        ModifiedTime = props.DateModified,
                        VersionTag = ParseVersionTag(f.Name)
                    });
                }
            }
            catch { }
            return items;
        }

        private void PopulateFileExpanders(List<FileItem> items)
        {
            FileItems.Items.Clear();
            foreach (var f in items)
            {
                var headerGrid = new Grid
                {
                    Height = 50
                };

                // 用 VerticalAlignment.Center 保证整个内容居中
                var headerStack = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0)
                };

                // 第一行：文件名
                headerStack.Children.Add(new TextBlock
                {
                    Text = f.DisplayName,
                    FontSize = 14,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold
                });

                // 第二行：日期 + 版本号
                headerStack.Children.Add(new TextBlock
                {
                    Text = $"{f.ModifiedTime:yyyy-MM-dd}" + (string.IsNullOrEmpty(f.VersionTag) ? "" : $" | {f.VersionTag}"),
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.Gray)
                });

                headerGrid.Children.Add(headerStack);

                var expander = new muxc.Expander
                {
                    Header = headerGrid,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch
                };

                // 折叠内容区：操作按钮
                var btnPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Margin = new Thickness(0, 8, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left
                };

                var openBtn = new Button { Content = "打开所在位置", Tag = f.FullPath };
                openBtn.Click += OpenLocation_Click;
                btnPanel.Children.Add(openBtn);

                var copyBtn = new Button { Content = "复制并移动到...", Tag = f.FullPath };
                copyBtn.Click += CopyMove_Click;
                btnPanel.Children.Add(copyBtn);

                var delBtn = new Button { Content = "删除", Tag = f.FullPath, Foreground = new SolidColorBrush(Colors.Red) };
                delBtn.Click += Delete_Click;
                btnPanel.Children.Add(delBtn);

                expander.Content = btnPanel;

                FileItems.Items.Add(expander);
            }
        }

        private string ParseVersionTag(string name)
        {
            var m = Regex.Match(name, "(v?\\d+\\.\\d+(\\.\\d+)?)");
            return m.Success ? m.Value : "";
        }

        private async void OpenLocation_Click(object sender, RoutedEventArgs e)
        {
            var path = (sender as Button)?.Tag as string;
            if (string.IsNullOrEmpty(path)) return;
            await Launcher.LaunchFolderPathAsync(Path.GetDirectoryName(path));
        }

        private async void CopyMove_Click(object sender, RoutedEventArgs e)
        {
            var path = (sender as Button)?.Tag as string;
            if (string.IsNullOrEmpty(path)) return;

            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            var dest = await picker.PickSingleFolderAsync();
            if (dest == null) return;

            try
            {
                var srcFile = await StorageFile.GetFileFromPathAsync(path);
                await srcFile.CopyAsync(dest, srcFile.Name, NameCollisionOption.GenerateUniqueName);

                var dialog = new ContentDialog
                {
                    Title = "复制完成",
                    Content = $"文件已复制到：{dest.Path}",
                    CloseButtonText = "确定"
                };
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                var dialog = new ContentDialog
                {
                    Title = "复制失败",
                    Content = $"错误信息：{ex.Message}",
                    CloseButtonText = "确定"
                };
                await dialog.ShowAsync();
            }
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            var path = (sender as Button)?.Tag as string;
            if (string.IsNullOrEmpty(path)) return;

            var dialog = new ContentDialog
            {
                Title = "确认删除",
                Content = $"确定删除文件：{Path.GetFileName(path)}？此操作不可恢复。",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消"
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                try
                {
                    var file = await StorageFile.GetFileFromPathAsync(path);
                    await file.DeleteAsync();
                    // 删除后刷新当前分类列表
                    await RefreshCurrentCategory();
                }
                catch (Exception ex)
                {
                    await new MessageDialog($"删除失败：{ex.Message}").ShowAsync();
                }
            }
        }

        private async Task RefreshCurrentCategory()
        {
            if (_currentVersion == null || string.IsNullOrEmpty(_currentCategory)) return;
            var path = Path.Combine(_currentVersion.RootPath, _currentCategory);
            var files = await LoadFileItems(path);
            PopulateFileExpanders(files);
        }
    }
}