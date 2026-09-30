using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ZstdSharp;

namespace Starward.RPC.GameInstall;

#pragma warning disable CS3016 // 作为特性参数的数组不符合 CLS


/// <summary>
/// Snap.HPatch（Starward.NativeLib）的托管外壳的替代品，调用的是同一个原生的 Snap.HPatch.dll。
/// <para/>
/// NativeLib 里的外壳在读压缩段时，把「剩余长度」先转成 int 再比较。
/// 压缩段落在 2～4 GiB 之间时转出来是负数，读到 0 字节，解压失败，整个补丁就报失败；
/// 再大则被截成一个错误的长度。鸣潮 3.7.0 的两个差分包（3.97 GB 与 3.63 GB）因此打不上，
/// 只能改为下载完整的新文件，多下了约 30 GB。这里用 ulong 比较，其余行为与原外壳一致。
/// </summary>
public static unsafe class HPatchLarge
{

    /// <summary>
    /// 与 Snap.HPatch.HPatch.PatchZstandard(Stream?, Stream, Stream) 相同，压缩段可以超过 2 GiB。
    /// </summary>
    /// <param name="source">旧数据，没有旧数据时为 null</param>
    /// <param name="diff">差分数据（HDIFF13&amp;zstd）</param>
    /// <param name="target">新数据的输出，长度由差分数据里写明的新文件大小决定</param>
    /// <returns>是否成功</returns>
    public static bool PatchZstandard(Stream? source, Stream diff, Stream target)
    {
        using StreamInput sourceAdapter = new(source);
        using StreamInput diffAdapter = new(diff);
        ulong newDataSize;
        if (NewDataSize(&diffAdapter, &newDataSize) == 0)
        {
            return false;
        }
        using StreamOutput targetAdapter = new(target, newDataSize);
        Decompress decompressor = Decompress.CreateZstandard();
        return PatchWithDecompressor(&sourceAdapter, &diffAdapter, &targetAdapter, &decompressor) != 0;
    }



    [DllImport("Snap.HPatch", ExactSpelling = true)]
    private static extern int NewDataSize(StreamInput* diff, ulong* pSize);

    [DllImport("Snap.HPatch", ExactSpelling = true)]
    private static extern int PatchWithDecompressor(StreamInput* source, StreamInput* diff, StreamOutput* target, Decompress* decompressor);



    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int StreamRead(void* input, ulong position, byte* start, byte* end)
    {
        try
        {
            GCHandle gcHandle = GCHandle.FromIntPtr(((StreamInput*)input)->Handle);
            if (gcHandle.Target is not Stream stream)
            {
                return 0;
            }
            stream.Position = (long)position;
            stream.ReadExactly(new Span<byte>(start, (int)(end - start)));
            return 1;
        }
        catch
        {
            return 0;
        }
    }


    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int StreamWrite(void* output, ulong position, byte* start, byte* end)
    {
        try
        {
            GCHandle gcHandle = GCHandle.FromIntPtr(((StreamOutput*)output)->Handle);
            if (gcHandle.Target is not Stream stream)
            {
                return 0;
            }
            stream.Position = (long)position;
            stream.Write(new ReadOnlySpan<byte>(start, (int)(end - start)));
            return 1;
        }
        catch
        {
            return 0;
        }
    }



    private struct StreamInput : IDisposable
    {
        public nint Handle;
        public readonly ulong Length;
        public readonly delegate* unmanaged[Cdecl]<void*, ulong, byte*, byte*, int> Read;
        private readonly void* reserved;

        public StreamInput(Stream? stream)
        {
            if (stream is not null)
            {
                Handle = GCHandle.ToIntPtr(GCHandle.Alloc(stream));
                Length = (ulong)stream.Length;
            }
            Read = &StreamRead;
        }

        public void Dispose()
        {
            if (Handle is not 0)
            {
                GCHandle.FromIntPtr(Handle).Free();
                Handle = 0;
            }
        }
    }


    private struct StreamOutput : IDisposable
    {
        public nint Handle;
        public readonly ulong Length;
        public readonly delegate* unmanaged[Cdecl]<void*, ulong, byte*, byte*, int> Read;
        public readonly delegate* unmanaged[Cdecl]<void*, ulong, byte*, byte*, int> Write;

