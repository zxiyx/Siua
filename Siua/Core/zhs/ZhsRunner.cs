using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Siua.Interfaces;

namespace Siua.Core.Zhs;

public sealed class ZhsRunner
{
    private readonly IPage _page;
    private readonly GlobalSettings _settings;
    private readonly ILogService _logService;

    public ZhsRunner(
        IPage page,
        GlobalSettings settings,
        ILogService logService)
    {
        _page = page;
        _settings = settings;
        _logService = logService;
    }

    public async Task<bool> RunAsync(CancellationToken cancellationToken = default)
    {
        var resolver = new PageResolver(_page);
        if (!await resolver.WaitLoadingAsync(cancellationToken))
        {
            _logService.AddLog("智慧树课程页面加载失败");
            return false;
        }

        var chapters = await resolver.ResolveChaptersAsync(cancellationToken);
        if (chapters.Count == 0)
        {
            _logService.AddLog("未识别到智慧树课程章节");
            return false;
        }

        _logService.AddLog("智慧树课程目录解析完成");
        foreach (var chapter in chapters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logService.AddLog("开始处理章节");
            await chapter.ExpandAsync(cancellationToken);

            foreach (var section in chapter.Sections)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_settings.JumpCompleted && await section.IsCompletedAsync(cancellationToken))
                {
                    _logService.AddLog("跳过已完成小节");
                    continue;
                }

                _logService.AddLog("进入小节");
                await section.ClickAsync(cancellationToken);
                if (!await resolver.WaitForContentLoadAsync(cancellationToken))
                {
                    _logService.AddLog("小节内容加载超时");
                    return false;
                }

                var video = await resolver.ResolveCurrentVideoAsync(cancellationToken);
                if (video is null)
                {
                    _logService.AddLog("当前小节没有可播放视频");
                    continue;
                }

                await video.PlayAsync(
                    _settings.VideoPlayRate,
                    _settings.IsMuted,
                    cancellationToken);

                if (_settings.TryFinishVideo)
                {
                    var finished = await video.TryFinishAsync(cancellationToken);
                    _logService.AddLog(finished
                        ? "操控视频进度成功，视频播放完毕"
                        : "操控视频进度失败，继续正常播放");
                }

                if (!await video.WaitForEndAsync(
                        _settings.VideoPlayRate,
                        _settings.IsMuted,
                        cancellationToken: cancellationToken))
                {
                    _logService.AddLog("视频结束检测超时");
                    return false;
                }

                _logService.AddLog("视频播放完毕");
                await Task.Delay(_settings.ChapterJumpInterval, cancellationToken);
            }

            if (chapter.HasTest)
            {
                _logService.AddLog(_settings.AutoTest
                    ? "检测到章节测试但智慧树答题尚未适配，已安全跳过"
                    : "跳过章节测试");
            }
        }

        _logService.AddLog("智慧树课程处理完成");
        return true;
    }
}
