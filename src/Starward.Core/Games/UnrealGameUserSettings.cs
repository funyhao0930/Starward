using System.Globalization;
using System.Text.RegularExpressions;

namespace Starward.Core.Games;

/// <summary>
/// 虚幻引擎的 <c>GameUserSettings.ini</c>。
/// <para/>
/// 这些键是引擎自己的，每个虚幻游戏都一样，不是哪一家的私有格式。
/// <para/>
/// 写入遵守两条规矩：<b>只改文件里已经存在的键</b>（不认识的游戏不该被我们
/// 塞进新设置），并且<b>写之前先留一份备份</b>。
/// </summary>
public static class UnrealGameUserSettings
{

    private const string ResolutionWidthKey = "ResolutionSizeX";
    private const string ResolutionHeightKey = "ResolutionSizeY";

    /// <summary>
    /// 引擎载入时实际采用的是这一对，只改上面那对会被还原
    /// </summary>
    private const string ConfirmedWidthKey = "LastUserConfirmedResolutionSizeX";
    private const string ConfirmedHeightKey = "LastUserConfirmedResolutionSizeY";

    /// <summary>
    /// 0 独占全屏，1 无边框全屏，2 窗口
    /// </summary>
    private const string FullscreenModeKey = "FullscreenMode";
    private const string LastConfirmedFullscreenModeKey = "LastConfirmedFullscreenMode";
    private const string PreferredFullscreenModeKey = "PreferredFullscreenMode";

    private const int WindowedMode = 2;

    /// <summary>
    /// 从窗口切回全屏时用无边框全屏，它比独占全屏稳，切出切入也快
    /// </summary>
    private const int BorderlessFullscreenMode = 1;


    /// <summary>
    /// 读取分辨率与窗口模式，文件不存在或读不到必要的键时返回 null
    /// </summary>
    public static GameResolutionSetting? Read(string? iniPath)
    {
        if (string.IsNullOrWhiteSpace(iniPath) || !File.Exists(iniPath))
        {
            return null;
        }
        string text;
        try
        {
            text = File.ReadAllText(iniPath);
        }
        catch (IOException)
        {
            return null;
        }
        int? width = ReadInt(text, ResolutionWidthKey);
        int? height = ReadInt(text, ResolutionHeightKey);
        int? mode = ReadInt(text, FullscreenModeKey);
        if (width is null || height is null)
        {
            return null;
        }
        return new GameResolutionSetting(width.Value, height.Value, mode != WindowedMode);
    }


    /// <summary>
    /// 写入分辨率与窗口模式，只改已存在的键。
    /// 文件不存在、或一个键都没改到时返回 false。
    /// </summary>
    public static bool Write(string? iniPath, GameResolutionSetting setting)
    {
        if (string.IsNullOrWhiteSpace(iniPath) || !File.Exists(iniPath))
        {
            return false;
        }
        if (setting.Width <= 0 || setting.Height <= 0)
        {
            return false;
        }
        string text = File.ReadAllText(iniPath);
        string origin = text;

        text = ReplaceIfPresent(text, ResolutionWidthKey, setting.Width.ToString(CultureInfo.InvariantCulture));
        text = ReplaceIfPresent(text, ConfirmedWidthKey, setting.Width.ToString(CultureInfo.InvariantCulture));
        text = ReplaceIfPresent(text, ResolutionHeightKey, setting.Height.ToString(CultureInfo.InvariantCulture));
        text = ReplaceIfPresent(text, ConfirmedHeightKey, setting.Height.ToString(CultureInfo.InvariantCulture));

        // 已经是想要的状态就别动模式：玩家可能特意选了独占全屏，
        // 把它改成无边框全屏是多管闲事
        int? currentMode = ReadInt(origin, FullscreenModeKey);
        bool currentIsFullScreen = currentMode != WindowedMode;
        if (currentMode is null || currentIsFullScreen != setting.FullScreen)
        {
            string mode = (setting.FullScreen ? BorderlessFullscreenMode : WindowedMode).ToString(CultureInfo.InvariantCulture);
            text = ReplaceIfPresent(text, FullscreenModeKey, mode);
            text = ReplaceIfPresent(text, LastConfirmedFullscreenModeKey, mode);
            text = ReplaceIfPresent(text, PreferredFullscreenModeKey, mode);
        }

        if (text == origin)
        {
            return false;
        }
        File.Copy(iniPath, iniPath + ".starward.bak", true);
        File.WriteAllText(iniPath, text);
        return true;
    }


    private static int? ReadInt(string text, string key)
    {
        Match match = KeyRegex(key).Match(text);
        return match.Success && int.TryParse(match.Groups[1].Value.Trim(), CultureInfo.InvariantCulture, out int value) ? value : null;
    }


    private static string ReplaceIfPresent(string text, string key, string value)
    {
        Regex regex = KeyRegex(key);
        return regex.IsMatch(text) ? regex.Replace(text, $"{key}={value}", 1) : text;
    }


    private static Regex KeyRegex(string key) => new($@"(?m)^[ \t]*{Regex.Escape(key)}[ \t]*=[ \t]*(.*)$");

}
