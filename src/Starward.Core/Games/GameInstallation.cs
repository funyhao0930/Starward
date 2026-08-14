namespace Starward.Core.Games;

/// <summary>
/// 已安装游戏的信息，由 <see cref="IGameDiscoveryProvider"/> 提供。
/// </summary>
public sealed record GameInstallation
{

    /// <summary>
    /// 通用游戏标识
    /// </summary>
    public required GameKey Key { get; init; }


    /// <summary>
    /// 游戏安装目录，完整路径
    /// </summary>
    public required string InstallPath { get; init; }


    /// <summary>
    /// 安装在可移动存储设备中
    /// </summary>
    public bool IsOnRemovableStorage { get; init; }


    /// <summary>
    /// 可移动存储设备已移除，此时 <see cref="InstallPath"/> 指向的目录不存在
    /// </summary>
    public bool StorageRemoved { get; init; }

}
