using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Starward.Core.Launcher;

/// <summary>
/// 从一个可随机读的 zip（可以是多个分卷首尾相接的流）里读出单个条目的内容。
/// <para/>
/// 支持存储、deflate，以及 WinZip AES 加密（方法 99，扩展字段 0x9901）：
/// 终末地的补丁包就是 AE-2、AES-256、密码为接口给的 <c>cd_key</c> 的 zip。
/// </summary>
public static class ZipEntryReader
{

    private const uint LocalFileHeaderSignature = 0x04034b50;

    private const int MethodStored = 0;

    private const int MethodDeflate = 8;

    private const int MethodWinZipAes = 99;

    private const ushort WinZipAesExtraId = 0x9901;



    /// <summary>
    /// 打开条目，返回解密、解压后的内容。
    /// <para/>
    /// 返回的流按自己的位置去读 <paramref name="archive"/>，读之前会设好位置，
    /// 但同一个 <paramref name="archive"/> 不能同时给多个线程用。
    /// 加密条目读到末尾时会校验 HMAC，对不上抛出 <see cref="InvalidDataException"/>。
    /// </summary>
    /// <param name="archive">整个 zip，要能随机读</param>
    /// <param name="entry">中央目录里的条目</param>
    /// <param name="password">加密条目的密码，未加密的条目不用</param>
    /// <param name="leaveOpen">关闭返回的流时是否保留 <paramref name="archive"/></param>
    public static Stream Open(Stream archive, ZipEntryInfo entry, string? password = null, bool leaveOpen = true)
    {
        byte[] header = new byte[30];
        archive.Position = entry.LocalHeaderOffset;
        archive.ReadExactly(header);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != LocalFileHeaderSignature)
        {
            throw new InvalidDataException($"Invalid local file header: {entry.Name}");
        }
        int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(26));
        int extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28));
        archive.Position = entry.LocalHeaderOffset + 30 + nameLength;
        byte[] extra = new byte[extraLength];
        archive.ReadExactly(extra);
        long dataStart = entry.LocalHeaderOffset + 30 + nameLength + extraLength;

        int method = entry.Method;
        Stream data = new SubReadStream(archive, dataStart, entry.CompressedSize, leaveOpen);
        if (method == MethodWinZipAes)
        {
            if (password is null)
            {
                throw new InvalidOperationException($"Password is required: {entry.Name}");
            }
            (int strength, int actualMethod) = ReadAesExtra(extra) ?? throw new InvalidDataException($"WinZip AES extra field not found: {entry.Name}");
            data = WinZipAesStream.Create(data, password, strength, entry.CompressedSize);
            method = actualMethod;
        }
        else if (entry.IsEncrypted)
        {
            throw new NotSupportedException($"Unsupported zip encryption: {entry.Name}");
        }
        return method switch
        {
            MethodStored => data,
            MethodDeflate => new DeflateStream(data, CompressionMode.Decompress, false),
            _ => throw new NotSupportedException($"Unsupported zip method {method}: {entry.Name}"),
        };
    }



    /// <summary>
    /// 0x9901：版本(2) 厂商 "AE"(2) 强度(1) 实际压缩方法(2)
    /// </summary>
    private static (int Strength, int Method)? ReadAesExtra(ReadOnlySpan<byte> extra)
    {
        int pos = 0;
        while (pos + 4 <= extra.Length)
        {
            ushort id = BinaryPrimitives.ReadUInt16LittleEndian(extra[pos..]);
            int length = BinaryPrimitives.ReadUInt16LittleEndian(extra[(pos + 2)..]);
            if (id == WinZipAesExtraId && length >= 7 && pos + 4 + length <= extra.Length)
            {
                ReadOnlySpan<byte> data = extra.Slice(pos + 4, length);
                return (data[4], BinaryPrimitives.ReadUInt16LittleEndian(data[5..]));
            }
            pos += 4 + length;
        }
        return null;
    }



    /// <summary>
    /// 底层流的一段，自己记位置，每次读之前设好底层流的位置
    /// </summary>
    private sealed class SubReadStream(Stream inner, long start, long length, bool leaveOpen) : Stream
    {
        private long _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            long remaining = length - _position;
            if (remaining <= 0)
            {
                return 0;
            }
            if (buffer.Length > remaining)
            {
                buffer = buffer[..(int)remaining];
            }
            inner.Position = start + _position;
            int read = inner.Read(buffer);
            _position += read;
            return read;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !leaveOpen)
            {
                inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }

}



