using Microsoft.Extensions.Logging;
using Starward.Core.Games;
using Starward.Features.GameSetting;
using System;

namespace Starward.Providers;

/// <summary>
/// 虚幻引擎游戏的画面设置，存在 <c>GameUserSettings.ini</c> 里。
/// <para/>
/// 键是引擎标准的，各游戏共用一套读写逻辑，只有文件位置不同：
/// 有的在游戏目录里，有的在用户目录下，因此由构造时传入的委托决定。
/// </summary>
internal class UnrealGameSettingProvider : IGameSettingProvider
{

    private readonly Func<GameKey, string?> _settingsPathResolver;

    private readonly ILogger<UnrealGameSettingProvider> _logger;


    public UnrealGameSettingProvider(string providerId, Func<GameKey, string?> settingsPathResolver, ILogger<UnrealGameSettingProvider> logger)
    {
        ProviderId = providerId;
        _settingsPathResolver = settingsPathResolver;
        _logger = logger;
    }


    public string ProviderId { get; }


    public GameResolutionSetting? GetResolution(GameKey key)
    {
        try
        {
            return UnrealGameUserSettings.Read(_settingsPathResolver(key));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Read game user settings ({key})", key);
            return null;
        }
    }


    public void SetResolution(GameKey key, GameResolutionSetting setting)
    {
        try
        {
            string? path = _settingsPathResolver(key);
            if (!UnrealGameUserSettings.Write(path, setting))
            {
                // 游戏没有生成过设置文件，或者文件里没有这些键
                _logger.LogInformation("Game user settings not written ({key}).", key);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Write game user settings ({key})", key);
        }
    }

}
