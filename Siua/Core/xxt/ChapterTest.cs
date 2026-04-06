using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core;

public class ChapterTest
{
    private IFrame? fframe;
    private string chapterTitle;
    private string score;
    private bool isCompleted = false;
    public string ChapterTitle => chapterTitle;
    private ILocator? ceyanDiv;
    private ILocator handle;
    private ILocator? form;
    private List<Question> questions = new();
    public List<Question> Questions => questions;

    public bool IsCompleted => isCompleted;
    public string Score
    {
        get => score;
    }

    public bool HasQuestion
    {
        get => questions.Count > 0;
    }
    public ChapterTest(ILocator data)
    {
        handle = data;
    }

    public async Task WaitFotGetTotalScore()
    {
        var sc = ceyanDiv!.Locator("div.DividerBox > div.dividerCon");
        await sc.WaitForAsync();
        score = await sc.InnerTextAsync();
    }

    public async Task SubmitAnswer()
    {
        var submitcontainer = ceyanDiv!.Locator("div.ZY_sub.clearfix");
        if (await submitcontainer.CountAsync() > 0 && await submitcontainer.IsVisibleAsync())
        {
            var submitbuttion = submitcontainer.Locator("a.btnSubmit");
            if (await submitbuttion.CountAsync() > 0)
            {
                await submitbuttion.First.ClickAsync();
            }
        }
    }

    private async Task WaitForLoading()
    {
        var fcontainer = handle.Locator("iframe").First;
        await fcontainer.WaitForAsync();
        var frame = await (await fcontainer.ElementHandleAsync())!.ContentFrameAsync();

        var ifcontainer = frame!.Locator("iframe").First;
        await ifcontainer.WaitForAsync();
        fframe = await (await ifcontainer.ElementHandleAsync())!.ContentFrameAsync();

        isCompleted = await fframe!.Locator("div.testTit_status_complete").CountAsync() > 0;
    }

    public async Task GetTitleAndQuestions()
    {
        await WaitForLoading();
        ceyanDiv = fframe!.Locator("div.radiusBG > div.CeYan");
        form = ceyanDiv.Locator("form");
        var zyb = form.Locator("#ZyBottom");
        if (!IsCompleted)
        {
            await zyb.Locator("div.TiMu.newTiMu").First.WaitForAsync();
            var tms = zyb.Locator("div.singleQuesId > div.TiMu.newTiMu");
            var count = await tms.CountAsync();
            for (int i = 0; i < count; i++)
            {
                questions.Add(new Question(tms.Nth(i)));
            }
        }
    }
}

public class Question
{
    private string title;
    public string Title => title;
    private Dictionary<ILocator, ILocator> answers = new();
    public Dictionary<ILocator, ILocator> Answers => answers;

    private ILocator handle;
    public Question(ILocator data)
    {
        handle = data;
    }

    private async Task WairForLoading()
    {
        var tvar = handle.Locator("div.Zy_TItle.clearfix").First;
        await tvar.WaitForAsync();
        title = await tvar.InnerTextAsync();
    }

    public async Task GetAnswers()
    {
        await WairForLoading();
        var lis = handle.Locator("li");
        var count = await lis.CountAsync();
        for (int i = 0; i < count; i++)
        {
            var l = lis.Nth(i);
            answers[l.Locator("label")] = l.Locator("a");
        }
    }

    public async Task<byte[]?> GetImageForQuestion()
    {
        try
        {
            var options = new LocatorScreenshotOptions()
            {
                Type = ScreenshotType.Png,
                Timeout = 3000
            };
            return await handle.ScreenshotAsync(options);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Error] GetImageForQuestion: {e}");
            return null;
        }
    }
}
