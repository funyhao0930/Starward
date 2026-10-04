using System.Globalization;

namespace Starward.Core.Games.Kuro;

/// <summary>
/// 鸣潮的 ini 调校项写在哪个文件里
/// </summary>
public enum KuroEngineIniFile
{
    Engine,
    Input,
}


/// <summary>
/// 调校项的取值方式
/// </summary>
public enum KuroEngineTweakKind
{
    /// <summary>
    /// 从几个固定值里选
    /// </summary>
    Choice,

    /// <summary>
    /// 填一个数
    /// </summary>
    Number,
}


/// <summary>
/// 可选值。<see cref="LabelKey"/> 为 null 时界面直接显示值本身（如阴影分辨率 1024）。
/// </summary>
public sealed record KuroEngineTweakOption(string Value, string? LabelKey = null);


/// <summary>
/// 一个可调的 ini 键。
/// <para/>
/// 每一项都允许「不设定」：文件里没有这个键，游戏就用自己的默认值，
/// 这样玩家随时能把单独一项退回官方行为，而不必整份文件一起还原。
/// </summary>
public sealed class KuroEngineTweak
{

    public required string Key { get; init; }

    public required string Category { get; init; }

    public KuroEngineIniFile File { get; init; } = KuroEngineIniFile.Engine;

    public string Section { get; init; } = KuroEngineTweakCatalog.SystemSettings;

    public KuroEngineTweakKind Kind { get; init; }

    public IReadOnlyList<KuroEngineTweakOption> Options { get; init; } = [];

    public double Minimum { get; init; }

    public double Maximum { get; init; }

    public double Step { get; init; } = 1;

    /// <summary>
    /// 在 resx 里查标题与说明用的名字：<c>Tweak_{ResourceName}_Title</c>、<c>Tweak_{ResourceName}_Desc</c>
    /// </summary>
    public string ResourceName => Key.Replace('.', '_');

    public override string ToString() => $"[{Section}] {Key}";

}


/// <summary>
/// AlteriaX/WuWa-Configs 的一份 Engine.ini（按显卡分 Config 1～5）
/// </summary>
public sealed record KuroEngineTweakPreset(int Number, IReadOnlyDictionary<string, string> Values);


/// <summary>
/// 鸣潮 Engine.ini / Input.ini 的调校项目录，取材自
/// <see href="https://github.com/AlteriaX/WuWa-Configs">AlteriaX/WuWa-Configs</see>
/// 与作者的说明页 <see href="https://alteriax.github.io/WuWa-Config-Info/"/>。
/// <para/>
/// 五份预设照抄仓库 <see cref="SourceCommit"/> 时的 Engine.ini，只取 <c>[SystemSettings]</c>
/// 与 <c>[/Script/Engine.RendererSettings]</c> 两节；文件开头的 <c>[Core.System]</c> Paths
/// 是游戏自己生成的，不归这里管。预设里注释掉的项（景深、胶片颗粒等）不算预设的一部分，
/// 但仍列在目录里供手动调整。
/// </summary>
public static class KuroEngineTweakCatalog
{

    public const string SystemSettings = "SystemSettings";

    /// <summary>
    /// 与 <see cref="SystemSettings"/> 一样会设置控制台变量，有人习惯写在这一节
    /// </summary>
    public const string ConsoleVariables = "ConsoleVariables";

    public const string RendererSettings = "/Script/Engine.RendererSettings";

    public const string InputSettings = "/Script/Engine.InputSettings";


    public const string SourceUrl = "https://github.com/AlteriaX/WuWa-Configs";

    public const string InfoUrl = "https://alteriax.github.io/WuWa-Config-Info/";

    public const string SourceCommit = "f0bf1059e27778a56da9bea7e7ca48af828d524d";


    public const string CategoryPostProcess = "PostProcess";
    public const string CategoryShadow = "Shadow";
    public const string CategoryAmbientOcclusion = "AmbientOcclusion";
    public const string CategoryViewDistance = "ViewDistance";
    public const string CategoryStreaming = "Streaming";
    public const string CategoryLighting = "Lighting";
    public const string CategoryReflection = "Reflection";
    public const string CategoryFog = "Fog";
    public const string CategoryEffects = "Effects";
    public const string CategoryRayTracing = "RayTracing";
    public const string CategoryInput = "Input";

