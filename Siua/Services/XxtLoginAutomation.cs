using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Siua.Interfaces;

namespace Siua.Services;

/// <summary>仅向受信任的学习通密码登录页提交一次；验证和失败重试交给用户。</summary>
internal static class XxtLoginAutomation
{
    internal static bool IsTrustedLoginUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
        (uri.Host.Equals("passport2.chaoxing.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("passport.chaoxing.com", StringComparison.OrdinalIgnoreCase));

    public static async Task<bool> SubmitOnceAsync(IPage page, string username, string password,
        ILogService log, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsTrustedLoginUrl(page.Url))
        {
            log.AddLog("当前页面不是支持的学习通 HTTPS 登录页，请手动登录");
            return false;
        }
        try
        {
            var phone = page.Locator("#phone");
            var pwd = page.Locator("#pwd");
            var button = page.Locator("#loginBtn");
            await phone.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 }).WaitAsync(cancellationToken);
            if (!IsTrustedLoginUrl(page.Url)) return false;
            await phone.FillAsync(username, new() { Timeout = 10_000 }).WaitAsync(cancellationToken);
            if (!IsTrustedLoginUrl(page.Url)) return false;
            await pwd.FillAsync(password, new() { Timeout = 10_000 }).WaitAsync(cancellationToken);
            if (!IsTrustedLoginUrl(page.Url)) return false;
            if (await phone.InputValueAsync().WaitAsync(cancellationToken) != username ||
                await pwd.InputValueAsync().WaitAsync(cancellationToken) != password)
            {
                log.AddLog("登录输入框未完整接收账号或密码，请在浏览器中检查后手动登录");
                return false;
            }
            cancellationToken.ThrowIfCancellationRequested();
            await button.ClickAsync(new() { Timeout = 10_000 }).WaitAsync(cancellationToken);
            log.AddLog("已填写账号并提交登录；如出现验证码、二次验证或登录错误，请在浏览器中处理");
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Playwright 的 Fill 异常可能包含输入值，不能输出原始异常或调用日志。
            log.AddLog("自动登录未能完成，请在浏览器中手动登录（不会自动重复提交）");
            return false;
        }
    }
}
