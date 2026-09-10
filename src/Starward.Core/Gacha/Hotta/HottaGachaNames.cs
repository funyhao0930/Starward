using System.Globalization;

namespace Starward.Core.Gacha.Hotta;

/// <summary>
/// 异环奖励的中文名。
/// <para/>
/// nte-exporter 的对照表只有英文，游戏自己的中文文本锁在加密的 pak 里取不到，
/// 所以这份表是照社区资料整理的：角色来自台服攻略站的中英对照，
/// 弧盘来自 BWIKI 的弧盘图鉴（简体），按稀有度与英文表一一对上后转成繁体；
/// 其中八个名字另有台服来源独立佐证，转换无误。
/// <para/>
/// <b>对不上的一律不写</b>，查不到就退回导出文件里的英文名，不去猜。
/// 道具与外观没有可靠来源，整类都没收。
/// <para/>
/// 只在显示时套用，不写进记录 ID，所以以后补名字或改名字，
/// 重新导入只会覆盖同一条记录，不会多出一条。
/// </summary>
public static class HottaGachaNames
{

    /// <param name="Traditional">繁体，台服用的写法</param>
    /// <param name="Simplified">简体，国服用的写法</param>
    private readonly record struct LocalizedName(string Traditional, string Simplified);


    private static readonly Dictionary<string, LocalizedName> _names = new(StringComparer.Ordinal)
    {
        // 角色
        ["1003"] = new("早霧", "早雾"),
        ["1004"] = new("安魂曲", "安魂曲"),
        ["1008"] = new("翳", "翳"),
        ["1010"] = new("娜娜莉", "娜娜莉"),
        ["1019"] = new("薄荷", "薄荷"),
        ["1020"] = new("哈尼婭", "哈尼娅"),
        ["1021"] = new("埃德嘉", "埃德嘉"),
        ["1023"] = new("白藏", "白藏"),
        ["1025"] = new("哈索爾", "哈索尔"),
        ["1033"] = new("阿德勒", "阿德勒"),
        ["1036"] = new("殘虹", "残虹"),
        ["1039"] = new("法蒂婭", "法蒂娅"),
        ["1046"] = new("零", "零"),
        ["1051"] = new("零", "零"),
        ["1052"] = new("潯", "浔"),
        ["1054"] = new("達芙蒂爾", "达芙蒂尔"),
        ["1055"] = new("九原", "九原"),
        ["1056"] = new("安魂曲", "安魂曲"),
        ["1070"] = new("海月", "海月"),
        ["1071"] = new("卡厄斯", "卡厄斯"),
        ["1072"] = new("靈可", "灵可"),
        ["1073"] = new("小吱", "小吱"),
        ["1075"] = new("伊洛伊", "伊洛伊"),
        ["1076"] = new("真紅", "真红"),
        ["1091"] = new("安魂曲", "安魂曲"),
        // 弧盘
        ["fork_Arachne"] = new("永恆圓舞曲", "永恒圆舞曲"),
        ["fork_BitGame"] = new("引爆全場", "引爆全场"),
        ["fork_BitterCake"] = new("良藥苦口", "良药苦口"),
        ["fork_BlackBook"] = new("漆黑青春妄想", "漆黑青春妄想"),
        ["fork_BlastCandy"] = new("無畏之綿", "无畏之绵"),
        ["fork_BoxingCandy"] = new("不屈之綿", "不屈之绵"),
        ["fork_Butterfly"] = new("現實避難所", "现实避难所"),
        ["fork_Castle"] = new("扭曲之城的呼喚", "扭曲之城的呼唤"),
        ["fork_Crowbar"] = new("時間大盜", "时间大盗"),
        ["fork_Kite"] = new("當心頭頂", "当心头顶"),
        ["fork_KnightCandy"] = new("凶猛之綿", "凶猛之绵"),
        ["fork_MotorCandy"] = new("極速之棉", "极速之棉"),
        ["fork_Nakupeda"] = new("千金難買你開心", "千金难买你开心"),
        ["fork_NestBird"] = new("面具下的淚", "面具下的泪"),
        ["fork_PaperPlane"] = new("開始淨空", "开始净空"),
        ["fork_PoliceRat"] = new("海特洛的安寧", "海特洛的安宁"),
        ["fork_Prokaryon"] = new("「我們。」", "「我们。」"),
        ["fork_Rose"] = new("「最後一朵玫瑰」", "「最后一朵玫瑰」"),
        ["fork_ThiefCandy"] = new("靈敏之綿", "灵敏之绵"),
        ["fork_TigerTally"] = new("預備備", "预备备"),
        ["fork_Time"] = new("行進於時間之外", "行进于时间之外"),
        ["fork_Whale"] = new("鯨之歌", "鲸之歌"),
        ["fork_appliance"] = new("「電音」狂歡", "「电音」狂欢"),
        ["fork_bopu"] = new("光波眩暈，迷走狂歡", "光波眩晕，迷走狂欢"),
        ["fork_dustbin"] = new("危險遊戲", "危险游戏"),
        ["fork_jiaojuan"] = new("閃耀的每一天", "闪耀的每一天"),
        ["fork_jingmotingyuan"] = new("茶花會", "茶花会"),
        ["fork_koinobori"] = new("終有時", "终有时"),
        ["fork_lingganzhongjiezhe"] = new("靈感大逃殺", "灵感大逃杀"),
        ["fork_mamen"] = new("思考喵", "思考喵"),
        ["fork_mofeikesi"] = new("好狗狗走四方", "好狗狗走四方"),
        ["fork_nonos"] = new("成功的第一步", "成功的第一步"),
        ["fork_oulaquantao"] = new("歐拉歐拉！", "欧拉欧拉！"),
        ["fork_rishi"] = new("休息日", "休息日"),
        ["fork_snowman"] = new("愚者種春", "愚者种春"),
        ["fork_spider"] = new("掛你在心口難開", "挂你在心口难开"),
        ["fork_vine"] = new("笑口常開", "笑口常开"),
        ["fork_worldrain"] = new("「傾世之雨」", "「倾世之雨」"),
        ["fork_wuhuakuang"] = new("被遺忘者", "被遗忘者"),
        ["fork_wushoutieyu"] = new("焰魂狂飆", "焰魂狂飙"),
        ["fork_yaodao"] = new("拔刀", "拔刀"),
        ["fork_yuren"] = new("勿忘傘", "勿忘伞"),
    };


    /// <summary>
    /// 把导出文件里的英文名换成中文名。
    /// <para/>
    /// 只有界面语言是中文时才换：别的语言下英文名才是对的，
    /// 而且这份表也只有中文。查不到 <paramref name="rewardId"/> 时原样返回。
    /// </summary>
    public static string Localize(string? rewardId, string fallback, CultureInfo? culture = null)
    {
        if (string.IsNullOrEmpty(rewardId) || !_names.TryGetValue(rewardId, out LocalizedName name))
        {
            return fallback;
        }
        culture ??= CultureInfo.CurrentUICulture;
        string tag = culture.Name;
        if (tag.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            // zh-Hans / zh-CN / zh-SG 用简体，其余中文（zh-TW、zh-HK、zh-Hant）用繁体
            bool simplified = tag.Contains("Hans", StringComparison.OrdinalIgnoreCase)
                           || tag.EndsWith("-CN", StringComparison.OrdinalIgnoreCase)
                           || tag.EndsWith("-SG", StringComparison.OrdinalIgnoreCase);
            return simplified ? name.Simplified : name.Traditional;
        }
        return fallback;
    }


    /// <summary>
    /// 表里收了多少个名字，测试用
    /// </summary>
    public static int Count => _names.Count;

}
