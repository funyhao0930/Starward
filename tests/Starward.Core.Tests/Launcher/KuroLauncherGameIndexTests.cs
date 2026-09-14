using Starward.Core.Launcher.Kuro;
using System;
using System.Text.Json;
using Xunit;

namespace Starward.Core.Tests.Launcher;

/// <summary>
/// 鸣潮官方启动器的游戏配置，只取其中的版本号。
/// <para/>
/// 全部离线：JSON 是照真实响应的形状手写的，不碰真实接口。
/// </summary>
public class KuroLauncherGameIndexTests
{

    /// <summary>
    /// 真实响应还带 cdnList、resources、config.patchConfig 等下载相关的字段，
    /// 这里保留一部分，确认多出来的键不会让反序列化失败。
    /// </summary>
    private const string IndexJson = """
        {"default":{
            "cdnList":[{"K1":1,"K2":1,"P":1677,"url":"https://example.invalid/"}],
            "resourcesBasePath":"launcher/game/G153/50004/3.6.1/abc/zip",
            "resources":"launcher/game/G153/50004/3.6.1/abc/resource.json",
            "config":{"version":"3.6.1","patchType":"hotFix"},
            "version":"3.6.1"},
         "predownloadSwitch":1}
        """;


    [Fact]
    public void Deserialize_ReadsTheOnlineGameVersion()
    {
        KuroLauncherGameIndex index = JsonSerializer.Deserialize<KuroLauncherGameIndex>(IndexJson)!;

        Assert.Equal("3.6.1", index.Default?.Version);
    }


    /// <summary>
    /// 版本号要能与游戏目录下 launcherDownloadConfig.json 的写法直接比较，
    /// 否则启动页永远提示不出新版本。
    /// </summary>
    [Fact]
    public void Version_ComparesWithTheLocalVersionString()
    {
        KuroLauncherGameIndex index = JsonSerializer.Deserialize<KuroLauncherGameIndex>(IndexJson)!;

        Assert.True(Version.TryParse(index.Default?.Version, out Version? latest));
        Assert.True(Version.TryParse("3.6.0", out Version? local));
        Assert.True(latest > local);
    }


    /// <summary>
    /// 渠道键缺失时不能抛，调用方按「查不到版本」处理
    /// </summary>
    [Fact]
    public void Deserialize_ToleratesAMissingDefaultChannel()
    {
        KuroLauncherGameIndex index = JsonSerializer.Deserialize<KuroLauncherGameIndex>("""{"predownloadSwitch":1}""")!;

        Assert.Null(index.Default);
        Assert.False(Version.TryParse(index.Default?.Version, out _));
    }

}
