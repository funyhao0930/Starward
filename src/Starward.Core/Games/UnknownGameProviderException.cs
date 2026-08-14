namespace Starward.Core.Games;

/// <summary>
/// 找不到指定的游戏供应商，或该供应商没有注册所需的能力。
/// </summary>
public class UnknownGameProviderException : Exception
{

    /// <summary>
    /// 供应商标识
    /// </summary>
    public string ProviderId { get; }


    public UnknownGameProviderException(string providerId)
        : base($"No game provider registered for provider id '{providerId}'.")
    {
        ProviderId = providerId;
    }


    public UnknownGameProviderException(string providerId, string message) : base(message)
    {
        ProviderId = providerId;
    }

}
