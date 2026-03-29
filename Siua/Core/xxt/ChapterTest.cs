using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Playwright;


namespace Siua.Core;

public class ChapterTest
{
    private IFrame fframe;
    private string chapterTitle;
    private string score;
    private bool isCompleted = false;
    public string ChapterTitle => chapterTitle;
    private IElementHandle ceyanDiv;
    private IElementHandle handle;
    private IElementHandle form;
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
    public ChapterTest(IElementHandle data)
    {
        handle = data;
    }

    public async Task WaitFotGetTotalScore()
    {
        var sc = await ceyanDiv.WaitForSelectorAsync("div.DividerBox > div.dividerCon");
        score = await sc.InnerTextAsync();
    }

    public async Task SubmitAnswer()
    {
        var submitcontainer = await ceyanDiv.QuerySelectorAsync("div.ZY_sub.clearfix");
        if (submitcontainer != null)
        {
            var submitbuttion = await submitcontainer.QuerySelectorAsync("a.btnSubmit");
            await submitbuttion.ClickAsync();
        }
    }

    private async Task WaitForLoading()
    {
        var fcontainer = await handle.WaitForSelectorAsync("iframe");
        var frame = await fcontainer.ContentFrameAsync();
        var ifcontainer = await frame.QuerySelectorAsync("iframe");
        fframe = await ifcontainer.ContentFrameAsync();
        if (await fframe.QuerySelectorAsync("div.testTit_status_complete") != null)
            isCompleted = true;
    }

    public async Task GetTitleAndQuestions()
    {
        await WaitForLoading();
        ceyanDiv = await fframe.QuerySelectorAsync("div.radiusBG > div.CeYan");
        var title = await ceyanDiv.QuerySelectorAsync("div.ceyan_name");
        chapterTitle = await title.InnerTextAsync();
        form = await ceyanDiv.QuerySelectorAsync("form");
        var zyb = await form.QuerySelectorAsync("#ZyBottom");
        if (!IsCompleted)
        {
            await zyb.WaitForSelectorAsync("div.TiMu.newTiMu");
            var tms= await zyb.QuerySelectorAllAsync("div.singleQuesId > div.TiMu.newTiMu");
            foreach (var t in tms)
            {
                questions.Add(new Question(t));
            }
        }
    }

}

public class Question
{
    private string title;
    public string Title => title;
    private Dictionary<IElementHandle, IElementHandle> answers = new();
    public Dictionary<IElementHandle, IElementHandle> Answers => answers;

    private IElementHandle handle;
    public Question(IElementHandle data)
    {
        handle = data;
    }

    private async Task WairForLoading()
    {
        await handle.WaitForSelectorAsync("div.Zy_TItle.clearfix");
        var tvar = await handle.QuerySelectorAsync("div.Zy_TItle.clearfix");
        title = await tvar.InnerTextAsync();
    }

    public async Task GetAnswers()
    {
        await WairForLoading();
        //var c = await handle.QuerySelectorAsync("div.clearfix");
        //var lis = await c.QuerySelectorAllAsync("li");
        var lis = await handle.QuerySelectorAllAsync("li");
        foreach (var l in lis)
        {
            answers.Add(await l.QuerySelectorAsync("label"), await l.QuerySelectorAsync("a"));
        }
    }

    public async Task<byte[]?> GetImageForQuestion()
    {
        try
        {
            var options = new ElementHandleScreenshotOptions()
            { 
                Type = ScreenshotType.Png,
                Timeout = 3000 // 设置短一点的超时，方便排查
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