/// <summary>
/// WinZip AES（AE-1 / AE-2）的解密流。
/// <para/>
/// 数据布局：盐（密钥长度的一半）、2 字节密码校验值、密文、10 字节 HMAC-SHA1。
/// 密钥由 PBKDF2-HMAC-SHA1（1000 次）导出：AES 密钥、HMAC 密钥、校验值依次排列。
/// 加密是 AES-CTR，计数器从 1 开始、按小端序递增。HMAC 算的是密文。
/// </summary>
public sealed class WinZipAesStream : Stream
{

    private const int MacLength = 10;

    private const int PasswordVerifierLength = 2;

    private const int Iterations = 1000;


    private readonly Stream _inner;

    private readonly Aes _aes;

    private readonly IncrementalHash _hmac;

    private readonly long _cipherLength;

    private long _position;

    private ulong _counter = 1;

    private readonly byte[] _counterBlock = new byte[16];

    private readonly byte[] _keyStream = new byte[16];

    private int _keyStreamUsed = 16;

    private bool _verified;



    private WinZipAesStream(Stream inner, byte[] aesKey, byte[] macKey, long cipherLength)
    {
        _inner = inner;
        _aes = Aes.Create();
        _aes.Key = aesKey;
        _hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, macKey);
        _cipherLength = cipherLength;
    }



    /// <summary>
    /// 读掉盐与校验值，导出密钥
    /// </summary>
    /// <param name="inner">条目的原始数据，从盐开始</param>
    /// <param name="strength">1 = AES-128，2 = AES-192，3 = AES-256</param>
    /// <param name="compressedSize">条目的原始数据总长，含盐、校验值与 HMAC</param>
    /// <exception cref="InvalidDataException">密码不对</exception>
    public static WinZipAesStream Create(Stream inner, string password, int strength, long compressedSize)
    {
        int keyLength = strength switch
        {
            1 => 16,
            2 => 24,
            3 => 32,
            _ => throw new NotSupportedException($"Unsupported WinZip AES strength: {strength}"),
        };
        int saltLength = keyLength / 2;
        long cipherLength = compressedSize - saltLength - PasswordVerifierLength - MacLength;
        if (cipherLength < 0)
        {
            throw new InvalidDataException("WinZip AES entry is too short.");
        }
        byte[] salt = new byte[saltLength];
        inner.ReadExactly(salt);
        byte[] verifier = new byte[PasswordVerifierLength];
        inner.ReadExactly(verifier);
        byte[] derived = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA1, keyLength * 2 + PasswordVerifierLength);
        if (!derived.AsSpan(keyLength * 2, PasswordVerifierLength).SequenceEqual(verifier))
        {
            throw new InvalidDataException("Wrong password for WinZip AES entry.");
        }
        return new WinZipAesStream(inner, derived[..keyLength], derived[keyLength..(keyLength * 2)], cipherLength);
    }



    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _cipherLength;
    public override long Position { get => _position; set => throw new NotSupportedException(); }


    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));


    public override int Read(Span<byte> buffer)
    {
        long remaining = _cipherLength - _position;
        if (remaining <= 0)
        {
            VerifyMac();
            return 0;
        }
        if (buffer.Length > remaining)
        {
            buffer = buffer[..(int)remaining];
        }
        int read = _inner.Read(buffer);
        if (read == 0)
        {
            throw new EndOfStreamException("WinZip AES entry is truncated.");
        }
        Span<byte> data = buffer[..read];
        _hmac.AppendData(data);
        for (int i = 0; i < data.Length; i++)
        {
            if (_keyStreamUsed == 16)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(_counterBlock, _counter++);
                _aes.EncryptEcb(_counterBlock, _keyStream, PaddingMode.None);
                _keyStreamUsed = 0;
            }
            data[i] ^= _keyStream[_keyStreamUsed++];
        }
        _position += read;
        if (_position == _cipherLength)
        {
            VerifyMac();
        }
        return read;
    }


    private void VerifyMac()
    {
        if (_verified)
        {
            return;
        }
        _verified = true;
        Span<byte> expected = stackalloc byte[MacLength];
        _inner.ReadExactly(expected);
        Span<byte> actual = stackalloc byte[20];
        _hmac.GetHashAndReset(actual);
        if (!actual[..MacLength].SequenceEqual(expected))
        {
            throw new InvalidDataException("WinZip AES authentication code mismatch.");
        }
    }


    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();


    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _aes.Dispose();
            _hmac.Dispose();
            _inner.Dispose();
        }
        base.Dispose(disposing);
    }

}
