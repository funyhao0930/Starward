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
/// 启动页左下角、横幅资讯下方的角色卡片。
/// <para/>
/// 本控件不认识具体是哪款游戏，数据都经 <see cref="RoleCardProviderRegistry"/> 取得；
/// 没有 Provider、或者本机查不到任何角色时整张藏起来。
/// </summary>
[INotifyPropertyChanged]
public sealed partial class GameRoleCard : UserControl
{

    private readonly ILogger<GameRoleCard> _logger = AppConfig.GetLogger<GameRoleCard>();

    private readonly RoleCardProviderRegistry _registry = AppConfig.GetService<RoleCardProviderRegistry>();

    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _countdownTimer;

    private CancellationTokenSource? _loadCts;


    public GameRoleCard()
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
            IsLoading = true;
            IReadOnlyList<GameRoleCardRole> roles;
            try
            {
                roles = await provider.GetRolesAsync(CurrentGameKey, token);
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
        }
        catch (GameRoleCardException ex)
        {
            token.ThrowIfCancellationRequested();
            // 保留上一次的等级与 UID，只把数值区换成原因
            ErrorMessage = ex.Message;
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
