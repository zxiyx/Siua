using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core.Xxt;

public sealed class XxtChapterTest
{
    private readonly ILocator _container;
    private readonly List<XxtQuestion> _questions = [];
    private ILocator? _testPanel;

    public XxtChapterTest(ILocator container)
    {
        _container = container;
    }

    public IReadOnlyList<XxtQuestion> Questions => _questions;
    public bool IsCompleted { get; private set; }
    public bool HasQuestion => _questions.Count > 0;

    public async Task SubmitAnswerAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_testPanel is null)
        {
            return;
        }
        var submitContainer = _testPanel.Locator("div.ZY_sub.clearfix");
        if (await submitContainer.CountAsync() == 0 || !await submitContainer.IsVisibleAsync())
        {
            return;
        }
        var submitButton = submitContainer.Locator("a.btnSubmit").First;
        if (await submitButton.CountAsync() > 0)
        {
            await submitButton.ClickAsync();
        }
    }

    public async Task LoadQuestionsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _questions.Clear();
        var frame = await GetRequiredContentFrameAsync(
            _container.Locator("iframe").First,
            cancellationToken);
        var innerFrame = await GetRequiredContentFrameAsync(
            frame.Locator("iframe").First,
            cancellationToken);

        IsCompleted = await innerFrame.Locator("div.testTit_status_complete").CountAsync() > 0;
        _testPanel = innerFrame.Locator("div.radiusBG > div.CeYan");
        if (IsCompleted)
        {
            return;
        }

        var questionsContainer = _testPanel.Locator("form #ZyBottom");
        await questionsContainer.Locator("div.TiMu.newTiMu").First.WaitForAsync();
        var questionLocators = questionsContainer.Locator("div.singleQuesId > div.TiMu.newTiMu");
        var count = await questionLocators.CountAsync();
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _questions.Add(new XxtQuestion(questionLocators.Nth(index)));
        }
    }

    private static async Task<IFrame> GetRequiredContentFrameAsync(
        ILocator locator,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await locator.WaitForAsync();
        var element = await locator.ElementHandleAsync()
            ?? throw new PlaywrightException("无法获取测试 iframe 元素。");
        return await element.ContentFrameAsync()
            ?? throw new PlaywrightException("无法进入测试 iframe。");
    }
}

public sealed class XxtQuestion
{
    private readonly ILocator _container;
    private readonly Dictionary<ILocator, ILocator> _answers = [];

    public XxtQuestion(ILocator container)
    {
        _container = container;
    }

    public string Title { get; private set; } = string.Empty;
    public IReadOnlyDictionary<ILocator, ILocator> Answers => _answers;

    public async Task LoadAnswersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _answers.Clear();
        var titleLocator = _container.Locator("div.Zy_TItle.clearfix").First;
        await titleLocator.WaitForAsync();
        Title = await titleLocator.InnerTextAsync();

        var answerItems = _container.Locator("li");
        var count = await answerItems.CountAsync();
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = answerItems.Nth(index);
            _answers[item.Locator("label")] = item.Locator("a");
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
                Timeout = 5000
            });
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }
}
