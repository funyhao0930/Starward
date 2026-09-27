using System.Buffers.Binary;
using System.Text;

namespace Starward.Core.Launcher;

/// <summary>
/// 只读 zip 的中央目录，不解压任何东西。
/// <para/>
/// 用途是拿到每个文件的路径、大小与 CRC32：终末地的整包是一个按 1 GiB 切开的 Zip64，
/// 本机的文件清单 game_files 是加密的，但整包末尾的中央目录是明文，
/// 只要用 Range 读回最后一小段，就有一份不必解密的完整清单。
/// </summary>
public static class ZipCentralDirectory
{

    private const uint EndOfCentralDirectorySignature = 0x06054b50;

    private const uint Zip64EndOfCentralDirectoryLocatorSignature = 0x07064b50;

    private const uint Zip64EndOfCentralDirectorySignature = 0x06064b50;

    private const uint CentralDirectoryEntrySignature = 0x02014b50;

    private const int EndOfCentralDirectorySize = 22;

    private const int Zip64LocatorSize = 20;

    private const int Zip64RecordMinSize = 56;


    /// <summary>
    /// 从文件末尾往前读这么多字节，一定能找到结尾记录：
    /// 结尾记录 22 字节，注释最长 65535 字节，前面再有 Zip64 的定位器与结尾记录
    /// </summary>
    public const int MaxTailSize = EndOfCentralDirectorySize + ushort.MaxValue + Zip64LocatorSize + Zip64RecordMinSize;



