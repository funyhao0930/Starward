using System.Text;

namespace Starward.Core.Launcher;

/// <summary>
/// HDiffPatch 的单流差分（<c>HDIFFSF20</c>）的托管实现，只支持不压缩的（类型名为空，头是 <c>HDIFFSF20&amp;\0</c>）。
/// <para/>
/// 终末地补丁里 VFS 资源的差分就是这种。Starward 带的原生 Snap.HPatch 只导出 HDIFF13 的接口，
/// 所以照 HDiffPatch <c>patch.c</c> 的 <c>_patch_single_stream_loop</c> 重写：
/// 差分数据由若干「步」组成，每步先给 cover 表与 rle0 数据的长度，再是这两段本身；
/// cover 之间的空隙是新数据，按顺序直接从差分流里抄；cover 覆盖的部分是旧数据逐字节加上 rle0 解出来的差值。
/// <para/>
/// 差分只顺序读一遍，新数据只顺序写一遍，旧数据按 cover 随机读，
/// 所以差分可以直接是 zip 条目的解密解压流，不必先落盘。
/// </summary>
public static class HDiffSingleStream
{

    private const string VersionType = "HDIFFSF20";

    /// <summary>
    /// 一步的缓冲上限。HDiffPatch 默认每步 256 KB，这里只用来挡住损坏的数据
    /// </summary>
    private const long MaxStepMemSize = 256L << 20;

    private const int CopyBufferSize = 1 << 16;



    /// <summary>
    /// 差分头里的信息
    /// </summary>
    public sealed record Header(string CompressType, long NewDataSize, long OldDataSize, long CoverCount, long StepMemSize, long UncompressedSize, long CompressedSize);



    /// <summary>
    /// 读差分头，读完后 <paramref name="diff"/> 停在差分数据的开头
    /// </summary>
    /// <exception cref="InvalidDataException">不是 HDIFFSF20</exception>
    public static Header ReadHeader(Stream diff)
    {
        string type = ReadTypeUntil(diff, (byte)'&');
        if (type != VersionType)
        {
            throw new InvalidDataException($"Not a single stream diff: {type}");
        }
        string compressType = ReadTypeUntil(diff, 0);
        var header = new Header(compressType,
                                ReadVarInt(diff),
                                ReadVarInt(diff),
                                ReadVarInt(diff),
                                ReadVarInt(diff),
                                ReadVarInt(diff),
                                ReadVarInt(diff));
        if (header.CompressedSize > header.UncompressedSize || header.StepMemSize > MaxStepMemSize)
        {
            throw new InvalidDataException("Invalid single stream diff header.");
        }
        return header;
    }



    /// <summary>
    /// 打补丁
    /// </summary>
    /// <param name="oldData">旧文件，要能随机读，长度必须与差分里写的一致</param>
    /// <param name="diff">差分，从头顺序读</param>
    /// <param name="newData">新文件的输出，顺序写</param>
    /// <exception cref="InvalidDataException">差分损坏或与旧文件对不上</exception>
    /// <exception cref="NotSupportedException">差分是压缩过的</exception>
    public static void Patch(Stream oldData, Stream diff, Stream newData, CancellationToken cancellationToken = default)
    {
        Header header = ReadHeader(diff);
        if (header.CompressType.Length > 0 || header.CompressedSize > 0)
        {
            throw new NotSupportedException($"Compressed single stream diff is not supported: {header.CompressType}");
        }
        if (oldData.Length != header.OldDataSize)
        {
            throw new InvalidDataException($"Old data size mismatch: {oldData.Length} != {header.OldDataSize}");
        }

        byte[] step = new byte[header.StepMemSize];
        byte[] buffer = new byte[CopyBufferSize];
        long coverCount = header.CoverCount;
        long written = 0;
        long oldPos = 0, newPos = 0, length = 0;
        while (coverCount > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long coverSize = ReadVarInt(diff);
            long rleSize = ReadVarInt(diff);
            if (coverSize + rleSize > step.Length)
            {
                throw new InvalidDataException("Step is larger than stepMemSize.");
            }
            diff.ReadExactly(step, 0, (int)(coverSize + rleSize));
            var covers = new SpanReader(0, (int)coverSize);
            var rle = new Rle0Decoder((int)coverSize, (int)(coverSize + rleSize));

            while (covers.Position < covers.End)
            {
                // 符号位在第一个字节的最高位：0 是相对上一段结尾往后，1 是往前
                bool backward = (step[covers.Position] >> 7) != 0;
                long lastOldEnd = oldPos + length;
                long lastNewEnd = newPos + length;
                long value = covers.ReadVarInt(step, 1);
                oldPos = backward ? lastOldEnd - value : lastOldEnd + value;
                newPos = covers.ReadVarInt(step, 0) + lastNewEnd;
                length = covers.ReadVarInt(step, 0);

                if (newPos > lastNewEnd)
                {
                    written += CopyExactly(diff, newData, newPos - lastNewEnd, buffer);
                }
                coverCount--;
                if (length > 0)
                {
                    if (oldPos < 0 || oldPos + length > oldData.Length)
                    {
                        throw new InvalidDataException("Cover is outside the old data.");
                    }
                    oldData.Position = oldPos;
                    long remaining = length;
                    while (remaining > 0)
                    {
                        int count = (int)Math.Min(remaining, buffer.Length);
                        oldData.ReadExactly(buffer, 0, count);
                        rle.Add(step, buffer.AsSpan(0, count));
                        newData.Write(buffer, 0, count);
                        remaining -= count;
                    }
                    written += length;
                }
                else if (coverCount != 0)
                {
                    // 长度为 0 的 cover 只会是最后那个，用来带出末尾的新数据
                    throw new InvalidDataException("Empty cover before the end.");
                }
            }
        }
        if (written != header.NewDataSize)
        {
            throw new InvalidDataException($"New data size mismatch: {written} != {header.NewDataSize}");
        }
    }



