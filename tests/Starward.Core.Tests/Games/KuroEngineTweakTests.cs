using Starward.Core.Games.Kuro;
using System.Globalization;
using System.Text;
using Xunit;

namespace Starward.Core.Tests.Games;

/// <summary>
/// 鸣潮 Engine.ini 调校：目录与预设的一致性，以及写入时不能碰坏文件里其他内容
/// </summary>
public class KuroEngineTweakTests : IDisposable
{

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "starward-kuro-ini-" + Guid.NewGuid().ToString("N"));

    public KuroEngineTweakTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);


    private static KuroEngineTweak Tweak(string key) => KuroEngineTweakCatalog.Find(key) ?? throw new KeyNotFoundException(key);


    [Fact]
    public void Catalog_KeysAreUnique()
    {
        var duplicated = KuroEngineTweakCatalog.Tweaks.GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1).Select(x => x.Key);
        Assert.Empty(duplicated);
    }


    [Fact]
    public void Catalog_EveryCategoryIsListed()
    {
        Assert.All(KuroEngineTweakCatalog.Tweaks, x => Assert.Contains(x.Category, KuroEngineTweakCatalog.Categories));
    }


    [Theory]
    [InlineData("")]
    [InlineData("zh-TW")]
    [InlineData("zh-HK")]
    [InlineData("zh-CN")]
    public void Text_EveryItemHasAString(string culture)
    {
        var info = CultureInfo.GetCultureInfo(culture);
        var rm = KuroEngineTweakText.ResourceManager;
        var names = new List<string>();
        foreach (var tweak in KuroEngineTweakCatalog.Tweaks)
        {
            names.Add($"Tweak_{tweak.ResourceName}_Title");
            names.Add($"Tweak_{tweak.ResourceName}_Desc");
            names.AddRange(tweak.Options.Where(x => x.LabelKey is not null).Select(x => $"Option_{x.LabelKey}"));
        }
        names.AddRange(KuroEngineTweakCatalog.Categories.Select(x => $"Category_{x}"));
        names.AddRange(KuroEngineTweakCatalog.Presets.Select(x => $"Preset_{x.Number}"));
        names.AddRange(typeof(KuroEngineTweakText).GetProperties().Where(x => x.Name.StartsWith("Ui_")).Select(x => x.Name));
        // 只看这份文化自己的 resx，不让缺漏被英文回退盖过去
        var set = rm.GetResourceSet(info, true, false);
        Assert.NotNull(set);
        var missing = names.Distinct().Where(x => string.IsNullOrWhiteSpace(set.GetString(x))).ToList();
        Assert.Empty(missing);
    }


    [Fact]
    public void Presets_AreFiveAndOnlyUseKnownKeysAndOptions()
    {
        Assert.Equal([1, 2, 3, 4, 5], KuroEngineTweakCatalog.Presets.Select(x => x.Number));
        foreach (var preset in KuroEngineTweakCatalog.Presets)
        {
            foreach (var (key, value) in preset.Values)
            {
                var tweak = Tweak(key);
                if (tweak.Kind is KuroEngineTweakKind.Choice)
                {
                    Assert.Contains(tweak.Options, x => KuroEngineTweakCatalog.ValueEquals(x.Value, value));
                }
                else
                {
                    double number = double.Parse(value, CultureInfo.InvariantCulture);
                    Assert.InRange(number, tweak.Minimum, tweak.Maximum);
                }
            }
        }
    }


    [Fact]
    public void Presets_SpotCheckAgainstUpstreamFiles()
    {
        var p1 = KuroEngineTweakCatalog.Presets[0].Values;
        var p5 = KuroEngineTweakCatalog.Presets[4].Values;
        Assert.Equal("1", p1["r.RayTracing.LoadConfig"]);
        Assert.Equal("3", p1["r.DFShadowQuality"]);
        Assert.Equal("50000", p1["r.Kuro.Foliage.Grass3_0CullDistanceMax"]);
        Assert.Equal("1", p5["foliage.CullAll"]);
        Assert.Equal("4", p5["r.MaxAnisotropy"]);
        Assert.False(p5.ContainsKey("r.AODownsampleFactor"));
        // 预设里注释掉的景深不算预设内容
        Assert.DoesNotContain("r.DepthOfFieldQuality", KuroEngineTweakCatalog.PresetKeys);
    }


    [Theory]
    [InlineData("1.0", "1", true)]
    [InlineData("0.50", "0.5", true)]
    [InlineData("True", "true", true)]
    [InlineData("2", "3", false)]
    [InlineData(null, null, true)]
    [InlineData("1", null, false)]
    public void ValueEquals_ComparesNumbersByValue(string? a, string? b, bool expected)
    {
        Assert.Equal(expected, KuroEngineTweakCatalog.ValueEquals(a, b));
    }


    private const string GameEngineIni =
        "[Core.System]\r\n" +
        "Paths=../../../Engine/Content\r\n" +
        "Paths=%GAMEDIR%Content\r\n" +
        "\r\n" +
        "[SystemSettings]\r\n" +
        "r.StaticMeshLODDistanceScale=0.7\r\n" +
        "r.Shadow.MinResolution=512\r\n" +
        "R.SSR.MAXROUGHNESS=0.8\r\n" +
        "r.Shadow.PerObjectResolutionMax=512\r\n" +
        "r.Shadow.PerObjectResolutionMax=256\r\n" +
        "\r\n" +
        "[/Script/Engine.RendererSettings]\r\n" +
        "r.MaxAnisotropy=16\r\n" +
        "r.RayTracing.LoadConfig=1\r\n" +
        "\r\n" +
        "[ConsoleVariables]\r\n" +
        "r.Reflections.Denoiser=2\r\n" +
        "\r\n" +
        "[WindowsApplication.Accessibility]\r\n" +
        "StickyKeysHotkey=False\r\n";


    private string EnginePath => Path.Combine(_dir, KuroEngineIniStore.EngineIniFileName);


    [Fact]
    public void Read_FindsKeysInAllCvarSectionsIgnoringCase()
    {
        File.WriteAllText(EnginePath, GameEngineIni);
        var values = KuroEngineIniStore.Read(_dir);
        Assert.Equal("0.7", values[Tweak("r.StaticMeshLODDistanceScale")]);
        Assert.Equal("0.8", values[Tweak("r.SSR.MaxRoughness")]);
        Assert.Equal("512", values[Tweak("r.Shadow.PerObjectResolutionMax")]);
        Assert.Equal("1", values[Tweak("r.RayTracing.LoadConfig")]);
        Assert.Equal("2", values[Tweak("r.Reflections.Denoiser")]);
        Assert.False(values.ContainsKey(Tweak("r.DepthOfFieldQuality")));
    }


    [Fact]
    public void Write_TouchesOnlyGivenKeysAndKeepsTheRest()
    {
        File.WriteAllText(EnginePath, GameEngineIni);
        var written = KuroEngineIniStore.Write(_dir, new Dictionary<KuroEngineTweak, string?>
        {
            [Tweak("r.StaticMeshLODDistanceScale")] = "0.5",          // 就地改
            [Tweak("r.Shadow.PerObjectResolutionMax")] = "1024",      // 去掉重复
            [Tweak("r.SSR.MaxRoughness")] = null,                     // 删除
            [Tweak("r.Reflections.Denoiser")] = "0",                  // 留在 [ConsoleVariables]
            [Tweak("r.DepthOfFieldQuality")] = "0",                   // 新增到 [SystemSettings] 末尾
            [Tweak("r.RayTracing.LoadConfig")] = "0",
        });
        Assert.Single(written);
        string expected =
            "[Core.System]\r\n" +
            "Paths=../../../Engine/Content\r\n" +
            "Paths=%GAMEDIR%Content\r\n" +
            "\r\n" +
            "[SystemSettings]\r\n" +
            "r.StaticMeshLODDistanceScale=0.5\r\n" +
            "r.Shadow.MinResolution=512\r\n" +
            "r.Shadow.PerObjectResolutionMax=1024\r\n" +
            "r.DepthOfFieldQuality=0\r\n" +
            "\r\n" +
            "[/Script/Engine.RendererSettings]\r\n" +
            "r.MaxAnisotropy=16\r\n" +
            "r.RayTracing.LoadConfig=0\r\n" +
            "\r\n" +
            "[ConsoleVariables]\r\n" +
            "r.Reflections.Denoiser=0\r\n" +
            "\r\n" +
            "[WindowsApplication.Accessibility]\r\n" +
            "StickyKeysHotkey=False\r\n";
        Assert.Equal(expected, File.ReadAllText(EnginePath));
        Assert.Equal(GameEngineIni, File.ReadAllText(EnginePath + KuroEngineIniStore.BackupSuffix));
    }


    [Fact]
    public void Write_NothingChanged_DoesNotRewriteOrBackup()
    {
        File.WriteAllText(EnginePath, GameEngineIni);
        var written = KuroEngineIniStore.Write(_dir, new Dictionary<KuroEngineTweak, string?>
        {
            [Tweak("r.MaxAnisotropy")] = "16",
            [Tweak("r.DepthOfFieldQuality")] = null,
        });
        Assert.Empty(written);
        Assert.False(File.Exists(EnginePath + KuroEngineIniStore.BackupSuffix));
    }


    [Fact]
    public void Write_CreatesMissingSectionAndFile()
    {
        var written = KuroEngineIniStore.Write(_dir, new Dictionary<KuroEngineTweak, string?>
        {
            [Tweak("r.SceneColorFringeQuality")] = "0",
            [Tweak("r.MaxAnisotropy")] = "8",
            [Tweak("bEnableMouseSmoothing")] = "False",
        });
        Assert.Equal(2, written.Count);
        Assert.Equal("[SystemSettings]\r\nr.SceneColorFringeQuality=0\r\n\r\n[/Script/Engine.RendererSettings]\r\nr.MaxAnisotropy=8\r\n", File.ReadAllText(EnginePath));
        Assert.Equal("[/Script/Engine.InputSettings]\r\nbEnableMouseSmoothing=False\r\n", File.ReadAllText(Path.Combine(_dir, KuroEngineIniStore.InputIniFileName)));
    }


    [Fact]
    public void Write_OnlyRemovals_DoesNotCreateFile()
    {
        var written = KuroEngineIniStore.Write(_dir, new Dictionary<KuroEngineTweak, string?> { [Tweak("r.SceneColorFringeQuality")] = null });
        Assert.Empty(written);
        Assert.False(File.Exists(EnginePath));
    }


    [Fact]
    public void Write_KeepsBomAndLfLineEndings()
    {
        string path = Path.Combine(_dir, KuroEngineIniStore.InputIniFileName);
        File.WriteAllText(path, "[/Script/Engine.InputSettings]\nbEnableMouseSmoothing=True\n", new UTF8Encoding(true));
        KuroEngineIniStore.Write(_dir, new Dictionary<KuroEngineTweak, string?> { [Tweak("bEnableMouseSmoothing")] = "False" });
        byte[] bytes = File.ReadAllBytes(path);
        Assert.True(bytes is [0xEF, 0xBB, 0xBF, ..]);
        Assert.Equal("[/Script/Engine.InputSettings]\nbEnableMouseSmoothing=False\n", Encoding.UTF8.GetString(bytes[3..]));
    }


    [Fact]
    public void Write_IgnoresCommentsAndArrayOperators()
    {
        File.WriteAllText(EnginePath, "[SystemSettings]\r\n;r.DepthOfFieldQuality=0\r\n+r.SSFS=1\r\n");
        Assert.Empty(KuroEngineIniStore.Read(_dir));
        KuroEngineIniStore.Write(_dir, new Dictionary<KuroEngineTweak, string?> { [Tweak("r.DepthOfFieldQuality")] = "1" });
        Assert.Equal("[SystemSettings]\r\n;r.DepthOfFieldQuality=0\r\n+r.SSFS=1\r\nr.DepthOfFieldQuality=1\r\n", File.ReadAllText(EnginePath));
    }

}
