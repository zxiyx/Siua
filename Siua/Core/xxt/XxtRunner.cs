using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Siua.Common;
using Siua.Core;
using Siua.Interfaces;
using Siua.Services;

namespace Siua.Core.Xxt;

/// <summary>组织学习通课程任务的完整执行流程。</summary>
public sealed class XxtRunner
{
    private const string LogSource = "Xxt";

    private readonly IPage _page;
    private readonly GlobalSettings _settings;
    private readonly ILogService _logService;
    private readonly Pix2TextService _ocrService;
    private readonly AiControlService _aiControlService;

    public XxtRunner(
        IPage page,
        GlobalSettings settings,
        ILogService logService,
        Pix2TextService ocrService,
        AiControlService aiControlService)
    {
        _page = page;
        _settings = settings;
        _logService = logService;
        _ocrService = ocrService;
        _aiControlService = aiControlService;
    }

    public async Task<bool> RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            var resolver = new XxtPageResolver(_page, _settings, _logService);
            if (!await resolver.WaitLoadingAsync())
            {
                LogError("当前课程页面加载失败，学习通任务已停止");
                return false;
            }

            await resolver.ResolvePageAsync();
            if (resolver.HasResolutionErrors && (_settings.AutoTest || _settings.RandomTest))
            {
                LogError("课程任务未能完整解析，已停止以避免漏掉章节测试");
                return false;
            }

            if (resolver.HasTest && (_settings.AutoTest || _settings.RandomTest) &&
                !await ProcessTestsAsync(resolver, cancellationToken))
            {
                return false;
            }

            await ProcessVideosAsync(resolver.Videos, cancellationToken);
            await ProcessDocumentsAsync(resolver.Docs, cancellationToken);