    private static long CopyExactly(Stream source, Stream destination, long count, byte[] buffer)
    {
        long remaining = count;
        while (remaining > 0)
        {
            int read = (int)Math.Min(remaining, buffer.Length);
            source.ReadExactly(buffer, 0, read);
            destination.Write(buffer, 0, read);
            remaining -= read;
        }
        return count;
    }


    private static string ReadTypeUntil(Stream stream, byte end)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 64; i++)
        {
            int b = stream.ReadByte();
            if (b < 0)
            {
                throw new EndOfStreamException();
            }
            if (b == end)
            {
                return sb.ToString();
            }
            sb.Append((char)b);
        }
        throw new InvalidDataException("Diff type is too long.");
    }


    /// <summary>
    /// HDiffPatch 的变长整数：高位在前，每字节 7 位，最高位为 1 表示还有下一个字节
    /// </summary>
    private static long ReadVarInt(Stream stream)
    {
        long value = 0;
        for (int i = 0; i < 10; i++)
        {
            int b = stream.ReadByte();
            if (b < 0)
            {
                throw new EndOfStreamException();
            }
            value = (value << 7) | (long)(b & 0x7F);
            if ((b & 0x80) == 0)
            {
                return value;
            }
        }
        throw new InvalidDataException("Variable-length integer is too long.");
    }


    /// <summary>
    /// 在一步的缓冲里顺序读变长整数
    /// </summary>
    private struct SpanReader(int position, int end)
    {
        public int Position = position;

        public readonly int End = end;

        /// <summary>
        /// 带标签位的变长整数：第一个字节最高的 <paramref name="tagBits"/> 位是标签，不算进数值
        /// </summary>
        public long ReadVarInt(byte[] data, int tagBits)
        {
            if (Position >= End)
            {
                throw new InvalidDataException("Unexpected end of covers.");
            }
            byte code = data[Position++];
            int valueBits = 7 - tagBits;
            long value = code & ((1 << valueBits) - 1);
            if ((code & (1 << valueBits)) != 0)
            {
                do
                {
                    if (Position >= End)
                    {
                        throw new InvalidDataException("Unexpected end of covers.");
                    }
                    if ((value >> (64 - 7)) != 0)
                    {
                        throw new InvalidDataException("Variable-length integer overflow.");
                    }
                    code = data[Position++];
                    value = (value << 7) | (long)(code & 0x7F);
                } while ((code & 0x80) != 0);
            }
            return value;
        }
    }


    /// <summary>
    /// rle0：交替给出「若干个 0」与「若干个字节」，后者逐字节加到旧数据上（按字节溢出）
    /// </summary>
    private sealed class Rle0Decoder(int position, int end)
    {
        private SpanReader _reader = new(position, end);

        private long _zeros;

        private long _values;

        private bool _nextIsZeros = true;

        public void Add(byte[] step, Span<byte> data)
        {
            int i = 0;
            while (i < data.Length)
            {
                if (_zeros > 0)
                {
                    int count = (int)Math.Min(_zeros, data.Length - i);
                    _zeros -= count;
                    i += count;
                    continue;
                }
                if (_values > 0)
                {
                    int count = (int)Math.Min(_values, data.Length - i);
                    for (int j = 0; j < count; j++)
                    {
                        data[i + j] += step[_reader.Position + j];
                    }
                    _reader.Position += count;
                    _values -= count;
                    i += count;
                    continue;
                }
                if (_nextIsZeros)
                {
                    _zeros = _reader.ReadVarInt(step, 0);
                }
                else
                {
                    _values = _reader.ReadVarInt(step, 0);
                    if (_values > _reader.End - _reader.Position)
                    {
                        throw new InvalidDataException("rle0 values are outside the step.");
                    }
                }
                _nextIsZeros = !_nextIsZeros;
            }
        }
    }

}
