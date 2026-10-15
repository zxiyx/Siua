using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Siua.Interfaces;

namespace Siua.Services;

/// <summary>智慧树账号登录只提交一次，验证码和登录失败交给用户手动处理。</summary>
internal static class ZhsLoginService
{
    internal static bool IsTrustedLoginUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
        uri.Host.Equals("login.zhihuishu.com", StringComparison.OrdinalIgnoreCase);

    public static async Task<bool> SubmitOnceAsync(IPage page, string username, string password,
        ILogService log, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsTrustedLoginUrl(page.Url))
        {
            log.AddLog("当前页面不是支持的智慧树 HTTPS 登录页，请手动登录");
            return false;
        }

        var stage = "定位账号登录页";
        try
        {
            var container = page.Locator(".login-container:visible").First;
            var accountTab = container.GetByRole(AriaRole.Tab, new() { Name = "账号登录", Exact = true });
            await accountTab.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 })
                .WaitAsync(cancellationToken);
            if (!IsTrustedLoginUrl(page.Url)) return false;
            if (await accountTab.GetAttributeAsync("aria-selected").WaitAsync(cancellationToken) != "true")
                await accountTab.ClickAsync(new() { Timeout = 10_000 }).WaitAsync(cancellationToken);

            // 页面自动生成 el-id，使用表单范围及字段含义定位，不依赖这些动态 ID。
            var usernameInput = container.Locator(".login-form input[name='mobile']");
            var passwordInput = container.Locator(".login-form input[type='password']");
            stage = "填写账号";
            if (!IsTrustedLoginUrl(page.Url)) return false;
            await usernameInput.FillAsync(username, new() { Timeout = 10_000 }).WaitAsync(cancellationToken);
            if (!IsTrustedLoginUrl(page.Url)) return false;
            stage = "填写密码";
            await passwordInput.FillAsync(password, new() { Timeout = 10_000 }).WaitAsync(cancellationToken);
            if (!IsTrustedLoginUrl(page.Url)) return false;
            if (await usernameInput.InputValueAsync().WaitAsync(cancellationToken) != username ||
                await passwordInput.InputValueAsync().WaitAsync(cancellationToken) != password)
            {
                log.AddLog("智慧树登录输入框未完整接收账号或密码，请在浏览器中检查后手动登录");
                return false;
            }

            var agreement = container.Locator(".privacy-checkbox input[type='checkbox']");
            stage = "勾选登录协议";
            if (!await agreement.IsCheckedAsync().WaitAsync(cancellationToken))
            {
                // Element Plus 将原生框隐藏，点击可见方框以触发原有的 Vue 状态更新。
                await container.Locator(".privacy-checkbox .el-checkbox__inner")
                    .ClickAsync(new() { Timeout = 10_000 }).WaitAsync(cancellationToken);
            }
            if (!IsTrustedLoginUrl(page.Url)) return false;
            if (!await agreement.IsCheckedAsync().WaitAsync(cancellationToken))
            {
                log.AddLog("智慧树登录协议未成功勾选，请在浏览器中手动处理");
                return false;
            }

            stage = "点击登录";
            await container.Locator(".login-bottom .btn-block__grandient_login")
                .ClickAsync(new() { Timeout = 10_000 }).WaitAsync(cancellationToken);
            log.AddLog("已填写智慧树账号密码、勾选协议并提交登录，请在浏览器中手动完成验证码；不会重复提交");
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Fill 的异常可能包含密码，不记录原始异常或 Playwright 调用日志。
            log.AddLog($"智慧树自动登录在{stage}时未能完成，请在浏览器中手动登录（不会自动重复提交）");
            return false;
        }
    }
}
