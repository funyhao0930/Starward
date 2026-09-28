using NuGet.Versioning;
using Starward.Setup.Core;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.Update;

/// <summary>
/// 從本分支的 GitHub Releases 挑出目前頻道的最新版本
/// </summary>
internal static class GithubReleaseChannel
{

    /// <summary>
    /// 本分支的版本號固定帶 <c>-odyssey.N</c>，語意化版本會把它當成預覽版，
    /// 這裡把只有 odyssey.N 的版本視為穩定版，其餘有標籤的（例如 -odyssey.9.beta）才算預覽版
    /// </summary>
    public static bool IsPreview(NuGetVersion version)
    {
        if (!version.IsPrerelease)
        {
            return false;
        }
        var labels = version.ReleaseLabels.ToArray();
        return !(labels.Length == 2 && string.Equals(labels[0], "odyssey", StringComparison.OrdinalIgnoreCase) && int.TryParse(labels[1], out _));
    }


    public static async Task<ReleaseInfoDetail> GetLatestGithubReleaseInfoDetailAsync(this ReleaseClient client, bool isPrerelease, Architecture arch, InstallType type, CancellationToken cancellationToken = default)
    {
        var releases = await client.GetGithubReleaseAsync(1, 20, cancellationToken);
        var latest = releases.Where(x => !x.Draft)
                             .Select(x => (Release: x, Version: NuGetVersion.TryParse(x.TagName, out var v) ? v : null))
                             .Where(x => x.Version is not null && (isPrerelease || !IsPreview(x.Version)))
                             .MaxBy(x => x.Version);
        if (latest.Release is null)
        {
            throw new InvalidOperationException($"No release found in {ReleaseClient.GithubRepository}.");
        }
        return await client.GetGithubReleaseInfoDetailAsync(latest.Release, arch, type, cancellationToken);
    }


}
