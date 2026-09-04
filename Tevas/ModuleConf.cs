using Shared.Models.Base;

namespace Tevas;

public class ModuleConf : BaseSettings
{
    public ModuleConf(string plugin, string host)
    {
        enable = true;
        this.plugin = plugin;
        this.host = host;
    }

    public string[] mirrors { get; set; }

    public string cdn_movie_host { get; set; }

    public string cdn_serial_host { get; set; }

    public string referer { get; set; }

    public string cookies { get; set; }

    public string cookie_user_agent { get; set; }

    public int serial_cache_hours { get; set; } = 6;
}
