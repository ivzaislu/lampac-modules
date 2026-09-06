using System.Collections.Generic;

namespace FanCDN2_0;

public class FanCDN2_0EmbedModel
{
    public FanCDN2_0MovieEpisode[] movies { get; set; }
}

public class FanCDN2_0MovieEpisode
{
    public string title { get; set; }

    public string file { get; set; }

    public string subtitles { get; set; }
}

public class FanCDN2_0SerialSeason
{
    public short season { get; set; }

    public FanCDN2_0SerialEpisode[] episodes { get; set; }
}

public class FanCDN2_0SerialEpisode
{
    public int episode { get; set; }

    public string title { get; set; }

    public Dictionary<string, string> streams { get; set; }
}
