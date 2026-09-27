using Starward.Core.Launcher;
using Starward.Core.Launcher.Gryphline;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Xunit;

namespace Starward.Core.Tests.Launcher;

/// <summary>
/// 终末地按文件下载：文件清单读自整包末尾的 zip 中央目录，不需要解密本机的 game_files。
/// <para/>
/// 两份真实样本取自 2026-09 线上 1.5.3 的第 54 卷：结尾的 Zip64 记录（98 字节），
/// 以及中央目录的最后三个条目（偏移都超过 4 GB，只能从 Zip64 扩展字段读）。
/// </summary>
public class GryphlineDownloadPlannerTests
{

    /// <summary>
    /// 54 个分卷首尾相接的总长度
    /// </summary>
    private const long PackageLength = 56913307594;


    /// <summary>
    /// Zip64 结尾记录、定位器与普通结尾记录，整包最后 98 字节
    /// </summary>
    private const string TailBase64 = "UEsGBiwAAAAAAAAALQAtAAAAAAAAAAAAngYAAAAAAACeBgAAAAAAAEr4AwAAAAAAHi9IQA0AAABQSwYHAAAAAGgnTEANAAAAAQAAAFBLBQYAAAAAngaeBkr4AwD/////AAA=";


    private const string LastThreeEntriesBase64 = """
        UEsBAj8DLQAAAAgASwkVXVzF5uD1SwAAiJEAABIAMAAAAAAAAAAggKSB/////3ZjcnVudGltZTE0MF8xLmRsbAEACADmgzVADQAAAAoAIAAAAAAAAQAY
        ABiquMfGMN0BAAAAAAAAAAAAAAAAAAAAAFBLAQI/Ay0AAAAIAI8JFV2CGn/zKJMRAIj5JQAOADAAAAAAAAAAIICkgf////93ZWJ2aWV3c2RrLmRsbAEACAAL
        0DVADQAAAAoAIAAAAAAAAQAYAK4vaxTHMN0BAAAAAAAAAAAAAAAAAAAAAFBLAQI/Ay0AAAAIAEsJFV3jPNZhmcsAAACZAQAIADAAAAAAAAAAIICkgf////96
        bGliLmRsbAEACABfY0dADQAAAAoAIAAAAAAAAQAYABu82cfGMN0BAAAAAAAAAAAAAAAAAAAAAA==
        """;


    private static byte[] Decode(string base64) => Convert.FromBase64String(string.Concat(base64.Where(c => !char.IsWhiteSpace(c))));



    /// <summary>
    /// 中央目录在第 54 卷里，偏移超过 4 GB，只写在 Zip64 记录里；普通结尾记录那一格是 0xFFFFFFFF
    /// </summary>
    [Fact]
    public void Locate_ReadsTheZip64Record()
    {
        byte[] tail = Decode(TailBase64);

        ZipCentralDirectoryInfo info = ZipCentralDirectory.Locate(tail, PackageLength - tail.Length);

        Assert.Equal(56913047326, info.Offset);
        Assert.Equal(260170, info.Size);
        Assert.Equal(1694, info.EntryCount);
        // 中央目录一直延伸到 Zip64 记录之前
        Assert.Equal(PackageLength - tail.Length, info.Offset + info.Size);
    }


    [Fact]
    public void ReadEntries_ReadsLargeOffsetsFromTheZip64Extra()
    {
        IReadOnlyList<ZipEntryInfo> entries = ZipCentralDirectory.ReadEntries(Decode(LastThreeEntriesBase64));

        Assert.Equal(["vcruntime140_1.dll", "webviewsdk.dll", "zlib.dll"], entries.Select(x => x.Name));
        Assert.Equal([56911823846L, 56911843339L, 56912995167L], entries.Select(x => x.LocalHeaderOffset));
        Assert.Equal([0xe0e6c55cu, 0xf37f1a82u, 0x61d63ce3u], entries.Select(x => x.Crc32));
        Assert.Equal([37256L, 2488712L, 104704L], entries.Select(x => x.Size));
        Assert.Equal([19445L, 1151784L, 52121L], entries.Select(x => x.CompressedSize));
        Assert.All(entries, x => Assert.Equal(8, x.Method));
        Assert.All(entries, x => Assert.False(x.IsEncrypted));
    }


