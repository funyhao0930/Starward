using System.Globalization;
using System.Resources;

namespace Starward.Core.Games.Kuro;

/// <summary>
/// 调校项的标题、说明与界面文字，来自 <c>Localization/KuroEngineTweakLang*.resx</c>。
/// <para/>
/// 八十多个参数的说明全塞进 CoreLang 会淹没其他文字，所以单独一份资源，
/// 键名由 <see cref="KuroEngineTweak.ResourceName"/> 拼出来，不生成强类型属性。
/// </summary>
public static class KuroEngineTweakText
{

    public static ResourceManager ResourceManager { get; } = new("Starward.Core.Localization.KuroEngineTweakLang", typeof(KuroEngineTweakText).Assembly);


    public static string Get(string name) => ResourceManager.GetString(name, CultureInfo.CurrentUICulture) ?? name;


    public static string Title(KuroEngineTweak tweak) => Get($"Tweak_{tweak.ResourceName}_Title");

    public static string Description(KuroEngineTweak tweak) => Get($"Tweak_{tweak.ResourceName}_Desc");

    public static string Category(string category) => Get($"Category_{category}");

    public static string Preset(KuroEngineTweakPreset preset) => Get($"Preset_{preset.Number}");


    /// <summary>
    /// 下拉选项的文字，有名字的显示「名字（值）」，免得玩家对不上 ini 里的数字
    /// </summary>
    public static string Option(KuroEngineTweakOption option)
    {
        return option.LabelKey is null ? option.Value : string.Format(Get("Ui_OptionFormat"), Get($"Option_{option.LabelKey}"), option.Value);
    }


    public static string Ui_Title => Get(nameof(Ui_Title));
    public static string Ui_Description => Get(nameof(Ui_Description));
    public static string Ui_Preset => Get(nameof(Ui_Preset));
    public static string Ui_LoadPreset => Get(nameof(Ui_LoadPreset));
    public static string Ui_PresetNote => Get(nameof(Ui_PresetNote));
    public static string Ui_ResetAll => Get(nameof(Ui_ResetAll));
    public static string Ui_OpenFolder => Get(nameof(Ui_OpenFolder));
    public static string Ui_SourceLink => Get(nameof(Ui_SourceLink));
    public static string Ui_GameDefault => Get(nameof(Ui_GameDefault));
    public static string Ui_GameDefaultPlaceholder => Get(nameof(Ui_GameDefaultPlaceholder));
    public static string Ui_CurrentValue => Get(nameof(Ui_CurrentValue));
    public static string Ui_ModifiedCount => Get(nameof(Ui_ModifiedCount));
    public static string Ui_UserEngineIniFound => Get(nameof(Ui_UserEngineIniFound));
    public static string Ui_DisableUserEngineIni => Get(nameof(Ui_DisableUserEngineIni));
    public static string Ui_SearchPlaceholder => Get(nameof(Ui_SearchPlaceholder));
    public static string Ui_NoSearchResult => Get(nameof(Ui_NoSearchResult));
    public static string Ui_ConfirmResetAll => Get(nameof(Ui_ConfirmResetAll));
    public static string Ui_ConfirmLoadPreset => Get(nameof(Ui_ConfirmLoadPreset));


    /// <summary>
    /// 搜索框用：按空白拆成几个词，每个词都要出现在某个字段里（忽略大小写）。空查询算全部符合。
    /// </summary>
    public static bool MatchesSearch(string? query, params string?[] fields)
    {
        string[] terms = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return terms.All(term => fields.Any(field => field?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false));
    }


    /// <summary>
    /// 「Engine.ini 里有 N 个设置不会生效：键（原因）、……」
    /// </summary>
    public static string IneffectiveKeys(IReadOnlyCollection<(string Key, KuroIneffectiveReason Reason)> keys)
    {
        string list = string.Join(Get("Ui_ListSeparator"), keys.Select(x => string.Format(Get("Ui_OptionFormat"), x.Key, Get($"Ineffective_{x.Reason}"))));
        return string.Format(Get("Ui_IneffectiveKeysFound"), keys.Count, list);
    }

}
