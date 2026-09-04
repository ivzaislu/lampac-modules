using Shared.Models.Base;

namespace Krasview;

public class ModuleConf : BaseSettings
{
    public ModuleConf(string plugin, string host)
    {
        enable = true;
        this.plugin = plugin;
        this.host = host;
    }

    public string searchhost { get; set; } = "https://hlamer.ru";
    public string moviehost { get; set; } = "https://smartkino.ru";
    public string serialhost { get; set; } = "https://sersoap.ru";
    public string stream_referer { get; set; } = "https://smartkino.ru/";
    public bool prefer_hls { get; set; } = true;
    public int match_year_tolerance { get; set; } = 1;
    public int cache_ttl { get; set; } = 1;
}