    /// <summary>
    /// 普通（非 Zip64）的 zip 由 .NET 自己生成：目录、存储与压缩、UTF-8 文件名都要对
    /// </summary>
    [Fact]
    public void ReadEntries_MatchesWhatZipArchiveWrote()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry("Endfield_Data/");
            using (Stream s = zip.CreateEntry("Endfield_Data/StreamingAssets/VFS/0CE8FA57/abc.chk", CompressionLevel.Optimal).Open())
            {
                s.Write(Encoding.UTF8.GetBytes(new string('x', 5000)));
            }
            using (Stream s = zip.CreateEntry("資料/說明.txt", CompressionLevel.NoCompression).Open())
            {
                s.Write("hello"u8);
            }
        }
        byte[] data = ms.ToArray();
        ms.Position = 0;
        using var read = new ZipArchive(ms, ZipArchiveMode.Read);

        ZipCentralDirectoryInfo info = ZipCentralDirectory.Locate(data, 0);
        IReadOnlyList<ZipEntryInfo> entries = ZipCentralDirectory.ReadEntries(data.AsSpan((int)info.Offset, (int)info.Size));

        Assert.Equal(3, info.EntryCount);
        Assert.Equal(read.Entries.Select(x => x.FullName), entries.Select(x => x.Name));
        Assert.Equal(read.Entries.Select(x => x.Crc32), entries.Select(x => x.Crc32));
        Assert.Equal(read.Entries.Select(x => x.Length), entries.Select(x => x.Size));
        Assert.Equal(read.Entries.Select(x => x.CompressedLength), entries.Select(x => x.CompressedSize));
        Assert.True(entries[0].IsDirectory);
        Assert.Equal(0, entries[2].Method);
    }


    [Fact]
    public void Locate_RejectsDataWithoutAnEndRecord()
    {
        Assert.Throws<InvalidDataException>(() => ZipCentralDirectory.Locate(new byte[100], 0));
    }


    /// <summary>
    /// 分卷是一个 zip 直接切开的，跨卷的一段要拆成两次 Range
    /// </summary>
    [Fact]
    public void MapRange_SplitsARangeThatCrossesAPackBoundary()
    {
        long[] sizes = [1073741824, 1073741824, 4990922];

        var within = GryphlineDownloadPlanner.MapRange(sizes, 2 * 1073741824L + 100, 50);
        Assert.Equal([(2, 100L, 50L)], within);

        var across = GryphlineDownloadPlanner.MapRange(sizes, 1073741824L - 10, 30);
        Assert.Equal([(0, 1073741814L, 10L), (1, 0L, 20L)], across);

        Assert.Throws<ArgumentOutOfRangeException>(() => GryphlineDownloadPlanner.MapRange(sizes, 2 * 1073741824L + 4990900, 100));
    }


    /// <summary>
    /// game_files 与 config.ini 最后写：半装好的游戏不能被当成已是最新版
    /// </summary>
    [Fact]
    public void GetFiles_SkipsDirectoriesAndPutsManifestFilesLast()
    {
        ZipEntryInfo Entry(string name) => new(name, 0, 0, 1, 0, 8, 0);
        var entries = new[] { Entry("config.ini"), Entry("AntiCheatExpert/"), Entry("game_files"), Entry("Endfield.exe"), Entry("Endfield_Data/a.chk") };

        Assert.Equal(["Endfield.exe", "Endfield_Data/a.chk", "game_files", "config.ini"], GryphlineDownloadPlanner.GetFiles(entries).Select(x => x.Name));
    }


    [Fact]
    public void GetFileUrl_EscapesEachSegment()
    {
        string url = GryphlineDownloadPlanner.GetFileUrl("https://beyond.hg-cdn.com/YDUTE5gscDZ229CW/1.5/update/6/6/Windows/1.5.3_X9dZfixN2KQwWvcQ/files/",
                                                         "Endfield_Data/Plugins/x86_64/some file.dll");
        Assert.Equal("https://beyond.hg-cdn.com/YDUTE5gscDZ229CW/1.5/update/6/6/Windows/1.5.3_X9dZfixN2KQwWvcQ/files/Endfield_Data/Plugins/x86_64/some%20file.dll", url);
    }


    /// <summary>
    /// 只清 VFS 目录，其他地方的多余文件（例如玩家换的 DLSS）不碰
    /// </summary>
    [Fact]
    public void GetStaleVfsFiles_OnlyLooksInsideTheVfsDirectory()
    {
        ZipEntryInfo Entry(string name) => new(name, 0, 0, 1, 0, 8, 0);
        var files = new[] { Entry("Endfield_Data/StreamingAssets/VFS/0CE8FA57/new.chk"), Entry("Endfield.exe") };
        var local = new[]
        {
            "Endfield_Data/StreamingAssets/VFS/0CE8FA57/new.chk",
            @"Endfield_Data\StreamingAssets\VFS\0CE8FA57\old.chk",
            "nvngx_dlssg.dll.dlsss",
        };

        Assert.Equal(["Endfield_Data/StreamingAssets/VFS/0CE8FA57/old.chk"], GryphlineDownloadPlanner.GetStaleVfsFiles(local, files));
    }

}
