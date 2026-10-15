using Starward.Core.Launcher;
using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Starward.Core.Tests.Launcher;

/// <summary>
/// 从 zip 读出单个条目：存储、deflate、WinZip AES。
/// <para/>
/// AES 条目由下面照 WinZip 的规格自己加密产生（PBKDF2-HMAC-SHA1 1000 次、AES-CTR 小端计数器从 1 开始、
/// 密文的 HMAC-SHA1 取前 10 字节），与终末地补丁包的布局一致：AE-2、强度 3、扩展字段 0x9901。
/// </summary>
public class ZipEntryReaderTests
{

    private const string Password = "T3stKeyForUnitTestsOnly000000000";


    /// <summary>
    /// 写一个本地文件头加数据，返回对应的中央目录条目。前面可以有别的数据，测偏移
    /// </summary>
    private static ZipEntryInfo WriteEntry(Stream zip, string name, byte[] content, bool deflate, string? password, int strength = 3)
    {
        byte[] data = content;
        if (deflate)
        {
            var ms = new MemoryStream();
            using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, true))
            {
                ds.Write(content);
            }
            data = ms.ToArray();
        }
        int method = deflate ? 8 : 0;
        byte[] extra = [];
        int flags = 0;
        if (password is not null)
        {
            data = Encrypt(data, password, strength);
            extra = new byte[11];
            BinaryPrimitives.WriteUInt16LittleEndian(extra, 0x9901);
            BinaryPrimitives.WriteUInt16LittleEndian(extra.AsSpan(2), 7);
            BinaryPrimitives.WriteUInt16LittleEndian(extra.AsSpan(4), 2);
            extra[6] = (byte)'A';
            extra[7] = (byte)'E';
            extra[8] = (byte)strength;
            BinaryPrimitives.WriteUInt16LittleEndian(extra.AsSpan(9), (ushort)method);
            method = 99;
            flags = 1;
        }
        long offset = zip.Position;
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        byte[] header = new byte[30];
        BinaryPrimitives.WriteUInt32LittleEndian(header, 0x04034b50);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), (ushort)flags);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), (ushort)method);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(18), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(22), (uint)content.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(26), (ushort)nameBytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(28), (ushort)extra.Length);
        zip.Write(header);
        zip.Write(nameBytes);
        zip.Write(extra);
        zip.Write(data);
        return new ZipEntryInfo(name, 0, data.Length, content.Length, offset, method, flags);
    }


    private static byte[] Encrypt(byte[] plain, string password, int strength)
    {
        int keyLength = strength switch { 1 => 16, 2 => 24, _ => 32 };
        byte[] salt = RandomNumberGenerator.GetBytes(keyLength / 2);
        byte[] derived = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 1000, HashAlgorithmName.SHA1, keyLength * 2 + 2);
        using Aes aes = Aes.Create();
        aes.Key = derived[..keyLength];
        byte[] cipher = new byte[plain.Length];
        byte[] counter = new byte[16];
        byte[] block = new byte[16];
        for (int i = 0; i < plain.Length; i += 16)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(counter, (ulong)(i / 16 + 1));
            aes.EncryptEcb(counter, block, PaddingMode.None);
            for (int j = 0; j < 16 && i + j < plain.Length; j++)
            {
                cipher[i + j] = (byte)(plain[i + j] ^ block[j]);
            }
        }
        byte[] mac = HMACSHA1.HashData(derived[keyLength..(keyLength * 2)], cipher)[..10];
        return [.. salt, .. derived[(keyLength * 2)..], .. cipher, .. mac];
    }


    private static byte[] ReadAll(Stream archive, ZipEntryInfo entry, string? password)
    {
        using Stream stream = ZipEntryReader.Open(archive, entry, password);
        var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }


    private static byte[] Content(int length, int seed)
    {
        // 前半可压缩、后半随机，deflate 与跨 AES 块边界都测到
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes.AsSpan(length / 2));
        return bytes;
    }



    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    [InlineData(true, 1)]
    public void Open_DecryptsWinZipAesEntries(bool deflate, int strength)
    {
        byte[] content = Content(100_003, 1);
        var zip = new MemoryStream();
        zip.Write(new byte[1234]);
        ZipEntryInfo entry = WriteEntry(zip, "vfs_files/vfs_patch/diff/a.chk_patch", content, deflate, Password, strength);

        Assert.Equal(content, ReadAll(zip, entry, Password));
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Open_ReadsPlainEntries(bool deflate)
    {
        byte[] content = Content(5000, 2);
        var zip = new MemoryStream();
        ZipEntryInfo entry = WriteEntry(zip, "a.bin", content, deflate, null);

        Assert.Equal(content, ReadAll(zip, entry, null));
    }


    [Fact]
    public void Open_ReadsEntriesIndependently()
    {
        // 两个条目交替读：每个流各自记位置，读之前才设底层流的位置
        byte[] a = Content(70_000, 3);
        byte[] b = Content(50_000, 4);
        var zip = new MemoryStream();
        ZipEntryInfo ea = WriteEntry(zip, "a", a, true, Password);
        ZipEntryInfo eb = WriteEntry(zip, "b", b, false, Password);
        using Stream sa = ZipEntryReader.Open(zip, ea, Password);
        using Stream sb = ZipEntryReader.Open(zip, eb, Password);
        var ra = new MemoryStream();
        var rb = new MemoryStream();
        byte[] buffer = new byte[4096];
        int na, nb;
        do
        {
            na = sa.Read(buffer);
            ra.Write(buffer, 0, na);
            nb = sb.Read(buffer);
            rb.Write(buffer, 0, nb);
        } while (na > 0 || nb > 0);

        Assert.Equal(a, ra.ToArray());
        Assert.Equal(b, rb.ToArray());
    }


    [Fact]
    public void Open_RejectsWrongPassword()
    {
        var zip = new MemoryStream();
        ZipEntryInfo entry = WriteEntry(zip, "a", Content(100, 5), false, Password);

        Assert.Throws<InvalidDataException>(() => ReadAll(zip, entry, "not the key"));
    }


    [Fact]
    public void Open_RejectsTamperedCipherText()
    {
        var zip = new MemoryStream();
        ZipEntryInfo entry = WriteEntry(zip, "a", Content(1000, 6), false, Password);
        // 改密文中间的一个字节：密码校验值还对，HMAC 不对
        byte[] bytes = zip.ToArray();
        bytes[30 + 1 + 11 + 16 + 2 + 500] ^= 1;

        Assert.Throws<InvalidDataException>(() => ReadAll(new MemoryStream(bytes), entry, Password));
    }


    [Fact]
    public void Open_RequiresPasswordForEncryptedEntries()
    {
        var zip = new MemoryStream();
        ZipEntryInfo entry = WriteEntry(zip, "a", Content(100, 7), false, Password);

        Assert.Throws<InvalidOperationException>(() => ReadAll(zip, entry, null));
    }

}