    /// <summary>
    /// 从文件末尾的一段数据里找出中央目录的位置
    /// </summary>
    /// <param name="tail">文件末尾的数据，建议读 <see cref="MaxTailSize"/> 字节，文件更小时就是整个文件</param>
    /// <param name="tailStart"><paramref name="tail"/> 第一个字节在整个文件中的偏移</param>
    /// <exception cref="InvalidDataException">找不到结尾记录</exception>
    public static ZipCentralDirectoryInfo Locate(ReadOnlySpan<byte> tail, long tailStart)
    {
        int eocd = -1;
        for (int i = tail.Length - EndOfCentralDirectorySize; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail[i..]) == EndOfCentralDirectorySignature)
            {
                // 注释长度要刚好写到末尾，防止把文件内容里碰巧出现的签名当成结尾记录
                int commentLength = BinaryPrimitives.ReadUInt16LittleEndian(tail[(i + 20)..]);
                if (i + EndOfCentralDirectorySize + commentLength == tail.Length)
                {
                    eocd = i;
                    break;
                }
            }
        }
        if (eocd < 0)
        {
            throw new InvalidDataException("End of central directory record not found.");
        }
        ReadOnlySpan<byte> record = tail[eocd..];
        long count = BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
        long size = BinaryPrimitives.ReadUInt32LittleEndian(record[12..]);
        long offset = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);

        int locator = eocd - Zip64LocatorSize;
        if (locator >= 0 && BinaryPrimitives.ReadUInt32LittleEndian(tail[locator..]) == Zip64EndOfCentralDirectoryLocatorSignature)
        {
            long zip64Offset = (long)BinaryPrimitives.ReadUInt64LittleEndian(tail[(locator + 8)..]);
            long index = zip64Offset - tailStart;
            if (index < 0 || index + Zip64RecordMinSize > tail.Length)
            {
                throw new InvalidDataException("Zip64 end of central directory record is outside the given data.");
            }
            ReadOnlySpan<byte> zip64 = tail[(int)index..];
            if (BinaryPrimitives.ReadUInt32LittleEndian(zip64) != Zip64EndOfCentralDirectorySignature)
            {
                throw new InvalidDataException("Invalid Zip64 end of central directory record.");
            }
            count = (long)BinaryPrimitives.ReadUInt64LittleEndian(zip64[32..]);
            size = (long)BinaryPrimitives.ReadUInt64LittleEndian(zip64[40..]);
            offset = (long)BinaryPrimitives.ReadUInt64LittleEndian(zip64[48..]);
        }
        else if (count == ushort.MaxValue || size == uint.MaxValue || offset == uint.MaxValue)
        {
            throw new InvalidDataException("Zip64 archive without a Zip64 locator.");
        }
        return new ZipCentralDirectoryInfo(offset, size, count);
    }



    /// <summary>
    /// 解析中央目录的全部条目，目录本身（以 / 结尾）也会列出
    /// </summary>
    /// <exception cref="InvalidDataException">条目损坏</exception>
    public static IReadOnlyList<ZipEntryInfo> ReadEntries(ReadOnlySpan<byte> centralDirectory)
    {
        var list = new List<ZipEntryInfo>();
        int pos = 0;
        while (pos + 46 <= centralDirectory.Length && BinaryPrimitives.ReadUInt32LittleEndian(centralDirectory[pos..]) == CentralDirectoryEntrySignature)
        {
            ReadOnlySpan<byte> header = centralDirectory[pos..];
            int flags = BinaryPrimitives.ReadUInt16LittleEndian(header[8..]);
            int method = BinaryPrimitives.ReadUInt16LittleEndian(header[10..]);
            uint crc = BinaryPrimitives.ReadUInt32LittleEndian(header[16..]);
            long compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(header[20..]);
            long size = BinaryPrimitives.ReadUInt32LittleEndian(header[24..]);
            int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header[28..]);
            int extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header[30..]);
            int commentLength = BinaryPrimitives.ReadUInt16LittleEndian(header[32..]);
            long localHeaderOffset = BinaryPrimitives.ReadUInt32LittleEndian(header[42..]);
            int total = 46 + nameLength + extraLength + commentLength;
            if (pos + total > centralDirectory.Length)
            {
                throw new InvalidDataException("Central directory entry is truncated.");
            }
            ReadOnlySpan<byte> nameBytes = header.Slice(46, nameLength);
            // 第 11 位表示 UTF-8；没有这一位时按规范是 CP437，这些包里只有 ASCII，同样按 UTF-8 读
            string name = Encoding.UTF8.GetString(nameBytes);
            ReadOnlySpan<byte> extra = header.Slice(46 + nameLength, extraLength);
            ReadZip64Extra(extra, ref size, ref compressedSize, ref localHeaderOffset);
            list.Add(new ZipEntryInfo(name, crc, compressedSize, size, localHeaderOffset, method, flags));
            pos += total;
        }
        return list.AsReadOnly();
    }



    /// <summary>
    /// Zip64 扩展字段：头里写成 0xFFFFFFFF 的那几项才会依次出现在这里
    /// </summary>
    private static void ReadZip64Extra(ReadOnlySpan<byte> extra, ref long size, ref long compressedSize, ref long localHeaderOffset)
    {
        int pos = 0;
        while (pos + 4 <= extra.Length)
        {
            int id = BinaryPrimitives.ReadUInt16LittleEndian(extra[pos..]);
            int length = BinaryPrimitives.ReadUInt16LittleEndian(extra[(pos + 2)..]);
            if (pos + 4 + length > extra.Length)
            {
                return;
            }
            if (id == 0x0001)
            {
                ReadOnlySpan<byte> data = extra.Slice(pos + 4, length);
                int p = 0;
                if (size == uint.MaxValue && p + 8 <= data.Length)
                {
                    size = (long)BinaryPrimitives.ReadUInt64LittleEndian(data[p..]);
                    p += 8;
                }
                if (compressedSize == uint.MaxValue && p + 8 <= data.Length)
                {
                    compressedSize = (long)BinaryPrimitives.ReadUInt64LittleEndian(data[p..]);
                    p += 8;
                }
                if (localHeaderOffset == uint.MaxValue && p + 8 <= data.Length)
                {
                    localHeaderOffset = (long)BinaryPrimitives.ReadUInt64LittleEndian(data[p..]);
                }
                return;
            }
            pos += 4 + length;
        }
    }

}


/// <summary>
/// 中央目录在整个文件中的位置
/// </summary>
public sealed record ZipCentralDirectoryInfo(long Offset, long Size, long EntryCount);


/// <summary>
/// 中央目录里的一个条目
/// </summary>
/// <param name="Name">相对路径，正斜杠，目录以 / 结尾</param>
/// <param name="Crc32">解压后内容的 CRC32</param>
/// <param name="CompressedSize">压缩后的大小</param>
/// <param name="Size">解压后的大小</param>
/// <param name="LocalHeaderOffset">本地文件头在整个文件中的偏移</param>
/// <param name="Method">压缩方式，0 为存储，8 为 deflate</param>
/// <param name="Flags">通用标志位</param>
public sealed record ZipEntryInfo(string Name, uint Crc32, long CompressedSize, long Size, long LocalHeaderOffset, int Method, int Flags)
{

    public bool IsDirectory => Name.EndsWith('/');

    /// <summary>
    /// 第 0 位：加密
    /// </summary>
    public bool IsEncrypted => (Flags & 1) != 0;

}
