using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;

namespace MineBBS.Views
{
    [Bindable]
    public class MinecraftVersion
    {
        public string Version { get; set; } = "";
        public string Size { get; set; } = "";
        public string Date { get; set; } = "";
        public string Downloads { get; set; } = "";
        public string Description { get; set; } = "";
        public string DetailUrl { get; set; } = "";
        public string ShareUrl { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string MirrorUrl { get; set; } = "";

        // 保持布尔属性
        public bool HasDetailUrl => !string.IsNullOrEmpty(DetailUrl);
        public bool HasShareUrl => !string.IsNullOrEmpty(ShareUrl);
        public bool HasMirrorUrl => !string.IsNullOrEmpty(MirrorUrl);
    }
}