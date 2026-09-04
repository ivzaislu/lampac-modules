using System.Collections.Generic;
using System.Text.Json;

namespace Krasview;

public class SearchItem
{
    public string url { get; set; }
    public string host { get; set; }
    public string kind { get; set; }
    public string slug { get; set; }
    public string en { get; set; }
    public int year { get; set; }
}

public class VideoItem
{
    public string id { get; set; }
    public string href { get; set; }
    public string slug { get; set; }
    public int s { get; set; }
    public int e { get; set; }
}

public class SeasonItem
{
    public int id { get; set; }
    public int number { get; set; }
}

public class VideoConfig
{
    public string url { get; set; }
    public Dictionary<string, JsonElement> audio_info { get; set; }
}