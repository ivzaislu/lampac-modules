using Shared.Models.Base;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Web;

namespace FanCDN2_0;

internal static class FanCDN2_0Helper
{
    #region Search
    public static IEnumerable<(string title, string originalTitle)> SearchNames(string title, string originalTitle)
    {
        yield return (title, originalTitle);

        if (!string.IsNullOrWhiteSpace(originalTitle) &&
            !originalTitle.Equals(title, StringComparison.OrdinalIgnoreCase))
            yield return (originalTitle, title);
    }
    #endregion

    #region HTTP
    public static bool IsChallengeResponse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        return value.Contains("cf-chl-", StringComparison.OrdinalIgnoreCase)
            || value.Contains("challenge-platform", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Just a moment", StringComparison.OrdinalIgnoreCase);
    }

    public static bool RequiresAuth(string html)
    {
        if (string.IsNullOrEmpty(html))
            return false;

        return html.Contains("требуется вход в систему", StringComparison.OrdinalIgnoreCase)
            || html.Contains("для доступа к видеоконтенту необходимо иметь учётную запись", StringComparison.OrdinalIgnoreCase)
            || html.Contains("для доступа к видеоконтенту необходимо иметь учетную запись", StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<HeadersModel> DocumentHeaders(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return null;

        string root = host.TrimEnd('/');
        return HeadersModel.Init(
            ("referer", $"{root}/"),
            ("sec-fetch-dest", "document"),
            ("sec-fetch-mode", "navigate"),
            ("sec-fetch-site", "same-origin")
        );
    }
    #endregion

    #region URL
    public static string NormalizeSiteUrl(string host, string rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl) ||
            string.IsNullOrWhiteSpace(host) ||
            !Uri.TryCreate(host.TrimEnd('/') + "/", UriKind.Absolute, out Uri baseUri))
            return null;

        string value = HttpUtility.HtmlDecode(rawUrl.Trim()).Replace("\\/", "/");
        if (value.StartsWith("//"))
            value = baseUri.Scheme + ":" + value;

        if (!Uri.TryCreate(baseUri, value, out Uri uri) ||
            !uri.Host.Equals(baseUri.Host, StringComparison.OrdinalIgnoreCase))
            return null;

        return uri.ToString();
    }

    public static string NormalizeFanCdnUrl(string rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
            return null;

        string value = HttpUtility.HtmlDecode(rawUrl.Trim()).Replace("\\/", "/");
        if (value.StartsWith("//"))
            value = "https:" + value;

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri))
            return null;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return null;

        if (!uri.Host.Equals("cdn.fancdn.net", StringComparison.OrdinalIgnoreCase) &&
            !uri.Host.EndsWith(".cdn.fancdn.net", StringComparison.OrdinalIgnoreCase))
            return null;

        return uri.ToString();
    }

    public static string SeriesRootPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";

        string value = path.TrimEnd('/');
        if (value.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            value = value.Substring(0, value.Length - 5);

        Match serialPath = Regex.Match(value, @"^(.*?)/[0-9]+-season(?:/[0-9]+-episode)?$", RegexOptions.IgnoreCase);
        if (serialPath.Success && !string.IsNullOrEmpty(serialPath.Groups[1].Value))
            value = serialPath.Groups[1].Value;

        return string.IsNullOrEmpty(value) ? "/" : value;
    }
    #endregion
}
