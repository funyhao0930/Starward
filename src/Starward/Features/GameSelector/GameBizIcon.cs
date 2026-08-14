using CommunityToolkit.Mvvm.ComponentModel;
using Starward.Core;
using Starward.Core.Games;
using Starward.Core.HoYoPlay;
using System;


namespace Starward.Features.GameSelector;

/// <summary>
/// 游戏选择器中的一个游戏渠道图标。
/// 数据来自 <see cref="GameDescriptor"/>，不再直接依赖任何游戏公司的接口。
/// </summary>
public partial class GameBizIcon : ObservableObject, IEquatable<GameBizIcon>
{


    private const double GB = 1 << 30;


    /// <summary>
    /// 通用游戏标识
    /// </summary>
    public GameKey Key { get; set; }


    /// <summary>
    /// 兼容层：现有页面与导航仍以 <see cref="HoYoPlay.GameId"/> 为货币
    /// </summary>
    public GameId GameId { get; set; }


    /// <summary>
    /// 兼容层：应用配置仍以 GameBiz 字符串为键
    /// </summary>
    public GameBiz GameBiz { get; set; }


    public string GameIcon { get; set => SetProperty(ref field, value); }

    public string GameName { get; set => SetProperty(ref field, value); }

    public string ServerIcon { get; set => SetProperty(ref field, value); }

    public string ServerName { get; set => SetProperty(ref field, value); }

    public double MaskOpacity { get; set => SetProperty(ref field, value); } = 1.0;

    public bool IsPinned { get; set => SetProperty(ref field, value); }

    public string? InstallPath { get; set => SetProperty(ref field, value); }

    public long TotalSize { get; set { field = value; OnPropertyChanged(nameof(TotalSizeText)); } }

    public string? TotalSizeText => TotalSize == 0 ? null : $"{TotalSize / GB:F2}GB";


    public bool IsSelected
    {
        get;
        set
        {
            field = value;
            MaskOpacity = value ? 0 : 1;
        }
    }



    public GameBizIcon(GameDescriptor descriptor)
    {
        Key = descriptor.Key;
        // 米哈游游戏是旧的 GameBiz 字符串，其他供应商是 GameKey 的正规字符串
        GameBiz = descriptor.SettingsKey;
        GameId = new GameId { Id = descriptor.ProviderGameId ?? descriptor.SettingsKey, GameBiz = GameBiz };
        GameIcon = descriptor.IconUri ?? "";
        ServerIcon = descriptor.ChannelIconUri ?? "";
        GameName = descriptor.DisplayName;
        ServerName = descriptor.ChannelName ?? "";
    }



    public void UpdateInfo(GameDescriptor descriptor)
    {
        Key = descriptor.Key;
        GameBiz = descriptor.SettingsKey;
        GameId = new GameId { Id = descriptor.ProviderGameId ?? descriptor.SettingsKey, GameBiz = GameBiz };
        GameIcon = descriptor.IconUri ?? "";
        ServerIcon = descriptor.ChannelIconUri ?? "";
        GameName = descriptor.DisplayName;
        ServerName = descriptor.ChannelName ?? "";
    }



    public bool Equals(GameBizIcon? other)
    {
        return ReferenceEquals(this, other) || Key == other?.Key;
    }


    public override bool Equals(object? obj) => Equals(obj as GameBizIcon);


    public override int GetHashCode() => Key.GetHashCode();

}
