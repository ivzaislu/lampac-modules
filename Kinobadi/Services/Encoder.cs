using Shared.Models.Events;
using Shared.Services.Pools;
using Shared.Services.Utilities;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Kinobadi;

public static class Encoder
{
    const string marker = "/x-en-x/";
    const string femdOrigin = "https://api.femd.ws";
    const string femdUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36";
    const string femdSecChUa = "\"Not=A?Brand\";v=\"99\", \"Google Chrome\";v=\"151\", \"Chromium\";v=\"151\"";
    const string femdAcceptLanguage = "de-DE,de;q=0.9,en-US;q=0.8,en;q=0.7,ru;q=0.6";

    static readonly ConcurrentDictionary<string, long> unixTimes = new(StringComparer.Ordinal);

    public static void Register(string url, long unixTime)
    {
        if (string.IsNullOrWhiteSpace(url) || unixTime <= 0)
            return;

        if (unixTimes.Count > 4096)
            unixTimes.Clear();

        unixTimes[url] = unixTime;
    }

    public static Task ProxyApiCreateHttpRequest(EventProxyApiCreateHttpRequest e)
    {
        if (e.plugin != null && e.plugin.Equals("kinobadi", StringComparison.OrdinalIgnoreCase))
        {
            long unixTime = ResolveUnixTime(e);

            if (!e.requestMessage.RequestUri.AbsolutePath.Contains(marker))
            {
                string newUri = EncodeUri(e.requestMessage.RequestUri, unixTime);
                if (newUri != null)
                    e.requestMessage.RequestUri = new Uri(newUri);
            }

            // Match the FEMD player context used to obtain the signed stream URLs.
            SetHeader(e, "Origin", femdOrigin);
            SetHeader(e, "User-Agent", femdUserAgent);
            SetHeader(e, "sec-ch-ua", femdSecChUa);
            SetHeader(e, "sec-ch-ua-mobile", "?0");
            SetHeader(e, "sec-ch-ua-platform", "\"Windows\"");
            SetHeader(e, "Accept-Language", femdAcceptLanguage);
            e.requestMessage.Headers.Referrer = new Uri(femdOrigin + "/");
        }

        return Task.CompletedTask;
    }

    public static string Uri(string url)
    {
        if (string.IsNullOrEmpty(url) || url.Contains(marker))
            return url;

        string newUri = EncodeUri(new Uri(url), 0);
        if (newUri == null)
            return url;

        return newUri + (url.Contains(".vtt") ? "#.vtt" : url.Contains(".mpd") ? "#.mpd" : "#.m3u8");
    }

    static long ResolveUnixTime(EventProxyApiCreateHttpRequest e)
    {
        if (e.decryptLink?.userdata is long inherited && inherited > 0)
            return inherited;

        string original = e.decryptLink?.uri;
        if (!string.IsNullOrEmpty(original) && unixTimes.TryGetValue(original, out long registered) && registered > 0)
        {
            // ProxyMpd/ProxyM3u8 clone decryptLink when creating child links, so this
            // metadata follows BaseURL/init/media requests without leaking into CDN query params.
            e.decryptLink.userdata = registered;
            return registered;
        }

        return 0;
    }

    static void SetHeader(EventProxyApiCreateHttpRequest e, string name, string value)
    {
        e.requestMessage.Headers.Remove(name);
        e.requestMessage.Headers.TryAddWithoutValidation(name, value);
    }

    static string EncodeUri(Uri uri, long unixTime)
    {
        long seconds = unixTime > 0
            ? unixTime
            : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Same rounding rule as the FEMD player/HAR contract: int(unixTime / 3600 + 0.5).
        long n = (seconds + 1800) / 3600;

        string newUri = null;

        CrypTo.Base64($"{n}/{uri.AbsolutePath}{uri.Query}", base64 =>
        {
            var sb = StringBuilderPool.ThreadInstance;

            sb.Append(uri.Scheme)
              .Append("://")
              .Append(uri.Authority)
              .Append(marker);

            for (int i = 0; i < base64.Length; i++)
            {
                char c = base64[i];

                sb.Append(c switch
                {
                    'A' => 'D', 'B' => 'l', 'C' => 'C', 'D' => 'h', 'E' => 'E', 'F' => 'X',
                    'G' => 'i', 'H' => 't', 'I' => 'L', 'J' => 'O', 'K' => 'N', 'L' => 'Y',
                    'M' => 'R', 'N' => 'k', 'O' => 'F', 'P' => 'j', 'Q' => 'A', 'R' => 's',
                    'S' => 'n', 'T' => 'B', 'U' => 'b', 'V' => 'y', 'W' => 'm', 'X' => 'W',
                    'Y' => 'z', 'Z' => 'S', 'a' => 'H', 'b' => 'M', 'c' => 'q', 'd' => 'K',
                    'e' => 'P', 'f' => 'g', 'g' => 'Q', 'h' => 'Z', 'i' => 'p', 'j' => 'v',
                    'k' => 'w', 'l' => 'e', 'm' => 'r', 'n' => 'o', 'o' => 'f', 'p' => 'J',
                    'q' => 'T', 'r' => 'V', 's' => 'd', 't' => 'I', 'u' => 'u', 'v' => 'U',
                    'w' => 'c', 'x' => 'x', 'y' => 'a', 'z' => 'G',
                    _ => c
                });
            }

            newUri = sb.ToString();
        });

        return newUri;
    }
}
