using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Siua.Interfaces;
using Siua.Services;

namespace Siua.Core;

public sealed class XxtRunner
{
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
        cancellationToken.ThrowIfCancellationRequested();
        await _page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        var resolver = new PageResolver(_page, _settings);
        if (!await resolver.WaitLoadingAsync())
        {
            _logService.AddLog("当前课程页面加载失败");
            return true;
        }

        await resolver.ResolvePageAsync();
        await ProcessVideosAsync(resolver.Videos, cancellationToken);
        await ProcessDocumentsAsync(resolver.Docs);

        if (resolver.HasTest && _settings.AutoTest &&
            !await ProcessTestsAsync(resolver, cancellationToken))
        {
            return false;
        }

        _logService.AddLog("进入下一节...");
        await resolver.NextPageAsync();
        await Task.Delay(_settings.ChapterJumpInterval, cancellationToken);
        return true;
    }

    private async Task ProcessVideosAsync(
        IReadOnlyList<Video> videos,
        CancellationToken cancellationToken)
    {
        foreach (var video in videos)
        {
            if (_settings.JumpCompleted && await video.IsCompletedAsync())
                continue;

            _logService.AddLog("播放视频中...");
            await video.InitializeAsync();
            await video.PlayAsync(cancellationToken);

            if (_settings.TryFinishVideo)
            {
                var finished = await video.TryFinishAsync(cancellationToken);
                _logService.AddLog(finished
                    ? "操控视频进度成功，视频播放完毕"
                    : "操控视频进度失败，正常播放视频");
            }

            if (!await video.WaitForEndAsync(cancellationToken: cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logService.AddLog("视频结束检测超时");
            }

            _logService.AddLog("播放完毕");
        }
    }

    private async Task ProcessDocumentsAsync(IReadOnlyList<Doc> documents)
    {
        if (documents.Count == 0)
            return;

        _logService.AddLog("检测到文档...");
        foreach (var document in documents)
        {
            if (_settings.JumpCompleted && document.IsCompleted)
                continue;

            _logService.AddLog("完成文档中...");
            await document.ScrollToEndAsync();
            _logService.AddLog("完成文档");
        }
    }

    private async Task<bool> ProcessTestsAsync(
        PageResolver resolver,
        CancellationToken cancellationToken)
    {
        var imagePath = Path.Combine(_settings.UserDataDir, "q.png");
        try
        {
            foreach (var chapterTest in resolver.Tests)
            {
                await chapterTest.LoadQuestionsAsync();
                if (chapterTest.IsCompleted)
                    _logService.AddLog("该章节测试已完成");

                if (!chapterTest.HasQuestion || chapterTest.IsCompleted)
                    continue;

                foreach (var question in chapterTest.Questions)
                {
                    if (!await AnswerQuestionAsync(question, imagePath))
                        return false;

                    await Task.Delay(_settings.AiAnsweringInterval, cancellationToken);
                }

                await chapterTest.SubmitAnswerAsync();
                await resolver.ConfirmTestSubmissionAsync();
                _logService.AddLog("该章节测试提交成功");
                await Task.Delay(_settings.ChapterJumpInterval, cancellationToken);
            }

            return true;
        }
        finally
        {
            if (File.Exists(imagePath))
                File.Delete(imagePath);
        }
    }

    private async Task<bool> AnswerQuestionAsync(Question question, string imagePath)
    {
        await question.LoadAnswersAsync();
        var image = await question.CaptureImageAsync();
        if (image is null)
            return DisableAutoTest("题目截图失败，已关闭自动答题");

        await File.WriteAllBytesAsync(imagePath, image);
        var questionText = _settings.UsedAiToOcr
            ? await _aiControlService.GetTextFromImage(imagePath)
            : await _ocrService.RecognizeAsync(imagePath);

        if (questionText is null)
        {
            return DisableAutoTest(_settings.UsedAiToOcr
                ? "AIOCR 识图异常，已自动关闭自动答题"
                : "OCR 识图异常，已关闭自动答题并结束刷课");
        }

        var answer = await _aiControlService.GetAnswer(questionText);
        if (answer is null)
            return DisableAutoTest("AI 配置异常，已关闭自动答题");

        var selectedCount = 0;
        foreach (var option in question.Answers)
        {
            var optionText = await option.Key.InnerTextAsync();
            var optionMarker = await option.Value.InnerTextAsync();
            if (!AnswerSelectsOption(answer, optionMarker, optionText))
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
        _logService.AddLog(message);
        return false;
    }

    private static bool AnswerSelectsOption(string answer, string marker, string optionText)
    {
        var markerCharacter = marker
            .Trim()
            .ToUpperInvariant()
            .FirstOrDefault(character => character is >= 'A' and <= 'H');

        if (markerCharacter != default &&
            answer.ToUpperInvariant().Contains(markerCharacter))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(optionText) &&
               answer.Contains(optionText.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
