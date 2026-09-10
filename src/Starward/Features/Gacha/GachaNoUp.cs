using Starward.Core;
using Starward.Core.Gacha.Genshin;
using Starward.Core.Gacha.Gryphline;
using Starward.Core.Gacha.Kuro;
using Starward.Core.Gacha.StarRail;
using Starward.Core.Gacha.ZZZ;
using Starward.Core.Games;
using Starward.Core.Games.Gryphline;
using Starward.Core.Games.Kuro;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Starward.Features.Gacha;




public class GachaNoUp
{

    public GameBiz Game { get; set; }

    public int GachaType { get; set; }

    /// <summary>
    /// 按物品 ID 索引的常驻物品，米哈游三款用它：它们的 item_id 就是游戏内的数字 ID，
    /// 与界面语言无关，认 ID 最稳。
    /// </summary>
    public Dictionary<int, GachaNoUpItem> Items { get; set; } = new();

    /// <summary>
    /// 按名称索引的常驻物品，同一个物品的各种写法都指向同一条记录。
    /// <para/>
    /// 终末地的 charId 是字符串，存进数据库时散列成了整数（见
    /// <see cref="Starward.Core.Gacha.GachaSyntheticId.ToItemId"/>），
    /// 鸣潮的 resourceId 虽是数字但没有公开对照表，两家都只能认名字。
    /// 因此每条记录要把简体、繁体、英文的写法都列出来；没列到的语言会被当成 UP。
    /// </summary>
    public Dictionary<string, GachaNoUpItem> NamedItems { get; set; } = new(StringComparer.OrdinalIgnoreCase);



    public static Dictionary<string, GachaNoUp> Dictionary { get; } = new();



    static GachaNoUp()
    {
        AddGachaNoUpGenshin();
        AddGachaNoUpStarRail();
        AddGachaNoUpZZZ();
        AddGachaNoUpWuwa();
        AddGachaNoUpEndfield();
    }



    /// <summary>
    /// 取某个游戏某个卡池的常驻物品表，字典键的拼法只在这里写一份
    /// </summary>
    public static bool TryGet(GameBiz game, int gachaType, out GachaNoUp? noUp)
    {
        return Dictionary.TryGetValue($"{game}{gachaType}", out noUp);
    }



    /// <summary>
    /// 这一抽是不是当期 UP。先认 ID，认不出再认名字；两者都不在常驻表里就算 UP。
    /// </summary>
    public bool IsUp(GachaLogItemEx item)
    {
        if (!Items.TryGetValue(item.ItemId, out GachaNoUpItem? noUpItem) && item.Name is not null)
        {
            NamedItems.TryGetValue(item.Name, out noUpItem);
        }
        if (noUpItem is null)
        {
            return true;
        }
        foreach ((DateTime start, DateTime end) in noUpItem.NoUpTimes)
        {
            if (item.Time >= start && item.Time <= end)
            {
                return false;
            }
        }
        return true;
    }



    /// <summary>
    /// 把物品按 <see cref="GachaNoUpItem.Names"/> 里的每种写法都登记一遍
    /// </summary>
    private static Dictionary<string, GachaNoUpItem> ToNameDictionary(IEnumerable<GachaNoUpItem> items)
    {
        var dictionary = new Dictionary<string, GachaNoUpItem>(StringComparer.OrdinalIgnoreCase);
        foreach (GachaNoUpItem item in items)
        {
            foreach (string name in item.Names)
            {
                dictionary[name] = item;
            }
        }
        return dictionary;
    }