    public static IReadOnlyList<string> Categories { get; } =
    [
        CategoryPostProcess,
        CategoryShadow,
        CategoryAmbientOcclusion,
        CategoryViewDistance,
        CategoryStreaming,
        CategoryLighting,
        CategoryReflection,
        CategoryFog,
        CategoryEffects,
        CategoryRayTracing,
        CategoryInput,
    ];


    private static readonly KuroEngineTweakOption[] OffOn = [new("0", "Off"), new("1", "On")];

    private static readonly KuroEngineTweakOption[] FalseTrue = [new("false", "Off"), new("true", "On")];

    private static readonly KuroEngineTweakOption[] DownSample124 = [new("1", "FullRes"), new("2", "HalfRes"), new("4", "QuarterRes")];

    private static readonly KuroEngineTweakOption[] ShadowResolutions = [new("256"), new("512"), new("768"), new("1024"), new("2048")];


    private static KuroEngineTweak Choice(string category, string key, KuroEngineTweakOption[] options, string section = SystemSettings, KuroEngineIniFile file = KuroEngineIniFile.Engine)
    {
        return new KuroEngineTweak { Category = category, Key = key, Kind = KuroEngineTweakKind.Choice, Options = options, Section = section, File = file };
    }


    private static KuroEngineTweak Number(string category, string key, double min, double max, double step)
    {
        return new KuroEngineTweak { Category = category, Key = key, Kind = KuroEngineTweakKind.Number, Minimum = min, Maximum = max, Step = step };
    }


