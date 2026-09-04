using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Tevas;

public class TevasController : BaseOnlineController<ModuleConf>
{
    const string UA = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";
    const string Q = "720p";

    static readonly object mirrorLock = new();
    static string aliveMirror;
    static DateTime aliveUntil;

    static readonly Dictionary<char, string> translit = new()
    {
        ['а']="a", ['б']="b", ['в']="v", ['г']="g", ['д']="d", ['е']="e", ['ё']="e", ['ж']="zh", ['з']="z",
        ['и']="i", ['й']="j", ['к']="k", ['л']="l", ['м']="m", ['н']="n", ['о']="o", ['п']="p", ['р']="r",
        ['с']="s", ['т']="t", ['у']="u", ['ф']="f", ['х']="h", ['ц']="c", ['ч']="ch", ['ш']="sh", ['щ']="sch",
        ['ъ']="", ['ы']="y", ['ь']="", ['э']="e", ['ю']="yu", ['я']="ya"
    };

    public TevasController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/tevas")]
    async public Task<ActionResult> Index(string title, string original_title, short year = 0, byte serial = 0,
        short s = -1, string tag = null, string href = null, bool rjson = false, bool checksearch = false)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        if (checksearch)
            return await CheckSearch(title, original_title, year, serial == 1);

        if (serial == 1)
            return await Serial(title, original_title, s, tag, href);

