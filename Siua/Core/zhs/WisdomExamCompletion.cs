using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core.Zhs;

/// <summary>wisdom-mooc 的试卷状态检测；不读取任何控件文本。</summary>
internal static class ZhsWisdomExamCompletion
{
    internal const string FullScoreSelector =
        ".dialog-has-ai-training-btn:visible:has(.userinfo .desc):has(.btns .try-btn):has(.btns button:not(.try-btn))";

    internal static async Task<string?> ReadScoreStateAsync(IPage page, CancellationToken token)
    {
        if (await page.Locator(FullScoreSelector).CountAsync().WaitAsync(token) > 0) return "满分";
        return await page.Locator(
            ".scoreTipBox:visible .scoreDetail:visible, .answerCard:visible .answerCard_score:visible, " +
            ".examPaper_box:visible .examPaper_subject:visible .subject_score:visible")
            .CountAsync().WaitAsync(token) > 0 ? "已显示成绩" : null;
    }

    // 0：尚未确认；1：可答题；2：已完成。与 mt20/mt30 等间距样式无关。
    internal static async Task<int> ReadStateAsync(IPage page, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            if (await ReadScoreStateAsync(page, token) is not null) return 2;
            return await page.EvaluateAsync<int>("""
                () => {
                    const visible = n => !!n && n.getBoundingClientRect().width > 0 &&
                        n.getBoundingClientRect().height > 0 && getComputedStyle(n).visibility !== 'hidden';
                    if (document.readyState === 'loading' ||
                        !visible(document.querySelector('.tit_operate_box')) ||
                        !visible(document.querySelector('.examPaper_box')) ||
                        !visible(document.querySelector('.examPaper_partTit'))) return 0;
                    if ([...document.querySelectorAll('.el-loading-mask,[aria-busy="true"]')].some(visible)) return 0;
                    const questions = [...document.querySelectorAll('.examPaper_box .examPaper_subject')];
                    if (!questions.some(visible)) return 0;
                    // btnStyleXSumit 是提交；btnStyleX 中不带 Sumit 的是暂存。
                    if ([...document.querySelectorAll('.operateBtn_box button.btnStyleX, .operateBtn_box button.btnStyleXSumit')]
                        .some(visible)) return 1;
                    // 按钮未加载不能直接算完成：同时确认现有试卷控件都是只读。
                    return questions.every(q => {
                        const inputs = [...q.querySelectorAll('.subject_node input[type=radio],.subject_node input[type=checkbox]')];
                        return inputs.length > 0 && inputs.every(i => i.disabled);
                    }) ? 2 : 0;
                }
                """).WaitAsync(token);
        }
        catch (PlaywrightException exception) when (ZhsExamPageOpening.IsNavigationInterrupted(exception))
        {
            return 0;
        }
    }

    internal static async Task<bool> IsCompletedAsync(IPage page, CancellationToken token = default)
    {
        var navigation = 0;
        void Navigated(object? sender, IFrame frame)
        {
            if (frame == page.MainFrame) Interlocked.Increment(ref navigation);
        }
        page.FrameNavigated += Navigated;
        try
        {
            for (var sample = 0; sample < 3; sample++)
            {
                if (await ReadStateAsync(page, token) != 2 || Volatile.Read(ref navigation) != 0) return false;
                if (sample < 2) await Task.Delay(250, token);
            }
            return true;
        }
        finally { page.FrameNavigated -= Navigated; }
    }
}
