using Starward.Core.Launcher;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Starward.Core.Tests.Launcher;

/// <summary>
/// HDIFFSF20（不压缩）的解码。
/// <para/>
/// 差分由下面的小编码器照 HDiffPatch 的格式产生，不放真实的游戏资源进仓库；
/// 与官方补丁的逐字节比对另外用真实数据做过（结果记在提交说明里）。
/// </summary>
public class HDiffSingleStreamTests
{

    private sealed record Cover(long OldPos, long NewPos, long Length);


    /// <summary>
    /// 照 HDiffPatch 的单流格式写差分：covers 按 <paramref name="stepSize"/> 个一组分步，
    /// 每步先写 cover 表与 rle0 的长度，再写这两段，之后是各 cover 前面的新数据
    /// </summary>
    private static byte[] Encode(byte[] oldData, byte[] newData, IReadOnlyList<Cover> covers, int stepSize = 100)
    {
        // 末尾补一个长度为 0 的 cover，带出最后一段新数据
        var all = covers.Append(new Cover(covers.Count > 0 ? covers[^1].OldPos + covers[^1].Length : 0, newData.Length, 0)).ToList();
        var body = new MemoryStream();
        long lastOldEnd = 0, lastNewEnd = 0;
        int maxStep = 0;
        for (int start = 0; start < all.Count; start += stepSize)
        {
            List<Cover> step = all.Skip(start).Take(stepSize).ToList();
            var coverBuf = new MemoryStream();
            var diffBytes = new List<byte>();
            var gaps = new MemoryStream();
            foreach (Cover c in step)
            {
                bool backward = c.OldPos < lastOldEnd;
                WriteVarInt(coverBuf, backward ? lastOldEnd - c.OldPos : c.OldPos - lastOldEnd, backward ? 1 : 0, 1);
                WriteVarInt(coverBuf, c.NewPos - lastNewEnd);
                WriteVarInt(coverBuf, c.Length);
                gaps.Write(newData, (int)lastNewEnd, (int)(c.NewPos - lastNewEnd));
                for (long i = 0; i < c.Length; i++)
                {
                    diffBytes.Add((byte)(newData[c.NewPos + i] - oldData[c.OldPos + i]));
                }
                lastOldEnd = c.OldPos + c.Length;
                lastNewEnd = c.NewPos + c.Length;
            }
            byte[] rle = EncodeRle0(diffBytes);
            byte[] covers2 = coverBuf.ToArray();
            maxStep = Math.Max(maxStep, covers2.Length + rle.Length);
            WriteVarInt(body, covers2.Length);
            WriteVarInt(body, rle.Length);
            body.Write(covers2);
            body.Write(rle);
            body.Write(gaps.ToArray());
        }
        var diff = new MemoryStream();
        diff.Write(Encoding.ASCII.GetBytes("HDIFFSF20&\0"));
        WriteVarInt(diff, newData.Length);
        WriteVarInt(diff, oldData.Length);
        WriteVarInt(diff, all.Count);
        WriteVarInt(diff, maxStep);
        WriteVarInt(diff, body.Length);
        WriteVarInt(diff, 0);
        diff.Write(body.ToArray());
        return diff.ToArray();
    }


    /// <summary>
    /// 交替写「0 的个数」与「非 0 字节的个数 + 字节」
    /// </summary>
    private static byte[] EncodeRle0(List<byte> data)
    {
        var ms = new MemoryStream();
        int i = 0;
        while (i < data.Count)
        {
            int zeros = 0;
            while (i + zeros < data.Count && data[i + zeros] == 0)
            {
                zeros++;
            }
            i += zeros;
            int values = 0;
            while (i + values < data.Count && data[i + values] != 0)
            {
                values++;
            }
            WriteVarInt(ms, zeros);
            WriteVarInt(ms, values);
            ms.Write(data.Skip(i).Take(values).ToArray());
            i += values;
        }
        return ms.ToArray();
    }


