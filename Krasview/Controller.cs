using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Krasview;

public class KrasviewController : BaseOnlineController<ModuleConf>
{
    const string UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";

    static readonly Regex SlugBlacklist = new("(treiyler|tizer|obzor|review|reklam|trailer|teaser|preview|fragment|sopustvuyusch|making|behind)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public KrasviewController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/krasview")]
    async public Task<ActionResult> Index(string title, string original_title, int year = 0, int serial = 0, short s = -1, bool rjson = false, bool checksearch = false)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        if (checksearch)
        {
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(original_title))
                return Json(new { rch = false });

            string checkKind = serial == 1 ? "series" : "movie";
            var checkMatch = await FindMatch(title, original_title, year, checkKind);
            if (checkMatch == null)
                checkMatch = await FindMatch(title, original_title, year, checkKind == "movie" ? "series" : "movie");

            if (checkMatch == null)
                return Json(new { rch = false });

            Response.Headers["X-Krasview-Match"] = $"{checkMatch.kind}/{checkMatch.slug}";
            Response.Headers["X-Krasview-Match-Year"] = checkMatch.year.ToString();

            return Json(new
            {
                rch = true,
                type = checkMatch.kind == "series" ? "serial" : "movie",
                quality = "FHD"
            });
        }

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(original_title))
            return OnError();

        string kind = serial == 1 ? "series" : "movie";
        var match = await FindMatch(title, original_title, year, kind);
        if (match == null)
            match = await FindMatch(title, original_title, year, kind == "movie" ? "series" : "movie");

        if (match == null)
            return OnError();

        Response.Headers["X-Krasview-Match"] = $"{match.kind}/{match.slug}";
        Response.Headers["X-Krasview-Match-Year"] = match.year.ToString();

        kind = match.kind;

        string sourceHost = kind == "series" ? init.serialhost : init.moviehost;
        if (IsMirrorHost(match.host))
            sourceHost = "https://" + match.host;

        string pageUrl = $"/{match.kind}/{match.slug}";
        string pageHtml = await GetCached(sourceHost + pageUrl, sourceHost + "/");
        if (string.IsNullOrEmpty(pageHtml))
            return OnError();

        if (kind == "movie")
        {
            var movieTpl = await BuildMovie(sourceHost, title, original_title, ParseMovieVideos(pageHtml));
            return ContentTpl(movieTpl);
        }

        var seasons = ParseSeasonCategories(pageHtml);
        if (seasons.Count == 0)
        {
            var eps = ParseSeriesEpisodes(pageHtml);
            if (eps.Count == 0)
            {
                var movieTpl = await BuildMovie(sourceHost, title, original_title, ParseMovieVideos(pageHtml));
                return ContentTpl(movieTpl);
            }

            int firstSeason = eps[0].s;
            var episodeTpl = await BuildEpisodes(sourceHost, title, original_title, eps, firstSeason);
            return ContentTpl(episodeTpl);
        }

        if (s == -1)
        {
            if (seasons.Count == 1)
            {
                int onlySeason = seasons[0].number > 0 ? seasons[0].number : 1;
                var eps = await FetchSeasonEpisodes(sourceHost, match.slug, seasons[0].id, onlySeason);
                if (eps.Count == 0)
                    return OnError();

                var episodeTpl = await BuildEpisodes(sourceHost, title, original_title, eps, onlySeason);
                return ContentTpl(episodeTpl);
            }

            return ContentTpl(BuildSeasons(title, original_title, year, seasons, rjson));
        }

        int index = seasons.FindIndex(i => i.number == s);
        if (index < 0)
            index = Math.Clamp(s - 1, 0, seasons.Count - 1);

        var selectedSeason = seasons[index];
        int realSeason = selectedSeason.number > 0 ? selectedSeason.number : s;
        var episodes = await FetchSeasonEpisodes(sourceHost, match.slug, selectedSeason.id, realSeason);
        if (episodes.Count == 0)
            return OnError();

        var resultTpl = await BuildEpisodes(sourceHost, title, original_title, episodes, realSeason);
        return ContentTpl(resultTpl);
    }

    async Task<string> GetCached(string url, string referer)
    {
        var cache = await InvokeCacheResult<string>($"krasview:{url}", TimeSpan.FromSeconds(init.cache_ttl), async e =>
        {
            var headers = HeadersModel.Init(("Referer", referer ?? string.Empty));
            string html = await httpHydra.Get(url, addheaders: headers, safety: true);
            if (string.IsNullOrEmpty(html))
                return e.Fail("html", refresh_proxy: true);

            return e.Success(html);
        });

        return cache.IsSuccess ? cache.Value : null;
    }

    async Task<List<SearchItem>> SearchOnce(string kind, string query, string matchTitle = null)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<SearchItem>();

        string url = $"{init.searchhost.TrimEnd('/')}/{kind}?mode=search&ajax&query={HttpUtility.UrlEncode(query)}";
        string html = await GetCached(url, init.searchhost.TrimEnd('/') + "/");
        if (string.IsNullOrEmpty(html))
            return new List<SearchItem>();

        var result = new List<SearchItem>();
        var matches = Regex.Matches(html, @"<a[^>]+href='(https?://[^']+/(?:movie|series)/[^']+)'\s+title='([^']*)'", RegexOptions.IgnoreCase);

        foreach (Match m in matches)
        {
            string href = m.Groups[1].Value;
            string rawTitle = HttpUtility.HtmlDecode(m.Groups[2].Value ?? string.Empty);

            var path = Regex.Match(href, @"/(movie|series)/([^/?#]+)", RegexOptions.IgnoreCase);
            if (!path.Success)
                continue;

            string foundHost = string.Empty;
            if (Uri.TryCreate(href, UriKind.Absolute, out Uri uri))
                foundHost = uri.Host;

            int foundYear = 0;
            var yearMatches = Regex.Matches(rawTitle, @"(?<!\d)((?:18|19|20|21)\d{2})(?!\d)");
            if (yearMatches.Count > 0)
                int.TryParse(yearMatches[0].Groups[1].Value, out foundYear);

            if (foundYear == 0)
            {
                var slugYears = Regex.Matches(path.Groups[2].Value, @"(?<!\d)((?:18|19|20|21)\d{2})(?!\d)");
                if (slugYears.Count > 0)
                    int.TryParse(slugYears[0].Groups[1].Value, out foundYear);
            }

            string en = ChooseSearchTitle(rawTitle, string.IsNullOrWhiteSpace(matchTitle) ? query : matchTitle);
            if (string.IsNullOrWhiteSpace(en))
                continue;

            result.Add(new SearchItem
            {
                url = href,
                host = foundHost,
                kind = path.Groups[1].Value.ToLowerInvariant(),
                slug = path.Groups[2].Value,
                en = en,
                year = foundYear
            });
        }

        return result;
    }

    async Task<SearchItem> FindMatch(string title, string originalTitle, int year, string kind)
    {
        string localized = title?.Trim();
        string original = originalTitle?.Trim();
        if (string.IsNullOrWhiteSpace(localized) && string.IsNullOrWhiteSpace(original))
            return null;

        string matchTitle = !string.IsNullOrWhiteSpace(original) ? original : localized;
        int tolerance = init.match_year_tolerance <= 0 ? 1 : init.match_year_tolerance;

        var originalResults = string.IsNullOrWhiteSpace(original)
            ? new List<SearchItem>()
            : await SearchOnce(kind, original, matchTitle);

        bool differentQueries = !string.IsNullOrWhiteSpace(localized)
            && !string.Equals(localized, original, StringComparison.OrdinalIgnoreCase);

        var localizedResults = differentQueries
            ? await SearchOnce(kind, localized, matchTitle)
            : new List<SearchItem>();

        await EnrichSearchYears(originalResults, localizedResults, matchTitle, year);

        if (originalResults.Count > 0 && localizedResults.Count > 0)
        {
            var localizedKeys = new HashSet<string>(
                localizedResults.Select(SearchIdentity).Where(i => !string.IsNullOrEmpty(i)),
                StringComparer.OrdinalIgnoreCase
            );

            var agreed = originalResults
                .Where(i => localizedKeys.Contains(SearchIdentity(i)))
                .ToList();

            var agreedBest = PickBest(agreed, matchTitle, year, tolerance);
            if (agreedBest != null)
                return agreedBest;

            // A localized query is useful for ambiguous English titles such as
            // "Big Mouth": prefer what Krasview itself finds for the Russian title.
            var localizedBest = PickBest(localizedResults, matchTitle, year, tolerance);
            if (localizedBest != null)
                return localizedBest;
        }
        else if (localizedResults.Count > 0)
        {
            var localizedBest = PickBest(localizedResults, matchTitle, year, tolerance);
            if (localizedBest != null)
                return localizedBest;
        }

        return PickBest(originalResults, matchTitle, year, tolerance);
    }

    async Task EnrichSearchYears(List<SearchItem> originalResults, List<SearchItem> localizedResults, string matchTitle, int wantedYear)
    {
        if (wantedYear <= 0)
            return;

        var groups = new[]
        {
            originalResults ?? new List<SearchItem>(),
            localizedResults ?? new List<SearchItem>()
        };

        var candidates = new Dictionary<string, SearchItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in groups.SelectMany(i => i))
        {
            if (item == null || item.year > 0 || string.IsNullOrWhiteSpace(item.url))
                continue;

            if (TitleMatchScore(matchTitle, item.en) < 85)
                continue;

            string identity = SearchIdentity(item);
            if (!string.IsNullOrEmpty(identity) && !candidates.ContainsKey(identity))
                candidates.Add(identity, item);
        }

        foreach (var candidate in candidates.Values.Take(8))
        {
            string html = await GetCached(candidate.url, init.searchhost.TrimEnd('/') + "/");
            int sourceYear = ParseSourceYear(html);
            if (sourceYear <= 0)
                continue;

            string identity = SearchIdentity(candidate);
            foreach (var item in groups.SelectMany(i => i).Where(i => SearchIdentity(i) == identity))
                item.year = sourceYear;
        }
    }

    static int ParseSourceYear(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return 0;

        var keywordsMeta = Regex.Match(
            html,
            @"<meta\b[^>]*\bname\s*=\s*['""]keywords['""][^>]*>",
            RegexOptions.IgnoreCase
        );

        if (!keywordsMeta.Success)
            return 0;

        var yearMatch = Regex.Match(
            HttpUtility.HtmlDecode(keywordsMeta.Value),
            @"(?<!\d)((?:18|19|20|21)\d{2})(?!\d)"
        );

        return yearMatch.Success && int.TryParse(yearMatch.Groups[1].Value, out int year)
            ? year
            : 0;
    }

    static string SearchIdentity(SearchItem item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.slug))
            return string.Empty;

        string slug = HttpUtility.UrlDecode(item.slug).Trim().Trim('/');
        return $"{item.kind}:{slug}".ToLowerInvariant();
    }

    static SearchItem PickBest(List<SearchItem> results, string originalTitle, int year, int tolerance)
    {
        if (results == null || results.Count == 0)
            return null;

        string want = AsciiNorm(originalTitle);
        if (string.IsNullOrEmpty(want))
            return null;

        SearchItem best = null;
        int bestScore = -1;

        foreach (var item in results)
        {
            string en = AsciiNorm(item.en);
            int titleScore = TitleMatchScore(want, en);
            if (titleScore < 0)
                continue;

            int score = titleScore;

            if (year > 0 && item.year > 0)
            {
                int diff = Math.Abs(item.year - year);
                if (diff > tolerance)
                    continue;

                score += diff == 0 ? 30 : 10;
            }
            else if (year > 0)
            {
                score -= 5;
            }

            string slug = AsciiNorm(item.slug);
            if (TitleMatchScore(want, slug) >= 95)
                score += 5;

            if (score > bestScore)
            {
                bestScore = score;
                best = item;
            }
        }

        return bestScore >= 85 ? best : null;
    }

    static string ChooseSearchTitle(string rawTitle, string query)
    {
        if (string.IsNullOrWhiteSpace(rawTitle))
            return string.Empty;

        string want = AsciiNorm(query);
        var parts = Regex.Split(rawTitle, @"\s+/\s+")
            .Select(CleanSearchTitle)
            .Where(i => !string.IsNullOrWhiteSpace(i) && Regex.IsMatch(i, "[A-Za-z]"))
            .ToList();

        if (parts.Count == 0)
        {
            string fallback = CleanSearchTitle(rawTitle);
            return Regex.IsMatch(fallback ?? string.Empty, "[A-Za-z]") ? fallback : string.Empty;
        }

        string exact = parts.FirstOrDefault(i => AsciiNorm(i) == want);
        if (!string.IsNullOrEmpty(exact))
            return exact;

        return parts
            .OrderByDescending(i => SearchTokenOverlap(want, AsciiNorm(i)))
            .ThenBy(i => Math.Abs(AsciiNorm(i).Length - want.Length))
            .FirstOrDefault() ?? string.Empty;
    }

    static string CleanSearchTitle(string value)
    {
        value = HttpUtility.HtmlDecode(value ?? string.Empty).Trim();
        value = Regex.Replace(value, @"(?:\s|\(|\[)+(?:18|19|20|21)\d{2}(?:\)|\])?\s*$", string.Empty);
        return Regex.Replace(value, @"\s+", " ").Trim();
    }

    static int SearchTokenOverlap(string left, string right)
    {
        var a = SearchTokens(left);
        var b = SearchTokens(right);
        if (a.Length == 0 || b.Length == 0)
            return 0;

        var set = new HashSet<string>(b, StringComparer.Ordinal);
        return a.Count(set.Contains);
    }

    static int TitleMatchScore(string want, string candidate)
    {
        want = AsciiNorm(want);
        candidate = AsciiNorm(candidate);
        if (string.IsNullOrEmpty(want) || string.IsNullOrEmpty(candidate))
            return -1;

        if (candidate == want)
            return 100;

        string wantNoArticle = WithoutLeadingArticle(want);
        string candidateNoArticle = WithoutLeadingArticle(candidate);
        if (!string.IsNullOrEmpty(wantNoArticle) && wantNoArticle == candidateNoArticle)
            return 95;

        string[] wantTokens = SearchTokens(want);
        string[] candidateTokens = SearchTokens(candidate);

        // Krasview commonly appends a discriminator such as (US)/(UK) to an
        // otherwise exact English title. Accept a short tail, then let the
        // strict year check in PickBest choose the requested adaptation.
        bool safeShortTitle = wantTokens.Length > 1 || want.Length >= 5;
        if (safeShortTitle && candidate.StartsWith(want + " ", StringComparison.Ordinal)
            && candidateTokens.Length <= wantTokens.Length + 2)
            return 90;

        if (safeShortTitle && want.StartsWith(candidate + " ", StringComparison.Ordinal)
            && wantTokens.Length <= candidateTokens.Length + 2)
            return 88;

        string[] wantNoArticleTokens = SearchTokens(wantNoArticle);
        string[] candidateNoArticleTokens = SearchTokens(candidateNoArticle);
        bool safeArticlePrefix = wantNoArticleTokens.Length > 1 || wantNoArticle.Length >= 5;
        if (safeArticlePrefix && candidateNoArticle.StartsWith(wantNoArticle + " ", StringComparison.Ordinal)
            && candidateNoArticleTokens.Length <= wantNoArticleTokens.Length + 2)
            return 89;

        if (safeArticlePrefix && wantNoArticle.StartsWith(candidateNoArticle + " ", StringComparison.Ordinal)
            && wantNoArticleTokens.Length <= candidateNoArticleTokens.Length + 2)
            return 87;

        // Однословные короткие названия (It, Up...) слишком опасны для contains.
        if (wantTokens.Length <= 1 || candidateTokens.Length == 0)
            return -1;

        var candidateSet = new HashSet<string>(candidateTokens, StringComparer.Ordinal);
        int common = wantTokens.Distinct(StringComparer.Ordinal).Count(candidateSet.Contains);
        int wantCount = wantTokens.Distinct(StringComparer.Ordinal).Count();
        int candidateCount = candidateTokens.Distinct(StringComparer.Ordinal).Count();

        double coverage = wantCount == 0 ? 0 : (double)common / wantCount;
        double precision = candidateCount == 0 ? 0 : (double)common / candidateCount;

        // Все слова запроса присутствуют, допускаем только небольшой хвост вроде Extended/Part.
        if (coverage >= 0.999 && precision >= 0.75)
            return 82;

        // Для длинных названий допускаем небольшую разницу в одном слове.
        if (wantCount >= 4 && coverage >= 0.80 && precision >= 0.80)
            return 75;

        return -1;
    }

    static string[] SearchTokens(string value)
        => AsciiNorm(value).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    static string WithoutLeadingArticle(string value)
    {
        string[] tokens = SearchTokens(value);
        if (tokens.Length > 1 && (tokens[0] == "the" || tokens[0] == "a" || tokens[0] == "an"))
            return string.Join(' ', tokens.Skip(1));

        return string.Join(' ', tokens);
    }

    static string AsciiNorm(string value)
    {
        value = (value ?? string.Empty).ToLowerInvariant().Replace("&", " and ");
        value = Regex.Replace(value, "[^a-z0-9]+", " ");
        return Regex.Replace(value, @"\s+", " ").Trim();
    }

    async Task<VideoConfig> FetchVideoConfig(string sourceHost, string href)
    {
        string baseHost = sourceHost.TrimEnd('/');
        string html = await GetCached(baseHost + href, baseHost + "/");
        var config = ParseVideoConfig(html);
        if (config != null)
            return config;

        var fallbackUrls = new List<string>();

        if (!string.IsNullOrEmpty(html))
        {
            foreach (Match m in Regex.Matches(html, @"(?:content|value|src)=['""]((?:https?:)?//[^'""]+)['""]", RegexOptions.IgnoreCase))
            {
                string url = HttpUtility.HtmlDecode(m.Groups[1].Value);
                if (url.StartsWith("//", StringComparison.Ordinal))
                    url = "https:" + url;

                if (url.Contains("/embed/", StringComparison.OrdinalIgnoreCase) ||
                    url.Contains("/media/", StringComparison.OrdinalIgnoreCase) ||
                    Regex.IsMatch(url, @"https?://(?:www\.)?zedfilm\.ru/\d+", RegexOptions.IgnoreCase))
                {
                    fallbackUrls.Add(url);
                }
            }
        }

        var idMatch = Regex.Match(href ?? string.Empty, @"/video/(\d+)", RegexOptions.IgnoreCase);
        if (idMatch.Success)
        {
            string id = idMatch.Groups[1].Value;
            fallbackUrls.Add($"https://krasview.ru/embed/{id}?play");
            fallbackUrls.Add($"https://hlamer.ru/media/{id}");
            fallbackUrls.Add($"https://zedfilm.ru/{id}");
        }

        foreach (string fallbackUrl in fallbackUrls.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string fallbackHtml = await GetCached(fallbackUrl, baseHost + "/");
            config = ParseVideoConfig(fallbackHtml);
            if (config != null)
                return config;
        }

        return null;
    }

    static VideoConfig ParseVideoConfig(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;

        var match = Regex.Match(html, @"video_Init\(\s*['""]([A-Za-z0-9+/=]+)['""]", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            try
            {
                string json = Encoding.UTF8.GetString(Convert.FromBase64String(match.Groups[1].Value));
                return JsonSerializer.Deserialize<VideoConfig>(json);
            }
            catch
            {
            }
        }

        string trimmed = html.Trim();
        if (trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            try
            {
                var direct = JsonSerializer.Deserialize<VideoConfig>(trimmed);
                if (!string.IsNullOrWhiteSpace(direct?.url))
                    return direct;
            }
            catch
            {
            }
        }

        var streamMatch = Regex.Match(trimmed, @"https?://[^\s'""]+\.(?:mpd|m3u8)(?:\?[^\s'""]*)?", RegexOptions.IgnoreCase);
        if (streamMatch.Success)
            return new VideoConfig { url = streamMatch.Value };

        return null;
    }

    string StreamUrl(VideoConfig data)
    {
        if (string.IsNullOrWhiteSpace(data?.url))
            return null;

        string url = data.url;
        if (init.prefer_hls && Regex.IsMatch(url, @"\.mpd($|\?)", RegexOptions.IgnoreCase))
            url = Regex.Replace(url, @"\.mpd(\?|$)", ".m3u8$1", RegexOptions.IgnoreCase);

        return url;
    }

    static int AudioCount(VideoConfig data)
        => data?.audio_info?.Count ?? 0;

    static List<VideoItem> ParseMovieVideos(string html)
    {
        var primary = new List<VideoItem>();
        var fallback = new List<VideoItem>();
        var seen = new HashSet<string>();

        foreach (Match m in Regex.Matches(html ?? string.Empty, @"href='(/video/(\d+)-([^']+))'", RegexOptions.IgnoreCase))
        {
            string id = m.Groups[2].Value;
            string slug = m.Groups[3].Value;

            if (!seen.Add(id) || SlugBlacklist.IsMatch(slug))
                continue;

            var item = new VideoItem
            {
                id = id,
                href = m.Groups[1].Value,
                slug = slug
            };

            if (Regex.IsMatch(slug, @"(^|[._-])film\b|[._-]film($|[._-])", RegexOptions.IgnoreCase))
                primary.Add(item);
            else
                fallback.Add(item);
        }

        return primary.Count > 0 ? primary : fallback;
    }

    static List<SeasonItem> ParseSeasonCategories(string html)
    {
        var result = new List<SeasonItem>();
        var seen = new HashSet<int>();
        var seenNumbers = new HashSet<int>();

        var matches = Regex.Matches(
            html ?? string.Empty,
            @"<li[^>]+id='c-(\d+)'[^>]*>\s*<a[^>]+href='/(?:series|movie)/[^']+\?category=\d+'[^>]*>([^<]*)",
            RegexOptions.IgnoreCase
        );

        foreach (Match m in matches)
        {
            if (!int.TryParse(m.Groups[1].Value, out int id) || id == 0 || !seen.Add(id))
                continue;

            var numberMatch = Regex.Match(m.Groups[2].Value, @"(\d+)");
            if (!numberMatch.Success || !int.TryParse(numberMatch.Groups[1].Value, out int number) || number <= 0)
                continue;

            // Blocks such as "Дополнительные материалы" are categories, not seasons.
            if (!seenNumbers.Add(number))
                continue;

            result.Add(new SeasonItem
            {
                id = id,
                number = number
            });
        }

        return result.OrderBy(i => i.number).ToList();
    }

    static List<VideoItem> ParseSeriesEpisodes(string html)
    {
        var result = new List<VideoItem>();
        var seenEpisodes = new HashSet<string>();

        foreach (Match m in Regex.Matches(html ?? string.Empty, @"href='(/video/(\d+)-([^']+))'", RegexOptions.IgnoreCase))
        {
            string id = m.Groups[2].Value;
            string slug = m.Groups[3].Value;
            if (SlugBlacklist.IsMatch(slug))
                continue;

            var sm = Regex.Match(slug, @"(\d+)[._-]+(?:sezon|season)[._-]+", RegexOptions.IgnoreCase);
            var em = Regex.Match(slug, @"(\d+)[._-]+(?:seriya|serija|series?)(?:[._-]|$)", RegexOptions.IgnoreCase);
            if (!sm.Success || !em.Success)
                continue;

            if (!int.TryParse(sm.Groups[1].Value, out int season) || !int.TryParse(em.Groups[1].Value, out int episode))
                continue;

            string episodeKey = $"{season}:{episode}";
            if (!seenEpisodes.Add(episodeKey))
                continue;

            result.Add(new VideoItem
            {
                id = id,
                href = m.Groups[1].Value,
                slug = slug,
                s = season,
                e = episode
            });
        }

        return result.OrderBy(i => i.s).ThenBy(i => i.e).ToList();
    }

    async Task<List<VideoItem>> FetchSeasonEpisodes(string sourceHost, string slug, int categoryId, int expectedSeason)
    {
        var all = new List<VideoItem>();
        var seenEpisodes = new HashSet<string>();

        void MergeEpisodes(IEnumerable<VideoItem> items)
        {
            if (items == null)
                return;

            foreach (var item in items.Where(i => expectedSeason <= 0 || i.s == expectedSeason))
            {
                string episodeKey = $"{item.s}:{item.e}";
                if (seenEpisodes.Add(episodeKey))
                    all.Add(item);
            }
        }

        for (int page = 1; page <= 10; page++)
        {
            string url = $"{sourceHost.TrimEnd('/')}/series/{slug}/?category={categoryId}" + (page > 1 ? $"&page={page}" : string.Empty);
            string html = await GetCached(url, $"{sourceHost.TrimEnd('/')}/series/{slug}");
            if (string.IsNullOrEmpty(html))
                break;

            var part = ParseSeriesEpisodes(html);
            if (expectedSeason > 0)
                part = part.Where(i => i.s == expectedSeason).ToList();

            if (part.Count == 0)
                break;

            int before = all.Count;
            MergeEpisodes(part);
            if (all.Count == before)
                break;
        }

        // The category page can lag behind the main series page when a new
        // episode has just been published. Merge fresh S/E links from the
        // series root as a second source.
        if (expectedSeason > 0)
        {
            string rootHtml = await GetCached(
                $"{sourceHost.TrimEnd('/')}/series/{slug}",
                sourceHost.TrimEnd('/') + "/"
            );

            if (!string.IsNullOrEmpty(rootHtml))
                MergeEpisodes(ParseSeriesEpisodes(rootHtml));
        }

        // Individual video pages expose a carousel with neighbouring episodes.
        // Use the newest known episode page as one more source for late additions.
        if (all.Count > 0 && expectedSeason > 0)
        {
            var last = all.OrderByDescending(i => i.e).First();
            string videoHtml = await GetCached(
                sourceHost.TrimEnd('/') + last.href,
                sourceHost.TrimEnd('/') + "/"
            );

            if (!string.IsNullOrEmpty(videoHtml))
                MergeEpisodes(ParseSeriesEpisodes(videoHtml));
        }

        return all.OrderBy(i => i.s).ThenBy(i => i.e).ToList();
    }

    async Task<ITplResult> BuildMovie(string sourceHost, string title, string originalTitle, List<VideoItem> videos)
    {
        if (videos == null || videos.Count == 0)
            return null;

        foreach (var video in videos)
        {
            var config = await FetchVideoConfig(sourceHost, video.href);
            string stream = ProxyStream(StreamUrl(config));
            if (string.IsNullOrEmpty(stream))
                continue;

            string name = JoinName(title, originalTitle);
            if (string.IsNullOrWhiteSpace(name))
                name = "Krasview";

            int audio = AudioCount(config);
            var tpl = new MovieTpl(title, originalTitle, 1);
            tpl.Append(
                audio > 1 ? $"{name} [{audio} озвучек]" : name,
                stream,
                voice_name: audio > 1 ? $"{audio} озвучек" : null
            );
            return tpl;
        }

        return null;
    }

    ITplResult BuildSeasons(string title, string originalTitle, int year, List<SeasonItem> seasons, bool rjson)
    {
        if (seasons == null || seasons.Count == 0)
            return null;

        var tpl = new SeasonTpl(seasons.Count);
        string encTitle = HttpUtility.UrlEncode(title);
        string encOriginal = HttpUtility.UrlEncode(originalTitle);

        for (int i = 0; i < seasons.Count; i++)
        {
            int season = seasons[i].number > 0 ? seasons[i].number : i + 1;
            tpl.Append(
                $"Сезон {season}",
                $"{host}/lite/krasview?title={encTitle}&original_title={encOriginal}&year={year}&serial=1&rjson={rjson}&s={season}",
                season
            );
        }

        return tpl;
    }

    async Task<ITplResult> BuildEpisodes(string sourceHost, string title, string originalTitle, List<VideoItem> episodes, int season)
    {
        var filtered = episodes?
            .Where(i => i.s == season)
            .GroupBy(i => i.e)
            .Select(g => g.First())
            .OrderBy(i => i.e)
            .ToList() ?? new List<VideoItem>();

        if (filtered.Count == 0)
            return null;

        var tpl = new EpisodeTpl(filtered.Count);
        string baseTitle = JoinName(title, originalTitle);
        if (string.IsNullOrWhiteSpace(baseTitle))
            baseTitle = title ?? originalTitle ?? "Krasview";

        foreach (var episode in filtered)
        {
            var config = await FetchVideoConfig(sourceHost, episode.href);
            string stream = ProxyStream(StreamUrl(config));
            if (string.IsNullOrEmpty(stream))
                continue;

            int audio = AudioCount(config);
            tpl.Append(
                $"Серия {episode.e}",
                baseTitle,
                (short)episode.s,
                episode.e.ToString(),
                stream,
                voice_name: audio > 1 ? $"{audio} озвучек" : null
            );
        }

        return tpl;
    }

    string ProxyStream(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        if (url.StartsWith("//", StringComparison.Ordinal))
            url = "https:" + url;

        if (!init.streamproxy)
            return url;

        string referer = string.IsNullOrWhiteSpace(init.stream_referer)
            ? init.moviehost.TrimEnd('/') + "/"
            : init.stream_referer;

        string origin = referer.TrimEnd('/');
        var headers = HeadersModel.Init(
            ("Referer", referer),
            ("Origin", origin),
            ("User-Agent", UA)
        );

        return HostStreamProxy(url, headers, force_streamproxy: true);
    }

    static bool IsMirrorHost(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return Regex.IsMatch(value, @"(smartkino|sersoap|zseek|krasview|hlamer)\.ru$", RegexOptions.IgnoreCase);
    }

    static string JoinName(string title, string originalTitle)
        => string.Join(" / ", new[] { title, originalTitle }.Where(i => !string.IsNullOrWhiteSpace(i)));
}
