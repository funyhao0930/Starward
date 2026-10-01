using Microsoft.Extensions.Logging.Abstractions;
using Starward.Core.Localization;
using Starward.RPC.Env;
using Starward.RPC.GameInstall;
using Starward.RPC.Tests.Fakes;
using System.Globalization;
using Xunit;

namespace Starward.RPC.Tests.Env;

/// <summary>
/// RPC 是另一个进程，不读 Starward 的设置。界面连上时经 SetEnviroment 带来自己的语言，
/// 安装器丢回界面的错误信息（CoreLang）要换成那个语言，而不是跟着 Windows 走。
/// </summary>
public sealed class EnviromentControllerTests : IDisposable
{

    private readonly EnviromentController _controller = new(NullLogger<EnviromentController>.Instance,
                                                            new GameInstallHelper(NullLogger<GameInstallHelper>.Instance, new FakeCdn()));


    public void Dispose()
    {
        // 语言是整个进程共用的，别留给其他测试
        CoreLang.Culture = null;
        CultureInfo.DefaultThreadCurrentUICulture = null;
    }


    [Fact]
    public async Task SetEnviroment_SwitchesTheLanguageOfErrorMessages()
    {
        await _controller.SetEnviroment(new EnviromentMessage { Language = "zh-TW" }, null!);

        Assert.StartsWith("請先更新遊戲", CoreLang.KuroInstall_UpdateBeforeChangingResourceTiers, StringComparison.Ordinal);

        await _controller.SetEnviroment(new EnviromentMessage { Language = "en-US" }, null!);

        Assert.StartsWith("Update the game", CoreLang.KuroInstall_UpdateBeforeChangingResourceTiers, StringComparison.Ordinal);
    }


    /// <summary>
    /// 旧版 Starward 不带这一项（空字符串），认不得的语言也不能让整个 SetEnviroment 失败
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("not-a-language-!!")]
    public async Task SetEnviroment_ToleratesMissingOrUnknownLanguages(string language)
    {
        await _controller.SetEnviroment(new EnviromentMessage { Language = language }, null!);

        Assert.False(string.IsNullOrEmpty(CoreLang.KuroInstall_UpdateBeforeChangingResourceTiers));
    }

}