        return await Movie(title, original_title, year);
    }

    async Task<ActionResult> CheckSearch(string title, string originalTitle, short year, bool serial)
    {
        bool ok;

        if (serial)
        {
            var index = await SerialIndex(firstOnly: true);
            ok = PickSerial(index, title, originalTitle) != null;
        }
        else
        {
            string query = !string.IsNullOrWhiteSpace(title) ? title : originalTitle;
            var hits = await SearchMovies(query, firstOnly: true);
            ok = PickMovie(hits, title, originalTitle, year) != null;
        }

        if (!ok)
            return Json(new { rch = false });

        return Json(new { rch = true, type = serial ? "serial" : "movie", quality = "HD" });
    }

    #region movie
    async Task<ActionResult> Movie(string title, string originalTitle, short year)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(originalTitle))
            return OnError();

        MovieHit hit = null;

        if (!string.IsNullOrWhiteSpace(title))
            hit = PickMovie(await SearchMovies(title), title, originalTitle, year);

        if (hit == null && !string.IsNullOrWhiteSpace(originalTitle) &&
            !string.Equals(title, originalTitle, StringComparison.OrdinalIgnoreCase))
            hit = PickMovie(await SearchMovies(originalTitle), title, originalTitle, year);

        if (hit == null)
            return OnError();

        string html = await GetPath(hit.href);
        string rel = ExtractPlayerPath(html);
        if (string.IsNullOrEmpty(rel))
            return OnError();

        string stream = Stream(init.cdn_movie_host, rel);
        if (string.IsNullOrEmpty(stream))
            return OnError();

        var sq = OneQuality(stream);
        var tpl = new MovieTpl(title, originalTitle, 1);
        tpl.Append("TEVAS", stream, streamquality: sq, quality: Q, headers: ClientStreamHeaders(), vast: init.vast);
        return ContentTpl(tpl);
    }

    async Task<List<MovieHit>> SearchMovies(string query, bool firstOnly = false)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<MovieHit>();

        string key = $"tevas:movie-search:{init.host}:{Normalize(query)}";
        if (!firstOnly && memoryCache.TryGetValue(key, out List<MovieHit> cached))
            return cached;

        string html = await GetPath($"/search/search.php?q={HttpUtility.UrlEncode(query)}", firstOnly);
        if (string.IsNullOrEmpty(html))
            return new List<MovieHit>();

        var result = new List<MovieHit>();
        var rx = new Regex(@"href=""(?<href>/kino/download/\?f=(?<file>[^""&]+)\.mp4[^""]*)""[\s\S]*?<img[^>]+src=""[^""]+""[\s\S]*?<span>(?<label>[^<]+)</span>", RegexOptions.IgnoreCase);

        foreach (Match m in rx.Matches(html))
        {
            string href = HttpUtility.HtmlDecode(m.Groups["href"].Value);
            string file = HttpUtility.UrlDecode(m.Groups["file"].Value) + ".mp4";
            string label = HttpUtility.HtmlDecode(m.Groups["label"].Value).Trim();

            if (!string.IsNullOrEmpty(href) && !string.IsNullOrEmpty(file))
                result.Add(new MovieHit(href, file, label, ExtractYear(file)));
        }

        if (!firstOnly && result.Count > 0)
            memoryCache.Set(key, result, TimeSpan.FromHours(2));

        return result;
    }

    static MovieHit PickMovie(List<MovieHit> hits, string title, string originalTitle, short year)
    {
        if (hits == null || hits.Count == 0)
            return null;

        string want = Normalize(!string.IsNullOrWhiteSpace(originalTitle) ? originalTitle : title);
        string wantTrans = Normalize(Translit(title));
        MovieHit best = null;
        int bestScore = -1;

        foreach (var hit in hits)
        {
            string label = Normalize(hit.label);
            string file = FileKey(hit.file);
            int score = 0;

            if (want.Length > 0 && label == want) score += 6;
            else if (want.Length > 0 && label.Contains(want, StringComparison.Ordinal)) score += 3;

            if (wantTrans.Length > 0 && file == wantTrans) score += 5;
            else if (wantTrans.Length > 0 && file.Contains(wantTrans, StringComparison.Ordinal)) score += 2;

            if (year > 0 && hit.year == year) score += 4;
            else if (year > 0 && hit.year > 0 && Math.Abs(hit.year - year) <= 1) score += 1;

            if (score > bestScore)
            {
                bestScore = score;
                best = hit;
            }
        }

        return bestScore > 0 ? best : null;
    }

    static string ExtractPlayerPath(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var m = Regex.Match(html, @"file\s*:\s*[""']//\{v\d+\}/(?<path>[^""'\s]+\.mp4)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups["path"].Value : null;
    }
    #endregion

    #region serial
    async Task<ActionResult> Serial(string title, string originalTitle, short season, string tag, string href)
    {
        SerialHit item;

        if (!string.IsNullOrWhiteSpace(href))
            item = new SerialHit(href.Trim('/'), title ?? originalTitle, Normalize(title ?? originalTitle));
        else
            item = PickSerial(await SerialIndex(), title, originalTitle);

        if (item == null || string.IsNullOrEmpty(item.slug))
            return OnError();

        var seasons = await Seasons(item.slug);
        if (seasons.Count == 0)
            return OnError();

        if (season <= 0)
        {
            var tpl = new SeasonTpl(Q, seasons.Count);
            foreach (var i in seasons)
            {
                string name = $"{i.season} сезон" + (string.IsNullOrEmpty(i.tag) ? string.Empty : $" ({i.tag})");
                tpl.Append(name, SeasonLink(title, originalTitle, item.slug, i), i.season);
            }
            return ContentTpl(tpl);
        }

        string wantTag = (tag ?? string.Empty).ToLowerInvariant();
        SeasonHit selected = seasons.FirstOrDefault(i => i.season == season && (wantTag.Length > 0 ? i.tag == wantTag : string.IsNullOrEmpty(i.tag)))
            ?? seasons.FirstOrDefault(i => i.season == season);

        if (selected == null)
            return OnError();

        var episodes = await Episodes(item.slug, selected);
        if (episodes.Count == 0)
            return OnError();

        string baseTitle = title ?? item.title ?? originalTitle ?? item.slug;
        var tplEpisodes = new EpisodeTpl(episodes.Count);

        foreach (var ep in episodes)
        {
            string stream = Stream(init.cdn_serial_host, $"serial/{item.slug}/{selected.dir}/{ep.file}");
            if (string.IsNullOrEmpty(stream))
                continue;

            string name = ep.episode > 0 ? $"{ep.episode} серия" : ep.file;
            tplEpisodes.Append(name, baseTitle, (short)selected.season, (short)ep.episode, stream,
                streamquality: OneQuality(stream), streamlink: stream, headers: ClientStreamHeaders(), vast: init.vast);
        }

        return ContentTpl(tplEpisodes);
    }

    async Task<List<SerialHit>> SerialIndex(bool firstOnly = false)
    {
        int ttl = init.serial_cache_hours < 0 ? 6 : init.serial_cache_hours;
        string key = $"tevas:serial-index:{init.host}";
        if (!firstOnly && ttl > 0 && memoryCache.TryGetValue(key, out List<SerialHit> cached))
            return cached;

        string html = await GetPath("/serial/", firstOnly);
        if (string.IsNullOrEmpty(html))
            return new List<SerialHit>();

        var block = Regex.Match(html, @"<div\s+id=""series"">(?<x>[\s\S]*?)<div\s+class=""clear"">", RegexOptions.IgnoreCase);
        string source = block.Success ? block.Groups["x"].Value : html;

        var result = new List<SerialHit>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rx = new Regex(@"<a\s+href=""(?<href>[^""#]+)""[^>]*>\s*<div\s+class=""v\s+vi\s+p"">\s*<img[^>]*src=""[^""]*""[^>]*>\s*<span>(?<title>[^<]+)</span>", RegexOptions.IgnoreCase);

        foreach (Match m in rx.Matches(source))
        {
            string slug = SerialSlug(HttpUtility.HtmlDecode(m.Groups["href"].Value));
            string name = HttpUtility.HtmlDecode(m.Groups["title"].Value).Trim();
            if (string.IsNullOrEmpty(slug) || !seen.Add(slug))
                continue;

            result.Add(new SerialHit(slug, name, Normalize(name)));
        }

        if (!firstOnly && ttl > 0 && result.Count > 0)
            memoryCache.Set(key, result, TimeSpan.FromHours(ttl));

        return result;
    }

    static SerialHit PickSerial(List<SerialHit> items, string title, string originalTitle)
    {
        if (items == null || items.Count == 0)
            return null;

        var keys = new List<string>();
        if (!string.IsNullOrWhiteSpace(title)) keys.Add(Normalize(title));
        if (!string.IsNullOrWhiteSpace(originalTitle) && !string.Equals(title, originalTitle, StringComparison.OrdinalIgnoreCase))
            keys.Add(Normalize(originalTitle));

        foreach (string key in keys)
        {
            var exact = items.FirstOrDefault(i => i.key == key);
            if (exact != null) return exact;
        }

        foreach (string key in keys)
        {
            if (key.Length < 3) continue;
            var part = items.FirstOrDefault(i => i.key.Contains(key, StringComparison.Ordinal) || key.Contains(i.key, StringComparison.Ordinal));
            if (part != null) return part;
        }

        return null;
    }

    async Task<List<SeasonHit>> Seasons(string slug)
    {
        string key = $"tevas:seasons:{slug}";
        if (memoryCache.TryGetValue(key, out List<SeasonHit> cached))
            return cached;

        string html = await GetPath($"/serial/{slug}/");
        var result = new List<SeasonHit>();
        if (string.IsNullOrEmpty(html)) return result;

        var seen = new HashSet<string>();
        var rx = new Regex(@"href=""(?<s>\d{1,2})(?:_(?<tag>[a-z0-9]+))?/\?big=(?<big>\d+)""", RegexOptions.IgnoreCase);
        foreach (Match m in rx.Matches(html))
        {
            int s = int.Parse(m.Groups["s"].Value);
            string tag = m.Groups["tag"].Value.ToLowerInvariant();
            if (!seen.Add($"{s}|{tag}")) continue;

            string dir = m.Groups["s"].Value + (tag.Length > 0 ? "_" + tag : string.Empty);
            result.Add(new SeasonHit(s, dir, tag, m.Groups["big"].Value));
        }

        result.Sort((a, b) => a.season != b.season ? a.season.CompareTo(b.season) :
            (string.IsNullOrEmpty(a.tag) ? -1 : string.IsNullOrEmpty(b.tag) ? 1 : string.Compare(a.tag, b.tag, StringComparison.Ordinal)));

        if (result.Count > 0) memoryCache.Set(key, result, TimeSpan.FromMinutes(30));
        return result;
    }

    async Task<List<EpisodeHit>> Episodes(string slug, SeasonHit season)
    {
        string key = $"tevas:episodes:{slug}:{season.dir}:{season.big}";
        if (memoryCache.TryGetValue(key, out List<EpisodeHit> cached))
            return cached;

        string html = await GetPath($"/serial/{slug}/{season.dir}/?big={season.big}");
        var result = new List<EpisodeHit>();
        if (string.IsNullOrEmpty(html)) return result;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rx = new Regex(@"href=""(?:\.\.?/[^""]*)?\?f=(?<file>[^""&]+)\.mp4(?:&[^""]*)?""", RegexOptions.IgnoreCase);
        foreach (Match m in rx.Matches(html))
        {
            string bare = HttpUtility.UrlDecode(m.Groups["file"].Value);
            string file = bare + ".mp4";
            if (!seen.Add(file)) continue;

            int ep = 0;
            var em = Regex.Match(bare, @"_(?<e>\d{1,3})$");
            if (em.Success) int.TryParse(em.Groups["e"].Value, out ep);
            result.Add(new EpisodeHit(ep, file));
        }

        result.Sort((a, b) => a.episode == 0 ? 1 : b.episode == 0 ? -1 : a.episode.CompareTo(b.episode));
        if (result.Count > 0) memoryCache.Set(key, result, TimeSpan.FromMinutes(10));
        return result;
    }

    string SeasonLink(string title, string originalTitle, string slug, SeasonHit season)
    {
        var q = new List<string> { "serial=1", $"s={season.season}", $"href={HttpUtility.UrlEncode(slug)}" };
        if (IsRjsonRequest()) q.Insert(0, "rjson=true");
        if (!string.IsNullOrEmpty(season.tag)) q.Add($"tag={HttpUtility.UrlEncode(season.tag)}");
        if (!string.IsNullOrWhiteSpace(title)) q.Add($"title={HttpUtility.UrlEncode(title)}");
        if (!string.IsNullOrWhiteSpace(originalTitle)) q.Add($"original_title={HttpUtility.UrlEncode(originalTitle)}");
        return $"{host}/lite/tevas?{string.Join("&", q)}";
    }
    #endregion

    #region http/stream
    async Task<string> GetPath(string path, bool firstOnly = false)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (!path.StartsWith('/')) path = "/" + path;

        var mirrors = Mirrors();
        if (firstOnly && mirrors.Count > 1) mirrors.RemoveRange(1, mirrors.Count - 1);

        foreach (string mirror in mirrors)
        {
            string html = null;
            try
            {
                await httpHydra.GetSpan(mirror + path, addheaders: RequestHeaders(mirror), spanAction: span => html = span.ToString());
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Tevas request failed: {Url}", mirror + path);
                continue;
            }

            if (string.IsNullOrEmpty(html) || Parking(html) || Challenge(html))
                continue;

            lock (mirrorLock)
            {
                aliveMirror = mirror;
                aliveUntil = DateTime.UtcNow.AddHours(1);
            }
            return html;
        }

        return null;
    }

    List<string> Mirrors()
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string value)
        {
            string x = (value ?? string.Empty).Trim().TrimEnd('/');
            if (x.Length > 0 && seen.Add(x)) result.Add(x);
        }

        lock (mirrorLock)
        {
            if (!string.IsNullOrEmpty(aliveMirror) && aliveUntil > DateTime.UtcNow) Add(aliveMirror);
        }
        Add(init.host);
        if (init.mirrors != null) foreach (string mirror in init.mirrors) Add(mirror);
        return result;
    }

    List<HeadersModel> RequestHeaders(string mirror)
    {
        string ua = string.IsNullOrWhiteSpace(init.cookie_user_agent) ? UA : init.cookie_user_agent;
        string cookies = string.IsNullOrWhiteSpace(init.cookies) ? init.cookie : init.cookies;
        return HeadersModel.Init(
            ("User-Agent", ua),
            ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"),
            ("Accept-Language", "ru-RU,ru;q=0.9,en;q=0.8"),
            ("Referer", mirror.TrimEnd('/') + "/"),
            ("Cookie", cookies));
    }

    List<HeadersModel> StreamHeaders()
    {
        string ua = string.IsNullOrWhiteSpace(init.cookie_user_agent) ? UA : init.cookie_user_agent;
        string referer = string.IsNullOrWhiteSpace(init.referer) ? init.host.TrimEnd('/') + "/" : init.referer;
        return HeadersModel.Init(("User-Agent", ua), ("Referer", referer));
    }

    IReadOnlyList<HeadersModel> ClientStreamHeaders() => init.streamproxy ? null : StreamHeaders();

    string Stream(string cdnHost, string rel)
    {
        if (string.IsNullOrWhiteSpace(cdnHost) || string.IsNullOrWhiteSpace(rel)) return null;
        string cdn = Regex.Replace(cdnHost.Trim(), "^https?://", string.Empty, RegexOptions.IgnoreCase).TrimEnd('/');
        string url = $"https://{cdn}/{rel.TrimStart('/')}";
        return init.streamproxy ? HostStreamProxy(url, StreamHeaders(), force_streamproxy: true) : url;
    }

    static StreamQualityTpl OneQuality(string stream)
    {
        var sq = new StreamQualityTpl(1);
        sq.Append(stream, Q);
        return sq;
    }

    static bool Parking(string html) => html.Length <= 65536 && Regex.IsMatch(html, @"quickresultseeker\.com|cdn-fileserver\.com|_ol_one_|_ol_lg_", RegexOptions.IgnoreCase);
    static bool Challenge(string html)
    {
        string head = html.Length > 4096 ? html[..4096] : html;
        return Regex.IsMatch(head, "cf-mitigated|Just a moment|challenge-platform", RegexOptions.IgnoreCase);
    }
    #endregion

    #region normalize
    static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        string x = value.ToLowerInvariant().Replace('ё', 'е');
        x = Regex.Replace(x, "[^a-zа-я0-9 ]+", " ", RegexOptions.IgnoreCase);
        return Regex.Replace(x, @"\s+", " ").Trim();
    }

    static string Translit(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var sb = new StringBuilder(value.Length * 2);
        foreach (char ch in value.ToLowerInvariant()) sb.Append(translit.TryGetValue(ch, out string x) ? x : ch.ToString());
        return sb.ToString();
    }

    static string FileKey(string value)
    {
        string x = Regex.Replace(value ?? string.Empty, @"\.mp4$", string.Empty, RegexOptions.IgnoreCase);
        x = Regex.Replace(x, "_+TEVAS_*", "_", RegexOptions.IgnoreCase);
        x = Regex.Replace(x, @"_+\d{4}_*", "_").Replace('_', ' ').ToLowerInvariant();
        x = Regex.Replace(x, "[^a-z0-9 ]+", " ");
        return Regex.Replace(x, @"\s+", " ").Trim();
    }

    static short ExtractYear(string value)
    {
        var m = Regex.Match(value ?? string.Empty, @"_(?<y>\d{4})(?:[_.]|$)");
        if (!m.Success || !short.TryParse(m.Groups["y"].Value, out short year)) return 0;
        return year >= 1900 && year <= 2100 ? year : (short)0;
    }

    static string SerialSlug(string href)
    {
        string x = (href ?? string.Empty).Trim();
        if (Uri.TryCreate(x, UriKind.Absolute, out Uri uri)) x = uri.AbsolutePath;
        int p = x.IndexOfAny(new[] { '?', '#' });
        if (p >= 0) x = x[..p];
        x = x.Trim('/');
        if (x.StartsWith("serial/", StringComparison.OrdinalIgnoreCase)) x = x[7..];
        return x.Trim('/');
    }
    #endregion

    record MovieHit(string href, string file, string label, short year);
    record SerialHit(string slug, string title, string key);
    record SeasonHit(int season, string dir, string tag, string big);
    record EpisodeHit(int episode, string file);
}
