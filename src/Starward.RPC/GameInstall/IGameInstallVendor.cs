using Starward.Core.Games;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.RPC.GameInstall;

/// <summary>
/// 非米哈游游戏的安装器。
/// <para/>
/// <see cref="GameInstallService"/> 的排队、暂停、继续、进度回报对所有游戏都一样，
/// 但「要下载什么、下载完怎么处理」每家都不同：米哈游走 HoYoPlay 的 Sophon 与压缩包，
/// 鸣潮是逐个文件加 krpdiff 目录差分。这里把后半段交给各家自己实现，
/// 下载、校验、限速等共用的工具仍在 <see cref="GameInstallHelper"/>。
/// </summary>
internal interface IGameInstallVendor
{

    /// <summary>
    /// 对应 <see cref="GameKey.ProviderId"/>
    /// </summary>
    string ProviderId { get; }


    /// <summary>
    /// 执行整个任务。进度写在 <paramref name="context"/> 上，失败时抛出异常，
    /// 被取消时抛出 <see cref="System.OperationCanceledException"/>。
    /// <para/>
    /// 可能被调用多次：暂停后继续时会用同一个 <paramref name="context"/> 再调用一次，
    /// 实现必须能从上次停下的地方接着做。
    /// </summary>
    Task ExecuteAsync(GameInstallContext context, GameKey key, CancellationToken cancellationToken);


    /// <summary>
    /// 卸载时要删除的目录。
    /// <para/>
    /// 米哈游游戏的安装路径就是游戏目录，整个删掉即可；这几家的安装路径是官方启动器的根目录，
    /// 游戏本体只是其中一个子目录，旁边还有官方启动器自己，不能一并删掉。
    /// </summary>
    IReadOnlyList<string> GetUninstallDirectories(GameKey key, string installPath);

}
