namespace Starward.Core.Games;

/// <summary>
/// 单款游戏（含渠道）支持的功能。
/// 只支持启动的游戏可以只设置 <c>Launch | Discovery | Screenshot | PlayTime</c>。
/// </summary>
[Flags]
public enum GameCapability
{

    None = 0,

    /// <summary>
    /// 启动游戏
    /// </summary>
    Launch = 1 << 0,

    /// <summary>
    /// 搜索已安装的游戏
    /// </summary>
    Discovery = 1 << 1,

    /// <summary>
    /// 查询最新版本
    /// </summary>
    VersionCheck = 1 << 2,

    /// <summary>
    /// 安装游戏
    /// </summary>
    Install = 1 << 3,

    /// <summary>
    /// 更新游戏
    /// </summary>
    Update = 1 << 4,

    /// <summary>
    /// 修复游戏
    /// </summary>
    Repair = 1 << 5,

    /// <summary>
    /// 截图管理
    /// </summary>
    Screenshot = 1 << 6,

    /// <summary>
    /// 游玩时间统计
    /// </summary>
    PlayTime = 1 << 7,

    /// <summary>
    /// 抽卡记录
    /// </summary>
    Gacha = 1 << 8,

    /// <summary>
    /// 游戏记录（米游社 / HoYoLAB 工具箱）
    /// </summary>
    GameRecord = 1 << 9,

    /// <summary>
    /// 游戏账号切换
    /// </summary>
    AccountSwitcher = 1 << 10,

    /// <summary>
    /// 游戏公告
    /// </summary>
    Announcement = 1 << 11,

    /// <summary>
    /// 游戏内设置（画质、分辨率等）
    /// </summary>
    GameSetting = 1 << 12,

    /// <summary>
    /// 消费记录查询
    /// </summary>
    SelfQuery = 1 << 13,

    /// <summary>
    /// 原神跨服抽卡记录
    /// </summary>
    BeyondGacha = 1 << 14,

    /// <summary>
    /// 多渠道之间硬链接共用游戏文件
    /// </summary>
    HardLink = 1 << 15,

    /// <summary>
    /// 云游戏
    /// </summary>
    CloudGame = 1 << 16,

    /// <summary>
    /// 实时便笺
    /// </summary>
    DailyNote = 1 << 17,

    /// <summary>
    /// 游戏内通知内容窗口
    /// </summary>
    InGameNotices = 1 << 18,

}
