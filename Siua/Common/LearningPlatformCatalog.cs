using System;

namespace Siua.Common;

public static class LearningPlatformCatalog
{
    public const string XueXiTong = "学习通";
    public const string ZhiHuiShu = "智慧树";
    public const string ChineseUniversityMooc = "中国大学MOOC";
    public const string Unipus = "U校园";

    public static bool IsSupported(string? platform) =>
        string.Equals(platform, XueXiTong, StringComparison.Ordinal) ||
        string.Equals(platform, ZhiHuiShu, StringComparison.Ordinal);

    public static bool TryValidateCourseUrl(
        string? platform,
        string? value,
        out string normalized,
        out string errorMessage)
    {
        normalized = value?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errorMessage = "地址格式无效，请以 http:// 或 https:// 开头";
            return false;
        }

        if (string.Equals(platform, XueXiTong, StringComparison.Ordinal) &&
            !IsHostOrSubdomain(uri.Host, "chaoxing.com") &&
            !IsHostOrSubdomain(uri.Host, "xuexitong.com"))
        {
            errorMessage = "学习通课程地址应来自 chaoxing.com";
            return false;
        }

        if (string.Equals(platform, ZhiHuiShu, StringComparison.Ordinal) &&
            !IsHostOrSubdomain(uri.Host, "zhihuishu.com") &&
            !IsHostOrSubdomain(uri.Host, "zhidao.com"))
        {
            errorMessage = "智慧树课程地址应来自 zhihuishu.com 或 zhidao.com";
            return false;
        }

        normalized = uri.AbsoluteUri;
        errorMessage = string.Empty;
        return true;
    }

    private static bool IsHostOrSubdomain(string host, string expectedHost) =>
        host.Equals(expectedHost, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith($".{expectedHost}", StringComparison.OrdinalIgnoreCase);
}
