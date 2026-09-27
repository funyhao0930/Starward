using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Starward.RPC.GameInstall;

/// <summary>
/// 把多个文件首尾相接当成一个可随机读写的流。
/// <para/>
/// 目录差分要这样用：旧文件接起来当成一个旧文件读，新文件接起来当成一个新文件写。
/// 与 <see cref="FileCombinedStream"/> 不同的是它能写、每次读写都会跨过文件边界读满，
/// 长度为 0 的文件也不会让读取提前结束。
/// <para/>
/// 它会被 hpatch 的原生回调调用，那里抛出异常会让整个进程崩溃，
/// 因此所有越界都由调用方事先保证，这里不做可能抛出的检查以外的事。
/// </summary>
internal sealed class MultiFileStream : Stream
{

    private readonly FileStream[] _streams;

    /// <summary>
    /// 每个文件在整个流里的起点，多一项作为终点
    /// </summary>
    private readonly long[] _offsets;

    private readonly bool _writable;

    private long _position;


    private MultiFileStream(FileStream[] streams, long[] lengths, bool writable)
    {
        _streams = streams;
        _writable = writable;
        _offsets = new long[lengths.Length + 1];
        for (int i = 0; i < lengths.Length; i++)
        {
            _offsets[i + 1] = _offsets[i] + lengths[i];
        }
    }


    /// <summary>
    /// 打开已有的文件只读
    /// </summary>
    public static MultiFileStream OpenRead(IReadOnlyList<string> paths)
    {
        var streams = new List<FileStream>(paths.Count);
        try
        {
            foreach (string path in paths)
            {
                streams.Add(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 1 << 16, FileOptions.RandomAccess));
            }
            return new MultiFileStream(streams.ToArray(), streams.Select(x => x.Length).ToArray(), false);
        }
        catch
        {
            foreach (FileStream fs in streams)
            {
                fs.Dispose();
            }
            throw;
        }
    }


    /// <summary>
    /// 新建（覆盖）文件并预先定好长度，之后可在任意位置读写
    /// </summary>
    public static MultiFileStream Create(IReadOnlyList<string> paths, IReadOnlyList<long> lengths)
    {
        if (paths.Count != lengths.Count)
        {
            throw new ArgumentException("Paths and lengths must have the same count.");
        }
        var streams = new List<FileStream>(paths.Count);
        try
        {
            for (int i = 0; i < paths.Count; i++)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(paths[i])!);
                var fs = new FileStream(paths[i], FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 1 << 16);
                streams.Add(fs);
                fs.SetLength(lengths[i]);
            }
            return new MultiFileStream(streams.ToArray(), lengths.ToArray(), true);
        }
        catch
        {
            foreach (FileStream fs in streams)
            {
                fs.Dispose();
            }
            throw;
        }
    }



    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => _writable;

    public override long Length => _offsets[^1];

    public override long Position
    {
        get => _position;
        set => _position = value;
    }


    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));


    public override int Read(Span<byte> buffer)
    {
        int total = 0;
        while (buffer.Length > 0 && _position < Length)
        {
            int index = FindStream(_position);
            FileStream fs = _streams[index];
            long inFile = _position - _offsets[index];
            int length = (int)Math.Min(buffer.Length, _offsets[index + 1] - _position);
            fs.Position = inFile;
            int read = fs.Read(buffer[..length]);
            if (read <= 0)
            {
                break;
            }
            total += read;
            _position += read;
            buffer = buffer[read..];
        }
        return total;
    }


    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));


    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (!_writable)
        {
            throw new NotSupportedException();
        }
        while (buffer.Length > 0 && _position < Length)
        {
            int index = FindStream(_position);
            FileStream fs = _streams[index];
            long inFile = _position - _offsets[index];
            int length = (int)Math.Min(buffer.Length, _offsets[index + 1] - _position);
            fs.Position = inFile;
            fs.Write(buffer[..length]);
            _position += length;
            buffer = buffer[length..];
        }
    }


    public override long Seek(long offset, SeekOrigin origin)
    {
        _position = origin switch
        {
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => Length + offset,
            _ => offset,
        };
        return _position;
    }


    public override void Flush()
    {
        foreach (FileStream fs in _streams)
        {
            fs.Flush();
        }
    }


    public override void SetLength(long value) => throw new NotSupportedException();


    /// <summary>
    /// 找出包含 <paramref name="position"/> 的文件。长度为 0 的文件不包含任何位置，会被跳过。
    /// </summary>
    private int FindStream(long position)
    {
        // 二分查找最后一个起点 <= position 且非空的文件
        int lo = 0, hi = _streams.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_offsets[mid] <= position)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }
        // 起点相同的空文件排在前面，二分会落在最后一个，正是非空的那个
        return lo;
    }


    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (FileStream fs in _streams)
            {
                fs.Dispose();
            }
        }
        base.Dispose(disposing);
    }

}
