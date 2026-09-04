using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core.Zhs;

/// <summary>控制智慧树章节测试页面及其提交流程。</summary>
public sealed class ZhsChapterTest
{
    private const string PartTitleSelector = "div.examPaper_partTit.mt20";
    private const string QuestionSelector = "div.examPaper_subject.mt20";
    private const string VisibleQuestionSelector = "div.examPaper_subject.mt20:visible";
    private const string SwitchButtonSelector = "div.switch-btn-box";

    private static readonly Regex NextButtonText =
        new(@"^\s*下一题\s*$", RegexOptions.Compiled);

    private static readonly Regex SaveButtonText =
        new(@"^\s*保存\s*$", RegexOptions.Compiled);

    private static readonly Regex SubmitButtonText =
        new(@"^\s*提交作业\s*$", RegexOptions.Compiled);

    private static readonly Regex ConfirmButtonText =
        new(@"^\s*(?:确定|确认)\s*$", RegexOptions.Compiled);

    private readonly ILocator _locator;

    public ZhsChapterTest(ILocator locator)
    {
        _locator = locator;
    }

    public async Task<bool> IsAvailableAsync() =>
        await _locator.CountAsync() > 0 && await _locator.IsVisibleAsync();

    public async Task<IPage> OpenAsync(
        IPage coursePage,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IPage examPage;
        try
        {
            examPage = await coursePage.Context.RunAndWaitForPageAsync(
                async () =>
                {
                    await _locator.ScrollIntoViewIfNeededAsync();
                    await _locator.ClickAsync(new LocatorClickOptions { Timeout = 15_000 });
                },
                new BrowserContextRunAndWaitForPageOptions { Timeout = 15_000 });
        }
        catch (TimeoutException)
        {
            examPage = coursePage;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await examPage.WaitForLoadStateAsync(
            LoadState.DOMContentLoaded,
            new PageWaitForLoadStateOptions { Timeout = 30_000 });
        await examPage.Locator(PartTitleSelector).First.WaitForAsync(
            new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 30_000
            });
        return examPage;
    }

    public async Task<int> GetQuestionCountAsync(
        IPage examPage,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var questions = examPage.Locator(QuestionSelector);
        await questions.First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 30_000
        });
        return await questions.CountAsync();
    }

    public async Task<ZhsQuestion> LoadCurrentQuestionAsync(
        IPage examPage,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var container = examPage.Locator(VisibleQuestionSelector).First;
        await container.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15_000
        });

        var question = new ZhsQuestion(container);
        await question.LoadAsync(cancellationToken);
        return question;
    }

    public async Task<bool> MoveNextOrSaveAsync(
        IPage examPage,
        string previousQuestionKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var switchBox = examPage.Locator(SwitchButtonSelector).First;
        await switchBox.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15_000
        });

        var saveButton = switchBox.Locator("button").Filter(
            new LocatorFilterOptions { HasTextRegex = SaveButtonText });
        if (await saveButton.CountAsync() > 0 && await saveButton.First.IsVisibleAsync())
        {
            await saveButton.First.ClickAsync();
            await Task.Delay(500, cancellationToken);
            return false;
        }

        var nextButton = switchBox.Locator("button").Filter(
            new LocatorFilterOptions { HasTextRegex = NextButtonText });
        if (await nextButton.CountAsync() == 0 || !await nextButton.First.IsVisibleAsync())
            throw new PlaywrightException("智慧树章节测试没有找到可见的“下一题”按钮。");

        await nextButton.First.ClickAsync();
        await WaitForQuestionChangedAsync(
            examPage,
            previousQuestionKey,
            cancellationToken);
        return true;
    }

    public async Task SubmitAsync(
        IPage examPage,
        int popupTimeout,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var submitButton = examPage
            .Locator("div.operateBtn_box.fr.mr5 button.btnStyleXSumit")
            .Filter(new LocatorFilterOptions { HasTextRegex = SubmitButtonText })
            .First;
        await submitButton.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15_000
        });
        await submitButton.ClickAsync();

        var timeout = Math.Max(1, popupTimeout);
        var messageBox = examPage.Locator("div.el-message-box__wrapper:visible").Last;
        await messageBox.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeout
        });

        var confirmButton = messageBox
            .Locator("div.el-message-box__btns button")
            .Filter(new LocatorFilterOptions { HasTextRegex = ConfirmButtonText })
            .First;
        await confirmButton.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeout
        });
        await confirmButton.ClickAsync();
    }

    public static async Task CloseAndReturnAsync(
        IPage coursePage,
        IPage examPage,
        string originalCourseUrl)
    {
        if (!ReferenceEquals(coursePage, examPage) && !examPage.IsClosed)
        {
            await examPage.CloseAsync();
        }
        else if (!coursePage.IsClosed &&
                 !string.Equals(coursePage.Url, originalCourseUrl, StringComparison.Ordinal))
        {
            await coursePage.GoBackAsync(new PageGoBackOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 30_000
            });
        }

        if (!coursePage.IsClosed)
            await coursePage.BringToFrontAsync();
    }

    private static async Task WaitForQuestionChangedAsync(
        IPage examPage,
        string previousQuestionKey,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 150; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = examPage.Locator(VisibleQuestionSelector).First;
            if (await current.CountAsync() > 0 && await current.IsVisibleAsync())
            {
                var number = current.Locator("div.subject_num span").First;
                var key = await number.GetAttributeAsync("id") ??
                          NormalizeText(await TryGetInnerTextAsync(number) ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(key) &&
                    !string.Equals(key, previousQuestionKey, StringComparison.Ordinal))
                {
                    return;
                }
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new PlaywrightException("智慧树章节测试切换下一题超时。");
    }

    internal static async Task<string?> TryGetInnerTextAsync(ILocator locator) =>
        await locator.CountAsync() > 0 ? await locator.InnerTextAsync() : null;

    internal static string NormalizeText(string value) =>
        Regex.Replace(value, @"\s+", " ").Trim();
}