    public static IReadOnlyList<KuroEngineTweak> Tweaks { get; } =
    [
        // 后处理
        Choice(CategoryPostProcess, "r.DepthOfFieldQuality", [new("0", "Off"), new("1", "Low"), new("2", "High"), new("3", "VeryHigh"), new("4", "Extreme")]),
        Choice(CategoryPostProcess, "r.Tonemapper.Quality", [new("0", "Off"), new("1", "Tonemapper_FilmContrast"), new("2", "Tonemapper_Vignette"), new("4", "Tonemapper_VignetteGrain")]),
        Choice(CategoryPostProcess, "r.SceneColorFringeQuality", OffOn),
        Choice(CategoryPostProcess, "r.Kuro.KuroEnableFFTBloom", OffOn),
        Choice(CategoryPostProcess, "r.Kuro.KuroEnableToonFFTBloom", OffOn),
        Choice(CategoryPostProcess, "r.Kuro.KuroBloomStreak", OffOn),
        Choice(CategoryPostProcess, "r.EnableLensflareSceneSample", OffOn),
        Choice(CategoryPostProcess, "r.Kuro.NiagaraBlur.Enable", OffOn),
        Choice(CategoryPostProcess, "r.KuroTonemapping", [new("0", "Off"), new("1", "Tonemapping_Genshin"), new("2", "Tonemapping_DeathStranding"), new("3", "Tonemapping_Kuro")]),

        // 阴影
        Number(CategoryShadow, "r.Shadow.RadiusThreshold", 0, 0.1, 0.01),
        Choice(CategoryShadow, "r.Shadow.PerObjectShadowMapResolution", ShadowResolutions),
        Choice(CategoryShadow, "r.Shadow.PerObjectResolutionMax", ShadowResolutions),
        Choice(CategoryShadow, "r.Shadow.PerObjectResolutionMin", ShadowResolutions),
        Choice(CategoryShadow, "r.DistanceFieldShadowing", OffOn),
        Choice(CategoryShadow, "r.DFShadowQuality", [new("1", "Low"), new("2", "Medium"), new("3", "High")]),
        Choice(CategoryShadow, "r.Shadow.CacheDirectLightShadow", OffOn),

        // 环境光遮蔽
        Choice(CategoryAmbientOcclusion, "r.AODownsampleFactor", [new("1", "FullRes"), new("2", "HalfRes")]),
        Number(CategoryAmbientOcclusion, "r.AmbientOcclusion.Intensity", -1, 2, 0.1),
        Number(CategoryAmbientOcclusion, "r.AmbientOcclusionMaxQuality", 0, 100, 10),

        // 视距
        Number(CategoryViewDistance, "foliage.LODDistanceScale", 0.5, 5, 0.5),
        Number(CategoryViewDistance, "r.Kuro.Foliage.NearCullDistanceMax", 0, 100000, 250),
        Number(CategoryViewDistance, "r.Kuro.Foliage.MiddleCullDistanceMax", 0, 100000, 250),
        Number(CategoryViewDistance, "r.Kuro.Foliage.FarCullDistanceMax", 0, 100000, 500),
        Number(CategoryViewDistance, "r.Kuro.Foliage.GrassCullDistanceMax", 0, 100000, 500),
        Number(CategoryViewDistance, "r.Kuro.Foliage.Grass3_0CullDistanceMax", 0, 100000, 500),
        Number(CategoryViewDistance, "wp.Runtime.SoraGridBlackListHeight", 0, 50000, 500),
        Choice(CategoryViewDistance, "foliage.CullAll", OffOn),
        Number(CategoryViewDistance, "r.Kuro.NpcDisappearDistance", 1000, 50000, 1000),

        // 串流与细节层级
        Choice(CategoryStreaming, "r.Streaming.UsingKuroStreamingPriority", [new("0", "Off"), new("1", "Streaming_Retention"), new("2", "Streaming_Load"), new("3", "Streaming_Both")]),
        Number(CategoryStreaming, "r.StaticMeshLODDistanceScale", 0.1, 2, 0.05),
        Number(CategoryStreaming, "wp.Runtime.PlannedLoadingRangeScale", 0.1, 1, 0.1),
        Number(CategoryStreaming, "r.Kuro.SkeletalMesh.DistanceLODBaseFOV", 30, 180, 10),
        Choice(CategoryStreaming, "r.Kuro.MaterialDesktopQualityShoulderRender", OffOn),
        Choice(CategoryStreaming, "r.MeshBlend.Quality", [new("1", "Low"), new("2", "Medium"), new("3", "High"), new("4", "Epic")]),
        Choice(CategoryStreaming, "r.MaxAnisotropy", [new("1"), new("2"), new("4"), new("8"), new("16")], RendererSettings),
        Choice(CategoryStreaming, "r.ParallelFrustumCull", OffOn),

        // 光照
        Choice(CategoryLighting, "r.TyndallScatteringQuality", [new("0", "Low"), new("1", "High")]),
        Choice(CategoryLighting, "r.LightShaftDownSampleFactor", [new("1", "FullRes"), new("2", "HalfRes"), new("4", "QuarterRes"), new("8")]),
        Choice(CategoryLighting, "r.Kuro.KuroTyndallScatteringsDownSampleFactor", DownSample124),
        Choice(CategoryLighting, "r.KuroVolumetricLight.ColorMaskDownSampleFactor", DownSample124),
        Choice(CategoryLighting, "r.KuroVolumetricLight.DownSampleFactor", DownSample124),

        // 反射
        Choice(CategoryReflection, "r.SSR.Quality", [new("0", "Off"), new("1", "Low"), new("2", "Medium"), new("3", "High"), new("4", "VeryHigh")]),
        Number(CategoryReflection, "r.SSR.MaxRoughness", 0, 1, 0.05),
        Choice(CategoryReflection, "r.SSR.HalfResSceneColor", [new("0", "FullRes"), new("1", "HalfRes")]),
        Choice(CategoryReflection, "r.GBufferFormat", [new("1", "GBuffer_Low"), new("3", "GBuffer_HighNormals"), new("5", "GBuffer_High")]),

        // 雾
        Choice(CategoryFog, "r.SSFS", OffOn),
        Choice(CategoryFog, "r.SSFS.DownSampleFator", DownSample124),

        // 特效与其他
        Choice(CategoryEffects, "a.URO.Enable", OffOn),
        Choice(CategoryEffects, "a.URO.EnableScreenRatio", OffOn),
        Choice(CategoryEffects, "r.Upscale.Quality", [new("0"), new("1"), new("2"), new("3")]),
        Choice(CategoryEffects, "r.DetailMode", [new("0", "Low"), new("1", "Medium"), new("2", "High")]),
        Choice(CategoryEffects, "r.VRS.Enable", OffOn),
        Choice(CategoryEffects, "r.Kuro.KuroFFTDynamicQuality", OffOn),
        Choice(CategoryEffects, "r.Kuro.KuroFFTHighQuality", [new("1"), new("2"), new("3"), new("4")]),
        Choice(CategoryEffects, "r.KuroDownsampleTranslucencyFullRes", FalseTrue),
        Choice(CategoryEffects, "foliage.DensityScaleLOD.DrawCallOptimize", OffOn),
        Choice(CategoryEffects, "r.Kuro.InteractionEffect.EnableBushInteractionEffect", OffOn),
        Choice(CategoryEffects, "r.Kuro.InteractionEffect.EnableFoliageEffect", OffOn),
        Choice(CategoryEffects, "r.Kuro.InteractionEffect.UseCppWaterEffect", OffOn),
        Choice(CategoryEffects, "r.HZBOcclusion", [new("0", "Occlusion_Hardware"), new("1", "Occlusion_HZB")]),
        Choice(CategoryEffects, "r.PSO.CacheEvictScheme", [new("0", "PSO_Default"), new("1", "PSO_LRU")]),
        Number(CategoryEffects, "r.PSO.LRUCapacity", 1000, 8192, 256),
        Choice(CategoryEffects, "Kuro.Blueprint.EnableGameBudget", FalseTrue),
        Choice(CategoryEffects, "t.Streamline.Reflex.Enable", OffOn),
        Choice(CategoryEffects, "t.Streamline.Reflex.Mode", [new("1", "Reflex_LowLatency"), new("2", "Reflex_Boost")]),
        Choice(CategoryEffects, "t.Streamline.Reflex.HandleMaxTickRate", FalseTrue),

        // 光线追踪
        Choice(CategoryRayTracing, "r.RayTracing.LoadConfig", OffOn, RendererSettings),
        Number(CategoryRayTracing, "r.Lumen.ScreenProbeGather.Temporal.DistanceThreshold", 0.005, 0.1, 0.005),
        Choice(CategoryRayTracing, "r.Lumen.Reflections.AsyncCompute", OffOn),
        Choice(CategoryRayTracing, "r.Lumen.Reflections.ScreenTraces", OffOn),
        Number(CategoryRayTracing, "r.Lumen.Reflections.SmoothBias", 0, 1, 0.1),
        Choice(CategoryRayTracing, "r.Lumen.Reflections.DownsampleFactor", [new("1", "FullRes"), new("2", "HalfRes")]),
        Choice(CategoryRayTracing, "r.Lumen.Reflections.WaterDownsampleFactor", [new("1", "FullRes"), new("2", "HalfRes")]),
        Choice(CategoryRayTracing, "r.Lumen.TranslucencyReflections.FrontLayer.Allow", OffOn),
        Choice(CategoryRayTracing, "r.Lumen.TranslucencyReflections.FrontLayer.DownsampleFactor", [new("1", "FullRes"), new("2", "HalfRes")]),
        Choice(CategoryRayTracing, "r.Water.SingleLayer.Reflection.DownsampleCheckerboard", OffOn),
        Choice(CategoryRayTracing, "r.Reflections.Denoiser", [new("0", "Off"), new("1", "Denoiser_Builtin"), new("2", "Denoiser_Plugin")]),
        Choice(CategoryRayTracing, "r.Water.SingleLayer.SSRTAA", OffOn),
        Choice(CategoryRayTracing, "r.Lumen.Reflections.BilateralFilter", OffOn),
        Choice(CategoryRayTracing, "r.Lumen.Reflections.Temporal", OffOn),
        Choice(CategoryRayTracing, "r.Lumen.Reflections.ScreenSpaceReconstruction", OffOn),
        Choice(CategoryRayTracing, "r.NGX.DLSS.DenoiserMode", [new("0", "Off"), new("1", "DLSS_RayReconstruction")]),

        // 输入（Input.ini）
        Choice(CategoryInput, "bEnableMouseSmoothing", [new("False", "Off"), new("True", "On")], InputSettings, KuroEngineIniFile.Input),
        Choice(CategoryInput, "bEnableFOVScaling", [new("False", "Off"), new("True", "On")], InputSettings, KuroEngineIniFile.Input),
    ];


