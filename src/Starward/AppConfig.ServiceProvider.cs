using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Starward.Core.Gacha.Genshin;
using Starward.Core.Gacha.Gryphline;
using Starward.Core.Gacha.Hotta;
using Starward.Core.Gacha.Kuro;
using Starward.Core.Gacha.StarRail;
using Starward.Core.Gacha.ZZZ;
using Starward.Core.GameNotice;
using Starward.Core.GameRecord;
using Starward.Core.Games;
using Starward.Core.Games.Gryphline;
using Starward.Core.Games.HoYo;
using Starward.Core.Games.Hotta;
using Starward.Core.Games.Kuro;
using Starward.Core.HoYoPlay;
using Starward.Core.Launcher.Gryphline;
using Starward.Core.Launcher.Kuro;
using Starward.Core.SelfQuery;
using Starward.Features.Background;
using Starward.Features.Database;
using Starward.Features.Gacha;
using Starward.Features.Gacha.UIGF;
using Starward.Features.GameAccount;
using Starward.Features.GameInstall;
using Starward.Features.GameLauncher;
using Starward.Features.GameRecord;
using Starward.Features.GameSetting;
using Starward.Features.HoYoPlay;
using Starward.Features.PlayTime;
using Starward.Features.RPC;
using Starward.Features.Screenshot;
using Starward.Features.SelfQuery;
using Starward.Features.Update;
using Starward.Providers;
using Starward.Providers.Gryphline;
using Starward.Providers.HoYo;
using Starward.Providers.Hotta;
using Starward.Providers.Kuro;
using Starward.Setup.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;

namespace Starward;

public static partial class AppConfig
{

    private static IServiceProvider _serviceProvider;