/// <summary>表示一道智慧树题目及其答案选项。</summary>
public sealed class ZhsQuestion
{
    private readonly ILocator _container;
    private readonly List<ZhsAnswerOption> _answers = [];

    public ZhsQuestion(ILocator container)
    {
        _container = container;
    }

    public string Key { get; private set; } = string.Empty;
    public string Number { get; private set; } = string.Empty;
    public string Type { get; private set; } = string.Empty;
    public bool AllowsMultipleAnswers { get; private set; }
    public IReadOnlyList<ZhsAnswerOption> Answers => _answers;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _answers.Clear();
        AllowsMultipleAnswers = false;

        var number = _container.Locator("div.subject_num span").First;
        Number = ZhsChapterTest.NormalizeText(
            await ZhsChapterTest.TryGetInnerTextAsync(number) ?? string.Empty);
        Key = await number.GetAttributeAsync("id") ?? Number;
        Type = ZhsChapterTest.NormalizeText(
            await ZhsChapterTest.TryGetInnerTextAsync(
                _container.Locator("span.subject_type").First) ?? string.Empty);

        var optionRows = _container.Locator("div.subject_node div.nodeLab");
        var optionCount = await optionRows.CountAsync();
        for (var index = 0; index < optionCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = optionRows.Nth(index);
            var marker = ZhsChapterTest.NormalizeText(
                await ZhsChapterTest.TryGetInnerTextAsync(
                    row.Locator("span.ABCase").First) ?? string.Empty);
            var text = ZhsChapterTest.NormalizeText(
                await ZhsChapterTest.TryGetInnerTextAsync(
                    row.Locator("div.examquestions-answer").First) ?? string.Empty);
            var input = row.Locator("input[type=radio], input[type=checkbox]").First;
            var inputType = await input.CountAsync() > 0
                ? await input.GetAttributeAsync("type")
                : null;
            AllowsMultipleAnswers |= string.Equals(
                inputType,
                "checkbox",
                StringComparison.OrdinalIgnoreCase);
            _answers.Add(new ZhsAnswerOption(row, input, marker, text));
        }
    }

    public async Task<byte[]?> CaptureImageAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await _container.ScreenshotAsync(new LocatorScreenshotOptions
            {
                Animations = ScreenshotAnimations.Disabled,
                Type = ScreenshotType.Png,
                Timeout = 15_000
            });
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }
}

/// <summary>封装智慧树题目的单个可选答案。</summary>
public sealed class ZhsAnswerOption
{
    private readonly ILocator _row;
    private readonly ILocator _input;

    public ZhsAnswerOption(
        ILocator row,
        ILocator input,
        string marker,
        string text)
    {
        _row = row;
        _input = input;
        Marker = marker;
        Text = text;
    }

    public string Marker { get; }
    public string Text { get; }

    public async Task SelectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var clickTarget = _row.Locator("div.label.clearfix").First;
        await clickTarget.ScrollIntoViewIfNeededAsync();
        await clickTarget.ClickAsync(new LocatorClickOptions { Timeout = 15_000 });

        if (await _input.CountAsync() > 0 && !await _input.IsCheckedAsync())
        {
            await _input.CheckAsync(new LocatorCheckOptions
            {
                Force = true,
                Timeout = 10_000
            });
        }
    }
}
