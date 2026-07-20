using System;
using System.Net;

namespace Siua.Common;

/// <summary>负责验证并创建 Pix2Text 服务端点。</summary>
public static class Pix2TextEndpoint
{
    public static bool TryCreate(
        string? host,
        int port,
        out IPAddress listenAddress,
        out Uri serviceUri,
        out string errorMessage)
    {
        listenAddress = IPAddress.Loopback;
        serviceUri = new Uri("http://127.0.0.1:8503/");

        if (!IPAddress.TryParse(host?.Trim(), out var parsedAddress))
        {
            errorMessage = "请输入有效的 IPv4 或 IPv6 地址";
            return false;
        }

        if (port is <= 0 or > 65535)
        {
            errorMessage = "端口范围应为 1–65535";
            return false;
        }

        listenAddress = parsedAddress;
        var connectionAddress = parsedAddress.Equals(IPAddress.Any)
            ? IPAddress.Loopback
            : parsedAddress.Equals(IPAddress.IPv6Any)
                ? IPAddress.IPv6Loopback
                : parsedAddress;
        serviceUri = new UriBuilder(Uri.UriSchemeHttp, connectionAddress.ToString(), port, "/").Uri;
        errorMessage = string.Empty;
        return true;
    }
}
