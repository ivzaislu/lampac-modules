using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Online.Settings;
using Shared.Services;
using System.Collections.Generic;

namespace Kinobadi;

public class ModInit : IModuleLoaded, IModuleOnline
{
    public static OnlinesSettings conf;

    public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        return new List<ModuleOnlineItem>()
        {
            new(conf)
        };
    }

    public void Loaded(InitspaceModel baseconf)
    {
        if (!CoreInit.conf.online.with_search.Contains("kinobadi"))
            CoreInit.conf.online.with_search.Add("kinobadi");

        updateConf();
        EventListener.UpdateInitFile += updateConf;
        EventListener.ProxyApiCreateHttpRequest += Encoder.ProxyApiCreateHttpRequest;
        EventListener.OnlineApiQuality += onlineApiQuality;
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
        EventListener.ProxyApiCreateHttpRequest -= Encoder.ProxyApiCreateHttpRequest;
        EventListener.OnlineApiQuality -= onlineApiQuality;
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("Kinobadi", new OnlinesSettings(
            "Kinobadi",
            "https://my.kinobadi.im",
            streamproxy: true
        ));
    }

    string onlineApiQuality(EventOnlineApiQuality e)
        => e.balanser == "kinobadi" ? " ~ 1080p" : null;
}
