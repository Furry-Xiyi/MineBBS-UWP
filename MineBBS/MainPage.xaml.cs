using System;
using System.Linq;
using Windows.ApplicationModel.Core;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;       // 用于 Page, Frame
using Windows.UI.Xaml.Navigation;     // 用于 NavigationEventArgs
using Microsoft.UI.Xaml.Controls;

namespace MineBBS
{
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();

            // 1. 设置自定义标题栏
            var coreTitleBar = CoreApplication.GetCurrentView().TitleBar;
            coreTitleBar.ExtendViewIntoTitleBar = true;

            // 设置窗口标题栏按钮背景为透明，以便融入我们的设计
            var titleBar = ApplicationView.GetForCurrentView().TitleBar;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

            // 设置可拖动区域 (重要：否则无法拖动窗口)
            Window.Current.SetTitleBar(DragRegion);

            // 默认加载首页
            if (NavView.MenuItems.Count > 0)
            {
                NavView.SelectedItem = NavView.MenuItems[0];
                NavigateToPage("HomePage");
            }
        }

        // 导航项点击事件
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

        // 核心导航逻辑 + 防重复
        private void NavigateToPage(string pageTag)
        {
            string pageName = $"MineBBS.Views.{pageTag}";
            Type pageType = Type.GetType(pageName + ", MineBBS"); // 指定程序集名

            if (pageType != null && ContentFrame.CurrentSourcePageType != pageType)
            {
                ContentFrame.Navigate(pageType);
            }
        }

        // 后退按钮逻辑
        private void NavView_BackRequested(Microsoft.UI.Xaml.Controls.NavigationView sender,
                                           Microsoft.UI.Xaml.Controls.NavigationViewBackRequestedEventArgs args)
        {
            if (ContentFrame.CanGoBack)
            {
                ContentFrame.GoBack();
            }
        }

        // 更新后退按钮状态
        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            NavView.IsBackEnabled = ContentFrame.CanGoBack;

            // 保持侧边栏选中状态同步
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
    }
}