            LogInfo("进入下一节...");
            await resolver.NextPageAsync();
            await Task.Delay(_settings.ChapterJumpInterval, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is PlaywrightException or TimeoutException && !_page.IsClosed)
        {
            LogError($"学习通页面处理失败，当前任务已停止：{exception.Message}");
            return false;
        }
    }

    private async Task ProcessVideosAsync(
        IReadOnlyList<XxtVideo> videos,
        CancellationToken cancellationToken)
    {
        foreach (var video in videos)
        {
            try
            {
                if (_settings.JumpCompleted && await video.IsCompletedAsync())
                    continue;

                LogInfo("播放视频中...");
                await video.InitializeAsync();
                await video.PlayAsync(cancellationToken);

                if (_settings.TryFinishVideo)
                {
                    var finished = await video.TryFinishAsync(cancellationToken);
                    LogInfo(finished
                        ? "操控视频进度成功，视频播放完毕"
                        : "操控视频进度失败，正常播放视频");
                }

                if (!await video.WaitForEndAsync(cancellationToken: cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    LogError("视频结束检测超时，继续处理后续任务点");
                }

                LogInfo("播放完毕");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is PlaywrightException or InvalidOperationException &&
                !_page.IsClosed)
            {
                LogError($"视频处理失败，已跳过当前任务点：{exception.Message}");
            }
        }
    }

    private async Task ProcessDocumentsAsync(
        IReadOnlyList<XxtDocument> documents,
        CancellationToken cancellationToken)
    {
        if (documents.Count == 0)
            return;

        LogInfo("检测到文档...");
        foreach (var document in documents)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_settings.JumpCompleted && document.IsCompleted)
                    continue;

                LogInfo("完成文档中...");
                await document.ScrollToEndAsync(cancellationToken);
                LogInfo("完成文档");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (PlaywrightException exception) when (!_page.IsClosed)
            {
                LogError($"文档处理失败，已跳过当前任务点：{exception.Message}");
            }
        }
    }

    private async Task<bool> ProcessTestsAsync(
        XxtPageResolver resolver,
        CancellationToken cancellationToken)
    {
        foreach (var chapterTest in resolver.Tests)
        {
            try
            {
                await chapterTest.LoadQuestionsAsync(cancellationToken);
                if (chapterTest.IsCompleted)
                {
                    LogInfo("该章节测试已完成");
                    continue;
                }

                if (!chapterTest.HasQuestion)
                {
                    LogError("章节测试尚未完成，但未识别到题目，已停止以避免跳过测试");
                    return false;
                }

                if (_settings.RegionScreenshot && !_settings.RandomTest)
                {
                    if (!await AnswerChapterAsync(chapterTest, cancellationToken))
                        return false;
                    await Task.Delay(_settings.AiAnsweringInterval, cancellationToken);
                }
                else
                {
                    for (var index = 0; index < chapterTest.Questions.Count; index++)
                    {
                        if (!await AnswerQuestionAsync(chapterTest.Questions[index], index + 1, cancellationToken))
                            return false;
                        if (!_settings.RandomTest)
                            await Task.Delay(_settings.AiAnsweringInterval, cancellationToken);
                    }
                }

                await chapterTest.SubmitAnswerAsync(cancellationToken);
                await resolver.ConfirmTestSubmissionAsync(cancellationToken);
                await chapterTest.WaitForSubmissionAsync(cancellationToken);
                LogInfo("该章节测试提交成功");
                await Task.Delay(_settings.ChapterJumpInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is PlaywrightException or TimeoutException && !_page.IsClosed)
            {
                LogError($"章节测试控件处理失败，已停止以避免跳过测试：{exception.Message}");
                return false;
            }
        }

        return true;
    }

    private async Task<bool> AnswerQuestionAsync(
        XxtQuestion question,
        int questionIndex,
        CancellationToken cancellationToken)
    {
        await question.LoadAnswersAsync(cancellationToken);
        if (_settings.RandomTest)
        {
            if (question.IsFillInBlank)
            {
                if (question.BlankCount == 0)
                    return DisableRandomTest("未识别到填空题答题框，已关闭随机答题");
                await question.FillBlanksAsync(RandomAnswerSelector.FillBlanks(question.BlankCount), cancellationToken);
                LogInfo($"已随机填写 {question.BlankCount} 个空（汉字或数字）");
                return true;
            }

            return await SelectRandomAnswersAsync(question, cancellationToken);
        }

        if (question.IsFillInBlank && question.BlankCount == 0)
            return DisableAutoTest("已识别为填空题，但未找到可填写的答题框，已关闭自动答题");

        if (!question.IsFillInBlank && question.Answers.Count == 0)
            return DisableAutoTest($"题型 {question.QuestionType ?? "未知"} 未识别到支持的答题控件，请手动处理，已关闭自动答题");

        var image = await question.CaptureImageAsync(cancellationToken);
        if (image is null)
            return DisableAutoTest("题目截图失败，已关闭自动答题");

        var questionText = _settings.UsedAiToOcr
            ? await _aiControlService.GetTextFromImage(image)
            : await _ocrService.RecognizeAsync(image, cancellationToken);

        if (questionText is null)
        {
            return DisableAutoTest(_settings.UsedAiToOcr
                ? "AIOCR 识图异常，已自动关闭自动答题"
                : "OCR 识图异常，已关闭自动答题并结束刷课");
        }

        var answer = question.IsFillInBlank
            ? await _aiControlService.GetFillInBlankAnswer(questionText, question.BlankCount)
            : await _aiControlService.GetAnswer(questionText);
        if (answer is null)
            return DisableAutoTest("AI 配置异常，已关闭自动答题");

        LogInfo(AnswerLogFormatter.Format(question.Number, questionIndex, answer, question.BlankCount));

        return await ApplyAnswerAsync(question, answer, cancellationToken);
    }

    private async Task<bool> ApplyAnswerAsync(XxtQuestion question, string answer, CancellationToken cancellationToken)
    {

        if (question.IsFillInBlank)
        {
            if (!FillInBlankAnswerParser.TryParse(answer, question.BlankCount, out var blanks))
                return DisableAutoTest($"填空题答案格式错误或与 {question.BlankCount} 个空不匹配，已关闭自动答题");

            await question.FillBlanksAsync(blanks, cancellationToken);
            return true;
        }

        var selectedCount = 0;
        foreach (var option in question.Answers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!AnswerMatcher.SelectsOption(answer, option.Marker, option.Text))
                continue;

            await option.Target.ClickAsync();
            selectedCount++;
        }

        return selectedCount > 0 ||
               DisableAutoTest("AI 返回的答案无法匹配任何选项，已关闭自动答题");
    }

    private async Task<bool> AnswerChapterAsync(XxtChapterTest chapterTest, CancellationToken cancellationToken)
    {
        var specs = new List<ChapterQuestionSpec>();
        foreach (var question in chapterTest.Questions)
        {
            await question.LoadAnswersAsync(cancellationToken);
            if ((question.IsFillInBlank && question.BlankCount == 0) ||
                (!question.IsFillInBlank && question.Answers.Count == 0))
                return DisableAutoTest($"区域截图发现第 {specs.Count + 1} 题的题型或答题控件不受支持，已停止答题");
            specs.Add(new ChapterQuestionSpec(specs.Count + 1, question.Number, question.BlankCount,
                question.AllowsMultipleAnswers, question.Answers.Select(option => option.Marker).ToArray()));
        }

        LogInfo($"区域截图：一次识别并回答 {specs.Count} 道学习通章节测试题");
        var image = await chapterTest.CaptureRegionAsync(cancellationToken);
        string? response;
        if (_settings.UsedAiToOcr)
            response = await _aiControlService.GetChapterAnswers(image, specs);
        else
        {
            var text = await _ocrService.RecognizeAsync(image, cancellationToken);
            if (string.IsNullOrWhiteSpace(text))
                return DisableAutoTest("章节测试区域 OCR 识别失败，已关闭自动答题");
            response = await _aiControlService.GetChapterAnswers(text, specs);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!ChapterAnswerParser.TryParse(response, specs, out var answers))
            return DisableAutoTest("整份试卷答案格式、题号、选项或空数不匹配，未填写或提交，已关闭自动答题");

        for (var index = 0; index < chapterTest.Questions.Count; index++)
        {
            var question = chapterTest.Questions[index];
            var values = answers[index + 1];
            LogInfo(AnswerLogFormatter.Format(question.Number, index + 1, values, question.IsFillInBlank));
            if (question.IsFillInBlank)
                await question.FillBlanksAsync(values, cancellationToken);
            else
            {
                foreach (var option in question.Answers.Where(option => values.Contains(option.Marker)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await option.Target.ClickAsync();
                }
            }
        }
        LogInfo($"已批量填写 {specs.Count} 道题");
        return true;
    }

    private async Task<bool> SelectRandomAnswersAsync(
        XxtQuestion question,
        CancellationToken cancellationToken)
    {
        var answers = question.Answers.ToArray();
        var selectedIndexes = RandomAnswerSelector.Select(
            answers.Length,
            question.AllowsMultipleAnswers);
        if (selectedIndexes.Count == 0)
            return DisableRandomTest("未识别到题目选项，已关闭随机答题");

        foreach (var index in selectedIndexes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await answers[index].Target.ClickAsync();
        }

        LogInfo($"已随机选择 {selectedIndexes.Count} 个答案");
        return true;
    }

    private bool DisableAutoTest(string message)
    {
        _settings.AutoTest = false;
        LogError(message);
        return false;
    }

    private bool DisableRandomTest(string message)
    {
        _settings.RandomTest = false;
        LogError(message);
        return false;
    }

    private void LogInfo(string message) =>
        _logService.AddLog(LogLevel.Info, LogSource, message);

    private void LogError(string message) =>
        _logService.AddLog(LogLevel.Error, LogSource, message);

}
