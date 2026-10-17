using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using Starward.Frameworks;
using Starward.Helpers;
using System;


namespace Starward.Features.Setting;

public sealed partial class SettingPage : PageBase
{


    private readonly ILogger<SettingPage> _logger = AppConfig.GetLogger<SettingPage>();


    public SettingPage()
    {
        this.InitializeComponent();
        Frame_Setting.Navigate(typeof(AboutSetting));
        Frame_Setting.Navigated += Frame_Setting_Navigated;
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, (_, _) => this.Bindings.Update());
    }



    private void NavigationView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        try
        {
            Type? type = args.InvokedItemContainer?.Tag switch
            {
                nameof(AboutSetting) => typeof(AboutSetting),
                nameof(GeneralSetting) => typeof(GeneralSetting),
                nameof(DownloadSetting) => typeof(DownloadSetting),
                nameof(FileManageSetting) => typeof(FileManageSetting),
                nameof(ScreenshotSetting) => typeof(ScreenshotSetting),
                nameof(AdvancedSetting) => typeof(AdvancedSetting),
                nameof(ToolboxSetting) => typeof(ToolboxSetting),
                nameof(HotkeySetting) => typeof(HotkeySetting),
                nameof(GamepadControlSetting) => typeof(GamepadControlSetting),
                _ => null,
            };
            if (type is not null)
            {
                _staggerNextPage = true;
                Frame_Setting.Navigate(type, null, new Microsoft.UI.Xaml.Media.Animation.SuppressNavigationTransitionInfo());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Setting page navigate.");
        }
    }



    private bool _staggerNextPage;


    private void Frame_Setting_Navigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        if (_staggerNextPage && e.Content is Microsoft.UI.Xaml.FrameworkElement page)
        {
            _staggerNextPage = false;
            // 先藏起来，等页面布局好再一项项浮上来，免得先闪一帧完整页面
            page.HideUntilEnter();
            page.Loaded += Page_Loaded;
        }
    }


    /// <summary>
    /// 设置子页都是 ScrollViewer 里一个 StackPanel，把它的子元素依次浮上来
    /// </summary>
    private void Page_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is not Microsoft.UI.Xaml.FrameworkElement page)
        {
            return;
        }
        page.Loaded -= Page_Loaded;
        page.ResetMotion();
        try
        {
            if (FindItemsPanel(page) is Panel panel)
            {
                var children = new System.Collections.Generic.List<Microsoft.UI.Xaml.UIElement>();
                foreach (var child in panel.Children)
                {
                    if (child.Visibility is Microsoft.UI.Xaml.Visibility.Visible)
                    {
                        children.Add(child);
                    }
                }
                _ = children.StaggerInAsync(offsetY: 12);
            }
            else
            {
                _ = page.EnterAsync(offsetY: 12);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stagger setting page");
        }
    }


    /// <summary>
    /// 广度优先找第一个有三个以上子元素的 Panel
    /// </summary>
    private static Panel? FindItemsPanel(Microsoft.UI.Xaml.DependencyObject root)
    {
        var queue = new System.Collections.Generic.Queue<Microsoft.UI.Xaml.DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node is Panel panel && panel.Children.Count >= 3)
            {
                return panel;
            }
            int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
            {
                queue.Enqueue(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i));
            }
        }
        return null;
    }



    protected override void OnUnloaded()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }



}
