using Starward.Core.Launcher.Kuro;
using System;
using System.IO;
using System.Linq;
using Xunit;
using ZstdSharp;

namespace Starward.Core.Tests.Launcher;

/// <summary>
/// 鸣潮 .krpdiff（HDiffPatch 目录差分）的文件头。
/// <para/>
/// 两份样本都是官方 CDN 上 3.5.2 → 3.6.0 的真实差分包，整个文件原样收进来：
/// group_15 只改 pakchunk28 里几个字节（513 字节），group_0 是 libcef.dll（2082 字节）。
/// 库洛的清单比上游多两段，这两份样本正好能确认多出来的是什么、排在哪里。
/// </summary>
public class HDiffDirDiffHeaderTests
{

    private const string Group15Base64 = """
        SERJRkYxOSZ6c3RkJmZhZGxlcjY0AAEBBWEFYQHLyvt4AcvK+3gAAAAAAACBVlkIcIBs1efiI7dr9jXyd4KqjwEAAAAAAAAAZd63
        8eObU1YotS/9INaFAgD0AwBDbGllbnQvQ29udFBha3NwYWtjaHVuazI4LVdpbmRvd3NOb0VkaXRvci5wYWsABATLyvt4gY/VoM7/
        kdfsawUAe1B2MkwOp0JmUvK9XEhESUZGMTMmenN0ZADLyvt4y8r7eAEGAAUAAACDTYItAADLyvgrIMvK+3cotS/9YM0AHQkAFBGW
        vGqTopKZslFnz4hzHlHP6KuA9X/ccon+72TXqn4HYmxBs9cwwXRZl2BjKLfh7QAILzx/1UClENN/NaQ1OvcPO1gPAV+jwf7WYwWd
        LVJVQb/V5zM5TsafIymK5419VzFBTXlDvHaXwCh71uyCj/gli8jPCPDsLgBy9584+/fxZ/5pT8B8mJquUj7OmMiT6p8tRziP2kAV
        76cXHzlALpvJ+0u86aXiq4ZDVzHBIKTz8ZgWUH/z7rwxkZzzsULWFV3aAjDOnRVbsKtYbDecvSoHZXhl3YczQ/PmhElk1KEq4J6R
        tm7HW7LobNb/XseggKUAAeESb1oM+7tyCcDaqADsp0bpxL1owywBDPQnLyZMsAAFADk4yjzz0rQIC1DiHZAC
        """;