    /// <summary>
    /// 高位在前，每字节 7 位；带标签时第一个字节最高的 <paramref name="tagBits"/> 位是标签
    /// </summary>
    private static void WriteVarInt(Stream stream, long value, int tag = 0, int tagBits = 0)
    {
        // 第一个字节：标签、续接位、7 - tagBits 位数值；之后每字节 7 位
        int firstValueBits = 7 - tagBits;
        var groups = new List<int>();
        while (value >= (1L << firstValueBits))
        {
            groups.Insert(0, (int)(value & 0x7F));
            value >>= 7;
        }
        int first = (tag << (8 - tagBits)) | (int)value;
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


    private static byte[] Apply(byte[] oldData, byte[] diff)
    {
        var output = new MemoryStream();
        HDiffSingleStream.Patch(new MemoryStream(oldData), new MemoryStream(diff), output);
        return output.ToArray();
    }


    private static byte[] RandomBytes(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }



    [Fact]
    public void Patch_CopiesNewDataAroundCoversAndAddsRleDifferences()
    {
        byte[] oldData = RandomBytes(5000, 1);
        byte[] newData = new byte[6000];
        RandomBytes(6000, 2).CopyTo(newData, 0);
        // 一段原样搬过来，一段改了少数几个字节，中间与末尾是全新的数据
        Array.Copy(oldData, 1000, newData, 200, 1500);
        Array.Copy(oldData, 3000, newData, 2500, 1800);
        newData[2600] ^= 0x5A;
        newData[3999] ^= 0xFF;
        byte[] diff = Encode(oldData, newData, [new Cover(1000, 200, 1500), new Cover(3000, 2500, 1800)]);

        Assert.Equal(newData, Apply(oldData, diff));
    }


    [Fact]
    public void Patch_HandlesBackwardCoversAndManySteps()
    {
        byte[] oldData = RandomBytes(100_000, 3);
        byte[] newData = RandomBytes(150_000, 4);
        var covers = new List<Cover>();
        // 旧数据的位置前后跳，测带符号的 oldPos；分成很多步，测步与步之间的状态延续
        long newPos = 10;
        for (int i = 0; i < 40; i++)
        {
            long oldPos = (i * 7919L) % 90_000;
            int length = 500 + i * 13;
            Array.Copy(oldData, oldPos, newData, newPos, length);
            newData[newPos + length / 2] += 1;
            covers.Add(new Cover(oldPos, newPos, length));
            newPos += length + 1000;
        }
        byte[] diff = Encode(oldData, newData, covers, stepSize: 3);

        Assert.Equal(newData, Apply(oldData, diff));
    }


    [Fact]
    public void Patch_LongVarIntsAreDecoded()
    {
        // 超过 2^20 的位置让变长整数占好几个字节，含带标签的那种
        byte[] oldData = RandomBytes(3_000_000, 5);
        byte[] newData = RandomBytes(2_500_000, 6);
        Array.Copy(oldData, 2_000_000, newData, 1_200_000, 700_000);
        Array.Copy(oldData, 100, newData, 1_950_000, 300_000);
        byte[] diff = Encode(oldData, newData, [new Cover(2_000_000, 1_200_000, 700_000), new Cover(100, 1_950_000, 300_000)]);

        Assert.Equal(newData, Apply(oldData, diff));
    }


    [Fact]
    public void Patch_WithoutCoversWritesOnlyNewData()
    {
        byte[] newData = RandomBytes(1234, 7);
        byte[] diff = Encode([], newData, []);

        Assert.Equal(newData, Apply([], diff));
    }


    [Fact]
    public void Patch_RejectsOldDataOfAnotherSize()
    {
        byte[] oldData = RandomBytes(100, 8);
        byte[] diff = Encode(oldData, RandomBytes(50, 9), []);

        Assert.Throws<InvalidDataException>(() => Apply(RandomBytes(99, 8), diff));
    }


    [Fact]
    public void Patch_RejectsCompressedDiffs()
    {
        byte[] diff = [.. Encoding.ASCII.GetBytes("HDIFFSF20&zstd\0"), 1, 0, 1, 1, 1, 1];

        Assert.Throws<NotSupportedException>(() => Apply([], diff));
    }


    [Fact]
    public void ReadHeader_RejectsOtherFormats()
    {
        Assert.Throws<InvalidDataException>(() => HDiffSingleStream.ReadHeader(new MemoryStream(Encoding.ASCII.GetBytes("HDIFF13&zstd\0"))));
    }


    [Fact]
    public void Patch_RejectsTruncatedDiff()
    {
        byte[] oldData = RandomBytes(5000, 10);
        byte[] newData = RandomBytes(6000, 11);
        Array.Copy(oldData, 0, newData, 100, 4000);
        byte[] diff = Encode(oldData, newData, [new Cover(0, 100, 4000)]);

        Assert.ThrowsAny<Exception>(() => Apply(oldData, diff[..^500]));
    }

}
