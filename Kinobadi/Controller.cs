using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Shared;
using Shared.Attributes;
using Shared.Models.Templates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Kinobadi;

public class KinobadiController : BaseOnlineController
{
    FemdInvoke oninvk;

    public KinobadiController() : base(ModInit.conf)
    {
        requestInitialization = () =>
        {
            oninvk = new FemdInvoke
            (
                host,
                "lite/kinobadi",
                httpHydra,
                onstreamtofile => HostStreamProxy(onstreamtofile)
            );
        };
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/kinobadi")]
    async public Task<ActionResult> Index(string title, string original_title, short year, long kinopoisk_id, short s = -1, bool rjson = false, string href = null, bool similar = false)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        if (similar || (kinopoisk_id <= 0 && string.IsNullOrWhiteSpace(href)))
            return await RouteSearch(title, original_title, year, kinopoisk_id);

        ResolveModel resolved;

        if (!string.IsNullOrWhiteSpace(href))
        {
            resolved = await ResolveCard(href, kinopoiskId: kinopoisk_id);
        }
        else
        {
            if (kinopoisk_id <= 0)
                return StageError("search-params", kinopoisk_id);

            var items = await SearchItems(title, year, kinopoisk_id);
            if (items == null || items.Count == 0)
                return StageError("search", kinopoisk_id);

            resolved = await ResolveItems(items, kinopoiskId: kinopoisk_id);
        }

        if (resolved == null)
            return StageError("resolve", kinopoisk_id);

        HttpContext.Response.Headers["X-Kinobadi-IdFile"] = resolved.id_file.ToString();
        HttpContext.Response.Headers["X-Kinobadi-Card"] = resolved.card ?? string.Empty;

        var cache = await InvokeCacheResult<FemdEmbedModel>($"kinobadi:femd:{resolved.id_file}", TimeSpan.FromMinutes(20), async e =>
        {
            var md = await oninvk.Embed(resolved.id_file);
            if (md == null)
                return e.Fail("femd");

            return e.Success(md);
        });

        if (!cache.IsSuccess || cache.Value == null)
            return StageError("femd", kinopoisk_id, resolved.id_file, cache.ErrorMsg);

        var tpl = oninvk.Tpl(
            cache.Value,
            resolved.id_file,
            kinopoisk_id,
            title,
            original_title,
            s,
            rjson,
            resolved.card
        );

        if (tpl == null || tpl.IsEmpty)
            return StageError("template", kinopoisk_id, resolved.id_file, s >= 0 ? $"season={s}" : null);

        HttpContext.Response.Headers["X-Kinobadi-Stage"] = "ok";
        return ContentTpl(tpl);
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/kinobadi/resolve")]
    async public Task<ActionResult> Resolve(string href, string title, short year, long kinopoisk_id, short s = -1, short e = -1)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        ResolveModel model;

        if (string.IsNullOrWhiteSpace(href))
        {
            if (kinopoisk_id <= 0 && string.IsNullOrWhiteSpace(title))
                return StageError("search-params", kinopoisk_id);

            var items = await SearchItems(title, year, kinopoisk_id);
            if (items == null || items.Count == 0)
                return StageError("search", kinopoisk_id);

            model = await ResolveItems(items, s, e, kinopoisk_id);
        }
        else
        {
            model = await ResolveCard(href, s, e, kinopoisk_id);
        }

        if (model == null)
            return StageError("resolve", kinopoisk_id);

        HttpContext.Response.Headers["X-Kinobadi-Stage"] = "resolve-ok";
        HttpContext.Response.Headers["X-Kinobadi-IdFile"] = model.id_file.ToString();
        HttpContext.Response.Headers["X-Kinobadi-Card"] = model.card ?? string.Empty;
        return JsonResult(model);
    }

