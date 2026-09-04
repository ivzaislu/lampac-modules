using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using System.Collections.Generic;

namespace Tevas;

public class ModInit : IModuleLoaded, IModuleOnline
{
    public static ModuleConf conf;

    public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        if (args.isanime)
            return null;

        return new List<ModuleOnlineItem>()
        {
            new(conf)
        };
    }

    public void Loaded(InitspaceModel baseconf)
    {
        if (!CoreInit.conf.online.with_search.Contains("tevas"))
            CoreInit.conf.online.with_search.Add("tevas");

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
        conf = ModuleInvoke.Init("Tevas", new ModuleConf("TEVAS", "https://pult.tevas.dev")
        {
            displayindex = 515,
            mirrors = new[]
            {
                "https://pult.tevas.dev",
                "https://tevas.team",
                "https://tevas.tech"
            },
            cdn_movie_host = "bigsgppgs.tevas.dev",
            cdn_serial_host = "bigjjxjjs.tevas.dev",
            referer = "https://pult.tevas.dev/",
            streamproxy = true,
            stream_access = "apk,cors,web",
            httptimeout = 8,
            serial_cache_hours = 6
        });
    }

    string onlineApiQuality(EventOnlineApiQuality e)
        => e.balanser == "tevas" ? " ~ 720p" : null;
}
