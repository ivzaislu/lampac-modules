using Shared.Models.Templates;

namespace Kinobadi;

public class SearchModel
{
    public SimilarTpl similar { get; set; }
}

public class SearchItem
{
    public string href { get; set; }
    public string title { get; set; }
    public short year { get; set; }
    public string img { get; set; }
}

public class ResolveModel
{
    public string source { get; set; }
    public string card { get; set; }
    public string player { get; set; }
    public long id_file { get; set; }
    public long kinopoisk_id { get; set; }
    public long provider_kp { get; set; }
    public short season { get; set; }
    public short episode { get; set; }
    public string embed { get; set; }
}

public class FemdEmbedModel
{
    public string token { get; set; }
    public long unixTime { get; set; }
    public FemdMovie movie { get; set; }
    public FemdSerial[] serial { get; set; }
}

public class FemdMovie
{
    public string name { get; set; }
    public string voicename { get; set; }
    public string hls { get; set; }
    public string dash { get; set; }
    public FemdCc[] cc { get; set; }
}

public class FemdSerial
{
    public int season { get; set; }
    public bool blocked { get; set; }
    public FemdEpisode[] episodes { get; set; }
}

public class FemdEpisode
{
    public string episode { get; set; }
    public string hls { get; set; }
    public string dasha { get; set; }
    public string dash { get; set; }
    public FemdCc[] cc { get; set; }
    public FemdAudio audio { get; set; }
}

public class FemdAudio
{
    public string[] names { get; set; }
}

public class FemdCc
{
    public string url { get; set; }
    public string name { get; set; }
}