    async Task<ActionResult> RouteSearch(string title, string original_title, short year, long kinopoisk_id)
    {
        if (kinopoisk_id <= 0 && string.IsNullOrWhiteSpace(title))
            return StageError("search-params", kinopoisk_id);

        var cache = await InvokeCacheResult<SearchModel>($"kinobadi:search:{title}:{year}:{kinopoisk_id}", TimeSpan.FromHours(2), async e =>
        {
            var items = await SearchItems(title, year, kinopoisk_id);
            if (items == null || items.Count == 0)
                return e.Fail("search");

            var tpl = new SimilarTpl(items.Count);
            string encTitle = HttpUtility.UrlEncode(title);
            string encOriginal = HttpUtility.UrlEncode(original_title);

            foreach (var item in items)
            {
                string uri = $"{host}/lite/kinobadi?title={encTitle}&original_title={encOriginal}&year={year}&kinopoisk_id={kinopoisk_id}&href={HttpUtility.UrlEncode(item.href)}";
                tpl.Append(item.title, item.year > 0 ? item.year.ToString() : null, "FEMD", uri, item.img);
            }

            if (tpl.IsEmpty)
                return e.Fail("similar");

            return e.Success(new SearchModel { similar = tpl });
        });

        if (!cache.IsSuccess || cache.Value?.similar == null)
            return StageError("similar", kinopoisk_id, detail: cache.ErrorMsg);

        HttpContext.Response.Headers["X-Kinobadi-Stage"] = "similar-ok";
        return ContentTpl(cache.Value.similar);
    }

    ActionResult StageError(string stage, long kinopoiskId = 0, long idFile = 0, string detail = null)
    {
        HttpContext.Response.Headers["X-Kinobadi-Stage"] = stage;

        if (kinopoiskId > 0)
            HttpContext.Response.Headers["X-Kinobadi-KP"] = kinopoiskId.ToString();

        if (idFile > 0)
            HttpContext.Response.Headers["X-Kinobadi-IdFile"] = idFile.ToString();

        Serilog.Log.Warning(
            "Kinobadi stage={Stage} kp={KinopoiskId} id_file={IdFile} detail={Detail}",
            stage,
            kinopoiskId,
            idFile,
            detail
        );

        string msg = string.IsNullOrWhiteSpace(detail)
            ? $"kinobadi:{stage}"
            : $"kinobadi:{stage}:{detail}";

        return OnError(msg);
    }

    ActionResult JsonResult(ResolveModel model)
    {
        return Content(JsonConvert.SerializeObject(model), "application/json; charset=utf-8");
    }

    async Task<List<SearchItem>> SearchItems(string title, short year, long kinopoisk_id)
    {
        // Основной и самый точный путь: Kinopoisk ID -> Kinobadi search.
        // Название используется только как fallback.
        if (kinopoisk_id > 0)
        {
            var kpItems = RankItems(await SearchQuery(kinopoisk_id.ToString()), title, year);
            if (kpItems != null && kpItems.Count > 0)
                return kpItems;
        }

        if (string.IsNullOrWhiteSpace(title))
            return null;

        return RankItems(await SearchQuery(title), title, year);
    }

    List<SearchItem> RankItems(List<SearchItem> items, string title, short year)
    {
        if (items == null || items.Count == 0)
            return items;

        // A known conflicting year is an identity mismatch, not merely a lower rank.
        // This prevents e.g. a same-title serial from replacing an older movie.
        if (year > 0)
        {
            var sameYear = items.Where(i => i.year == year).ToList();
            if (sameYear.Count > 0)
            {
                items = sameYear;
            }
            else
            {
                var unknownYear = items.Where(i => i.year == 0).ToList();
                if (unknownYear.Count == 0)
                {
                    Serilog.Log.Warning(
                        "Kinobadi search rejected known year mismatch title={Title} expected={Year} got={Years}",
                        title,
                        year,
                        string.Join(',', items.Select(i => i.year).Where(y => y > 0).Distinct().OrderBy(y => y))
                    );
                    return new List<SearchItem>();
                }

                items = unknownYear;
            }
        }

        string nt = Normalize(title);

        return items
            .OrderByDescending(i => !string.IsNullOrWhiteSpace(nt) && Normalize(i.title) == nt)
            .ThenByDescending(i => !string.IsNullOrWhiteSpace(nt) && Normalize(i.title).Contains(nt))
            .ThenBy(i => i.title)
            .ToList();
    }

    async Task<List<SearchItem>> SearchQuery(string query)
    {
        List<SearchItem> items = null;
        string searchUrl = $"{init.host}/film/poisk.php?q={HttpUtility.UrlEncode(query)}";

        await httpHydra.GetSpan(searchUrl, html =>
        {
            items = ParseSearch(html.ToString());
        });

        if (items == null || items.Count == 0)
            Serilog.Log.Warning("Kinobadi search empty query={Query} url={Url}", query, searchUrl);

        return items;
    }