    private static readonly Dictionary<string, KuroEngineTweak> _byKey = Tweaks.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);


    public static KuroEngineTweak? Find(string key) => _byKey.GetValueOrDefault(key);


    /// <summary>
    /// 五份预设共用、每份都一样的部分
    /// </summary>
    private static readonly (string Key, string Value)[] PresetCommon =
    [
        ("r.ParallelFrustumCull", "1"),
        ("r.SceneColorFringeQuality", "0"),
        ("r.Streaming.UsingKuroStreamingPriority", "0"),
        ("r.Kuro.SkeletalMesh.DistanceLODBaseFOV", "150.0"),
        ("a.URO.Enable", "0"),
        ("a.URO.EnableScreenRatio", "0"),
        ("r.Upscale.Quality", "3"),
        ("r.VRS.Enable", "0"),
        ("r.Kuro.KuroFFTDynamicQuality", "0"),
        ("foliage.DensityScaleLOD.DrawCallOptimize", "1"),
    ];


    private static KuroEngineTweakPreset Preset(int number, params (string Key, string Value)[] values)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in PresetCommon.Concat(values))
        {
            dict[key] = value;
        }
        return new KuroEngineTweakPreset(number, dict);
    }


    public static IReadOnlyList<KuroEngineTweakPreset> Presets { get; } =
    [
        Preset(1,
            ("r.Shadow.RadiusThreshold", "0.01"),
            ("r.Shadow.PerObjectResolutionMax", "1024"),
            ("r.Shadow.PerObjectResolutionMin", "1024"),
            ("r.DistanceFieldShadowing", "1"),
            ("r.DFShadowQuality", "3"),
            ("r.AODownsampleFactor", "1"),
            ("r.AmbientOcclusion.Intensity", "-1"),
            ("r.AmbientOcclusionMaxQuality", "100"),
            ("foliage.LODDistanceScale", "3.0"),
            ("r.Kuro.Foliage.NearCullDistanceMax", "13000"),
            ("r.Kuro.Foliage.MiddleCullDistanceMax", "21000"),
            ("r.Kuro.Foliage.FarCullDistanceMax", "45000"),
            ("r.Kuro.Foliage.GrassCullDistanceMax", "45000"),
            ("r.Kuro.Foliage.Grass3_0CullDistanceMax", "50000"),
            ("wp.Runtime.SoraGridBlackListHeight", "20000"),
            ("r.StaticMeshLODDistanceScale", "0.5"),
            ("wp.Runtime.PlannedLoadingRangeScale", "1.0"),
            ("r.TyndallScatteringQuality", "1"),
            ("r.LightShaftDownSampleFactor", "1"),
            ("r.Kuro.KuroTyndallScatteringsDownSampleFactor", "1"),
            ("r.KuroVolumetricLight.ColorMaskDownSampleFactor", "1"),
            ("r.KuroVolumetricLight.DownSampleFactor", "1"),
            ("r.SSR.MaxRoughness", "1.0"),
            ("r.SSR.HalfResSceneColor", "0"),
            ("r.SSFS.DownSampleFator", "1"),
            ("r.MeshBlend.Quality", "3"),
            ("r.Kuro.KuroFFTHighQuality", "1"),
            ("r.KuroDownsampleTranslucencyFullRes", "true"),
            ("r.MaxAnisotropy", "16"),
            ("r.RayTracing.LoadConfig", "1")),
        Preset(2,
            ("r.Shadow.RadiusThreshold", "0.01"),
            ("r.Shadow.PerObjectResolutionMax", "768"),
            ("r.Shadow.PerObjectResolutionMin", "768"),
            ("r.AODownsampleFactor", "2"),
            ("r.AmbientOcclusion.Intensity", "-1"),
            ("r.AmbientOcclusionMaxQuality", "100"),
            ("foliage.LODDistanceScale", "2.0"),
            ("r.Kuro.Foliage.NearCullDistanceMax", "9750"),
            ("r.Kuro.Foliage.MiddleCullDistanceMax", "15750"),
            ("r.Kuro.Foliage.FarCullDistanceMax", "35000"),
            ("r.Kuro.Foliage.GrassCullDistanceMax", "35000"),
            ("r.Kuro.Foliage.Grass3_0CullDistanceMax", "40000"),
            ("wp.Runtime.SoraGridBlackListHeight", "15000"),
            ("r.StaticMeshLODDistanceScale", "0.5"),
            ("wp.Runtime.PlannedLoadingRangeScale", "1.0"),
            ("r.TyndallScatteringQuality", "1"),
            ("r.LightShaftDownSampleFactor", "1"),
            ("r.Kuro.KuroTyndallScatteringsDownSampleFactor", "1"),
            ("r.KuroVolumetricLight.ColorMaskDownSampleFactor", "1"),
            ("r.KuroVolumetricLight.DownSampleFactor", "1"),
            ("r.SSR.MaxRoughness", "1.0"),
            ("r.SSR.HalfResSceneColor", "0"),
            ("r.SSFS.DownSampleFator", "2"),
            ("r.Kuro.KuroFFTHighQuality", "2"),
            ("r.KuroDownsampleTranslucencyFullRes", "false"),
            ("r.MaxAnisotropy", "16"),
            ("r.RayTracing.LoadConfig", "0")),
        Preset(3,
            ("r.Shadow.RadiusThreshold", "0.01"),
            ("r.Shadow.PerObjectResolutionMax", "768"),
            ("r.Shadow.PerObjectResolutionMin", "768"),
            ("r.DistanceFieldShadowing", "0"),
            ("r.AODownsampleFactor", "2"),
            ("r.AmbientOcclusion.Intensity", "-1"),
            ("r.AmbientOcclusionMaxQuality", "100"),
            ("r.Kuro.Foliage.NearCullDistanceMax", "9750"),
            ("r.Kuro.Foliage.MiddleCullDistanceMax", "15750"),
            ("r.Kuro.Foliage.FarCullDistanceMax", "22500"),
            ("r.Kuro.Foliage.GrassCullDistanceMax", "22500"),
            ("r.Kuro.Foliage.Grass3_0CullDistanceMax", "35000"),
            ("wp.Runtime.SoraGridBlackListHeight", "10000"),
            ("r.StaticMeshLODDistanceScale", "0.5"),
            ("wp.Runtime.PlannedLoadingRangeScale", "0.9"),
            ("r.TyndallScatteringQuality", "0"),
            ("r.LightShaftDownSampleFactor", "1"),
            ("r.Kuro.KuroTyndallScatteringsDownSampleFactor", "1"),
            ("r.KuroVolumetricLight.ColorMaskDownSampleFactor", "1"),
            ("r.KuroVolumetricLight.DownSampleFactor", "1"),
            ("r.SSR.MaxRoughness", "1.0"),
            ("r.SSR.HalfResSceneColor", "0"),
            ("r.SSFS.DownSampleFator", "2"),
            ("r.Kuro.KuroFFTHighQuality", "2"),
            ("r.KuroDownsampleTranslucencyFullRes", "false"),
            ("r.MaxAnisotropy", "16"),
            ("r.RayTracing.LoadConfig", "0")),
        Preset(4,
            ("r.Shadow.RadiusThreshold", "0.02"),
            ("r.Shadow.PerObjectShadowMapResolution", "512"),
            ("r.Shadow.PerObjectResolutionMax", "512"),
            ("r.Shadow.PerObjectResolutionMin", "512"),
            ("r.DistanceFieldShadowing", "0"),
            ("r.AODownsampleFactor", "2"),
            ("r.AmbientOcclusion.Intensity", "-1"),
            ("r.AmbientOcclusionMaxQuality", "100"),
            ("r.Kuro.Foliage.NearCullDistanceMax", "6500"),
            ("r.Kuro.Foliage.MiddleCullDistanceMax", "10500"),
            ("r.Kuro.Foliage.FarCullDistanceMax", "15000"),
            ("r.Kuro.Foliage.GrassCullDistanceMax", "15000"),
            ("r.Kuro.Foliage.Grass3_0CullDistanceMax", "15000"),
            ("wp.Runtime.SoraGridBlackListHeight", "7500"),
            ("r.StaticMeshLODDistanceScale", "0.7"),
            ("wp.Runtime.PlannedLoadingRangeScale", "0.6"),
            ("r.TyndallScatteringQuality", "0"),
            ("r.LightShaftDownSampleFactor", "1"),
            ("r.Kuro.KuroTyndallScatteringsDownSampleFactor", "2"),
            ("r.KuroVolumetricLight.ColorMaskDownSampleFactor", "2"),
            ("r.KuroVolumetricLight.DownSampleFactor", "2"),
            ("r.SSR.MaxRoughness", "1.0"),
            ("r.SSR.HalfResSceneColor", "0"),
            ("r.SSFS.DownSampleFator", "4"),
            ("r.Kuro.KuroFFTHighQuality", "3"),
            ("r.KuroDownsampleTranslucencyFullRes", "false"),
            ("r.MaxAnisotropy", "16"),
            ("r.RayTracing.LoadConfig", "0")),
        Preset(5,
            ("r.Shadow.RadiusThreshold", "0.06"),
            ("r.Shadow.PerObjectShadowMapResolution", "256"),
            ("r.Shadow.PerObjectResolutionMax", "256"),
            ("r.Shadow.PerObjectResolutionMin", "256"),
            ("r.DistanceFieldShadowing", "0"),
            ("r.AmbientOcclusionMaxQuality", "0"),
            ("r.Kuro.Foliage.NearCullDistanceMax", "5000"),
            ("r.Kuro.Foliage.MiddleCullDistanceMax", "7500"),
            ("r.Kuro.Foliage.FarCullDistanceMax", "7500"),
            ("r.Kuro.Foliage.GrassCullDistanceMax", "7500"),
            ("r.Kuro.Foliage.Grass3_0CullDistanceMax", "7500"),
            ("wp.Runtime.SoraGridBlackListHeight", "1500"),
            ("r.StaticMeshLODDistanceScale", "0.7"),
            ("r.Kuro.MaterialDesktopQualityShoulderRender", "0"),
            ("wp.Runtime.PlannedLoadingRangeScale", "0.4"),
            ("r.TyndallScatteringQuality", "0"),
            ("r.LightShaftDownSampleFactor", "2"),
            ("r.Kuro.KuroTyndallScatteringsDownSampleFactor", "2"),
            ("r.KuroVolumetricLight.ColorMaskDownSampleFactor", "4"),
            ("r.KuroVolumetricLight.DownSampleFactor", "4"),
            ("r.SSR.Quality", "0"),
            ("r.SSFS", "0"),
            ("r.Kuro.KuroFFTHighQuality", "4"),
            ("r.KuroDownsampleTranslucencyFullRes", "false"),
            ("r.Kuro.InteractionEffect.EnableBushInteractionEffect", "0"),
            ("r.Kuro.InteractionEffect.EnableFoliageEffect", "0"),
            ("r.Kuro.InteractionEffect.UseCppWaterEffect", "0"),
            ("foliage.CullAll", "1"),
            ("r.MaxAnisotropy", "4"),
            ("r.RayTracing.LoadConfig", "0")),
    ];


    /// <summary>
    /// 至少出现在一份预设里的键。载入预设时只重设这些键，
    /// 预设没涉及的进阶项（Reflex、光追降噪等）保留玩家自己的选择。
    /// </summary>
    public static IReadOnlySet<string> PresetKeys { get; } = Presets.SelectMany(x => x.Values.Keys).ToHashSet(StringComparer.OrdinalIgnoreCase);


    /// <summary>
    /// 两个值是否等价：数值按数值比（<c>1.0</c> 与 <c>1</c> 相同），其余忽略大小写
    /// </summary>
    public static bool ValueEquals(string? a, string? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }
        a = a.Trim();
        b = b.Trim();
        if (double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
            && double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
        {
            return Math.Abs(x - y) < 1e-9;
        }
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

}
