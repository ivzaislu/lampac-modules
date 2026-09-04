using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using System.Collections.Generic;

namespace Krasview;

public class ModInit : IModuleLoaded, IModuleOnline
{
    public static ModuleConf conf;

    public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        return new List<ModuleOnlineItem>()
        {
            new(conf)
        };
    }

    public void Loaded(InitspaceModel baseconf)
    {
        updateConf();
        EventListener.UpdateInitFile += updateConf;
        EventListener.OnlineApiQuality += onlineApiQuality;
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
        EventListener.OnlineApiQuality -= onlineApiQuality;
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("Krasview", new ModuleConf("Krasview", "https://hlamer.ru")
        {
            searchhost = "https://hlamer.ru",
            moviehost = "https://smartkino.ru",
            serialhost = "https://sersoap.ru",
            stream_referer = "https://smartkino.ru/",
            prefer_hls = true,
            match_year_tolerance = 1,
            cache_ttl = 1,
            displayindex = 545,
            streamproxy = true,
            stream_access = "apk,cors,web",
            headers = HeadersModel.Init(
                ("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36"),
                ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"),
                ("Accept-Language", "ru,en;q=0.9")
            ).ToDictionary()
        });

        // Keep Krasview discovery fresh even if an older persisted module config
        // still contains the previous 30-minute cache value.
        conf.cache_ttl = 1;
    }

    string onlineApiQuality(EventOnlineApiQuality e)
    {
        return e.balanser == "krasview" ? " ~ 1080p" : null;
    }
}