    List<SearchItem> ParseSearch(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        html = WebUtility.HtmlDecode(html);
        var result = new List<SearchItem>();
        var hrefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = Regex.Matches(html, "<a\\b[^>]*href=[\\\"'](?<href>[^\\\"']*/film/(?:file-|poisk-)[0-9]+[^\\\"']*)[\\\"'][^>]*>(?<body>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        foreach (Match match in matches)
        {
            string href = WebUtility.HtmlDecode(match.Groups["href"].Value).Trim();
            if (string.IsNullOrEmpty(href) || !hrefs.Add(href))
                continue;

            string block = match.Value;
            string body = match.Groups["body"].Value;
            string bodyText = Text(body);
            string attrTitle = Attr(block, "title");
            string attrAlt = Attr(block, "alt");
            string spanTitle = Text(Regex.Match(
                body,
                "<span\\b[^>]*class=[\\\"'][^\\\"']*p_title_film[^\\\"']*[\\\"'][^>]*>(.*?)</span>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline
            ).Groups[1].Value);

            // On some Kinobadi layouts p_title_film contains only the year.
            // Prefer semantic title/alt attributes and never render a bare year as a title from that span.
            string name = CleanSearchTitle(attrTitle);
            if (string.IsNullOrWhiteSpace(name))
                name = CleanSearchTitle(attrAlt);
            if (string.IsNullOrWhiteSpace(name) && !IsYearOnly(spanTitle))
                name = CleanSearchTitle(spanTitle);
            if (string.IsNullOrWhiteSpace(name))
                name = CleanSearchTitle(bodyText);

            string id = Regex.Match(href, "/film/(?:file-|poisk-)([0-9]+)", RegexOptions.IgnoreCase).Groups[1].Value;
            if (string.IsNullOrWhiteSpace(name))
                name = string.IsNullOrEmpty(id) ? "Kinobadi" : $"Kinobadi #{id}";

            // Keep year extraction inside the current card. Looking around match.Index can steal
            // the year from a neighbouring result and was the reason for false identities.
            short itemYear = ParseYear($"{attrTitle} {attrAlt} {spanTitle} {bodyText}");

            string img = Attr(block, "data-src");
            if (string.IsNullOrEmpty(img))
                img = Attr(block, "src");
            if (!string.IsNullOrEmpty(img) && img.StartsWith("/"))
                img = init.host + img;

            result.Add(new SearchItem
            {
                href = href,
                title = name,
                year = itemYear,
                img = img
            });
        }

        return result;
    }

    async Task<ResolveModel> ResolveItems(List<SearchItem> items, short season = -1, short episode = -1, long kinopoiskId = 0)
    {
        if (items == null || items.Count == 0)
            return null;

        // One stale provider card must not prevent a later equally ranked candidate from resolving.
        foreach (var item in items.Take(8))
        {
            var resolved = await ResolveCard(item.href, season, episode, kinopoiskId);
            if (resolved != null)
                return resolved;
        }

        return null;
    }

    async Task<ResolveModel> ResolveCard(string href, short season = -1, short episode = -1, long kinopoiskId = 0)
    {
        if (string.IsNullOrWhiteSpace(href))
            return null;

        long routeId = Number(href, "/film/(?:file-|poisk-)([0-9]+)");
        string originalCard = Absolute(href);
        string fileCard = routeId > 0
            ? $"{init.host.TrimEnd('/')}/film/file-{routeId}"
            : originalCard;
        ResolveModel result = null;

        // Preserve the exact search-result route first. Some serial cards work on /poisk-N
        // while forcing /file-N can resolve a different/stale representation. Keep /file-N as fallback.
        foreach (string card in new[] { originalCard, fileCard }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await httpHydra.GetSpan(card, html =>
            {
                string content = WebUtility.HtmlDecode(html.ToString());
                long idFile = Number(content, "(?:[?&])id_file=([0-9]+)");
                long providerKp = Number(content, "(?:[?&])kp=([0-9]+)");
                string player = Regex.Match(content, "https?://[^\\\"'<>\\s]+/kino/pleer(?:_serial)?\\.php\\?[^\\\"'<>\\s]+", RegexOptions.IgnoreCase).Value;

                // A route number alone is not enough to prove that the returned HTML is the media card.
                // Require an explicit id_file or a real FEMD player before inheriting routeId.
                if (idFile <= 0 && string.IsNullOrWhiteSpace(player))
                    return;

                if (idFile <= 0)
                    idFile = routeId;

                if (idFile <= 0)
                    return;

                result = new ResolveModel
                {
                    source = "femd",
                    card = card,
                    player = player,
                    id_file = idFile,
                    kinopoisk_id = kinopoiskId,
                    provider_kp = providerKp,
                    season = season > 0 ? season : (short)0,
                    episode = episode > 0 ? episode : (short)0,
                    embed = FemdInvoke.EmbedUrl(idFile)
                };
            });

            if (result != null)
                break;
        }

        if (result == null && routeId > 0)
        {
            // Last-resort compatibility fallback for cards temporarily unavailable at Kinobadi.
            result = new ResolveModel
            {
                source = "femd",
                card = originalCard,
                id_file = routeId,
                kinopoisk_id = kinopoiskId,
                season = season > 0 ? season : (short)0,
                episode = episode > 0 ? episode : (short)0,
                embed = FemdInvoke.EmbedUrl(routeId)
            };
        }

        if (result == null)
            Serilog.Log.Warning("Kinobadi card resolve empty href={Href} card={Card} kp={KinopoiskId}", href, originalCard, kinopoiskId);

        return result;
    }

    string Absolute(string href)
    {
        href = WebUtility.HtmlDecode(href).Trim();

        if (Uri.TryCreate(href, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute.ToString();
        }

        if (href.StartsWith("//", StringComparison.Ordinal))
            return "https:" + href;

        return new Uri(new Uri(init.host.TrimEnd('/') + "/"), href).ToString();
    }

    static long Number(string input, string pattern)
    {
        var match = Regex.Match(input ?? string.Empty, pattern, RegexOptions.IgnoreCase);
        return match.Success && long.TryParse(match.Groups[1].Value, out long value) ? value : 0;
    }

    static short ParseYear(string input)
    {
        var match = Regex.Match(input ?? string.Empty, "(?<![0-9])(19[0-9]{2}|20[0-9]{2})(?![0-9])");
        return match.Success && short.TryParse(match.Groups[1].Value, out short value) ? value : (short)0;
    }

    static bool IsYearOnly(string value)
    {
        return Regex.IsMatch(value?.Trim() ?? string.Empty, "^(19[0-9]{2}|20[0-9]{2})$");
    }

    static string CleanSearchTitle(string value)
    {
        string text = Text(value);
        if (string.IsNullOrWhiteSpace(text))
            return null;

        text = Regex.Replace(text, "^\\s*Смотреть\\s+", string.Empty, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "\\s+в\\s+хорошем\\s+качестве.*$", string.Empty, RegexOptions.IgnoreCase);

        // Strip a display year only when the candidate also contains letters, so titles such as
        // "1984", "1917" and "2012" remain valid movie names.
        if (Regex.IsMatch(text, "[a-zа-яё]", RegexOptions.IgnoreCase))
        {
            text = Regex.Replace(text, "^\\s*(19[0-9]{2}|20[0-9]{2})\\s*[-–—|,:]?\\s*", string.Empty);
            text = Regex.Replace(text, "\\s*[-–—|,:]?\\s*(19[0-9]{2}|20[0-9]{2})\\s*$", string.Empty);
        }

        return text.Trim();
    }

    static string Attr(string html, string name)
    {
        return WebUtility.HtmlDecode(Regex.Match(html ?? string.Empty, $"\\b{Regex.Escape(name)}=[\\\"']([^\\\"']+)[\\\"']", RegexOptions.IgnoreCase).Groups[1].Value).Trim();
    }

    static string Text(string html)
    {
        return Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html ?? string.Empty, "<[^>]+>", " ")), "\\s+", " ").Trim();
    }

    static string Normalize(string value)
    {
        return Regex.Replace((value ?? string.Empty).ToLowerInvariant().Replace('ё', 'е'), "[^a-zа-я0-9]+", string.Empty);
    }
}
