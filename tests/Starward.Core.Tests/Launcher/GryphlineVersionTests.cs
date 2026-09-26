using Starward.Core.Games;
using Starward.Core.Launcher.Gryphline;
using System;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Starward.Core.Tests.Launcher;

/// <summary>
/// 终末地的更新判断。
/// <para/>
/// 全部离线：JSON 是照真实响应的形状手写的，不碰真实接口。
/// </summary>
public class GryphlineVersionTests
{

    /// <summary>
    /// 与官方接口返回的形状一致。真实的 packs 有十几个分卷，这里留一个，链接换成了假值。
    /// </summary>
    private const string ResponseJson = """
        {"proxy_rsps":[{"kind":"get_latest_game","get_latest_game_rsp":{
            "action":1,"version":"1.5.3","request_version":"",
            "pkg":{"packs":[{"url":"https://example.invalid/packs/Beyond.zip.001","md5":"111ded6ea6f9bf9f415afe852dd2f1a8","package_size":"1073741824"}],
                   "total_size":"54000000000","file_path":"https://example.invalid/files",
                   "game_files_md5":"7293de35f90ebce542a68d076e455309"},
            "patch":null,"state":0,"launcher_action":0}}]}
        """;


    private static GryphlineLatestGame Latest()
    {
        var response = JsonSerializer.Deserialize<GryphlineBatchProxyResponse>(ResponseJson)!;
        return response.ProxyResponses!.First(x => x.Kind == GryphlineLauncherClient.KIND_LATEST_GAME).LatestGameResponse!;
    }


    [Fact]
    public void Deserialize_ReadsTheVersionAndTheManifestChecksum()
    {
        GryphlineLatestGame latest = Latest();

        Assert.Equal("1.5.3", latest.Version);
        Assert.Equal("7293de35f90ebce542a68d076e455309", latest.Package?.GameFilesMd5);
    }


    /// <summary>
    /// 本机清单与这一版一致：已是最新，不该提示
    /// </summary>
    [Fact]
    public void ToUpdateInfo_IsUpToDateWhenTheManifestsMatch()
    {
        GameUpdateInfo? info = GryphlineVersionMapper.ToUpdateInfo(Latest(), "7293de35f90ebce542a68d076e455309");

        Assert.NotNull(info);
        Assert.Equal(new Version(1, 5, 3), info!.LatestVersion);
        Assert.False(info.UpdateAvailable);
    }


    /// <summary>
    /// 校验值大小写不同也是同一份清单
    /// </summary>
    [Fact]
    public void ToUpdateInfo_ComparesChecksumsCaseInsensitively()
    {
        GameUpdateInfo? info = GryphlineVersionMapper.ToUpdateInfo(Latest(), "7293DE35F90EBCE542A68D076E455309");

        Assert.False(info!.UpdateAvailable);
    }


    [Fact]
    public void ToUpdateInfo_ReportsAnUpdateWhenTheManifestsDiffer()
    {
        GameUpdateInfo? info = GryphlineVersionMapper.ToUpdateInfo(Latest(), "00000000000000000000000000000000");

        Assert.True(info!.UpdateAvailable);
        Assert.Equal(new Version(1, 5, 3), info.LatestVersion);
    }


    /// <summary>
    /// 任何一边缺了都不下结论：报「有更新」会让已是最新的玩家白跑一趟官方启动器
    /// </summary>
    [Fact]
    public void ToUpdateInfo_RefusesToGuessWithoutBothChecksums()
    {
        Assert.Null(GryphlineVersionMapper.ToUpdateInfo(Latest(), null));
        Assert.Null(GryphlineVersionMapper.ToUpdateInfo(Latest(), ""));
        Assert.Null(GryphlineVersionMapper.ToUpdateInfo(new GryphlineLatestGame { Version = "1.5.3" }, "7293de35f90ebce542a68d076e455309"));
        Assert.Null(GryphlineVersionMapper.ToUpdateInfo(null, "7293de35f90ebce542a68d076e455309"));
    }


    /// <summary>
    /// 渠道填错时接口不报错，只是 proxy_rsps 整个缺席，要当成「问不到」
    /// </summary>
    [Fact]
    public void Deserialize_ToleratesAMissingResponseList()
    {
        var response = JsonSerializer.Deserialize<GryphlineBatchProxyResponse>("""{"code":1,"msg":"invalid"}""")!;

        Assert.Null(response.ProxyResponses);
    }

}
