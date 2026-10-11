using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core.Zhs;

/// <summary>等待智慧树认证，并从个人课程页返回启动任务时指定的课程。</summary>
public static class ZhsLoginNavigator
{
    private const string OldCatalog = "div.course-catalog div.chapter-wrapper div.el-collapse-item";

    public static bool IsAuthenticationPage(string url)
    {
        if (!TryParse(url, out var uri)) return false;
        return uri.Host.Equals("login.zhihuishu.com", StringComparison.OrdinalIgnoreCase) ||
            (uri.Host.Equals("onlineservice-api.zhihuishu.com", StringComparison.OrdinalIgnoreCase) &&
             uri.AbsolutePath.StartsWith("/gateway/f/v1/login/", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsPersonalCoursePage(string url) => TryParse(url, out var uri) &&
        (uri.Host.Equals("onlineweb.zhihuishu.com", StringComparison.OrdinalIgnoreCase) || IsHikeCourseCenter(url));

    private static bool IsHikeCourseCenter(string url) => TryParse(url, out var uri) &&
        uri.Host.Equals("hike-teaching-center.polymas.com", StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath.TrimEnd('/').Equals("/stu-hike/agent-course-hike/ai-course-center", StringComparison.OrdinalIgnoreCase);

    public static bool IsTargetCourse(string url, string courseUrl)
    {
        if (!TryParse(url, out var current) || !TryParse(courseUrl, out var target) ||
            !IsCourseHost(target.Host) || !current.Host.Equals(target.Host, StringComparison.OrdinalIgnoreCase) ||
            !current.AbsolutePath.TrimEnd('/').Equals(target.AbsolutePath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            return false;

        // 兼容旧版 hash 路由；认证附加参数不影响课程身份比较。
        var targetFragment = target.Fragment.Split('?')[0];
        if (targetFragment.Length > 1 && current.Fragment.Split('?')[0] != targetFragment) return false;
        var currentParameters = Parameters(current);
        foreach (var pair in Parameters(target))
            if (pair.Key.EndsWith("id", StringComparison.OrdinalIgnoreCase) &&
                (!currentParameters.TryGetValue(pair.Key, out var value) || value != pair.Value))
                return false;
        return true;
    }

    public static async Task<IPage> OpenCourseAsync(IPage page, string courseUrl, Action<string> log,
        CancellationToken cancellationToken = default, int loginTimeoutMilliseconds = 300_000,
        int courseTimeoutMilliseconds = 30_000,
        Func<IPage, CancellationToken, Task>? submitLogin = null)
    {
        if (!TryParse(courseUrl, out var target) || !IsCourseHost(target.Host))
            throw new InvalidOperationException("请填写智慧树具体课程的播放地址，不能使用登录页或个人课程页地址。");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(loginTimeoutMilliseconds);
        var token = deadline.Token;
        var timer = Stopwatch.StartNew();
        var waitingForLogin = false;
        var loggedWaiting = false;
        var loginAttempted = false;
        var returnAttempts = 0;
        var returnedFromPersonalPage = false;
        var courseStarted = 0L;
        var readySince = -1L;
        var landingSince = -1L;
        var lastUrl = "";
        var context = page.Context;
        var existingPages = new HashSet<IPage>(context.Pages);
        var loggedCourseCenter = false;
        Exception? lastNavigationError = null;

        async Task NavigateToCourseAsync()
        {
            courseStarted = timer.ElapsedMilliseconds;
            readySince = -1;
            try
            {
                // Commit 后由下面的 DOM 检查判断是否真正就绪，避免把首次 DOMContentLoaded 当作登录成功。
                await page.GotoAsync(courseUrl, new PageGotoOptions
                { WaitUntil = WaitUntilState.Commit, Timeout = courseTimeoutMilliseconds }).WaitAsync(token);
            }
            catch (TimeoutException exception) { lastNavigationError = exception; }
            catch (PlaywrightException exception) when (!page.IsClosed && IsNavigationRace(exception))
            { lastNavigationError = exception; }
        }

        try
        {
            log("正在打开智慧树课程，等待登录和课程加载...");
            await NavigateToCourseAsync();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                // 课程中心通过新标签页打开课程，不能一直等待原登录页跳转。
                foreach (var candidate in context.Pages)
                {
                    if (candidate == page || existingPages.Contains(candidate) || candidate.IsClosed ||
                        !IsTargetCourse(candidate.Url, courseUrl)) continue;
                    if (!await IsCatalogReadyAsync(candidate, target, token)) continue;
                    existingPages.UnionWith(context.Pages);
                    page = candidate;
                    courseStarted = timer.ElapsedMilliseconds;
                    readySince = landingSince = -1;
                    log("检测到新打开的目标课程页面，正在确认章节目录...");
                    break;
                }
                if (page.IsClosed) throw new InvalidOperationException("等待智慧树登录时浏览器页面已关闭。");
                var url = page.Url;
                if (url != lastUrl)
                {
                    readySince = landingSince = -1;
                    lastUrl = url;
                }
                if (IsAuthenticationPage(url))
                {
                    waitingForLogin = true;
                    readySince = -1;
                    if (!loggedWaiting) { log("检测到智慧树登录或认证页面，请完成登录，正在等待..."); loggedWaiting = true; }
                    // 只在确实回到登录流程后允许再次从个人课程页返回，防止跳转循环。
                    returnedFromPersonalPage = false;
                    if (!loginAttempted && submitLogin is not null &&
                        new Uri(url).Host.Equals("login.zhihuishu.com", StringComparison.OrdinalIgnoreCase) &&
                        await IsDocumentReadyAsync(page, token) &&
                        await page.Locator(".login-container:visible").CountAsync().WaitAsync(token) > 0)
                    {
                        // 一次任务只填写、提交一次，停留在验证码或重新返回登录页也不重试。
                        loginAttempted = true;
                        await submitLogin(page, token);
                    }
                }
                else
                {
                    if (waitingForLogin) { courseStarted = timer.ElapsedMilliseconds; waitingForLogin = false; }
                    if (IsHikeCourseCenter(url))
                    {
                        if (!loggedCourseCenter)
                        {
                            log("已到达课程中心，请点击本次任务对应的课程；新打开的学习页面将自动接管。");
                            loggedCourseCenter = true;
                        }
                        // 这里需要课程中心发起课程跳转，保留页面等待用户点击；仍受总超时约束。
                        courseStarted = timer.ElapsedMilliseconds;
                    }
                    else if (IsPersonalCoursePage(url))
                    {
                        if (landingSince < 0) landingSince = timer.ElapsedMilliseconds;
                        if (!returnedFromPersonalPage && timer.ElapsedMilliseconds - landingSince >= 500 &&
                            await IsDocumentReadyAsync(page, token))
                        {
                            if (++returnAttempts > 3)
                                throw new InvalidOperationException("智慧树反复返回登录或个人课程页，请检查课程权限和登录状态后重试。");
                            returnedFromPersonalPage = true;
                            log("已到达个人课程页，正在打开指定课程...");
                            await NavigateToCourseAsync();
                        }
                    }
                    else if (IsTargetCourse(url, courseUrl) && await IsCatalogReadyAsync(page, target, token))
                    {
                        if (readySince < 0) readySince = timer.ElapsedMilliseconds;
                        if (timer.ElapsedMilliseconds - readySince >= 1_000 && IsTargetCourse(page.Url, courseUrl))
                            return page;
                    }
                    else readySince = -1;

                    if (timer.ElapsedMilliseconds - courseStarted >= courseTimeoutMilliseconds)
                        throw new TimeoutException("智慧树目标课程加载失败：未能进入指定课程并加载章节目录。", lastNavigationError);
                }
                await Task.Delay(200, token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(waitingForLogin
                ? "等待智慧树登录超时，请完成登录后重新启动任务。"
                : "智慧树目标课程加载超时，未确认课程目录就绪。", lastNavigationError);
        }
    }

    private static async Task<bool> IsDocumentReadyAsync(IPage page, CancellationToken token)
    {
        try { return await page.EvaluateAsync<bool>("document.readyState !== 'loading'").WaitAsync(token); }
        catch (PlaywrightException exception) when (!page.IsClosed && IsNavigationRace(exception)) { return false; }
    }

    private static async Task<bool> IsCatalogReadyAsync(IPage page, Uri target, CancellationToken token)
    {
        if (!await IsDocumentReadyAsync(page, token)) return false;
        var selector = target.Host.Equals("studyvideoh5.zhihuishu.com", StringComparison.OrdinalIgnoreCase)
            ? ".chapterScrollbar ul.list" : OldCatalog;
        try
        {
            var catalog = page.Locator(selector).First;
            return await catalog.CountAsync().WaitAsync(token) > 0 && await catalog.IsVisibleAsync().WaitAsync(token);
        }
        catch (PlaywrightException exception) when (!page.IsClosed && IsNavigationRace(exception)) { return false; }
    }

    private static bool IsNavigationRace(PlaywrightException exception) =>
        exception.Message.Contains("Execution context was destroyed", StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("interrupted by another navigation", StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("Cannot find context", StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("ERR_ABORTED", StringComparison.OrdinalIgnoreCase);

    private static bool IsCourseHost(string host) =>
        host.Equals("studyvideoh5.zhihuishu.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("wisdom-mooc.zhihuishu.com", StringComparison.OrdinalIgnoreCase);

    private static bool TryParse(string url, out Uri uri) =>
        Uri.TryCreate(url, UriKind.Absolute, out uri!) && (uri.Scheme == "https" || uri.Scheme == "http");

    private static Dictionary<string, string> Parameters(Uri uri)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var fragmentQuery = uri.Fragment.IndexOf('?');
        var query = uri.Query.TrimStart('?') + (fragmentQuery >= 0 ? "&" + uri.Fragment[(fragmentQuery + 1)..] : "");
        foreach (var item in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = item.Split('=', 2);
            result[Uri.UnescapeDataString(parts[0])] = parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : "";
        }
        return result;
    }
}
