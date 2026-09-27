using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Starward.Core.Launcher.Kuro;

/// <summary>
/// HDiffPatch 目录差分（<c>HDIFF19</c>）的文件头，鸣潮的 <c>.krpdiff</c> 就是这种格式。
/// <para/>
/// 目录差分把所有「被引用的旧文件」首尾相接当成一个虚拟的旧文件，
/// 把所有「由差分生成的新文件」首尾相接当成一个虚拟的新文件，
/// 中间是一份普通的单文件差分（<c>HDIFF13</c>），从 <see cref="HDiffDataOffset"/> 开始到文件末尾。
/// 这里只解析文件头与路径清单，真正打补丁交给单文件的 hpatch。
/// <para/>
/// 库洛用的是改过的 hpatchz（官方启动器目录里的 hpatchz.exe，版本号仍写 v4.8.0），
/// 在上游格式之外多写了两段清单：<see cref="OldRefSizes"/> 插在 newRefSizeList 前面，
/// newRefHashList（每个新引用文件一个 64 位值）接在 newRefSizeList 后面、相同文件配对之前。
/// 这个顺序是拿自己生成的差分包喂给那支 hpatchz 试出来的：它不收上游格式，
/// 报错信息里直接写出了 <c>newRefHashList</c> 这个字段名与它的项数（newRefFileCount）；
/// 把哈希清单放在相同文件配对之后，有相同文件时它也不收。
/// <see cref="Parse"/> 两种格式都认，并用「各清单总和必须等于文件头记载的总大小、
/// 读完不能有剩余」来判断是哪一种，都对不上就当作不支持，由调用方退回下载完整文件。
/// </summary>
public sealed class HDiffDirDiffHeader
{

    public const string VersionType = "HDIFF19";


    public string CompressType { get; private init; } = "";

    public string ChecksumType { get; private init; } = "";

    public bool OldPathIsDir { get; private init; }

    public bool NewPathIsDir { get; private init; }


    /// <summary>
    /// 旧目录的所有路径，目录以 <c>/</c> 结尾，第一项通常是空字符串（根目录本身）
    /// </summary>
    public IReadOnlyList<string> OldPaths { get; private init; } = [];

    /// <summary>
    /// 新目录的所有路径，规则同 <see cref="OldPaths"/>
    /// </summary>
    public IReadOnlyList<string> NewPaths { get; private init; } = [];


    /// <summary>
    /// 被差分引用的旧文件，是 <see cref="OldPaths"/> 的下标，按顺序首尾相接
    /// </summary>
    public IReadOnlyList<int> OldRefIndexes { get; private init; } = [];

    /// <summary>
    /// 由差分生成的新文件，是 <see cref="NewPaths"/> 的下标，按顺序首尾相接
    /// </summary>
    public IReadOnlyList<int> NewRefIndexes { get; private init; } = [];

    /// <summary>
    /// 每个旧引用文件的大小。上游格式没有这一段，这时为 null。
    /// </summary>
    public IReadOnlyList<long>? OldRefSizes { get; private init; }

    /// <summary>
    /// 每个新引用文件的大小，用来把差分输出切回一个个文件
    /// </summary>
    public IReadOnlyList<long> NewRefSizes { get; private init; } = [];

    /// <summary>
    /// 新旧内容完全相同、直接复制的文件，(新路径下标, 旧路径下标)
    /// </summary>
    public IReadOnlyList<(int NewIndex, int OldIndex)> SameFilePairs { get; private init; } = [];

    /// <summary>
    /// 需要可执行权限的新文件，Windows 上用不到
    /// </summary>
    public IReadOnlyList<int> NewExecuteIndexes { get; private init; } = [];


    /// <summary>
    /// 旧引用文件的总大小
    /// </summary>
    public long OldRefSize { get; private init; }

    /// <summary>
    /// 新引用文件的总大小
    /// </summary>
    public long NewRefSize { get; private init; }


    /// <summary>
    /// 内嵌的单文件差分在整个文件中的偏移
    /// </summary>
    public long HDiffDataOffset { get; private init; }

    /// <summary>
    /// 内嵌的单文件差分的长度
    /// </summary>
    public long HDiffDataSize { get; private init; }



    /// <summary>
    /// 被引用的旧文件的相对路径，按首尾相接的顺序
    /// </summary>
    public IReadOnlyList<string> GetOldRefPaths() => OldRefIndexes.Select(i => OldPaths[i]).ToList().AsReadOnly();


    /// <summary>
    /// 由差分生成的新文件的相对路径与大小，按首尾相接的顺序
    /// </summary>
    public IReadOnlyList<(string Path, long Size)> GetNewRefFiles() => NewRefIndexes.Select((index, i) => (NewPaths[index], NewRefSizes[i])).ToList().AsReadOnly();


