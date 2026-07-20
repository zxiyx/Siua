using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Siua.Common;
using Siua.Core;
using Siua.Interfaces;
using Siua.Services;

namespace Siua.Core.Xxt;

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
            await ProcessVideosAsync(resolver.Videos, cancellationToken);
            await ProcessDocumentsAsync(resolver.Docs, cancellationToken);

            if (resolver.HasTest && _settings.AutoTest &&
                !await ProcessTestsAsync(resolver, cancellationToken))
            {
                return false;
            }

            LogInfo("进入下一节...");
            await resolver.NextPageAsync();
            await Task.Delay(_settings.ChapterJumpInterval, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PlaywrightException exception) when (!_page.IsClosed)
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
        var imagePath = Path.Combine(_settings.UserDataDir, "q.png");
        try
        {
            foreach (var chapterTest in resolver.Tests)
            {
                try
                {
                    await chapterTest.LoadQuestionsAsync(cancellationToken);
                    if (chapterTest.IsCompleted)
                        LogInfo("该章节测试已完成");

                    if (!chapterTest.HasQuestion || chapterTest.IsCompleted)
                        continue;

                    foreach (var question in chapterTest.Questions)
                    {
                        if (!await AnswerQuestionAsync(
                                question,
                                imagePath,
                                cancellationToken))
                        {
                            return false;
                        }

                        await Task.Delay(_settings.AiAnsweringInterval, cancellationToken);
                    }

                    await chapterTest.SubmitAnswerAsync(cancellationToken);
                    await resolver.ConfirmTestSubmissionAsync(cancellationToken);
                    LogInfo("该章节测试提交成功");
                    await Task.Delay(_settings.ChapterJumpInterval, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (PlaywrightException exception) when (!_page.IsClosed)
                {
                    LogError($"章节测试控件处理失败，已跳过：{exception.Message}");
                }
            }

            return true;
        }
        finally
        {
            if (File.Exists(imagePath))
                File.Delete(imagePath);
        }
    }

    private async Task<bool> AnswerQuestionAsync(
        XxtQuestion question,
        string imagePath,
        CancellationToken cancellationToken)
    {
        await question.LoadAnswersAsync(cancellationToken);
        var image = await question.CaptureImageAsync(cancellationToken);
        if (image is null)
            return DisableAutoTest("题目截图失败，已关闭自动答题");

        await File.WriteAllBytesAsync(imagePath, image, cancellationToken);
        var questionText = _settings.UsedAiToOcr
            ? await _aiControlService.GetTextFromImage(imagePath)
            : await _ocrService.RecognizeAsync(imagePath, cancellationToken);

        if (questionText is null)
        {
            return DisableAutoTest(_settings.UsedAiToOcr
                ? "AIOCR 识图异常，已自动关闭自动答题"
                : "OCR 识图异常，已关闭自动答题并结束刷课");
        }

        var answer = await _aiControlService.GetAnswer(questionText);
        if (answer is null)
            return DisableAutoTest("AI 配置异常，已关闭自动答题");

        LogInfo($"AI 返回答案：{answer.Trim()}");

        var selectedCount = 0;
        foreach (var option in question.Answers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var optionText = await option.Key.InnerTextAsync();
            var optionMarker = await option.Value.InnerTextAsync();
            if (!AnswerMatcher.SelectsOption(answer, optionMarker, optionText))
                continue;

            await option.Key.ClickAsync();
            selectedCount++;
        }

        return selectedCount > 0 ||
               DisableAutoTest("AI 返回的答案无法匹配任何选项，已关闭自动答题");
    }

    private bool DisableAutoTest(string message)
    {
        _settings.AutoTest = false;
        LogError(message);
        return false;
    }

    private void LogInfo(string message) =>
        _logService.AddLog(LogLevel.Info, LogSource, message);

    private void LogError(string message) =>
        _logService.AddLog(LogLevel.Error, LogSource, message);

}
