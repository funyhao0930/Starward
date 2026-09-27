using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Starward.Core.Games;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


namespace Starward.Features.GameLauncher;

/// <summary>
/// 启动页右边侧栏的角色卡片按钮，点开才显示卡片，与实时便笺（<c>DailyNoteButton</c>）同一个样子。
/// <para/>
/// 本控件不认识具体是哪款游戏，数据都经 <see cref="RoleCardProviderRegistry"/> 取得；
/// 没有 Provider、或者本机查不到任何角色时连按钮一起藏起来。
/// </summary>
[INotifyPropertyChanged]
public sealed partial class GameRoleCardButton : UserControl
{

    private readonly ILogger<GameRoleCardButton> _logger = AppConfig.GetLogger<GameRoleCardButton>();

    private readonly RoleCardProviderRegistry _registry = AppConfig.GetService<RoleCardProviderRegistry>();

    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _countdownTimer;

    private CancellationTokenSource? _loadCts;

    /// <summary>
    /// 上次查询成功的时间，点开卡片时据此判断要不要重查
    /// </summary>
    private DateTimeOffset _lastLoadTime;


    public GameRoleCardButton()
    {
        this.InitializeComponent();
        this.Loaded += GameRoleCard_Loaded;
        this.Unloaded += GameRoleCard_Unloaded;
        // 倒计时只精确到分钟，半分钟推算一次足够
        _countdownTimer = DispatcherQueue.CreateTimer();
        _countdownTimer.Interval = TimeSpan.FromSeconds(30);
        _countdownTimer.IsRepeating = true;
        _countdownTimer.Tick += (_, _) => UpdateStats();
    }



    public GameKey CurrentGameKey
    {
        get; set
        {
            if (field != value)
            {
                field = value;
                if (IsLoaded)
                {
                    _ = LoadAsync();
                }
            }
        }
    }


    /// <summary>
    /// 侧栏按钮的图标，由 Provider 给出
    /// </summary>
    public string? IconUri { get; set => SetProperty(ref field, value); }


    public List<GameRoleCardRole>? Roles { get; set => SetProperty(ref field, value); }

    public bool CanSwitchRole => Roles?.Count > 1;

    public GameRoleCardRole? SelectedRole { get; set => SetProperty(ref field, value); }