    private static void BuildServiceProvider()
    {
        if (_serviceProvider == null)
        {
            var logFolder = Path.Combine(CacheFolder, "log");
            Directory.CreateDirectory(logFolder);
            LogFile = Path.Combine(logFolder, $"Starward_{DateTime.Now:yyMMdd}.log");
            Log.Logger = new LoggerConfiguration().WriteTo.File(path: LogFile, shared: true, outputTemplate: $$"""[{Timestamp:HH:mm:ss.fff}] [{Level:u4}] [{{Path.GetFileName(Environment.ProcessPath)}} ({{Environment.ProcessId}})] {SourceContext}{NewLine}{Message}{NewLine}{Exception}{NewLine}""")
                                                  .Enrich.FromLogContext()
                                                  .CreateLogger();
            Log.Information($"Welcome to Starward v{AppVersion}\r\nSystem: {Environment.OSVersion}\r\nCommand Line: {Environment.CommandLine}");

            var sc = new ServiceCollection();
            sc.AddMemoryCache();
            sc.AddLogging(c => c.AddSerilog(Log.Logger));
            sc.AddHttpClient().ConfigureHttpClientDefaults(ConfigDefaultHttpClient);

            sc.AddSingleton<HoYoPlayClient>();
            sc.AddSingleton<GameNoticeClient>();
            sc.AddSingleton<HoYoPlayService>();

            // 多游戏 Provider 架构
            sc.AddSingleton<IGameProviderRegistry, GameProviderRegistry>();
            sc.AddSingleton<IGameLaunchSettings, AppConfigGameLaunchSettings>();
            sc.AddSingleton<IHoYoGameInfoSource, HoYoGameInfoSource>();
            sc.AddSingleton<IHoYoLaunchHooks, HoYoLaunchHooks>();
            sc.AddSingleton<IGameCatalogProvider, HoYoCatalogProvider>();
            sc.AddSingleton<IGameDiscoveryProvider, HoYoDiscoveryProvider>();
            sc.AddSingleton<IGameLaunchProvider, HoYoLaunchProvider>();

            // 仅支持启动的游戏，目录是固定的，启动流程也没有特殊之处，
            // 因此复用通用的 Simple 实现，不必各自写一套。
            AddSimpleGameProvider(sc, KuroGameMapping.ProviderId, KuroGameMapping.GetDescriptors);
            sc.AddSingleton<IGameDiscoveryProvider, KuroDiscoveryProvider>();

            AddSimpleGameProvider(sc, HottaGameMapping.ProviderId, HottaGameMapping.GetDescriptors);
            sc.AddSingleton<IGameDiscoveryProvider, HottaDiscoveryProvider>();

            AddSimpleGameProvider(sc, GryphlineGameMapping.ProviderId, GryphlineGameMapping.GetDescriptors);
            sc.AddSingleton<IGameDiscoveryProvider, GryphlineDiscoveryProvider>();

            // 在线背景图。各家官方启动器的接口形状差别很大，
            // 由各自的 Provider 换成统一的 GameBackground。
            sc.AddSingleton<BackgroundProviderRegistry>();
            sc.AddSingleton<IGameBackgroundProvider, HoYoBackgroundProvider>();
            sc.AddSingleton<KuroLauncherClient>();
            sc.AddSingleton<IGameBackgroundProvider, KuroBackgroundProvider>();
            sc.AddSingleton<GryphlineLauncherClient>();
            sc.AddSingleton<IGameBackgroundProvider, GryphlineBackgroundProvider>();

            sc.AddSingleton<BackgroundService>();
            sc.AddSingleton<GameLauncherService>();
            sc.AddSingleton<GamePackageService>();
            sc.AddSingleton<PlayTimeRecordService>();
            sc.AddSingleton<PlayTimeStatsService>();
            sc.AddSingleton<GameNoticeService>();
            sc.AddSingleton<SetupService>();

            sc.AddSingleton<GenshinGachaClient>();
            sc.AddSingleton<StarRailGachaClient>();
            sc.AddSingleton<ZZZGachaClient>();
            sc.AddSingleton<GenshinGachaService>();
            sc.AddSingleton<StarRailGachaService>();
            sc.AddSingleton<ZZZGachaService>();
            sc.AddSingleton<UIGFGachaService>();

            // 抽卡记录：按供应商提供服务，页面不认识具体游戏
            sc.AddSingleton<GachaProviderRegistry>();
            sc.AddSingleton<IGameGachaProvider, HoYoGachaProvider>();
            sc.AddSingleton<KuroGachaClient>();
            sc.AddSingleton<WuwaGachaService>();
            sc.AddSingleton<IGameGachaProvider, KuroGachaProvider>();
            sc.AddSingleton<GryphlineGachaClient>();
            sc.AddSingleton<EndfieldGachaService>();
            sc.AddSingleton<IGameGachaProvider, GryphlineGachaProvider>();
            sc.AddSingleton<HottaGachaClient>();
            sc.AddSingleton<NteGachaService>();
            sc.AddSingleton<IGameGachaProvider, HottaGachaProvider>();
            sc.AddSingleton<GenshinBeyondGachaClient>();
            sc.AddSingleton<GenshinBeyondGachaService>();

            // 画面设置：分辨率与窗口模式，各引擎存的位置不同
            sc.AddSingleton<GameSettingProviderRegistry>();
            sc.AddSingleton<IGameSettingProvider, HoYoGameSettingProvider>();
            sc.AddSingleton<IGameSettingProvider>(sp => new UnrealGameSettingProvider(
                KuroGameMapping.ProviderId,
                key => Path.Join(GameLauncherService.GetGameInstallPath(key), KuroGameMapping.GameUserSettingsRelativePath),
                sp.GetRequiredService<ILogger<UnrealGameSettingProvider>>()));
            sc.AddSingleton<IGameSettingProvider>(sp => new UnrealGameSettingProvider(
                HottaGameMapping.ProviderId,
                HottaGameMapping.GetGameUserSettingsPath,
                sp.GetRequiredService<ILogger<UnrealGameSettingProvider>>()));
            sc.AddSingleton<IGameSettingProvider, GryphlineGameSettingProvider>();

            sc.AddSingleton<HoyolabClient>();
            sc.AddSingleton<HyperionClient>();
            sc.AddSingleton<GameRecordService>();

            sc.AddSingleton<SelfQueryClient>();
            sc.AddSingleton<SelfQueryService>();

            sc.AddHttpClient<ReleaseClient>().ConfigStarwardHttpClient();
            sc.AddTransient<UpdateService>();

            sc.AddSingleton<RpcService>();
            sc.AddSingleton<GameInstallService>();

            sc.AddSingleton<GameAuthLoginService>();
            sc.AddSingleton<GameAccountService>();

            sc.AddSingleton<ScreenCaptureService>();

            sc.AddHttpClient<LogUploadClient>().ConfigStarwardHttpClient();


            _serviceProvider = sc.BuildServiceProvider();
        }
    }