    private const string Group0Base64 = """
        SERJRkYxOSZ6c3RkJmZhZGxlcjY0AAEBCYJWCYJWAcrN3zABys3fOAAAAAAAAIVAgQIIEFaluYa9QaD572/nAWYO1AEAAAAAAAAA
        QZyxuvtwMIkotS/9YMABxQMAhAUAQ2xpZW50L0JpbmFyaWVzV2luNjRUaGlyZFBhcnR5S3JQY1Nka19HbG9iYWxLUlNES1Jlc1dl
        YlZpZXdsaWJjZWYuZGxsAAgIys3fMDiB1IeZwJ67v995CgA7UKayVvWlBGQsGHhXJzdVkVIUiXSCmqbnicsFSERJRkYxMyZ6c3Rk
        AMrN3zjKzd8wBiAABQAAAJAUjghCJYIlysy5Wx8fw1UFBYYVRlKGUp0vBQWGJUE4gTmoCyDKzd83KLUv/WAUB/U3AERoTVp4AAEA
        AAAEAEAAeA4fug4AtAnNIbgBTM0hVGhpcyBwcm9ncmFtIGNhbm5vdCBiZSBydW4gaW4gRE9TIG1vZGUuJAAAUEUAAGSGDQC7cPNg
        APAAIiALAg4AAOqtBwAapQEAMPynBwAQAIACAAAFAACwewl+oVMJAwBgQQD39eUIBhwAAP0R5ghcA2UJmBQCAACQGgm0KEoAAB5T
        Cbi4UQICADCCUaUGCSqGSIb3DQEHAqCCUZaSvDCCL7iiMIIDIgkGMYIDEw8CAQEwfTBpMQswCQYDVQQGEwJVUzEXMBUKEw5EaWdp
        Q2VydCwgSW5jLjFBMD8DEzggVHJ1c3RlZCBHNCBUaW1lU3RhbXBpbmcgUlNBNDA5NiBTSEEyNTYgMjAyNSBDQTECEAqA7xhLjfEF
        gtHEdqeVdGgwDQYJYIZIAWUDBAIBBQCgaTAYAzELATAcBTEPFw0yNjA3MjMxMTU3MzBaMC8EMSIEIIZmITHNYaj3boURr0pLzVEX
        VW2KZLpxh6ntFmWOBIAiMA0BBIICAIjLa+5qinmMxyq+nhzbsrhTEYA4Y4WOkHDGfcDOqv5R5EqwSf0dm5yyLVg4qrMvfp3kQHqZ
        YuoDsv8zTc5kllCMt6Q4DEyM1xbROsUf8jfojWbo3SZ3G4OjPbOMdYvfWcJn5cILhHevYjmnmzLhg/oOF+A7kamnk+jV+yE9vB99
        gUtuSivDQzsSW0m9PbCUSVr8Xq3aPhyopd3IGvt1UcsOeEdDUuAgBLPR84akU1iKThh9UhZGnfzIGjxcJnCYZwMHGaSunpTmxHOX
        0jWS2JkdkF6wvnY36su52Rpf0Ffq0UKy20Hj3XtWSCfyZEfRkdw/xr5OzBcMUYMLd0hmdw6Bz3sP1vSbrCE0vPWKAjdiIMsps/NP
        kPXABYHRAm4iiBHUFj+671ynoDOItFkKyEkU8zZAcvn4EWG5Wz+mlwGASWeyyJ5I3HTxWIbT7cjlt9FOTffyw5Ovg1Q02g06XMJw
        QuM4SkLBD4bI3yZIXR/dNDnlSf9hd24BO6K0mFcUxquDHk7d5nq/bSiqwJ4UeodziTYbx8BgAej0YVKeYdRaBNvm6kx9zuPEc+7O
        8EAWMT/Uf5l+6P4KHLAHgSxKdUU/Hiy0IUbPReXPzx3CZg4bi0HSUm969ZStcR4LwVYIVSDiVHXtkWgPkJrjU0+T/ozRXixS+SVw
        bH1eoRWgmEeyMIIpeAYKKwYBBAGCNwIEATGCKWhkKVVRoTCCGp13MIIXcwMDF2NfF1BMAgEDMQ8weAYLEAEEoGkEZzBlAgEBhv1s
        MQQgUgDpCdzfmGSFbsSRnlVca2TUA6xxiuEnH/1BtiQTESYCEQC8l4fXpkFVPD+sbfJwJSLmGA8yMDZaMCsCDDEcMBowGDAWBBTd
        YjCshgotMGvaOLFoeVIwB/tBfqwLmpQUaQYPLwxULxk9ly2eqQoe0qTbCa0QK0bWz6Z8MDcvMSgwJjAkMEqgP6Is11yExVyTj4KO
        Z2ucrsqzP+NtJpqjNPFGEQozis54hlE7RwhohFtvC4LQIjI5FKMrM3i+gfUFTaOARBzfKPegtj5EtLMZn+3MM2t9OA4uaugodgRQ
        rdyLXphVd3wPRZMEqJQwVgWIFfDIxRoVdQfQ9YPxBA4USi+JZ3ek7zTJmziOjjO3XjjShv91KvOaVLJwcOAuKo4e0U/CboOdzbnB
        RYEJeP7xm4BmOOXuSUTBw6Kbw4ld9KZNwqbWUVsiGCs5yER0U7LquXzIpTIFwGNQd2DYv39Dx4JwcKUkhu/0oadZnfqfEojeRUzU
        ouOBjO0k3gYmoZ23LGg4NKfqbl6cTyegkjCQQMrf/UIY2ULBCyOI23un5DWzYqvXuMThuLEDzFAKsZlVY6FJJjhOLRd34EXof5ON
        QkExbonEHkcMAIInDDlE3n+0o5E56ZPrvqIGaRhGowlm1JedT/PrVv086bV6UfRvIIRxixzNtHeUsV+cejhMZii5xODrzEkgdkvM
        6qfQGxwoA5fYDuHRoFN+QZMPc6RjqJAt1Fs9N8ghnvJRF3WKJgN+yfh/QoGZDhe7ASbIg6JuYj/JH16vwQJl8EcQMQRvuOsLqy76
        HK2iw2/lf/yRXvzucDzPmptoFyBwdwwihu6qXckPLXwFYTkQlWI4vwS0wycsH1jpdnPOowdpEjMda68r2kjDuEFy5DqYBfFqME9K
        QniA7HEAAAAAAAAAMSBgSFBoYhwATfK4BrhCcA9nM5/hcVh9M+YMuOkYPgGHOOitEDleunIA1MFuoBE0WjEtQNO6Q8BwAkg9gDi9
        PKHN2veodk+1+jaSy8QNPMACEFRMZkA03PwHdTcYSZD6Mh2ObKB9L270G4h7W00GW0hmCBA4WL5I
        """;


