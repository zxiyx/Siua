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

namespace Siua.Core.Zhs;

/// <summary>组织智慧树课程任务的完整执行流程。</summary>
public sealed class ZhsRunner
{
    private const string LogSource = "Zhs";

    private readonly IPage _page;
    private readonly GlobalSettings _settings;
    private readonly ILogService _logService;
    private readonly Pix2TextService _ocrService;
    private readonly AiControlService _aiControlService;

    public ZhsRunner(
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
        var resolver = new ZhsPageResolver(_page);
        if (!await resolver.WaitLoadingAsync(cancellationToken))
        {
            LogError("智慧树课程页面加载失败");
            return false;
        }

        var chapters = await resolver.ResolveChaptersAsync(cancellationToken);
        if (chapters.Count == 0)
        {
            LogError("未识别到智慧树课程章节");
            return false;
        }

        LogInfo("智慧树课程目录解析完成");
        foreach (var chapter in chapters)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                LogInfo("开始处理章节");
                await chapter.ExpandAsync(cancellationToken);

                foreach (var section in chapter.Sections)
                {
                    try
                    {
                        await ProcessSectionAsync(
                            resolver,
                            chapter,
                            section,
                            cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception) when (
                        exception is PlaywrightException or InvalidOperationException &&
                        !_page.IsClosed)
                    {
                        LogError($"智慧树小节处理失败，已跳过：{exception.Message}");
                    }
                }

                if (chapter.HasTest)
                {
                    if (!_settings.AutoTest && !_settings.RandomTest)
                    {
                        LogInfo("跳过章节测试");
                        continue;
                    }

                    await chapter.ExpandAsync(cancellationToken);
                    if (!await ProcessChapterTestAsync(
                            chapter.ChapterTest!,
                            cancellationToken))
                    {
                        return false;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is PlaywrightException or InvalidOperationException &&
                !_page.IsClosed)
            {
                LogError($"智慧树章节处理失败，已跳过：{exception.Message}");
            }
        }

        LogInfo("智慧树课程处理完成");
        return true;
    }

    private async Task ProcessSectionAsync(
        ZhsPageResolver resolver,
        ZhsChapter chapter,
        ZhsSection section,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await chapter.ExpandAsync(cancellationToken);
        if (_settings.JumpCompleted && await section.IsCompletedAsync(cancellationToken))
        {
            LogInfo("跳过已完成小节");
            return;
        }

        LogInfo("进入小节");
        await section.ClickAsync(cancellationToken);
        if (!await resolver.WaitForContentLoadAsync(cancellationToken))
        {
            LogError("小节内容加载超时，已跳过当前小节");
            return;
        }

        var video = await resolver.ResolveCurrentVideoAsync(cancellationToken);
        if (video is null)
        {
            LogInfo("当前小节没有可播放视频，已跳过");
            return;
        }

        await video.PlayAsync(
            _settings.VideoPlayRate,
            _settings.IsMuted,
            cancellationToken);
        if (_settings.TryFinishVideo)
        {
            var finished = await video.TryFinishAsync(cancellationToken);
            LogInfo(finished
                ? "操控视频进度成功，视频播放完毕"
                : "操控视频进度失败，继续正常播放");
        }

        if (!await video.WaitForEndAsync(
                _settings.VideoPlayRate,
                _settings.IsMuted,
                cancellationToken: cancellationToken))
        {
            LogError("视频结束检测超时，已继续处理后续小节");
            return;
        }

        LogInfo("视频播放完毕");
        await Task.Delay(_settings.ChapterJumpInterval, cancellationToken);
    }

    private async Task<bool> ProcessChapterTestAsync(
        ZhsChapterTest chapterTest,
        CancellationToken cancellationToken)
    {
        var originalCourseUrl = _page.Url;
        IPage? examPage = null;
        try
        {
            LogInfo("进入智慧树章节测试...");
            examPage = await chapterTest.OpenAsync(_page, cancellationToken);
            var questionCount = await chapterTest.GetQuestionCountAsync(
                examPage,
                cancellationToken);
            if (questionCount <= 0)
                return DisableAutoTest("未识别到智慧树章节测试题目，已关闭自动答题");

            LogInfo($"检测到 {questionCount} 道智慧树章节测试题");
            IReadOnlyDictionary<string, IReadOnlyList<string>>? chapterAnswers = null;
            if (_settings.RegionScreenshot && !_settings.RandomTest)
            {
                chapterAnswers = await AnswerChapterAsync(chapterTest, examPage, questionCount, cancellationToken);
                if (chapterAnswers is null)
                    return false;
                await Task.Delay(_settings.AiAnsweringInterval, cancellationToken);
            }
            var processedCount = 0;
            var previousQuestionKey = string.Empty;
            var processedKeys = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < questionCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var question = await chapterTest.LoadCurrentQuestionAsync(
                    examPage,
                    cancellationToken);
                if (string.IsNullOrWhiteSpace(question.Key) ||
                    string.Equals(
                        question.Key,
                        previousQuestionKey,
                        StringComparison.Ordinal) || !processedKeys.Add(question.Key))
                {
                    return DisableAutoTest("智慧树章节测试未能切换到下一题，已关闭自动答题");
                }

                previousQuestionKey = question.Key;
                if (chapterAnswers is not null)
                {
                    if (!chapterAnswers.TryGetValue(question.Key, out var answers))
                        return DisableAutoTest("区域截图后的题目发生变化，已停止答题");
                    var selected = question.Answers.Where(option => answers.Contains(option.Marker)).ToArray();
                    if (selected.Length != answers.Count)
                        return DisableAutoTest("区域截图后的选项发生变化，已停止答题");
                    LogInfo(AnswerLogFormatter.Format(question.Number, index + 1, answers));
                    foreach (var option in selected)
                        await option.SelectAsync(cancellationToken);
                }
                else if (!await AnswerQuestionAsync(question, index + 1, cancellationToken))
                    return false;

                processedCount++;
                if (chapterAnswers is null && !_settings.RandomTest)
                    await Task.Delay(_settings.AiAnsweringInterval, cancellationToken);
                var hasNextQuestion = await chapterTest.MoveNextOrSaveAsync(
                    examPage,
                    question.Key,
                    cancellationToken);
                if (!hasNextQuestion)
                    break;
            }

            if (processedCount != questionCount)
            {
                return DisableAutoTest(
                    $"智慧树章节测试题目数量异常：应处理 {questionCount} 题，实际处理 {processedCount} 题");
            }

            await chapterTest.SubmitAsync(
                examPage,
                _settings.PopupTimeout,
                cancellationToken);
            LogInfo("智慧树章节测试提交成功");
            await Task.Delay(_settings.ChapterJumpInterval, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return DisableAutoTest(
                $"智慧树章节测试处理失败：{exception.Message}，已关闭自动答题");
        }
        finally
        {
            if (examPage is not null)
            {
                try
                {
                    await ZhsChapterTest.CloseAndReturnAsync(
                        _page,
                        examPage,
                        originalCourseUrl);
                }
                catch (PlaywrightException exception)
                {
                    LogError($"返回智慧树课程页面失败：{exception.Message}");
                }
            }
        }
    }

    private async Task<bool> AnswerQuestionAsync(
        ZhsQuestion question,
        int questionIndex,
        CancellationToken cancellationToken)
    {
        if (_settings.RandomTest)
            return await SelectRandomAnswersAsync(question, cancellationToken);

        var image = await question.CaptureImageAsync(cancellationToken);
        if (image is null)
            return DisableAutoTest("智慧树题目截图失败，已关闭自动答题");

        var questionText = await _ocrService.RecognizeAsync(image, cancellationToken);
        if (questionText is null)
        {
            return DisableAutoTest("智慧树题目 OCR 识别异常，已关闭自动答题并结束刷课");
        }

        var answer = await _aiControlService.GetAnswer(questionText);
        if (string.IsNullOrWhiteSpace(answer))
            return DisableAutoTest("AI 配置或回答异常，已关闭智慧树自动答题");

        LogInfo(AnswerLogFormatter.Format(question.Number, questionIndex, answer));
        var selectedCount = 0;
        foreach (var option in question.Answers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!AnswerMatcher.SelectsOption(answer, option.Marker, option.Text))
                continue;

            await option.SelectAsync(cancellationToken);
            selectedCount++;
            if (!question.AllowsMultipleAnswers)
                break;
        }

        return selectedCount > 0 ||
               DisableAutoTest("AI 返回的答案无法匹配智慧树题目选项，已关闭自动答题");
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>?> AnswerChapterAsync(
        ZhsChapterTest chapterTest, IPage examPage, int questionCount, CancellationToken cancellationToken)
    {
        var questions = await chapterTest.LoadAllQuestionsAsync(examPage, cancellationToken);
        if (questions.Count != questionCount || questions.Any(question =>
                string.IsNullOrWhiteSpace(question.Key) || question.Answers.Count == 0) ||
            questions.Select(question => question.Key).Distinct().Count() != questionCount)
        {
            DisableAutoTest("智慧树整份试卷尚未完整加载或含不支持的题型，已停止区域截图答题");
            return null;
        }
        var specs = questions.Select((question, index) => new ChapterQuestionSpec(
            index + 1, question.Number, 0, question.AllowsMultipleAnswers,
            question.Answers.Select(option => option.Marker).ToArray())).ToArray();
        LogInfo($"区域截图：一次识别并回答 {questionCount} 道智慧树章节测试题");
        var image = await chapterTest.CaptureRegionAsync(examPage, questionCount, cancellationToken);
        var text = await _ocrService.RecognizeAsync(image, cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
        {
            DisableAutoTest("智慧树章节测试区域 OCR 识别失败，已停止答题");
            return null;
        }
        var response = await _aiControlService.GetChapterAnswers(text, specs);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ChapterAnswerParser.TryParse(response, specs, out var answers))
        {
            DisableAutoTest("智慧树整份试卷答案的题号或选项不匹配，未填写或提交，已停止答题");
            return null;
        }
        return questions.Select((question, index) => (question.Key, Answers: answers[index + 1]))
            .ToDictionary(item => item.Key, item => item.Answers, StringComparer.Ordinal);
    }

    private async Task<bool> SelectRandomAnswersAsync(
        ZhsQuestion question,
        CancellationToken cancellationToken)
    {
        var selectedIndexes = RandomAnswerSelector.Select(
            question.Answers.Count,
            question.AllowsMultipleAnswers);
        if (selectedIndexes.Count == 0)
            return DisableRandomTest("未识别到智慧树题目选项，已关闭随机答题");

        foreach (var index in selectedIndexes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await question.Answers[index].SelectAsync(cancellationToken);
        }

        LogInfo($"已随机选择 {selectedIndexes.Count} 个答案");
        return true;
    }

    private bool DisableAutoTest(string message)
    {
        // 章节加载、切题、提交等共用流程也可能由随机答题触发。
        if (_settings.RandomTest)
            return DisableRandomTest(message.Replace("自动答题", "随机答题", StringComparison.Ordinal));
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