    public GameRoleCardData? Card
    {
        get; set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HasCard));
                OnPropertyChanged(nameof(RoleName));
            }
        }
    }

    public bool HasCard => Card is not null;

    public string? RoleName => Card?.RoleName ?? SelectedRole?.RoleName;

    public List<GameRoleCardStatItem>? Stats { get; set => SetProperty(ref field, value); }

    public string? ErrorMessage { get; set => SetProperty(ref field, value); }

    public bool IsLoading { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 要先登录的游戏还没登录（或凭证失效），卡片换成登录入口
    /// </summary>
    public bool NeedsLogin { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 已经在 Starward 里登录过，可以登出
    /// </summary>
    public bool CanLogout { get; set => SetProperty(ref field, value); }



    private async void GameRoleCard_Loaded(object sender, RoutedEventArgs e)
    {
        _countdownTimer.Start();
        await LoadAsync();
    }


    private void GameRoleCard_Unloaded(object sender, RoutedEventArgs e)
    {
        _countdownTimer.Stop();
        _loadCts?.Cancel();
        _loadCts = null;
    }



    /// <summary>
    /// 重新列出角色并查询选中的那个
    /// </summary>
    private async Task LoadAsync()
    {
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        CancellationToken token = cts.Token;
        try
        {
            IGameRoleCardProvider? provider = _registry.GetProvider(CurrentGameKey);
            if (provider is null)
            {
                ResetAndHide();
                return;
            }
            IconUri = provider.IconUri;
            NeedsLogin = false;
            CanLogout = provider.RequiresLogin && provider.IsLoggedIn(CurrentGameKey);
            // 要登录的游戏还没登录时照样显示按钮，否则玩家找不到登录入口
            if (provider.RequiresLogin && !provider.IsLoggedIn(CurrentGameKey))
            {
                ShowLogin(provider.LoginPrompt);
                return;
            }
            if (provider.RequiresLogin)
            {
                // 已经登录的游戏一定有东西可显示（卡片或原因），先把按钮亮出来，卡片里转圈。
                // 这类游戏要走网络才查得到角色，终末地的账号服务偶尔要十几秒才回应，
                // 等查完才显示的话，玩家看到的就是按钮不见了。
                this.Visibility = Visibility.Visible;
            }
            IsLoading = true;
            IReadOnlyList<GameRoleCardRole> roles;
            try
            {
                roles = await provider.GetRolesAsync(CurrentGameKey, token);
            }
            catch (GameRoleCardLoginRequiredException ex)
            {
                token.ThrowIfCancellationRequested();
                ShowLogin(ex.Message);
                return;
            }
            catch (GameRoleCardException ex)
            {
                // 本机有登录记录但查不到角色（凭证过期、维护中），卡片留着显示原因
                token.ThrowIfCancellationRequested();
                Roles = null;
                SelectedRole = null;
                Card = null;
                Stats = null;
                ErrorMessage = ex.Message;
                OnPropertyChanged(nameof(CanSwitchRole));
                this.Visibility = Visibility.Visible;
                return;
            }
            token.ThrowIfCancellationRequested();
            if (roles.Count == 0)
            {
                if (provider.RequiresLogin)
                {
                    // 存着的凭证解不开（数据文件夹被搬到别的电脑）时也会走到这里
                    ShowLogin(provider.LoginPrompt);
                    return;
                }
                ResetAndHide();
                return;
            }
            Roles = roles.ToList();
            OnPropertyChanged(nameof(CanSwitchRole));
            string? lastKey = AppConfig.GetValue<string>(null, SelectionConfigKey);
            SelectedRole = roles.FirstOrDefault(x => x.SelectionKey == lastKey) ?? roles[0];
            this.Visibility = Visibility.Visible;
            await QueryCardAsync(provider, SelectedRole, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // 换游戏或离开页面，新的一轮会接手
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load role card ({key})", CurrentGameKey);
        }
        finally
        {
            if (_loadCts == cts)
            {
                IsLoading = false;
            }
        }
    }



    private async Task QueryCardAsync(IGameRoleCardProvider provider, GameRoleCardRole role, CancellationToken token)
    {
        try
        {
            GameRoleCardData card = await provider.GetCardAsync(CurrentGameKey, role, token);
            token.ThrowIfCancellationRequested();
            ErrorMessage = null;
            Card = card;
            Stats = card.Stats.Select(x => new GameRoleCardStatItem(x)).ToList();
            _lastLoadTime = DateTimeOffset.Now;
        }
        catch (GameRoleCardLoginRequiredException ex)
        {
            token.ThrowIfCancellationRequested();
            ShowLogin(ex.Message);
        }
        catch (GameRoleCardException ex)
        {
            token.ThrowIfCancellationRequested();
            // 保留上一次的等级与 UID，只把数值区换成原因
            ErrorMessage = ex.Message;
        }
    }



    /// <summary>
    /// 卡片换成登录入口：一句说明加一个登录按钮
    /// </summary>
    private void ShowLogin(string? message)
    {
        Roles = null;
        SelectedRole = null;
        Card = null;
        Stats = null;
        ErrorMessage = message ?? "";
        NeedsLogin = true;
        OnPropertyChanged(nameof(CanSwitchRole));
        this.Visibility = Visibility.Visible;
    }



    [RelayCommand]
    private async Task LoginAsync()
    {
        try
        {
            if (_registry.GetProvider(CurrentGameKey) is not IGameRoleCardProvider provider)
            {
                return;
            }
            // 登录窗口是另开的，卡片留着会挡在前面
            Flyout_Card.Hide();
            if (await provider.LoginAsync(CurrentGameKey, XamlRoot))
            {
                await LoadAsync();
            }
        }
        catch (GameRoleCardException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login role card ({key})", CurrentGameKey);
        }
    }



    [RelayCommand]
    private async Task LogoutAsync()
    {
        try
        {
            if (_registry.GetProvider(CurrentGameKey) is not IGameRoleCardProvider provider)
            {
                return;
            }
            provider.Logout(CurrentGameKey);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Logout role card ({key})", CurrentGameKey);
        }
    }



    /// <summary>
    /// 页面加载时已经查过一次；卡片久没打开，数值可能已经过时，打开时补查一次。
    /// 结晶波片的倒计时由本机推算，这里只管其他会变的数值。
    /// </summary>
    private async void Flyout_Card_Opening(object sender, object e)
    {
        if (!IsLoading && DateTimeOffset.Now - _lastLoadTime > TimeSpan.FromMinutes(5))
        {
            await LoadAsync();
        }
    }



    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadAsync();
    }



    private async void ListView_Roles_ItemClick(object sender, ItemClickEventArgs e)
    {
        try
        {
            Button_SwitchRole.Flyout?.Hide();
            if (e.ClickedItem is not GameRoleCardRole role || _registry.GetProvider(CurrentGameKey) is not IGameRoleCardProvider provider)
            {
                return;
            }
            AppConfig.SetValue(role.SelectionKey, SelectionConfigKey);
            SelectedRole = role;
            Card = null;
            Stats = null;
            _loadCts?.Cancel();
            var cts = _loadCts = new CancellationTokenSource();
            IsLoading = true;
            try
            {
                await QueryCardAsync(provider, role, cts.Token);
            }
            finally
            {
                if (_loadCts == cts)
                {
                    IsLoading = false;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Switch role card ({key})", CurrentGameKey);
        }
    }



    [RelayCommand]
    private void CopyUid()
    {
        try
        {
            if (Card?.Role.RoleId is string uid)
            {
                ClipboardHelper.SetText(uid);
                InAppToast.MainWindow?.Success(Lang.Common_CopiedToClipboard, null, 1500);
            }
        }
        catch { }
    }



    private void UpdateStats()
    {
        if (Stats is null)
        {
            return;
        }
        DateTimeOffset now = DateTimeOffset.Now;
        foreach (GameRoleCardStatItem item in Stats)
        {
            item.Update(now);
        }
    }



    private void ResetAndHide()
    {
        this.Visibility = Visibility.Collapsed;
        NeedsLogin = false;
        CanLogout = false;
        Roles = null;
        SelectedRole = null;
        Card = null;
        Stats = null;
        ErrorMessage = null;
        OnPropertyChanged(nameof(CanSwitchRole));
    }



    /// <summary>
    /// 上次选的角色按游戏分开记
    /// </summary>
    private string SelectionConfigKey => $"GameRoleCardSelection_{CurrentGameKey}";


}