    private static byte[] Decode(string base64) => Convert.FromBase64String(string.Concat(base64.Where(c => !char.IsWhiteSpace(c))));


    private static byte[] Decompress(byte[] data, int size)
    {
        using var decompressor = new Decompressor();
        return decompressor.Unwrap(data, size).ToArray();
    }


    private static HDiffDirDiffHeader Parse(string base64)
    {
        using var ms = new MemoryStream(Decode(base64));
        return HDiffDirDiffHeader.Parse(ms, Decompress);
    }


    [Fact]
    public void Parse_ReadsTypesAndDirectoryFlags()
    {
        HDiffDirDiffHeader header = Parse(Group15Base64);

        Assert.Equal("zstd", header.CompressType);
        Assert.Equal("fadler64", header.ChecksumType);
        Assert.True(header.OldPathIsDir);
        Assert.True(header.NewPathIsDir);
    }


    /// <summary>
    /// 路径清单含目录本身，第一项是根目录的空字符串
    /// </summary>
    [Fact]
    public void Parse_ReadsPathLists()
    {
        HDiffDirDiffHeader header = Parse(Group15Base64);

        Assert.Equal(["", "Client/", "Client/Content/", "Client/Content/Paks/", "Client/Content/Paks/pakchunk28-WindowsNoEditor.pak"], header.OldPaths);
        Assert.Equal(header.OldPaths, header.NewPaths);
        Assert.Equal(["Client/", "Client/Content/", "Client/Content/Paks/"], header.GetNewDirectories());
        Assert.Empty(header.GetNewEmptyFiles());
        Assert.Empty(header.GetSameFileCopies());
    }


    /// <summary>
    /// 库洛多出来的一段是旧文件大小，插在新文件大小前面。
    /// libcef.dll 新旧大小不同（156463024 → 156463032），正好分得出哪段是哪段；
    /// 若按上游格式读，读到的「新文件大小」会是旧的那个，总和对不上。
    /// </summary>
    [Fact]
    public void Parse_UnderstandsTheKuroOldRefSizeList()
    {
        HDiffDirDiffHeader header = Parse(Group0Base64);

        const string libcef = "Client/Binaries/Win64/ThirdParty/KrPcSdk_Global/KRSDKRes/KRSDKWebView/libcef.dll";
        Assert.Equal([libcef], header.GetOldRefPaths());
        Assert.Equal([(libcef, 156463032L)], header.GetNewRefFiles());
        Assert.Equal([156463024L], header.OldRefSizes!);
        Assert.Equal(156463024L, header.OldRefSize);
        Assert.Equal(156463032L, header.NewRefSize);
    }