    private static void AddGachaNoUpGenshin()
    {
        GachaNoUp hk4e301 = new GachaNoUp
        {
            Game = GameBiz.hk4e,
            GachaType = GenshinGachaType.CharacterEventWish,
            Items = new List<GachaNoUpItem>
            {
                new GachaNoUpItem
                {
                    Id = 10000003,
                    Name = "琴",
                    NoUpTimes = [(new DateTime(2020, 9, 1), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 10000016,
                    Name = "迪卢克",
                    NoUpTimes = [(new DateTime(2020, 9, 1), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 10000035,
                    Name = "七七",
                    NoUpTimes = [(new DateTime(2020, 9, 1), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 10000041,
                    Name = "莫娜",
                    NoUpTimes = [(new DateTime(2020, 9, 1), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 10000042,
                    Name = "刻晴",
                    NoUpTimes =
                    [
                        (new DateTime(2020, 9, 1), new DateTime(2021, 2, 17, 17, 59, 59)),
                        (new DateTime(2021, 3, 2, 16, 00, 00), DateTime.MaxValue),
                    ],
                },
                new GachaNoUpItem
                {
                    Id = 10000069,
                    Name = "提纳里",
                    NoUpTimes = [(new DateTime(2022, 9, 27, 18, 00, 00), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 10000079,
                    Name = "迪希雅",
                    NoUpTimes = [(new DateTime(2023, 4, 11, 18, 00, 00), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 10000109,
                    Name = "梦见月瑞希",
                    NoUpTimes = [(new DateTime(2025, 3, 25, 18, 00, 00), DateTime.MaxValue)],
                },
            }.ToDictionary(item => item.Id),
        };
        Dictionary.Add("hk4e301", hk4e301);
    }


    private static void AddGachaNoUpStarRail()
    {
        GachaNoUp hkrpg11 = new GachaNoUp
        {
            Game = GameBiz.hkrpg,
            GachaType = StarRailGachaType.CharacterEventWarp,
            Items = new List<GachaNoUpItem>
            {
                new GachaNoUpItem
                {
                    Id = 1003,
                    Name = "姬子",
                    NoUpTimes = [(new DateTime(2023, 4, 1), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 1004,
                    Name = "瓦尔特",
                    NoUpTimes = [(new DateTime(2023, 4, 1), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 1101,
                    Name = "布洛妮娅",
                    NoUpTimes = [(new DateTime(2023, 4, 1), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 1104,
                    Name = "杰帕德",
                    NoUpTimes = [(new DateTime(2023, 4, 1), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 1107,
                    Name = "克拉拉",
                    NoUpTimes = [(new DateTime(2023, 4, 1), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 1209,
                    Name = "彦卿",
                    NoUpTimes = [(new DateTime(2023, 4, 1), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 1211,
                    Name = "白露",
                    NoUpTimes = [(new DateTime(2023, 4, 1), DateTime.MaxValue)],
                },
                // 3.2版本，自定义非UP五星角色
                new GachaNoUpItem
                {
                    Id = 1102,
                    Name = "希儿",
                    NoUpTimes = [(new DateTime(2025, 4, 8, 18, 00, 00), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 1205,
                    Name = "刃",
                    NoUpTimes =
                    [
                        (new DateTime(2025, 4, 8, 18, 00, 00), new DateTime(2025, 7, 23, 11, 59, 59)),
                        (new DateTime(2025, 8, 12, 15, 00, 00), DateTime.MaxValue),
                    ],
                },
                new GachaNoUpItem
                {
                    Id = 1208,
                    Name = "符玄",
                    NoUpTimes = [(new DateTime(2025, 4, 8, 18, 00, 00), DateTime.MaxValue)],
                },
                // 4.2版本，自定义非UP五星角色
                new GachaNoUpItem
                {
                    Id = 1006,
                    Name = "银狼",
                    NoUpTimes = [(new DateTime(2026, 4, 21, 18, 00, 00), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 1221,
                    Name = "云璃",
                    NoUpTimes = [(new DateTime(2026, 4, 21, 18, 00, 00), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 1302,
                    Name = "银枝",
                    NoUpTimes = [(new DateTime(2026, 4, 21, 18, 00, 00), DateTime.MaxValue)],
                },
            }.ToDictionary(item => item.Id),
        };
        Dictionary.Add("hkrpg11", hkrpg11);

        GachaNoUp hkrpg12 = new GachaNoUp
        {
            Game = GameBiz.hkrpg,
            GachaType = StarRailGachaType.LightConeEventWarp,
            Items = new List<GachaNoUpItem>
            {
                new GachaNoUpItem
                {
                    Id = 23000,
                    Name = "银河铁道之夜",
                    NoUpTimes = [(new DateTime(2023, 4, 1), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 23002,
                    Name = "无可取代的东西",
                    NoUpTimes = [(new DateTime(2023, 4, 1), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 23003,
                    Name = "但战斗还未结束",
                    NoUpTimes = [(new DateTime(2023, 4, 1), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 23004,
                    Name = "以世界之名",
                    NoUpTimes = [(new DateTime(2023, 4, 1), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 23005,
                    Name = "制胜的瞬间",
                    NoUpTimes = [(new DateTime(2023, 4, 1), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 23012,
                    Name = "如泥酣眠",
                    NoUpTimes = [(new DateTime(2023, 4, 1), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 23013,
                    Name = "时节不居",
                    NoUpTimes = [(new DateTime(2023, 4, 1), DateTime.MaxValue)],
                },
            }.ToDictionary(item => item.Id),
        };
        Dictionary.Add("hkrpg12", hkrpg12);

        GachaNoUp hkrpg21 = new GachaNoUp
        {
            Game = GameBiz.hkrpg,
            GachaType = StarRailGachaType.CharacterCollaborationWarp,
            Items = new List<GachaNoUpItem>
            {
                new GachaNoUpItem
                {
                    Id = 1003,
                    Name = "姬子",
                    NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 1004,
                    Name = "瓦尔特",
                    NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 1101,
                    Name = "布洛妮娅",
                    NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 1104,
                    Name = "杰帕德",
                    NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 1107,
                    Name = "克拉拉",
                    NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 1209,
                    Name = "彦卿",
                    NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 1211,
                    Name = "白露",
                    NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
                },
                // 3.2版本，自定义非UP五星角色
                new GachaNoUpItem
                {
                    Id = 1102,
                    Name = "希儿",
                    NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 1205,
                    Name = "刃",
                    NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 1208,
                    Name = "符玄",
                    NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
                },
                // 4.2版本，自定义非UP五星角色
                new GachaNoUpItem
                {
                    Id = 1006,
                    Name = "银狼",
                    NoUpTimes = [(new DateTime(2026, 4, 21, 18, 00, 00), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 1221,
                    Name = "云璃",
                    NoUpTimes = [(new DateTime(2026, 4, 21, 18, 00, 00), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 1302,
                    Name = "银枝",
                    NoUpTimes = [(new DateTime(2026, 4, 21, 18, 00, 00), DateTime.MaxValue)],
                },
            }.ToDictionary(item => item.Id),
        };
        Dictionary.Add("hkrpg21", hkrpg21);

        GachaNoUp hkrpg22 = new GachaNoUp
        {
            Game = GameBiz.hkrpg,
            GachaType = StarRailGachaType.LightConeCollaborationWarp,
            Items = new List<GachaNoUpItem>
            {
                new GachaNoUpItem
                {
                    Id = 23000,
                    Name = "银河铁道之夜",
                    NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 23002,
                    Name = "无可取代的东西",
                    NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 23003,
                    Name = "但战斗还未结束",
                    NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 23004,
                    Name = "以世界之名",
                    NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 23005,
                    Name = "制胜的瞬间",
                    NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 23012,
                    Name = "如泥酣眠",
                    NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 23013,
                    Name = "时节不居",
                    NoUpTimes = [(new DateTime(2025, 7, 11), DateTime.MaxValue)],
                },
            }.ToDictionary(item => item.Id),
        };
        Dictionary.Add("hkrpg22", hkrpg22);
    }


    private static void AddGachaNoUpZZZ()
    {
        GachaNoUp nap2 = new GachaNoUp
        {
            Game = GameBiz.nap,
            GachaType = ZZZGachaType.ExclusiveChannel,
            Items = new List<GachaNoUpItem>
            {
                new GachaNoUpItem
                {
                    Id = 1021,
                    Name = "猫又",
                    NoUpTimes = [(new DateTime(2024, 7, 1), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 1041,
                    Name = "「11号」",
                    NoUpTimes = [(new DateTime(2024, 7, 1), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 1101,
                    Name = "珂蕾妲",
                    NoUpTimes = [(new DateTime(2024, 7, 1), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 1141,
                    Name = "莱卡恩",
                    NoUpTimes = [(new DateTime(2024, 7, 1), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 1181,
                    Name = "格莉丝",
                    NoUpTimes = [(new DateTime(2024, 7, 1), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 1211,
                    Name = "丽娜",
                    NoUpTimes = [(new DateTime(2024, 7, 1), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 1071,
                    Name = "凯撒",
                    NoUpTimes = [(new DateTime(2026, 7, 28, 18, 00, 00), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 1221,
                    Name = "柳",
                    NoUpTimes = [(new DateTime(2026, 7, 28, 18, 00, 00), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 1241,
                    Name = "朱鸢",
                    NoUpTimes = [(new DateTime(2026, 7, 28, 18, 00, 00), DateTime.MaxValue)],
                },
            }.ToDictionary(item => item.Id),
        };
        Dictionary.Add("nap2", nap2);
        Dictionary.Add("nap102", nap2);

        GachaNoUp nap3 = new GachaNoUp
        {
            Game = GameBiz.nap,
            GachaType = ZZZGachaType.WEngineChannel,
            Items = new List<GachaNoUpItem>
            {
                new GachaNoUpItem
                {
                    Id = 14102,
                    Name = "钢铁肉垫",
                    NoUpTimes = [(new DateTime(2024, 7, 1), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 14104,
                    Name = "硫磺石",
                    NoUpTimes = [(new DateTime(2024, 7, 1), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 14110,
                    Name = "燃狱齿轮",
                    NoUpTimes = [(new DateTime(2024, 7, 1), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 14114,
                    Name = "拘缚者",
                    NoUpTimes = [(new DateTime(2024, 7, 1), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 14118,
                    Name = "嵌合编译器",
                    NoUpTimes = [(new DateTime(2024, 7, 1), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 14121,
                    Name = "啜泣摇篮",
                    NoUpTimes = [(new DateTime(2024, 7, 1), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 14107,
                    Name = "奔袭獠牙",
                    NoUpTimes = [(new DateTime(2026, 7, 28, 18, 00, 00), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 14122,
                    Name = "时流贤者",
                    NoUpTimes = [(new DateTime(2026, 7, 28, 18, 00, 00), DateTime.MaxValue)],
                },
                new GachaNoUpItem
                {
                    Id = 14124,
                    Name = "防暴者Ⅵ型",
                    NoUpTimes = [(new DateTime(2026, 7, 28, 18, 00, 00), DateTime.MaxValue)],
                },
            }.ToDictionary(item => item.Id),
        };
        Dictionary.Add("nap3", nap3);
        Dictionary.Add("nap103", nap3);
    }


    /// <summary>
    /// 鸣潮的角色活动唤取（限定池）。
    /// <para/>
    /// 歪掉时给的是常驻五星共鸣者，开服至今这五位没有变过。
    /// 武器活动唤取的五星必定是当期武器，没有歪的概念，因此不列。
    /// </summary>
    private static void AddGachaNoUpWuwa()
    {
        // 开服前不可能有记录，起点给宽一点即可
        DateTime launch = new DateTime(2024, 5, 1);
        List<GachaNoUpItem> items =
        [
            new GachaNoUpItem
            {
                Name = "卡卡罗",
                Names = ["卡卡罗", "卡卡羅", "Calcharo"],
                NoUpTimes = [(launch, DateTime.MaxValue)],
            },
            new GachaNoUpItem
            {
                Name = "安可",
                Names = ["安可", "Encore"],
                NoUpTimes = [(launch, DateTime.MaxValue)],
            },
            new GachaNoUpItem
            {
                Name = "维里奈",
                Names = ["维里奈", "維里奈", "Verina"],
                NoUpTimes = [(launch, DateTime.MaxValue)],
            },
            new GachaNoUpItem
            {
                Name = "鉴心",
                Names = ["鉴心", "鑑心", "鑒心", "Jianxin"],
                NoUpTimes = [(launch, DateTime.MaxValue)],
            },
            new GachaNoUpItem
            {
                Name = "凌阳",
                Names = ["凌阳", "淩陽", "凌陽", "Lingyang"],
                NoUpTimes = [(launch, DateTime.MaxValue)],
            },
        ];
        GachaNoUp wuwa1 = new GachaNoUp
        {
            Game = GameKeyResolver.ToSettingsKey(KuroGameMapping.WutheringWavesGlobal),
            GachaType = KuroGachaType.FeaturedResonator,
            NamedItems = ToNameDictionary(items),
        };
        Dictionary.Add($"{wuwa1.Game}{wuwa1.GachaType}", wuwa1);
    }


    /// <summary>
    /// 终末地的特许寻访（限定池）。
    /// <para/>
    /// 歪掉时给的不只是五位常驻六星干员：上一期、上上一期的限定干员也留在池子里。
    /// 所以除了常驻干员，还要把每位限定干员自己的概率提升期以外的时间都算成非 UP，
    /// 表里没有的新干员会被当成 UP，每个版本都得补一行。
    /// <para/>
    /// 辉光庆典是四位干员同时 UP，没有歪的概念；武库申领虽然也有非 UP 武器，
    /// 但常驻六星武器没有可靠的公开名单，两者都不列。
    /// </summary>
    private static void AddGachaNoUpEndfield()
    {
        // 开服前不可能有记录，起点给宽一点即可
        DateTime launch = new DateTime(2026, 1, 1);
        List<GachaNoUpItem> items =
        [
            // 基础寻访的五位常驻六星干员，从头到尾都不会是当期 UP
            new GachaNoUpItem
            {
                Name = "余烬",
                Names = ["余烬", "餘燼", "Ember"],
                NoUpTimes = [(launch, DateTime.MaxValue)],
            },
            new GachaNoUpItem
            {
                Name = "黎风",
                Names = ["黎风", "黎風", "Lifeng"],
                NoUpTimes = [(launch, DateTime.MaxValue)],
            },
            new GachaNoUpItem
            {
                Name = "艾尔黛拉",
                Names = ["艾尔黛拉", "艾爾黛拉", "Ardelia"],
                NoUpTimes = [(launch, DateTime.MaxValue)],
            },
            new GachaNoUpItem
            {
                Name = "别礼",
                Names = ["别礼", "別禮", "Last Rite"],
                NoUpTimes = [(launch, DateTime.MaxValue)],
            },
            new GachaNoUpItem
            {
                Name = "骏卫",
                Names = ["骏卫", "駿衛", "Pogranichnik"],
                NoUpTimes = [(launch, DateTime.MaxValue)],
            },
            // 限定六星干员，只有自己的概率提升期才算 UP
            new GachaNoUpItem
            {
                Name = "莱万汀",
                Names = ["莱万汀", "萊萬汀", "Laevatain"],
                NoUpTimes = ExceptUpTimes((new DateTime(2026, 1, 22), new DateTime(2026, 2, 7))),
            },
            new GachaNoUpItem
            {
                Name = "洁尔佩塔",
                Names = ["洁尔佩塔", "潔爾佩塔"],
                NoUpTimes = ExceptUpTimes((new DateTime(2026, 2, 7), new DateTime(2026, 2, 24))),
            },
            new GachaNoUpItem
            {
                Name = "伊冯",
                Names = ["伊冯", "伊馮", "Yvonne"],
                NoUpTimes = ExceptUpTimes(
                    (new DateTime(2026, 2, 24), new DateTime(2026, 3, 12)),
                    (new DateTime(2026, 9, 24), new DateTime(2026, 10, 14))),
            },
            new GachaNoUpItem
            {
                Name = "汤汤",
                Names = ["汤汤", "湯湯", "Tangtang"],
                NoUpTimes = ExceptUpTimes((new DateTime(2026, 3, 12), new DateTime(2026, 3, 29))),
            },
            new GachaNoUpItem
            {
                Name = "洛茜",
                Names = ["洛茜", "Rossi"],
                NoUpTimes = ExceptUpTimes((new DateTime(2026, 3, 29), new DateTime(2026, 4, 17))),
            },
            new GachaNoUpItem
            {
                Name = "庄方宜",
                Names = ["庄方宜", "莊方宜", "Zhuang Fangyi"],
                NoUpTimes = ExceptUpTimes((new DateTime(2026, 4, 17), new DateTime(2026, 5, 22))),
            },
            new GachaNoUpItem
            {
                Name = "弭弗",
                Names = ["弭弗", "Mi Fu"],
                NoUpTimes = ExceptUpTimes((new DateTime(2026, 6, 5), new DateTime(2026, 6, 26))),
            },
            new GachaNoUpItem
            {
                Name = "卡缪",
                Names = ["卡缪", "卡繆", "Camille"],
                NoUpTimes = ExceptUpTimes((new DateTime(2026, 6, 26), new DateTime(2026, 7, 16))),
            },
            new GachaNoUpItem
            {
                Name = "诀",
                Names = ["诀", "訣", "Arcane"],
                NoUpTimes = ExceptUpTimes((new DateTime(2026, 7, 16), new DateTime(2026, 8, 9))),
            },
            new GachaNoUpItem
            {
                Name = "梨诺",
                Names = ["梨诺", "梨諾", "Liino"],
                NoUpTimes = ExceptUpTimes((new DateTime(2026, 8, 9), new DateTime(2026, 9, 2))),
            },
            new GachaNoUpItem
            {
                Name = "提弗洛斯",
                Names = ["提弗洛斯", "Typhoeus"],
                NoUpTimes = ExceptUpTimes((new DateTime(2026, 9, 2), new DateTime(2026, 9, 30))),
            },
        ];
        GachaNoUp endfield1 = new GachaNoUp
        {
            Game = GameKeyResolver.ToSettingsKey(GryphlineGameMapping.EndfieldDefault),
            GachaType = GryphlineGachaType.Special,
            NamedItems = ToNameDictionary(items),
        };
        Dictionary.Add($"{endfield1.Game}{endfield1.GachaType}", endfield1);
    }


    /// <summary>
    /// 把「概率提升期」翻过来，得到 <see cref="GachaNoUpItem.NoUpTimes"/> 要的非 UP 时间段。
    /// 卡池换期都在同一天，这里以当天零点为界，换期当天算新一期的。
    /// </summary>
    private static List<(DateTime Start, DateTime End)> ExceptUpTimes(params (DateTime Start, DateTime End)[] upTimes)
    {
        var noUpTimes = new List<(DateTime Start, DateTime End)>();
        DateTime cursor = DateTime.MinValue;
        foreach ((DateTime start, DateTime end) in upTimes.OrderBy(x => x.Start))
        {
            if (cursor < start)
            {
                noUpTimes.Add((cursor, start.AddTicks(-1)));
            }
            cursor = end;
        }
        noUpTimes.Add((cursor, DateTime.MaxValue));
        return noUpTimes;
    }


}



public class GachaNoUpItem
{

    /// <summary>
    /// 物品 ID，按名称匹配的游戏不填
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 物品名称，只是给读代码的人看的
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 这个物品在各语言下的写法，按名称匹配的游戏用它，见 <see cref="GachaNoUp.NamedItems"/>
    /// </summary>
    public string[] Names { get; set; } = [];

    /// <summary>
    /// 这些时间段里它不是当期 UP。常驻物品从头到尾都不是，
    /// 曾经 UP 过的限定物品则要把自己的概率提升期挖掉。
    /// </summary>
    public List<(DateTime Start, DateTime End)> NoUpTimes { get; set; }

}