        public StreamOutput(Stream stream, ulong length)
        {
            Handle = GCHandle.ToIntPtr(GCHandle.Alloc(stream));
            Length = length;
            Read = &StreamRead;
            Write = &StreamWrite;
        }

        public void Dispose()
        {
            if (Handle is not 0)
            {
                GCHandle.FromIntPtr(Handle).Free();
                Handle = 0;
            }
        }
    }



    private struct Decompress
    {
        private readonly delegate* unmanaged[Cdecl]<byte*, int> isCanOpen;
        private readonly delegate* unmanaged[Cdecl]<Decompress*, ulong, StreamInput*, ulong, ulong, nint> open;
        private readonly delegate* unmanaged[Cdecl]<Decompress*, nint, int> close;
        private readonly delegate* unmanaged[Cdecl]<nint, byte*, byte*, int> decompress;
        private readonly delegate* unmanaged[Cdecl]<nint, ulong, StreamInput*, ulong, ulong, int> reset;
        private int error;

        private Decompress(
            delegate* unmanaged[Cdecl]<byte*, int> isCanOpen,
            delegate* unmanaged[Cdecl]<Decompress*, ulong, StreamInput*, ulong, ulong, nint> open,
            delegate* unmanaged[Cdecl]<Decompress*, nint, int> close,
            delegate* unmanaged[Cdecl]<nint, byte*, byte*, int> decompress)
        {
            this.isCanOpen = isCanOpen;
            this.open = open;
            this.close = close;
            this.decompress = decompress;
            reset = null;
            error = 0;
        }

        public static Decompress CreateZstandard()
        {
            return new(&ZstandardIsCanOpen, &ZstandardOpen, &ZstandardClose, &ZstandardDecompress);
        }
    }


    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ZstandardIsCanOpen(byte* compressType)
    {
        return MemoryMarshal.CreateReadOnlySpanFromNullTerminated(compressType).SequenceEqual("zstd"u8) ? 1 : 0;
    }


    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint ZstandardOpen(Decompress* decompressor, ulong dataSize, StreamInput* codeStream, ulong codeBegin, ulong codeEnd)
    {
        var stream = new DecompressionStream(new InputSliceStream(codeStream, codeBegin, codeEnd));
        return GCHandle.ToIntPtr(GCHandle.Alloc(stream));
    }


    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ZstandardClose(Decompress* decompressor, nint handle)
    {
        GCHandle gcHandle = GCHandle.FromIntPtr(handle);
        if (gcHandle.Target is DecompressionStream stream)
        {
            stream.Dispose();
            gcHandle.Free();
        }
        return 1;
    }


    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ZstandardDecompress(nint handle, byte* data, byte* dataEnd)
    {
        GCHandle gcHandle = GCHandle.FromIntPtr(handle);
        if (gcHandle.Target is not DecompressionStream stream)
        {
            return 0;
        }
        try
        {
            stream.ReadExactly(new Span<byte>(data, (int)(dataEnd - data)));
            return 1;
        }
        catch
        {
            return 0;
        }
    }



    /// <summary>
    /// 差分数据里某一压缩段的只读视图，读取走原生侧给的 read 回调
    /// </summary>
    private sealed class InputSliceStream : Stream
    {
        private readonly StreamInput* input;
        private readonly ulong begin;
        private readonly ulong end;
        private ulong position;

        public InputSliceStream(StreamInput* input, ulong begin, ulong end)
        {
            this.input = input;
            this.begin = begin;
            this.end = end;
            position = begin;
        }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => (long)(end - begin);

        public override long Position { get => (long)(position - begin); set => position = (ulong)value + begin; }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            // 剩余长度必须按 ulong 比较，转成 int 会在超过 2 GiB 时变成负数
            ulong remaining = position < end ? end - position : 0;
            int count = (ulong)buffer.Length > remaining ? (int)remaining : buffer.Length;
            if (count <= 0)
            {
                return 0;
            }
            fixed (byte* pBuffer = buffer)
            {
                if (input->Read(input, position, pBuffer, pBuffer + count) != 0)
                {
                    position += (ulong)count;
                    return count;
                }
            }
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            offset = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => Position + offset,
                SeekOrigin.End => Length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            Position = offset;
            return offset;
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

}
