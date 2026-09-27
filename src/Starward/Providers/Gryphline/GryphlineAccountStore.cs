using System;
using System.Threading.Tasks;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;
using Windows.Storage.Streams;

namespace Starward.Providers.Gryphline;

/// <summary>
/// 存下鹰角通行证的 <c>ACCOUNT_TOKEN</c>。
/// <para/>
/// 这把令牌是整个账号层级的：拿到它就能以玩家的身份登录 SKPort、操作账号，
/// 比抽卡用的一次性 token 敏感得多，因此不以明文落地。
/// 用 Windows 的数据保护（DPAPI，绑定当前 Windows 用户）加密后才写进设置，
/// 换一台电脑或换一个 Windows 用户都解不开，也就不会随着数据文件夹被带走。
/// </summary>
internal static class GryphlineAccountStore
{

    private const string SettingKey = "gryphline_skport_account_token";


    /// <summary>
    /// 本机有没有存下令牌。不代表令牌还有效：可能已经过期，或者玩家在网页上登出了。
    /// </summary>
    public static bool HasToken => !string.IsNullOrWhiteSpace(AppConfig.GetValue<string>(null, SettingKey));


    /// <summary>
    /// 取出令牌，没有或解不开时返回 null。
    /// 解不开通常是数据文件夹被搬到了别的电脑或别的 Windows 用户下，这时当成没有登录。
    /// </summary>
    public static async Task<string?> ReadTokenAsync()
    {
        string? stored = AppConfig.GetValue<string>(null, SettingKey);
        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }
        try
        {
            // 解密时不必给描述符，受保护的数据里自己带着
            var provider = new DataProtectionProvider();
            IBuffer plain = await provider.UnprotectAsync(CryptographicBuffer.DecodeFromBase64String(stored));
            string token = CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, plain);
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }
        catch (Exception)
        {
            return null;
        }
    }


    public static async Task SaveTokenAsync(string token)
    {
        // LOCAL=user：只有当前 Windows 用户能解开
        var provider = new DataProtectionProvider("LOCAL=user");
        IBuffer protectedBuffer = await provider.ProtectAsync(CryptographicBuffer.ConvertStringToBinary(token, BinaryStringEncoding.Utf8));
        AppConfig.SetValue(CryptographicBuffer.EncodeToBase64String(protectedBuffer), SettingKey);
    }


    /// <summary>
    /// 登出：删掉令牌，并请下一次打开登录窗口时把网页的登录状态也清掉。
    /// <para/>
    /// 网页那边的 Cookie 只有开着 WebView2 才删得了，这里只能先记一笔。
    /// 不清的话，登录窗口一打开就会拿到刚登出的那个账号，换不了人。
    /// </summary>
    public static void Clear()
    {
        AppConfig.SetValue<string?>(null, SettingKey);
        AppConfig.SetValue(true, ClearWebLoginKey);
    }


    private const string ClearWebLoginKey = "gryphline_skport_clear_web_login";


    /// <summary>
    /// 有没有待处理的「清掉网页登录状态」请求，读了就清掉这个请求
    /// </summary>
    public static bool ConsumeClearWebLoginRequest()
    {
        if (!AppConfig.GetValue(false, ClearWebLoginKey))
        {
            return false;
        }
        AppConfig.SetValue(false, ClearWebLoginKey);
        return true;
    }

}
