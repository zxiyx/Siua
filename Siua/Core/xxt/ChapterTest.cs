using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core;

public sealed class ChapterTest
{
    private readonly ILocator _container;
    private readonly List<Question> _questions = [];
    private ILocator? _testPanel;

    public ChapterTest(ILocator container)
    {
        _container = container;
    }

    public IReadOnlyList<Question> Questions => _questions;
    public bool IsCompleted { get; private set; }
    public bool HasQuestion => _questions.Count > 0;

    public async Task SubmitAnswerAsync()
    {
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

    public async Task LoadQuestionsAsync()
    {
        _questions.Clear();
        var frame = await GetRequiredContentFrameAsync(_container.Locator("iframe").First);
        var innerFrame = await GetRequiredContentFrameAsync(frame.Locator("iframe").First);

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
            _questions.Add(new Question(questionLocators.Nth(index)));
        }
    }

    private static async Task<IFrame> GetRequiredContentFrameAsync(ILocator locator)
    {
        await locator.WaitForAsync();
        var element = await locator.ElementHandleAsync()
            ?? throw new PlaywrightException("无法获取测试 iframe 元素。");
        return await element.ContentFrameAsync()
            ?? throw new PlaywrightException("无法进入测试 iframe。");
    }
}

public sealed class Question
{
    private readonly ILocator _container;
    private readonly Dictionary<ILocator, ILocator> _answers = [];

    public Question(ILocator container)
    {
        _container = container;
    }

    public string Title { get; private set; } = string.Empty;
    public IReadOnlyDictionary<ILocator, ILocator> Answers => _answers;

    public async Task LoadAnswersAsync()
    {
        _answers.Clear();
        var titleLocator = _container.Locator("div.Zy_TItle.clearfix").First;
        await titleLocator.WaitForAsync();
        Title = await titleLocator.InnerTextAsync();

        var answerItems = _container.Locator("li");
        var count = await answerItems.CountAsync();
        for (var index = 0; index < count; index++)
        {
            var item = answerItems.Nth(index);
            _answers[item.Locator("label")] = item.Locator("a");
        }
    }

    public async Task<byte[]?> CaptureImageAsync()
    {
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