using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Starward.Core.Games;
using Starward.Core.Games.Gryphline;
using Starward.Features.GameSetting;
using System;
using System.Linq;

namespace Starward.Providers.Gryphline;

/// <summary>
/// 终末地的画面设置。它是 Unity 游戏，分辨率与窗口模式存在引擎自己的
/// 注册表键里（<see cref="UnityScreenSettingKeys"/>），与厂商无关。
/// </summary>
internal class GryphlineGameSettingProvider : IGameSettingProvider
{

    /// <summary>
    /// Unity 的存放位置是 HKCU\Software\&lt;公司&gt;\&lt;产品&gt;
    /// </summary>
    private const string CompanyKeyPath = @"Software\Hypergryph";


    private readonly ILogger<GryphlineGameSettingProvider> _logger;


    public GryphlineGameSettingProvider(ILogger<GryphlineGameSettingProvider> logger)
    {
        _logger = logger;
    }


    public string ProviderId => GryphlineGameMapping.ProviderId;


    public GameResolutionSetting? GetResolution(GameKey key)
    {
        try
        {
            using RegistryKey? product = OpenProductKey(false);
            if (product is null)
            {
                return null;
            }
            if (product.GetValue(UnityScreenSettingKeys.ResolutionWidth) is not int width
             || product.GetValue(UnityScreenSettingKeys.ResolutionHeight) is not int height)
            {
                return null;
            }
            int mode = product.GetValue(UnityScreenSettingKeys.FullscreenMode) as int? ?? UnityScreenSettingKeys.FullScreenValue;
            return new GameResolutionSetting(width, height, mode != UnityScreenSettingKeys.WindowedValue);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Read screen settings ({key})", key);
            return null;
        }
    }


    public void SetResolution(GameKey key, GameResolutionSetting setting)
    {
        try
        {
            if (setting.Width <= 0 || setting.Height <= 0)
            {
                return;
            }
            using RegistryKey? product = OpenProductKey(true);
            if (product is null)
            {
                return;
            }
            // 只改已经存在的键，游戏没写过的东西不该由我们创建
            SetIfPresent(product, UnityScreenSettingKeys.ResolutionWidth, setting.Width);
            SetIfPresent(product, UnityScreenSettingKeys.ResolutionHeight, setting.Height);
            SetIfPresent(product, UnityScreenSettingKeys.FullscreenMode,
                         setting.FullScreen ? UnityScreenSettingKeys.FullScreenValue : UnityScreenSettingKeys.WindowedValue);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Write screen settings ({key})", key);
        }
    }


    private static void SetIfPresent(RegistryKey product, string name, int value)
    {
        if (product.GetValue(name) is not null)
        {
            product.SetValue(name, value, RegistryValueKind.DWord);
        }
    }


    /// <summary>
    /// 产品子键名带着版本代号（如 EndfieldTBeta2），正式版会变，
    /// 因此按「含有 Unity 屏幕设置键」来认，而不是写死名字。
    /// </summary>
    private static RegistryKey? OpenProductKey(bool writable)
    {
        using RegistryKey? company = Registry.CurrentUser.OpenSubKey(CompanyKeyPath, false);
        if (company is null)
        {
            return null;
        }
        foreach (string name in company.GetSubKeyNames())
        {
            RegistryKey? product = company.OpenSubKey(name, writable);
            if (product is null)
            {
                continue;
            }
            if (product.GetValueNames().Contains(UnityScreenSettingKeys.ResolutionWidth))
            {
                return product;
            }
            product.Dispose();
        }
        return null;
    }

}
