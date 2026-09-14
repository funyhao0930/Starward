using Starward.Core.HoYoPlay;

namespace Starward.Core.Launcher.Kuro;

/// <summary>
/// 把鸣潮的轮播图与资讯配置换成应用统一使用的 <see cref="GameContent"/>。
/// <para/>
/// 换成米哈游那套模型之后，启动页的横幅与资讯控件一行都不用改。
/// 这段映射不依赖任何 IO，值得单独测。
/// </summary>
public static class KuroContentMapper
{

    /// <summary>
    /// 换成统一的横幅与资讯，没有可用内容时返回 null
    /// </summary>
    public static GameContent? ToGameContent(KuroLauncherInformation? information)
    {
        if (information is null)
        {
            return null;
        }
        var banners = new List<GameBanner>();
        foreach (KuroLauncherSlide slide in information.Slideshow ?? [])
        {
            if (string.IsNullOrWhiteSpace(slide.Url))
            {
                continue;
            }
            banners.Add(new GameBanner
            {
                // 接口没有轮播图 ID，校验值是随图一起换的，拿它当唯一标识
                Id = slide.Md5 ?? slide.Url,
                Image = new GameImage
                {
                    Url = slide.Url,
                    Link = slide.JumpUrl ?? "",
                },
            });
        }

        var posts = new List<GamePost>();
        KuroLauncherGuidance? guidance = information.Guidance;
        // 三组的键是固定的英文名，不必从本地化的标题去猜分类
        AddPosts(posts, guidance?.Notice, GamePostType.POST_TYPE_ANNOUNCE);
        AddPosts(posts, guidance?.Activity, GamePostType.POST_TYPE_ACTIVITY);
        AddPosts(posts, guidance?.News, GamePostType.POST_TYPE_INFO);

        if (banners.Count is 0 && posts.Count is 0)
        {
            return null;
        }
        return new GameContent
        {
            Banners = banners,
            Posts = posts,
            SocialMediaList = [],
        };
    }


    private static void AddPosts(List<GamePost> posts, KuroLauncherGuidanceGroup? group, string type)
    {
        // 开关关掉的分组官方启动器自己也不显示，跟着藏起来
        if (group is null || group.FunctionSwitch != KuroLauncherGuidanceGroup.FUNCTION_ON)
        {
            return;
        }
        foreach (KuroLauncherGuidanceItem item in group.Contents ?? [])
        {
            if (string.IsNullOrWhiteSpace(item.Content))
            {
                continue;
            }
            posts.Add(new GamePost
            {
                // 接口没有资讯 ID，同一组里标题够用
                Id = item.Content,
                Type = type,
                Title = item.Content,
                Link = item.JumpUrl ?? "",
                Date = FormatDate(item.Time),
            });
        }
    }


    /// <summary>
    /// 接口给的是 <c>MM-dd</c>，米哈游那边是 <c>MM/dd</c>，统一成后者。
    /// 认不出来的写法原样返回，总比显示空白好。
    /// </summary>
    private static string FormatDate(string? time)
    {
        return string.IsNullOrWhiteSpace(time) ? "" : time.Replace('-', '/');
    }

}
