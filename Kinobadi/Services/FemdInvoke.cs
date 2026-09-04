using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Kinobadi;

public struct FemdInvoke
{
    const string FemdHost = "https://api.femd.ws";
    const string FemdConsumerHost = "kinotik.top";

    static readonly IReadOnlyList<HeadersModel> EmbedHeaders = HeadersModel.Init(
        ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7"),
        ("Accept-Language", "de-DE,de;q=0.9,en-US;q=0.8,en;q=0.7,ru;q=0.6"),
        ("Cache-Control", "no-cache"),
        ("Pragma", "no-cache"),
        ("Sec-Fetch-Dest", "iframe"),
        ("Sec-Fetch-Mode", "navigate"),
        ("Sec-Fetch-Site", "cross-site"),
        ("Sec-Fetch-Storage-Access", "active"),
        ("Upgrade-Insecure-Requests", "1"),
        ("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36"),
        ("sec-ch-ua", "\"Not=A?Brand\";v=\"99\", \"Google Chrome\";v=\"151\", \"Chromium\";v=\"151\""),
        ("sec-ch-ua-mobile", "?0"),
        ("sec-ch-ua-platform", "\"Windows\"")
    );

    string host, route;
    Func<string, string> onstreamfile;
    HttpHydra httpHydra;

    public FemdInvoke(string host, string route, HttpHydra httpHydra, Func<string, string> onstreamfile)
    {
        this.host = host != null ? $"{host}/" : null;
        this.route = route;
        this.httpHydra = httpHydra;
        this.onstreamfile = onstreamfile;
    }

    public static string EmbedUrl(long idFile)
    {
        return $"{FemdHost}/embed/movie/{idFile}?sharing=false&host={FemdConsumerHost}";
    }

    async public Task<FemdEmbedModel> Embed(long idFile)
    {
        if (idFile <= 0)
            return null;

        FemdEmbedModel embed = null;

        await httpHydra.GetSpan(EmbedUrl(idFile), content =>
        {
            string html = content.ToString();
            string token = Regex.Match(
                html,
                "var\\s+lok\\s*=\\s*1\\s*,\\s*[A-Za-z0-9_$]+\\s*=\\s*\\\"([^\\\"]+)\\\"",
                RegexOptions.IgnoreCase
            ).Groups[1].Value;

            long femdUnixTime = 0;
            long.TryParse(
                Regex.Match(html, "\\bunixTime\\s*=\\s*(\\d+)", RegexOptions.IgnoreCase).Groups[1].Value,
                out femdUnixTime
            );

            if (!content.Contains("seasons:", StringComparison.Ordinal))
            {
                var rx = Rx.Split("makePlayer\\(\\{", content);
                if (1 > rx.Count)
                    return;

                var movie = new FemdMovie()
                {
                    hls = Rx.Match(rx[1].Span, "hls: +\\\"(https?://[^\\\"]+\\.m3u[^\\\"]+)\\\""),
                    dash = Rx.Match(rx[1].Span, "dasha?: +\\\"(https?://[^\\\"]+\\.mp[^\\\"]+)\\\""),
                    name = Rx.Match(rx[1].Span, "audio: +\\{\\\"names\\\":\\[\\\"([^\\\"]+)\\\"")
                };

                if (string.IsNullOrWhiteSpace(movie.name))
                    movie.name = "По умолчанию";

                movie.voicename = movie.name;

                try
                {
                    ReadOnlySpan<char> cc = Rx.Slice(rx[1].Span, "cc:", "\n");
                    if (cc != ReadOnlySpan<char>.Empty && cc.Contains("[", StringComparison.Ordinal))
                    {
                        movie.cc = JsonSerializer.Deserialize<FemdCc[]>(cc, new JsonSerializerOptions
                        {
                            AllowTrailingCommas = true
                        });
                    }
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "{Class} {CatchId} id_file={IdFile}", "Kinobadi", "femd_movie_cc", idFile);
                }

                if (!string.IsNullOrWhiteSpace(movie.hls) || !string.IsNullOrWhiteSpace(movie.dash))
                {
                    embed = new FemdEmbedModel()
                    {
                        token = token,
                        unixTime = femdUnixTime,
                        movie = movie
                    };
                }
            }
            else
            {
                try
                {
                    string json = ExtractArray(html, "seasons:");
                    if (string.IsNullOrEmpty(json))
                        return;

                    var root = JsonSerializer.Deserialize<FemdSerial[]>(json, new JsonSerializerOptions
                    {
                        AllowTrailingCommas = true,
                        PropertyNameCaseInsensitive = true
                    });

                    if (root != null && root.Length > 0)
                    {
                        var serial = root
                            .Where(i => !i.blocked && i.episodes != null && i.episodes.Length > 0)
                            .OrderBy(i => i.season)
                            .ToArray();

                        if (serial.Length > 0)
                        {
                            embed = new FemdEmbedModel()
                            {
                                token = token,
                                unixTime = femdUnixTime,
                                serial = serial
                            };
                        }
                    }
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(
                        ex,
                        "{Class} {CatchId} id_file={IdFile} html_len={HtmlLength}",
                        "Kinobadi",
                        "femd_serial",
                        idFile,
                        html.Length
                    );
                }
            }
        }, addheaders: EmbedHeaders);

