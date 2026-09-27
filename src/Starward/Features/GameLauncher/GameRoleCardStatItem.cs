using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace Starward.Features.GameLauncher;

/// <summary>
/// 角色卡片上一项数值的显示状态。
/// <para/>
/// 有回满时间的数值（结晶波片）每分钟由卡片调用 <see cref="Update"/> 推算一次，
/// 不必为了倒计时反复查询接口。
/// </summary>
public partial class GameRoleCardStatItem : ObservableObject
{

    private readonly GameRoleCardStat _stat;


    public GameRoleCardStatItem(GameRoleCardStat stat)
    {
        _stat = stat;
        Update(DateTimeOffset.Now);
    }


    public string Name => _stat.Name;

    /// <summary>
    /// 给 Image.Source 用，不能是 null：没有图片的数值（改画字形）那张图虽然收起来了，
    /// 绑定照样会求值，而把 null 转成 ImageSource 会让 WinUI 抛「参数错误」，整个应用直接闪退。
    /// </summary>
    public string IconUri => string.IsNullOrWhiteSpace(_stat.IconUri) ? TransparentImage : _stat.IconUri;

    private const string TransparentImage = "ms-appx:///Assets/Image/Transparent.png";

    /// <summary>
    /// 给 FontIcon 用，不能是 null：没有字形的数值框虽然收起来了，绑定照样会求值
    /// </summary>
    public string Glyph => _stat.Glyph ?? "";

    /// <summary>
    /// 有图片时画图片，否则画字形
    /// </summary>
    public bool HasImage => !string.IsNullOrWhiteSpace(_stat.IconUri);

    public bool HasGlyph => !HasImage && !string.IsNullOrWhiteSpace(_stat.Glyph);

    public bool IsLocked => _stat.IsLocked;


    [ObservableProperty]
    public partial string CurrentText { get; set; } = "";

    [ObservableProperty]
    public partial string MaxText { get; set; } = "";

    [ObservableProperty]
    public partial string? DetailText { get; set; }

    /// <summary>
    /// 小字是倒计时，前面要画一个时钟
    /// </summary>
    [ObservableProperty]
    public partial bool IsCountdown { get; set; }



    public void Update(DateTimeOffset now)
    {
        if (_stat.IsLocked)
        {
            // 未解锁时数字没有意义，官方卡片也只写「待解锁」
            CurrentText = "--";
            MaxText = "";
            DetailText = _stat.Detail;
            IsCountdown = false;
            return;
        }

        int current = _stat.Current;
        MaxText = $"/{_stat.Max}";
        if (_stat.FullTime is DateTimeOffset fullTime)
        {
            TimeSpan remaining = fullTime - now;
            if (remaining <= TimeSpan.Zero)
            {
                current = Math.Max(current, _stat.Max);
                DetailText = Lang.GameRoleCard_Full;
                IsCountdown = false;
            }
            else
            {
                if (_stat.RecoveryInterval is TimeSpan interval && interval > TimeSpan.Zero)
                {
                    // 离回满还差几点 = 剩余时间 ÷ 每点时间，向上取整；查询之后恢复的部分由此推算
                    int missing = (int)Math.Ceiling(remaining / interval);
                    current = Math.Max(current, _stat.Max - missing);
                }
                // 分钟向上取整，免得还差几十秒时显示成 0 分钟
                int totalMinutes = (int)Math.Ceiling(remaining.TotalMinutes);
                DetailText = string.Format(Lang.GameRoleCard_HoursMinutes, totalMinutes / 60, totalMinutes % 60);
                IsCountdown = true;
            }
        }
        else
        {
            DetailText = _stat.Detail;
            IsCountdown = false;
        }
        CurrentText = current.ToString();
    }

}