    /// <summary>
    /// 直接从旧文件复制过去的新文件，(新路径, 旧路径)
    /// </summary>
    public IReadOnlyList<(string NewPath, string OldPath)> GetSameFileCopies() => SameFilePairs.Select(x => (NewPaths[x.NewIndex], OldPaths[x.OldIndex])).ToList().AsReadOnly();


    /// <summary>
    /// 新目录里既不是差分生成、也不是复制来的文件，只能是空文件。
    /// 目录（以 <c>/</c> 结尾，包括根目录的空字符串）不算在内。
    /// </summary>
    public IReadOnlyList<string> GetNewEmptyFiles()
    {
        var used = new HashSet<int>(NewRefIndexes);
        used.UnionWith(SameFilePairs.Select(x => x.NewIndex));
        var list = new List<string>();
        for (int i = 0; i < NewPaths.Count; i++)
        {
            string path = NewPaths[i];
            if (!used.Contains(i) && !IsDirectory(path))
            {
                list.Add(path);
            }
        }
        return list.AsReadOnly();
    }


    /// <summary>
    /// 新目录里的子目录，不含根目录
    /// </summary>
    public IReadOnlyList<string> GetNewDirectories() => NewPaths.Where(x => x.Length > 0 && IsDirectory(x)).ToList().AsReadOnly();


    /// <summary>
    /// 路径清单里目录以分隔符结尾，根目录本身是空字符串
    /// </summary>
    public static bool IsDirectory(string path) => path.Length == 0 || path[^1] is '/' or '\\';



    /// <summary>
    /// 解析目录差分的文件头
    /// </summary>
    /// <param name="stream">可随机读取的差分文件</param>
    /// <param name="decompress">
    /// 路径清单是压缩的（目前只见过 zstd），传入 (压缩数据, 解压后长度) → 解压数据。
    /// 清单未压缩时不会调用。
    /// </param>
    /// <exception cref="InvalidDataException">不是目录差分，或清单对不上</exception>
    /// <exception cref="NotSupportedException">清单的排法认不出来</exception>
    public static HDiffDirDiffHeader Parse(Stream stream, Func<byte[], int, byte[]> decompress)
    {
        stream.Position = 0;
        var reader = new HeadReader(stream);

        string type = reader.ReadTypeString((byte)'&');
        if (type != VersionType)
        {
            throw new InvalidDataException($"Not a directory diff: {type}");
        }
        string compressType = reader.ReadTypeString((byte)'&');
        string checksumType = reader.ReadTypeString(0);

        bool oldPathIsDir = reader.ReadUInt() != 0;
        bool newPathIsDir = reader.ReadUInt() != 0;
        int oldPathCount = checked((int)reader.ReadUInt());
        int oldPathSumSize = checked((int)reader.ReadUInt());
        int newPathCount = checked((int)reader.ReadUInt());
        int newPathSumSize = checked((int)reader.ReadUInt());
        int oldRefFileCount = checked((int)reader.ReadUInt());
        long oldRefSize = checked((long)reader.ReadUInt());
        int newRefFileCount = checked((int)reader.ReadUInt());
        long newRefSize = checked((long)reader.ReadUInt());
        int sameFilePairCount = checked((int)reader.ReadUInt());
        _ = reader.ReadUInt(); // sameFileSize
        int newExecuteCount = checked((int)reader.ReadUInt());
        int privateReservedDataSize = checked((int)reader.ReadUInt());
        long privateExternDataSize = checked((long)reader.ReadUInt());
        long externDataSize = checked((long)reader.ReadUInt());
        int headDataSize = checked((int)reader.ReadUInt());
        int headDataCompressedSize = checked((int)reader.ReadUInt());
        int checksumByteSize = checked((int)reader.ReadUInt());
        // 四段校验值：旧引用、新引用、复制的文件、差分数据
        reader.Skip(checksumByteSize * 4L);

        long headDataOffset = reader.Position;
        byte[] headData;
        if (headDataCompressedSize > 0)
        {
            byte[] compressed = reader.ReadBytes(headDataCompressedSize);
            headData = decompress(compressed, headDataSize);
            if (headData.Length != headDataSize)
            {
                throw new InvalidDataException($"Head data size mismatch: {headData.Length} != {headDataSize}");
            }
        }
        else
        {
            headData = reader.ReadBytes(headDataSize);
        }
        long hdiffDataOffset = headDataOffset + (headDataCompressedSize > 0 ? headDataCompressedSize : headDataSize)
                             + privateExternDataSize + externDataSize;
        if (hdiffDataOffset > stream.Length)
        {
            throw new InvalidDataException("Diff data offset is beyond the end of file.");
        }

        // 路径清单：旧路径与新路径依次排列，每个以 \0 结尾
        int pathSumSize = oldPathSumSize + newPathSumSize;
        if (pathSumSize > headData.Length)
        {
            throw new InvalidDataException("Path list is larger than head data.");
        }
        List<string> paths = SplitPaths(headData.AsSpan(0, pathSumSize));
        if (paths.Count != oldPathCount + newPathCount)
        {
            throw new InvalidDataException($"Path count mismatch: {paths.Count} != {oldPathCount + newPathCount}");
        }
        List<string> oldPaths = paths.GetRange(0, oldPathCount);
        List<string> newPaths = paths.GetRange(oldPathCount, newPathCount);

        var counts = new ListCounts(oldPathCount, newPathCount, oldRefFileCount, newRefFileCount, sameFilePairCount, newExecuteCount);
        ReadOnlySpan<byte> lists = headData.AsSpan(pathSumSize);

        foreach (ListLayout layout in Layouts)
        {
            if (TryReadLists(lists, counts, layout, privateReservedDataSize, out ParsedLists? parsed)
                && parsed.NewRefSizes.Sum() == newRefSize
                && (parsed.OldRefSizes is null || parsed.OldRefSizes.Sum() == oldRefSize))
            {
                return new HDiffDirDiffHeader
                {
                    CompressType = compressType,
                    ChecksumType = checksumType,
                    OldPathIsDir = oldPathIsDir,
                    NewPathIsDir = newPathIsDir,
                    OldPaths = oldPaths,
                    NewPaths = newPaths,
                    OldRefIndexes = parsed.OldRefIndexes,
                    NewRefIndexes = parsed.NewRefIndexes,
                    OldRefSizes = parsed.OldRefSizes,
                    NewRefSizes = parsed.NewRefSizes,
                    SameFilePairs = parsed.SameFilePairs,
                    NewExecuteIndexes = parsed.NewExecuteIndexes,
                    OldRefSize = oldRefSize,
                    NewRefSize = newRefSize,
                    HDiffDataOffset = hdiffDataOffset,
                    HDiffDataSize = stream.Length - hdiffDataOffset,
                };
            }
        }
        throw new NotSupportedException("Unrecognized directory diff list layout.");
    }



