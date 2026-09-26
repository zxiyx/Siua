using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Siua.Services;

public sealed record ApiConnectionResult(bool IsReachable, bool IsSuccess, string Message, long ElapsedMilliseconds = 0);

/// <summary>探测配置地址的 HTTP 可达性；任何响应均算连通，不验证密钥或模型。</summary>
public sealed class ApiConnectionTester : IDisposable
{
    private const int TimeoutSeconds = 8;
    private const int MaxRetries = 1;
    private readonly HttpClient _client;

    public ApiConnectionTester(HttpMessageHandler? handler = null)
    {
        _client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(TimeoutSeconds)
        };
    }

    public async Task<ApiConnectionResult> TestAsync(string? address, CancellationToken cancellationToken = default)
    {
        var value = address?.Trim() ?? string.Empty;
        if (!value.Contains("://", StringComparison.Ordinal))
            value = "https://" + value;
        if (string.IsNullOrWhiteSpace(address) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(endpoint.Host) || !string.IsNullOrEmpty(endpoint.UserInfo))
            return new(false, false, "请输入有效的 HTTP 或 HTTPS 接口地址。");

        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 与 CC Switch 的 reachability 探测一致：延迟只统计当前这次请求的响应头耗时。
            var timer = Stopwatch.StartNew();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.Accept.ParseAdd("*/*");
                request.Headers.AcceptEncoding.ParseAdd("identity");
                using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                timer.Stop();
                return new(true, true,
                    $"接口已响应，延迟 {timer.ElapsedMilliseconds} ms（HTTP {(int)response.StatusCode}）。",
                    timer.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt < MaxRetries) continue;
                return new(false, false, $"连接超时（每次 {TimeoutSeconds} 秒，已重试 {MaxRetries} 次）。", timer.ElapsedMilliseconds);
            }
            catch (HttpRequestException error)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (attempt < MaxRetries && IsTransientFailure(error)) continue;
                return new(false, false, "无法连接接口，请检查接口地址、网络或证书。", timer.ElapsedMilliseconds);
            }
        }
    }

    private static bool IsTransientFailure(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (current is TimeoutException || current is SocketException
                { SocketErrorCode: SocketError.TimedOut or SocketError.ConnectionAborted })
                return true;
        }
        return false;
    }

    public void Dispose() => _client.Dispose();
}
