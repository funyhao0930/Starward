using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Starward.RPC.Tests.Fakes;

/// <summary>
/// 照终末地补丁包的格式做一个小 zip：每个条目先 deflate、再用 WinZip AES-256（AE-2）加密，
/// 密码就是 cd_key。中央目录是普通的（不用 Zip64），读取那一侧两种都认。
/// </summary>
internal static class FakePatchZip
{

    public static byte[] Build(IReadOnlyList<(string Name, byte[] Content)> entries, string password)
    {
        var zip = new MemoryStream();
        var central = new MemoryStream();
        foreach ((string name, byte[] content) in entries)
        {
            var deflated = new MemoryStream();
            using (var ds = new DeflateStream(deflated, CompressionLevel.Optimal, true))
            {
                ds.Write(content);
            }
            byte[] data = Encrypt(deflated.ToArray(), password);
            byte[] extra = new byte[11];
            BinaryPrimitives.WriteUInt16LittleEndian(extra, 0x9901);
            BinaryPrimitives.WriteUInt16LittleEndian(extra.AsSpan(2), 7);
            BinaryPrimitives.WriteUInt16LittleEndian(extra.AsSpan(4), 2);
            extra[6] = (byte)'A';
            extra[7] = (byte)'E';
            extra[8] = 3;
            BinaryPrimitives.WriteUInt16LittleEndian(extra.AsSpan(9), 8);
            byte[] nameBytes = Encoding.UTF8.GetBytes(name);
            long offset = zip.Position;

            byte[] local = new byte[30];
            BinaryPrimitives.WriteUInt32LittleEndian(local, 0x04034b50);
            BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(4), 51);
            BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(6), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(8), 99);
            BinaryPrimitives.WriteUInt32LittleEndian(local.AsSpan(18), (uint)data.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(local.AsSpan(22), (uint)content.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(26), (ushort)nameBytes.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(28), (ushort)extra.Length);
            zip.Write(local);
            zip.Write(nameBytes);
            zip.Write(extra);
            zip.Write(data);

            byte[] record = new byte[46];
            BinaryPrimitives.WriteUInt32LittleEndian(record, 0x02014b50);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(4), 51);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(6), 51);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(8), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(10), 99);
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(20), (uint)data.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(24), (uint)content.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(28), (ushort)nameBytes.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(30), (ushort)extra.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(42), (uint)offset);
            central.Write(record);
            central.Write(nameBytes);
            central.Write(extra);
        }
        long cdOffset = zip.Position;
        zip.Write(central.ToArray());
        byte[] eocd = new byte[22];
        BinaryPrimitives.WriteUInt32LittleEndian(eocd, 0x06054b50);
        BinaryPrimitives.WriteUInt16LittleEndian(eocd.AsSpan(8), (ushort)entries.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(eocd.AsSpan(10), (ushort)entries.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(eocd.AsSpan(12), (uint)central.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(eocd.AsSpan(16), (uint)cdOffset);
        zip.Write(eocd);
        return zip.ToArray();
    }


    /// <summary>
    /// 像官方那样把 zip 直接切成几段
    /// </summary>
    public static List<byte[]> Split(byte[] zip, int partSize)
    {
        var parts = new List<byte[]>();
        for (int i = 0; i < zip.Length; i += partSize)
        {
            parts.Add(zip[i..Math.Min(zip.Length, i + partSize)]);
        }
        return parts;
    }


    private static byte[] Encrypt(byte[] plain, string password)
    {
        const int keyLength = 32;
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


    /// <summary>
    /// 最简单的 HDIFFSF20：一个从头覆盖整个旧文件的 cover（差值走 rle0），之后的新数据原样附上。
    /// 新文件要比旧文件长
    /// </summary>
    public static byte[] Diff(byte[] oldData, byte[] newData)
    {
        if (newData.Length < oldData.Length)
        {
            throw new ArgumentException("New data must not be shorter than old data.");
        }
        var covers = new MemoryStream();
        // cover 1：oldPos 0（正向、差 0），newPos 0，长度为旧文件全长
        WriteVarInt(covers, 0, tagBits: 1);
        WriteVarInt(covers, 0);
        WriteVarInt(covers, oldData.Length);
        // 结尾的空 cover 带出剩下的新数据
        WriteVarInt(covers, 0, tagBits: 1);
        WriteVarInt(covers, newData.Length - oldData.Length);
        WriteVarInt(covers, 0);

        var rle = new MemoryStream();
        WriteVarInt(rle, 0);
        WriteVarInt(rle, oldData.Length);
        for (int i = 0; i < oldData.Length; i++)
        {
            rle.WriteByte((byte)(newData[i] - oldData[i]));
        }

        var body = new MemoryStream();
        WriteVarInt(body, covers.Length);
        WriteVarInt(body, rle.Length);
        body.Write(covers.ToArray());
        body.Write(rle.ToArray());
        body.Write(newData, oldData.Length, newData.Length - oldData.Length);

        var diff = new MemoryStream();
        diff.Write(Encoding.ASCII.GetBytes("HDIFFSF20&\0"));
        WriteVarInt(diff, newData.Length);
        WriteVarInt(diff, oldData.Length);
        WriteVarInt(diff, 2);
        WriteVarInt(diff, covers.Length + rle.Length);
        WriteVarInt(diff, body.Length);
        WriteVarInt(diff, 0);
        diff.Write(body.ToArray());
        return diff.ToArray();
    }


    private static void WriteVarInt(Stream stream, long value, int tagBits = 0)
    {
        int firstValueBits = 7 - tagBits;
        var groups = new List<int>();
        while (value >= (1L << firstValueBits))
        {
            groups.Insert(0, (int)(value & 0x7F));
            value >>= 7;
        }
        int first = (int)value;
        if (groups.Count > 0)
        {
            first |= 1 << firstValueBits;
        }
        stream.WriteByte((byte)first);
        for (int i = 0; i < groups.Count; i++)
        {
            stream.WriteByte((byte)(groups[i] | (i < groups.Count - 1 ? 0x80 : 0)));
        }
    }

}
