using System;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core;

public class Video
{
    private IFrame frame;
    private IElementHandle handle;
    private IElementHandle videoElement;

    private readonly GlobalSettings _settings;
    public Video(IElementHandle data ,GlobalSettings settings)
    {
        handle = data;
        _settings = settings;
    }

    public async Task<bool> IsCompleted()
    {
        try
        {
            var iconHandle = await handle.QuerySelectorAsync("div.ans-job-icon.ans-job-icon-clear[aria-label='任务点未完成']");
            return iconHandle == null;
        }
        catch (Exception e)
        {
            return false;
        }
    }
    

    public async Task ControlVideos()
    {
        //GetFrameAndVedio
        var iframeElement = await handle.WaitForSelectorAsync("iframe");
        frame = await iframeElement.ContentFrameAsync();
        var div = await frame.QuerySelectorAsync("div.fullScreenContainer > #reader");
        await div.WaitForSelectorAsync("video.vjs-tech");
        videoElement = await div.QuerySelectorAsync("video.vjs-tech");
    }

   
    public async Task Play()
    {
        await videoElement.EvaluateAsync($@"
    (async function() {{
        const VIDEO_PAUSED_CLASS = 'vjs-paused';
        const VIDEO_ENDED_CLASS = 'vjs-ended';
        const VIDEO_STARTED_CLASS = 'vjs-has-started';
        
        const videoDiv = document.getElementById('video');
        const playBtn = document.querySelector('.vjs-play-control');
        const bigPlayBtn = document.querySelector('.vjs-big-play-button');
        
        if (!videoDiv || !playBtn) {{
            console.log('未找到视频元素，跳过');
            return;
        }}
        if (!videoDiv.classList.contains(VIDEO_STARTED_CLASS) && bigPlayBtn) {{
            bigPlayBtn.click();
        }}
        
        const video = videoDiv.querySelector('video');
        if (video) {{
            video.playbackRate = {_settings.VideoPlayRate};
            video.muted = {_settings.IsMuted.ToString().ToLower()};
            Object.defineProperty(video, 'playbackRate', {{
                get: () => {_settings.VideoPlayRate},
                set: () => {{}},
                configurable: true
            }});
        }}
        let pauseFreeze = false;
        const observer = new MutationObserver(() => {{
            // 视频结束
            if (videoDiv.classList.contains(VIDEO_ENDED_CLASS)) {{
                console.log('视频播放结束');
                observer.disconnect();
                return;
            }}
            // 视频暂停
            if (videoDiv.classList.contains(VIDEO_PAUSED_CLASS) && !pauseFreeze) {{
                console.log('检测到暂停，自动恢复');
                
                // 延迟确认，避免误判
                setTimeout(() => {{
                    if (videoDiv.classList.contains(VIDEO_PAUSED_CLASS)) {{
                        // 点击播放按钮
                        if (playBtn && !videoDiv.classList.contains(VIDEO_ENDED_CLASS)) {{
                            playBtn.click();
                            console.log('已点击播放按钮');
                        }}
                        
                        // JS 强制播放
                        if (video && video.paused) {{
                            video.play().catch(() => {{}});
                        }}
                    }}
                }}, 400);
            }}
        }});
        // 监听 class
        observer.observe(videoDiv, {{ attributes: true, attributeFilter: ['class'] }});
        if (videoDiv.classList.contains(VIDEO_PAUSED_CLASS)) {{
            playBtn?.click();
        }}
    }})();
    ",videoElement);
    }

    public async Task<bool> TryFinishVideo()
    {
        return await handle.EvaluateAsync<bool>(@"
(async function() {
    try {
        const video = document.querySelector('video.vjs-tech');
        const player = window.videojs?.getPlayer('video');
        if (!video) return false;
        if (!video.duration || isNaN(video.duration)) {
            await new Promise(r => video.addEventListener('loadedmetadata', r, { once: true }));
        }
        const targetTime = video.duration * 0.999;
        if (player) {
            player.currentTime(targetTime);
            player.pause();
            player.trigger('ended');
        } else {
            video.currentTime = targetTime;
            video.pause();
        }
        await new Promise(r => setTimeout(r, 500));
        return (video.currentTime / video.duration) >= 0.99;
    } catch {
        return false;
    }
})();
");
    }
    public async Task WaitForVideoEnd(int timeout =6000000)
    {
        await frame.WaitForFunctionAsync(
            @"() => {
            const videoDiv = document.getElementById('video');
            return videoDiv && videoDiv.classList.contains('vjs-ended');
        }",null,new (){Timeout =  timeout});
        
    }
}