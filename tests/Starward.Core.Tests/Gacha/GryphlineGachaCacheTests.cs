using Starward.Core.Gacha.Gryphline;
using System;
using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace Starward.Core.Tests.Gacha;

/// <summary>
/// 从启动器的 Chromium blockfile 缓存（<c>data_1</c>）里挑寻访 URL。
/// <para/>
/// 条目在文件里的先后与访问时间无关，必须按条目的创建时间挑最新的。
/// 这里手工拼出最小的 blockfile，token 都是假值。
/// </summary>
public class GryphlineGachaCacheTests
{

    private const int HeaderSize = 8192;

    private const int BlockSize = 256;


    private static string RecordUrl(string token) => $"https://ef-webview.gryphline.com/api/record/char?lang=zh-tw&token={token}&server_id=2&pool_type=E_CharacterGachaPoolType_Special";


    /// <summary>
    /// 每个条目占 4 个块：+24 creation_time、+32 key_len、+96 key
    /// </summary>
    private static byte[] BuildBlockFile(params (string Key, long CreationTime)[] entries)
    {
        byte[] file = new byte[HeaderSize + entries.Length * BlockSize * 4];
        BinaryPrimitives.WriteUInt32LittleEndian(file, 0xC104CAC3);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(12), BlockSize);
        for (int i = 0; i < entries.Length; i++)
        {
            int block = HeaderSize + i * BlockSize * 4;
            byte[] key = Encoding.ASCII.GetBytes(entries[i].Key);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(block + 24), (ulong)entries[i].CreationTime);
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(block + 32), key.Length);
            key.CopyTo(file, block + 96);
        }
        return file;
    }


    [Fact]
    public void FindRecordUrl_TakesTheNewestEntryNotTheLastOneInTheFile()
    {
        // 文件里最靠后的是旧 token，中间那条才是最近一次打开寻访页时留下的
        byte[] cache = BuildBlockFile(
            (RecordUrl("older"), 13_400_000_000_000_000),
            (RecordUrl("newest"), 13_408_000_000_000_000),
            (RecordUrl("stale"), 13_404_000_000_000_000));

        Assert.Equal(RecordUrl("newest"), GryphlineGachaClient.FindRecordUrl(cache));
    }


    [Fact]
    public void FindRecordUrl_SkipsEntriesWithoutToken()
    {
        byte[] cache = BuildBlockFile(
            (RecordUrl("usable"), 13_400_000_000_000_000),
            ("https://ef-webview.gryphline.com/api/record/char?lang=zh-tw&server_id=2", 13_408_000_000_000_000));

        Assert.Equal(RecordUrl("usable"), GryphlineGachaClient.FindRecordUrl(cache));
    }


    [Fact]
    public void FindRecordUrl_FallsBackToFileOrderWhenItIsNotABlockFile()
    {
        byte[] cache = Encoding.ASCII.GetBytes($"\0{RecordUrl("first")}\0garbage\0{RecordUrl("second")}\0");

        Assert.Equal(RecordUrl("second"), GryphlineGachaClient.FindRecordUrl(cache));
    }


    [Fact]
    public void FindCacheEntryCreationTime_FindsTheEntryWhoseKeySpansSeveralBlocks()
    {
        string key = RecordUrl(new string('x', 616));
        byte[] cache = BuildBlockFile((key, 13_408_123_456_789_000));
        int start = HeaderSize + 96;

        Assert.Equal(13_408_123_456_789_000, GryphlineGachaClient.FindCacheEntryCreationTime(cache, start, start + key.Length));
        Assert.Null(GryphlineGachaClient.FindCacheEntryCreationTime(cache, start, start + key.Length + 1));
    }


    [Fact]
    public void ErrorResponse_ReadsTheMsgField()
    {
        var response = System.Text.Json.JsonSerializer.Deserialize("""{"code":40100,"msg":"Token is invalid","data":{}}""", GryphlineGachaJsonContext.Default.GryphlineGachaResponse);

        Assert.Equal(40100, response!.Code);
        Assert.Equal("Token is invalid", response.Message);
    }

}