    /// <summary>
    /// 清单的排法
    /// </summary>
    private enum ListLayout
    {
        /// <summary>
        /// 上游 HDiffPatch：oldRef、newRef、newRefSize、samePair、newExecute
        /// </summary>
        Upstream,

        /// <summary>
        /// 库洛：oldRef、newRef、oldRefSize、newRefSize、newRefHash、samePair、newExecute
        /// </summary>
        Kuro,
    }


    private static readonly ListLayout[] Layouts = [ListLayout.Kuro, ListLayout.Upstream];


    private sealed record ListCounts(int OldPathCount, int NewPathCount, int OldRefCount, int NewRefCount, int SamePairCount, int ExecuteCount);


    private sealed record ParsedLists(List<int> OldRefIndexes, List<int> NewRefIndexes, List<long>? OldRefSizes, List<long> NewRefSizes,
                                      List<(int, int)> SameFilePairs, List<int> NewExecuteIndexes);


    private static bool TryReadLists(ReadOnlySpan<byte> data, ListCounts counts, ListLayout layout, int reservedSize, [NotNullWhen(true)] out ParsedLists? result)
    {
        result = null;
        int pos = 0;
        try
        {
            List<int> oldRefs = ReadIncList(data, ref pos, counts.OldRefCount, counts.OldPathCount);
            List<int> newRefs = ReadIncList(data, ref pos, counts.NewRefCount, counts.NewPathCount);
            List<long>? oldRefSizes = null;
            if (layout is ListLayout.Kuro)
            {
                oldRefSizes = ReadList(data, ref pos, counts.OldRefCount);
            }
            List<long> newRefSizes = ReadList(data, ref pos, counts.NewRefCount);
            if (layout is ListLayout.Kuro)
            {
                // newRefHashList，打补丁用不到
                _ = ReadList(data, ref pos, counts.NewRefCount);
            }
            List<(int, int)> samePairs = ReadSamePairList(data, ref pos, counts.SamePairCount, counts.NewPathCount, counts.OldPathCount);
            List<int> executes = ReadIncList(data, ref pos, counts.ExecuteCount, counts.NewPathCount);
            if (data.Length - pos != reservedSize)
            {
                return false;
            }
            result = new ParsedLists(oldRefs, newRefs, oldRefSizes, newRefSizes, samePairs, executes);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }



    private static List<string> SplitPaths(ReadOnlySpan<byte> data)
    {
        var list = new List<string>();
        if (data.Length == 0)
        {
            return list;
        }
        if (data[^1] != 0)
        {
            throw new InvalidDataException("Path list does not end with \\0.");
        }
        int start = 0;
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] == 0)
            {
                list.Add(Encoding.UTF8.GetString(data[start..i]));
                start = i + 1;
            }
        }
        return list;
    }



    /// <summary>
    /// 递增清单：每一项存的是与上一项的差值减一，第一项相对于 -1
    /// </summary>
    private static List<int> ReadIncList(ReadOnlySpan<byte> data, ref int pos, int count, int endValue)
    {
        var list = new List<int>(count);
        long back = -1;
        for (int i = 0; i < count; i++)
        {
            ulong inc = UnpackUInt(data, ref pos, 0);
            back += 1 + (long)inc;
            if (back >= endValue)
            {
                throw new InvalidDataException("Index out of range.");
            }
            list.Add((int)back);
        }
        return list;
    }


    private static List<long> ReadList(ReadOnlySpan<byte> data, ref int pos, int count)
    {
        var list = new List<long>(count);
        for (int i = 0; i < count; i++)
        {
            ulong value = UnpackUInt(data, ref pos, 0);
            list.Add(unchecked((long)value));
        }
        return list;
    }


    /// <summary>
    /// 相同文件配对：新下标是递增清单，旧下标带一个符号位，可以往回跳
    /// </summary>
    private static List<(int, int)> ReadSamePairList(ReadOnlySpan<byte> data, ref int pos, int count, int newEnd, int oldEnd)
    {
        var list = new List<(int, int)>(count);
        long backNew = -1, backOld = -1;
        for (int i = 0; i < count; i++)
        {
            ulong incNew = UnpackUInt(data, ref pos, 0);
            backNew += 1 + (long)incNew;
            if (backNew >= newEnd)
            {
                throw new InvalidDataException("New index out of range.");
            }
            if (pos >= data.Length)
            {
                throw new InvalidDataException("Unexpected end of same pair list.");
            }
            bool negative = (data[pos] >> 7) != 0;
            ulong incOld = UnpackUInt(data, ref pos, 1);
            backOld = negative ? backOld + 1 - (long)incOld : backOld + 1 + (long)incOld;
            if (backOld < 0 || backOld >= oldEnd)
            {
                throw new InvalidDataException("Old index out of range.");
            }
            list.Add(((int)backNew, (int)backOld));
        }
        return list;
    }


    /// <summary>
    /// HDiffPatch 的变长整数：大端 7 位一组，最高位表示后面还有；
    /// 第一个字节的高 <paramref name="tagBits"/> 位另作他用。
    /// </summary>
    internal static ulong UnpackUInt(ReadOnlySpan<byte> data, ref int pos, int tagBits)
    {
        if (pos >= data.Length)
        {
            throw new InvalidDataException("Unexpected end of data.");
        }
        byte code = data[pos++];
        ulong value = (ulong)(code & ((1 << (7 - tagBits)) - 1));
        if ((code & (1 << (7 - tagBits))) != 0)
        {
            do
            {
                if ((value >> (64 - 7)) != 0 || pos >= data.Length)
                {
                    throw new InvalidDataException("Invalid packed integer.");
                }
                code = data[pos++];
                value = (value << 7) | (uint)(code & 0x7F);
            } while ((code & 0x80) != 0);
        }
        return value;
    }



    /// <summary>
    /// 按字节读文件头。文件头前半段很短，逐字节读就够了。
    /// </summary>
    private sealed class HeadReader(Stream stream)
    {

        public long Position => stream.Position;

        public byte ReadByte()
        {
            int b = stream.ReadByte();
            if (b < 0)
            {
                throw new InvalidDataException("Unexpected end of file.");
            }
            return (byte)b;
        }

        public string ReadTypeString(byte end)
        {
            var bytes = new List<byte>(16);
            while (true)
            {
                byte b = ReadByte();
                if (b == end)
                {
                    break;
                }
                bytes.Add(b);
                if (bytes.Count > 64)
                {
                    throw new InvalidDataException("Type string is too long.");
                }
            }
            return Encoding.ASCII.GetString(bytes.ToArray());
        }

        public ulong ReadUInt()
        {
            Span<byte> buffer = stackalloc byte[10];
            int length = 0;
            do
            {
                buffer[length] = ReadByte();
                length++;
            } while ((buffer[length - 1] & 0x80) != 0 && length < buffer.Length);
            int pos = 0;
            return UnpackUInt(buffer[..length], ref pos, 0);
        }

        public void Skip(long count)
        {
            stream.Seek(count, SeekOrigin.Current);
        }

        public byte[] ReadBytes(int count)
        {
            byte[] bytes = new byte[count];
            stream.ReadExactly(bytes);
            return bytes;
        }

    }

}