    /// <summary>
    /// 注册一个游戏与渠道固定、启动流程通用的供应商。
    /// 目录与启动使用 <see cref="SimpleGameCatalogProvider"/> 与 <see cref="SimpleGameLaunchProvider"/>，
    /// 搜索由各供应商自行实现，因为每家写入安装路径的位置都不同。
    /// </summary>
    private static void AddSimpleGameProvider(IServiceCollection sc, string providerId, Func<IReadOnlyList<GameDescriptor>> descriptorFactory)
    {
        // 图标从已安装游戏的可执行文件中提取，不把美术资源复制进代码仓库
        sc.AddSingleton<IGameCatalogProvider>(sp => new LocalIconGameCatalogProvider(
            new SimpleGameCatalogProvider(providerId, descriptorFactory),
            sp.GetRequiredService<ILogger<LocalIconGameCatalogProvider>>()));
        sc.AddSingleton<IGameLaunchProvider>(sp => new SimpleGameLaunchProvider(
            providerId,
            new SimpleGameCatalogProvider(providerId, descriptorFactory),
            sp.GetRequiredService<IGameLaunchSettings>()));
    }


    public static T GetService<T>()
    {
        BuildServiceProvider();
        return _serviceProvider.GetService<T>()!;
    }

    public static ILogger<T> GetLogger<T>()
    {
        BuildServiceProvider();
        return _serviceProvider.GetService<ILogger<T>>()!;
    }

    public static SqliteConnection CreateDatabaseConnection()
    {
        return DatabaseService.CreateConnection();
    }


    private static void ConfigDefaultHttpClient(this IHttpClientBuilder builder)
    {
        builder.RemoveAllLoggers();
        builder.ConfigureHttpClient(client =>
        {
            client.DefaultRequestHeaders.Clear();
#if DEBUG
            client.DefaultRequestHeaders.Add("User-Agent", $"Starward.Debug/{AppVersion}");
#else
            client.DefaultRequestHeaders.Add("User-Agent", $"Starward/{AppVersion}");
#endif
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
        });
        builder.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            EnableMultipleHttp2Connections = true,
            EnableMultipleHttp3Connections = true,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        });
    }


    private static void ConfigStarwardHttpClient(this IHttpClientBuilder builder)
    {
        builder.RemoveAllLoggers();
        builder.ConfigureHttpClient(client =>
        {
            client.DefaultRequestHeaders.Clear();
#if DEBUG
            client.DefaultRequestHeaders.Add("User-Agent", $"Starward.Debug/{AppVersion}");
#else
            client.DefaultRequestHeaders.Add("User-Agent", $"Starward/{AppVersion}");
#endif
            client.DefaultRequestHeaders.Add("X-Sw-Device-Id", DeviceId.ToString());
            client.DefaultRequestHeaders.Add("X-Sw-Session-Id", SessionId.ToString());
            client.DefaultRequestHeaders.Add("X-Sw-App-Version", AppVersion);
            client.DefaultRequestHeaders.Add("X-Sw-App-Type", "Desktop");
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
        });
        builder.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            EnableMultipleHttp2Connections = true,
            EnableMultipleHttp3Connections = true,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        });
    }


}