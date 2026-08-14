namespace Starward.Core.Gacha;

/// <summary>
/// 抽卡记录接口返回了错误码。
/// <para/>
/// 米哈游用 <see cref="miHoYoApiException"/>，其他厂商的接口不是那套协议，
/// 但错误处理的形状一样：一个错误码加一句话。
/// </summary>
public class GachaApiException : Exception
{

    public int Retcode { get; init; }


    public GachaApiException(int retcode, string message) : base(message)
    {
        Retcode = retcode;
    }

}
