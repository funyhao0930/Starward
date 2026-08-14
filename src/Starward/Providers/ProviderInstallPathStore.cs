using Starward.Core;
using Starward.Core.Games;
using Starward.Features.GameLauncher;
using System.IO;

namespace Starward.Providers;

/// <summary>
/// 以 <see cref="GameDescriptor.SettingsKey"/> 为键读写游戏安装目录。
/// <para/>
/// 米哈游游戏的键仍是旧的 GameBiz 字符串（例如 <c>install_path_hk4e_cn</c>），
/// 其他供应商使用 GameKey 的正规字符串（例如 <c>install_path_kuro:wuwa:global</c>）。
/// <see cref="GameBiz"/> 本身只是字符串包装，因此两种键可以共存，
/// 不需要更改配置文件或数据库的结构。
/// </summary>
internal static class ProviderInstallPathStore
{

    /// <summary>
    /// 已记录的安装目录，目录不存在且不在可移动存储设备上时清除记录并返回 null
    /// </summary>
    public static string? GetInstallPath(GameDescriptor descriptor)
    {
        GameBiz key = descriptor.SettingsKey;
        string? path = AppConfig.GetGameInstallPath(key);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        path = GameLauncherService.GetFullPathIfRelativePath(path);
        if (Directory.Exists(path))
        {
            return Path.GetFullPath(path);
        }
        if (AppConfig.GetGameInstallPathRemovable(key))
        {
            return path;
        }
        SetInstallPath(descriptor, null);
        return null;
    }


    /// <summary>
    /// 安装在可移动存储设备中
    /// </summary>
    public static bool GetRemovable(GameDescriptor descriptor)
    {
        return AppConfig.GetGameInstallPathRemovable(descriptor.SettingsKey);
    }


    /// <summary>
    /// 记录安装目录，传入 null 或不存在的目录时清除记录
    /// </summary>
    public static string? SetInstallPath(GameDescriptor descriptor, string? path)
    {
        GameBiz key = descriptor.SettingsKey;
        if (Directory.Exists(path))
        {
            path = Path.GetFullPath(path);
            string relativePath = GameLauncherService.GetRelativePathIfInRemovableStorage(path, out bool removable);
            AppConfig.SetGameInstallPath(key, relativePath);
            AppConfig.SetGameInstallPathRemovable(key, removable);
        }
        else
        {
            path = null;
            AppConfig.SetGameInstallPath(key, null);
            AppConfig.SetGameInstallPathRemovable(key, false);
        }
        return path;
    }

}