        return embed;
    }

    public ITplResult Tpl(FemdEmbedModel md, long idFile, long kinopoiskId, string title, string originalTitle, short s, bool rjson = false, string href = null)
    {
        if (md == null)
            return default;

        if (md.movie != null)
        {
            // FEMD DASH carries the 1080p representation; HLS is the compatibility fallback.
            string stream = md.movie.dash ?? md.movie.hls;
            stream = Protect(stream, md.token);

            if (string.IsNullOrEmpty(stream))
                return default;

            var mtpl = new MovieTpl(title, originalTitle, 1);

            SubtitleTpl subtitles = null;
            if (md.movie.cc != null && md.movie.cc.Length > 0)
            {
                subtitles = new SubtitleTpl(md.movie.cc.Length);
                foreach (var cc in md.movie.cc)
                {
                    if (!string.IsNullOrEmpty(cc.url) && !string.IsNullOrEmpty(cc.name))
                        subtitles.Append(cc.name, ProxyStream(cc.url, md.unixTime));
                }
            }

            mtpl.Append(
                md.movie.name,
                ProxyStream(stream, md.unixTime),
                subtitles: subtitles,
                voice_name: md.movie.voicename
            );

            return mtpl;
        }

        if (md.serial == null || md.serial.Length == 0)
            return default;

        string encTitle = HttpUtility.UrlEncode(title);
        string encOriginal = HttpUtility.UrlEncode(originalTitle);
        string encHref = HttpUtility.UrlEncode(href);
        string hrefQuery = string.IsNullOrWhiteSpace(encHref) ? string.Empty : $"&href={encHref}";

        if (s == -1)
        {
            var tpl = new SeasonTpl(md.serial.Length);

            foreach (var season in md.serial)
            {
                tpl.Append(
                    $"{season.season} сезон",
                    host + $"{route}?rjson={rjson}&kinopoisk_id={kinopoiskId}&title={encTitle}&original_title={encOriginal}&s={season.season}{hrefQuery}",
                    season.season
                );
            }

            return tpl;
        }

        var episodes = md.serial.FirstOrDefault(i => i.season == s)?.episodes;
        if (episodes == null)
            return default;

        var etpl = new EpisodeTpl(episodes.Length);

        foreach (var episode in episodes)
        {
            // Prefer FEMD DASH (normally 1080p); fall back to HLS when DASH is absent.
            string stream = Protect(episode.dasha ?? episode.dash ?? episode.hls, md.token);
            if (string.IsNullOrEmpty(stream) || string.IsNullOrEmpty(episode.episode))
                continue;

            string voicename = string.Empty;
            if (episode.audio?.names != null)
                voicename = string.Join(", ", episode.audio.names.Where(i => !string.Equals(i, "delete", StringComparison.OrdinalIgnoreCase)));

            SubtitleTpl subtitles = null;
            if (episode.cc != null && episode.cc.Length > 0)
            {
                subtitles = new SubtitleTpl(episode.cc.Length);
                foreach (var cc in episode.cc)
                {
                    if (!string.IsNullOrEmpty(cc.url))
                        subtitles.Append(cc.name, ProxyStream(cc.url, md.unixTime));
                }
            }

            etpl.Append(
                $"{episode.episode} серия",
                title ?? originalTitle,
                s,
                episode.episode,
                ProxyStream(stream, md.unixTime),
                subtitles: subtitles,
                voice_name: voicename
            );
        }

        return etpl;
    }

    string ProxyStream(string url, long unixTime)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url;

        url = url.Replace("\\u0026", "&");
        Encoder.Register(url, unixTime);
        return onstreamfile.Invoke(url);
    }

    static string ExtractArray(string html, string marker)
    {
        if (string.IsNullOrEmpty(html) || string.IsNullOrEmpty(marker))
            return null;

        int markerIndex = html.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
            return null;

        int start = html.IndexOf('[', markerIndex + marker.Length);
        if (start < 0)
            return null;

        int depth = 0;
        char quote = '\0';
        bool escape = false;

        for (int i = start; i < html.Length; i++)
        {
            char c = html[i];

            if (quote != '\0')
            {
                if (escape)
                {
                    escape = false;
                }
                else if (c == '\\')
                {
                    escape = true;
                }
                else if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (c == '\"' || c == '\'')
            {
                quote = c;
                continue;
            }

            if (c == '[')
            {
                depth++;
            }
            else if (c == ']')
            {
                depth--;
                if (depth == 0)
                    return html.Substring(start, i - start + 1);
            }
        }

        return null;
    }

    static string Protect(string url, string token)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url;

        url = url.Replace("\\u0026", "&");

        if (string.IsNullOrWhiteSpace(token))
            return url;

        if (url.EndsWith("&" + token, StringComparison.Ordinal) || url.EndsWith("?" + token, StringComparison.Ordinal))
            return url;

        return url + (url.Contains('?') ? "&" : "?") + token;
    }
}