    /// <summary>
    /// 内嵌的单文件差分从 HDiffDataOffset 开始，是 hpatch 认得的 HDIFF13
    /// </summary>
    [Theory]
    [InlineData(Group15Base64)]
    [InlineData(Group0Base64)]
    public void Parse_LocatesTheEmbeddedSingleFileDiff(string base64)
    {
        byte[] data = Decode(base64);
        using var ms = new MemoryStream(data);
        HDiffDirDiffHeader header = HDiffDirDiffHeader.Parse(ms, Decompress);

        Assert.Equal(data.Length, header.HDiffDataOffset + header.HDiffDataSize);
        string magic = System.Text.Encoding.ASCII.GetString(data, (int)header.HDiffDataOffset, 12);
        Assert.Equal("HDIFF13&zstd", magic);
    }


    /// <summary>
    /// 自己生成的库洛格式差分包，截到内嵌差分的开头为止。
    /// 官方样本里「相同文件」都是空的，看不出哈希清单排在它前面还是后面；
    /// 这一份有一个相同文件（readme.txt），并且确认过官方启动器的 hpatchz 能打，
    /// 把哈希清单挪到相同文件之后它就不收了。另有空文件、新目录、新旧引用数不同（3 旧 4 新）。
    /// </summary>
    private const string GeneratedWithSameFileBase64 = """
        SERJRkYxOSZ6c3RkJgABAQmBFAyBOwOb2nAEnMZBAQAAAAAAgw8AAABDbGllbnQvAENsaWVudC9CaW5hcmllcy8AQ2xpZW50L0JpbmFy
        aWVzL3guZGxsAENsaWVudC9Db250ZW50LwBDbGllbnQvQ29udGVudC9QYWtzLwBDbGllbnQvQ29udGVudC9QYWtzL2EucGFrAENsaWVu
        dC9Db250ZW50L1Bha3MvYi5wYWsAcmVhZG1lLnR4dAAAQ2xpZW50LwBDbGllbnQvQmluYXJpZXMvAENsaWVudC9CaW5hcmllcy94LmRs
        bABDbGllbnQvQ29udGVudC8AQ2xpZW50L0NvbnRlbnQvUGFrcy8AQ2xpZW50L0NvbnRlbnQvUGFrcy9hLnBhawBDbGllbnQvQ29udGVu
        dC9QYWtzL2IucGFrAENsaWVudC9uZXcvAENsaWVudC9uZXcvYy5iaW4AZW1wdHkudHh0AHJlYWRtZS50eHQAAwIAAwIAAZ8gkqdgiZNw
        pwiSp2CJuni8YZzAjK/6ncXzHYG965CfsLOctVG+2M7yhLOhpFej3LC9k8mRrV4LCEhESUZGMTMmenN0ZAA=
        """;


    [Fact]
    public void Parse_PutsTheHashListBeforeTheSameFilePairs()
    {
        HDiffDirDiffHeader header = Parse(GeneratedWithSameFileBase64);

        Assert.Equal(["Client/Binaries/x.dll", "Client/Content/Paks/a.pak", "Client/Content/Paks/b.pak"], header.GetOldRefPaths());
        Assert.Equal(["Client/Binaries/x.dll", "Client/Content/Paks/a.pak", "Client/Content/Paks/b.pak", "Client/new/c.bin"],
                     header.GetNewRefFiles().Select(x => x.Path));
        Assert.Equal([5000L, 300000L, 155000L, 7777L], header.GetNewRefFiles().Select(x => x.Size));
        Assert.Equal([("readme.txt", "readme.txt")], header.GetSameFileCopies());
        Assert.Equal(["empty.txt"], header.GetNewEmptyFiles());
        Assert.Contains("Client/new/", header.GetNewDirectories());
    }


    [Fact]
    public void Parse_RejectsSomethingThatIsNotADirectoryDiff()
    {
        using var ms = new MemoryStream("HDIFF13&zstd "u8.ToArray());

        Assert.Throws<InvalidDataException>(() => HDiffDirDiffHeader.Parse(ms, Decompress));
    }

}
