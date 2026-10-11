using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Siua.Core;

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

    public bool IsStudyVideo { get; }

    public ZhsChapterTest(ILocator locator, bool studyVideo = false)
    {
        _locator = locator;
        IsStudyVideo = studyVideo;
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
        var previousCount = -1;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = await questions.CountAsync();
            var cardCount = await examPage.Locator(".answerCard_list li").CountAsync();
            if (count > 0 && count == previousCount && (cardCount == 0 || cardCount == count))
                return count;
            previousCount = count;
            await Task.Delay(250, cancellationToken);
        }
        throw new TimeoutException("智慧树试卷题目数量与答题卡不一致或尚未加载完整。");
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

    public async Task<IReadOnlyList<ZhsQuestion>> LoadAllQuestionsAsync(IPage examPage,
        CancellationToken cancellationToken = default)
    {
        var containers = examPage.Locator(QuestionSelector);
        var count = await containers.CountAsync();
        var questions = new List<ZhsQuestion>(count);
        for (var index = 0; index < count; index++)
        {
            var question = new ZhsQuestion(containers.Nth(index));
            await question.WaitForContentAsync(cancellationToken);
            await question.LoadAsync(cancellationToken);
            questions.Add(question);
        }
        return questions;
    }

    public async Task<byte[]> CaptureRegionAsync(IPage examPage, int questionCount,
        CancellationToken cancellationToken = default)
    {
        foreach (var container in await examPage.Locator(QuestionSelector).AllAsync())
            await new ZhsQuestion(container).WaitForContentAsync(cancellationToken);
        // 最近的共同祖先包含全卷题目，避免只截到当前分页中可见的一题。
        var region = examPage.Locator(QuestionSelector).First.Locator(
            "xpath=ancestor::*[count(.//div[contains(concat(' ', normalize-space(@class), ' '), ' examPaper_subject ') " +
            $"and contains(concat(' ', normalize-space(@class), ' '), ' mt20 ')]) = {questionCount}][1]");
        return await ChapterRegionCapture.CaptureAsync(examPage.MainFrame, region, cancellationToken, QuestionSelector);
    }

    public async Task<bool> MoveNextOrSaveAsync(
        IPage examPage,
        string previousQuestionKey,
        CancellationToken cancellationToken = default,
        bool isLastQuestion = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var switchBox = examPage.Locator(SwitchButtonSelector).First;
        if (!IsStudyVideo)
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
        {
            // 下一题可能在切题时短暂隐藏；必须同时核对处理数量和最后题目的身份。
            await Task.Delay(500, cancellationToken);
            if (await nextButton.CountAsync() == 0 || !await nextButton.First.IsVisibleAsync())
            {
                var last = new ZhsQuestion(examPage.Locator(QuestionSelector).Last);
                await last.LoadAsync(cancellationToken);
                var current = await LoadCurrentQuestionAsync(examPage, cancellationToken);
                if (IsStudyVideo && isLastQuestion && last.Key == previousQuestionKey && current.Key == last.Key)
                    return false;
                throw new PlaywrightException("智慧树章节测试没有找到可见的“下一题”按钮，且未确认已到最后一题。");
            }
        }

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

    public async Task<string> WaitForScoreAsync(IPage examPage, CancellationToken cancellationToken = default)
    {
        await examPage.Locator(".scoreTipBox").First.WaitForAsync(new LocatorWaitForOptions
        { State = WaitForSelectorState.Visible, Timeout = 30_000 }).WaitAsync(cancellationToken);
        var detail = examPage.Locator(".scoreTipBox .scoreDetail").Filter(new LocatorFilterOptions
        { HasTextRegex = new Regex(@"\d") }).First;
        await detail.WaitForAsync(new LocatorWaitForOptions
        { State = WaitForSelectorState.Visible, Timeout = 10_000 }).WaitAsync(cancellationToken);
        var score = await detail.InnerTextAsync();
        if (string.IsNullOrWhiteSpace(score))
            throw new InvalidOperationException("提交后成绩提示为空，请检查考试页面。");
        return NormalizeText(score);
    }

    public async Task VerifySavedAnswersAsync(IPage examPage,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>> expected,
        CancellationToken cancellationToken = default)
    {
        var containers = await examPage.Locator(QuestionSelector).AllAsync();
        if (containers.Count != expected.Count)
            throw new InvalidOperationException("提交前试卷题目数量发生变化，已停止提交。");
        foreach (var container in containers)
        {
            var question = new ZhsQuestion(container);
            await question.LoadAsync(cancellationToken);
            var actual = await question.ReadAnswerStateAsync();
            if (!expected.TryGetValue(question.Key, out var saved) || actual.Count != saved.Count ||
                actual.Any(pair => !saved.TryGetValue(pair.Key, out var selected) || selected != pair.Value))
                throw new InvalidOperationException($"第 {question.Number} 题切题后答案状态发生变化，已停止提交以避免漏题。");
        }
    }

    public async Task ReturnFromScoreAsync(IPage examPage, CancellationToken cancellationToken = default)
    {
        await examPage.Locator(".scoreTipBox a[title='返回作业考试']").ClickAsync(
            new LocatorClickOptions { Timeout = 10_000 });
        for (var attempt = 0; attempt < 100; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (examPage.IsClosed) return;
            await Task.Delay(100, cancellationToken);
        }
        throw new TimeoutException("已点击返回作业考试，但考试页未关闭，请检查页面提示。");
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
        AllowsMultipleAnswers = Type.Contains("多选", StringComparison.Ordinal);

        var optionRows = _container.Locator("div.subject_node div.nodeLab");
        // 等待异步组件把选项补齐，避免随机答题在选项尚未渲染完时取样。
        string? previousOptions = null;
        var stableSamples = 0;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await optionRows.EvaluateAllAsync<string>(
                "rows => JSON.stringify(rows.map(r => [r.textContent, r.querySelector('input')?.value]))");
            if (snapshot != "[]" && snapshot == previousOptions && ++stableSamples >= 2)
                break;
            if (snapshot != previousOptions) stableSamples = 0;
            previousOptions = snapshot;
            if (attempt == 39 && snapshot != "[]")
                throw new TimeoutException($"第 {Number} 题选项持续变化，已停止答题。");
            await Task.Delay(150, cancellationToken);
        }
        var optionCount = await optionRows.CountAsync();
        for (var index = 0; index < optionCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = optionRows.Nth(index);
            var marker = ZhsChapterTest.NormalizeText(
                await ZhsChapterTest.TryGetInnerTextAsync(
                    row.Locator("span.ABCase, .label span.mr10").First) ?? string.Empty).TrimEnd('.', '、', '．');
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
            _answers.Add(new ZhsAnswerOption(row, input, marker, text,
                await input.CountAsync() > 0 ? await input.GetAttributeAsync("value") ?? "" : ""));
        }
    }

    public async Task WaitForContentAsync(CancellationToken cancellationToken = default)
    {
        // 某些题干由异步组件生成，HTML 空壳不能直接送给模型。
        for (var attempt = 0; attempt < 40; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await _container.EvaluateAsync<bool>("""
                q => {
                    const stem = q.querySelector('.subject_describe');
                    if (!stem) return true; // 兼容旧版题干结构
                    const images = [...stem.querySelectorAll('img')];
                    return (!!stem.textContent.trim() || images.length > 0 || !!stem.querySelector('canvas, svg')) &&
                        images.every(i => i.complete && i.naturalWidth > 0);
                }
                """)) return;
            await Task.Delay(250, cancellationToken);
        }
        throw new TimeoutException($"第 {Number} 题题干尚未加载，已停止截图答题。");
    }

    public async Task ApplyAnswersAsync(IReadOnlyCollection<ZhsAnswerOption> selected,
        CancellationToken cancellationToken = default)
    {
        var number = _container.Locator("div.subject_num span").First;
        var currentKey = await number.GetAttributeAsync("id") ??
            ZhsChapterTest.NormalizeText(await number.InnerTextAsync());
        if (currentKey != Key)
            throw new InvalidOperationException($"第 {Number} 题已切换，停止填写过期答案。");
        foreach (var option in _answers)
            if (!await option.MatchesSnapshotAsync())
                throw new InvalidOperationException($"第 {Number} 题选项发生变化，停止填写过期答案。");
        if (selected.Count == 0 || (!AllowsMultipleAnswers && selected.Count != 1))
            throw new InvalidOperationException($"第 {Number} 题答案数量不符合题型。");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            foreach (var option in _answers)
            {
                var expected = selected.Contains(option);
                if (expected || AllowsMultipleAnswers)
                    await option.SetSelectedAsync(expected, cancellationToken);
            }
            await Task.Delay(400, cancellationToken);
            var matches = true;
            foreach (var option in _answers)
                matches &= await option.IsSelectedAsync() == selected.Contains(option);
            if (matches) return;
        }
        throw new InvalidOperationException($"第 {Number} 题选择后校验失败，已停止以避免漏题。");
    }

    public async Task<IReadOnlyDictionary<string, bool>> ReadAnswerStateAsync()
    {
        var state = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var option in _answers)
            state.Add(option.Value + "\u001f" + option.Text, await option.IsSelectedAsync());
        return state;
    }

    public async Task<byte[]?> CaptureImageAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await WaitForContentAsync(cancellationToken);
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

    public ZhsAnswerOption(
        ILocator row,
        ILocator input,
        string marker,
        string text,
        string value = "")
    {
        _row = row;
        Marker = marker;
        Text = text;
        Value = value;
    }

    public string Marker { get; }
    public string Text { get; }
    public string Value { get; }

    public Task<bool> MatchesSnapshotAsync() => _row.EvaluateAsync<bool>("""
        (row, expected) => {
            const input = row.querySelector('input[type=radio], input[type=checkbox]');
            const text = row.querySelector('.examquestions-answer')?.innerText.replace(/\s+/g, ' ').trim() || '';
            return (input?.getAttribute('value') || '') === expected.value && text === expected.text;
        }
        """, new { value = Value, text = Text });

    public Task<bool> IsSelectedAsync() => _row.EvaluateAsync<bool>("""
        row => {
            const input = row.querySelector('input[type=radio], input[type=checkbox]');
            const q = row.closest('.examPaper_subject');
            const model = q && q.querySelector(':scope > input[type=hidden]');
            if (model && input) {
                try {
                    const values = JSON.parse(model.value);
                    if (Array.isArray(values)) return values.some(v => String(v) === input.value);
                } catch {}
            }
            const custom = row.querySelector('[aria-checked]');
            if (custom) return custom.getAttribute('aria-checked') === 'true';
            if (input) return input.checked;
            throw new Error('未识别到可验证的选项状态');
        }
        """);

    public Task SelectAsync(CancellationToken cancellationToken = default) => SetSelectedAsync(true, cancellationToken);

    public async Task SetSelectedAsync(bool selected, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await IsSelectedAsync() == selected) return;
            await _row.Locator("div.label.clearfix").First.ClickAsync(new LocatorClickOptions { Timeout = 10_000 });
            // 等待 Vue 写入真实答题状态，不强行 Check 隐藏 input。
            for (var poll = 0; poll < 10; poll++)
            {
                await Task.Delay(100, cancellationToken);
                if (await IsSelectedAsync() == selected) return;
            }
        }
        throw new InvalidOperationException($"智慧树选项 {Marker} 点击后未保存选择状态。");
    }
}
