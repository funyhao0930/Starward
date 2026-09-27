namespace Starward.RPC.GameInstall;

/// <summary>
/// 下载完的文件用什么校验。
/// <para/>
/// 米哈游与鸣潮的清单都给 MD5；终末地的文件清单取自整包的 zip 中央目录，只有 CRC32。
/// </summary>
public readonly record struct FileChecksum(FileChecksumType Type, string Value)
{

    public static FileChecksum Md5(string md5) => new(FileChecksumType.Md5, md5);

    public static FileChecksum Crc32(uint crc32) => new(FileChecksumType.Crc32, crc32.ToString("x8"));

}


public enum FileChecksumType
{

    Md5,

    /// <summary>
    /// zip 用的 CRC-32（IEEE 802.3），<see cref="FileChecksum.Value"/> 是 8 位小写十六进制
    /// </summary>
    Crc32,

}
