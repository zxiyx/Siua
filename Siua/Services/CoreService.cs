using System;
using System.IO;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Microsoft.Win32;
using Siua.Interfaces;
using Siua.Core;

namespace Siua.Services;

public class CoreService : ICoreService
{
    private IPlaywright _playwright;
    private IBrowser _context;
    private IPage _page;
    private StringBuilder _sbQuestion = new();
    private readonly ILogService _logService;
    private readonly GlobalSettings _settings;
    private readonly PaddleOcrService _ocrService;
    private readonly AiControlService _aiControlService;
    private CancellationTokenSource _loginHeartbeatCts;

    public CoreService(ILogService logService, GlobalSettings settings,PaddleOcrService paddleOcrService,AiControlService aiControlService)
    {
        _logService = logService;
        _settings = settings;
        _ocrService = paddleOcrService;
        _aiControlService = aiControlService;
    }
    public async Task LoadPlaywright()
    {
        _logService.Clear();
        _logService.AddLog($"Create Playwright");
        _playwright = await Playwright.CreateAsync();
        var op = new BrowserTypeLaunchOptions()
        {
            Headless = false,
            Args = new[]
            {
                "--disable-blink-features=AutomationControlled",
                "--disable-site-isolation-trials",
                "--no-default-browser-check"
            }
        };
        if (_settings.BrowserCannel == "系统默认")
        {
            var p = GetDefaultBrowserPath();
            if (!string.IsNullOrEmpty(p) && File.Exists(p))
            {
                op.ExecutablePath = GetDefaultBrowserPath();
            }
        }
        else
        {
            op.Channel = _settings.BrowserCannel.ToLower() switch
            {
                "edge" =>"msedge",
                "chrome" => "chrome",
                _=>null
                
            };
        }
        _context = await _playwright.Chromium.LaunchAsync(op);
        _page = await _context.NewPageAsync();
        _page.Console += async (sender, e) =>
        {
            try
            {

                _logService.AddLog($"[Browser] {e.Text}");
            }
            catch(Exception ex)
            {
                _logService.AddLog($"[Console Event Error] {ex.Message}");
            }
        };
        await _page.GotoAsync(_settings.Courses[0]);
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        
        if (_page.Url.Contains("passport", StringComparison.OrdinalIgnoreCase))
        {
            _logService.AddLog("检测到登录界面，请先登录.");
            _logService.AddLog("等待登录中...");
            await _page.WaitForURLAsync("**/mooc1.chaoxing.com/**", new() { Timeout = 300000 });
        }

        if (!_page.Url.Contains("passport", StringComparison.OrdinalIgnoreCase))
        {
            _logService.AddLog("登录成功");
        }
        else
        {
            _logService.AddLog("登录状态异常,请重新启动");
            return;
        }
        
        StartLoginHeartbeat();
    }

    private void StartLoginHeartbeat()
    {
        _loginHeartbeatCts = new CancellationTokenSource();
        var token = _loginHeartbeatCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(300000, token); 
                    if (_page.Url.Contains("passport", StringComparison.OrdinalIgnoreCase) ||
                        _page.Url == "https://i.chaoxing.com/base")
                    {
                        _logService.AddLog("检测到登录态失效，尝试恢复...");
                        await _page.GotoAsync(_settings.Courses[0]);
                        await _page.WaitForLoadStateAsync(LoadState.Load, new() { Timeout = 30000 });
                        if (_page.Url.Contains("passport"))
                        {
                            _logService.AddLog("需要手动重新登录");
                        }
                        else
                        {
                            _logService.AddLog("登录态已恢复，继续刷课");
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logService.AddLog($"[心跳] 检测异常：{ex.Message}");
                }
            }
        }, token);
    }
    public void StopLoginHeartbeat()
    {
        _loginHeartbeatCts?.Cancel();
        _loginHeartbeatCts?.Dispose();
    }
    private async Task SaveCookies()
    {
        var cookies = await _page.Context.CookiesAsync();
        await File.WriteAllTextAsync(Path.Combine(_settings.UserDataDir, "cookies.json"), System.Text.Json.JsonSerializer.Serialize(cookies));
    }


    public async Task ParsePage()
    {
        try
        {
            _sbQuestion.Clear();
            await _page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            PageResolver pr = new (_page, _settings);
            await pr.WaitLoading();
            await pr.ResolvePage();
            
            if (pr.HasVideo) 
            { 
                foreach (var video in pr.Videos) 
                { 
                    if (!await video.IsCompleted()) 
                    { 
                        _logService.AddLog("播放视频中...."); 
                        await video.ControlVideos(); 
                        await video.Play(); 
                        if (_settings.TryFinishVideo)
                        {
                            if (await video.TryFinishVideo())
                            {
                                _logService.AddLog("操控视频进度成功，视频播放完毕");
                            }
                            else
                            {
                                _logService.AddLog("操控视频进度失败，正常播放视频");
                            }
                        }
                        await video.WaitForVideoEnd();
                        _logService.AddLog("播放完毕"); 
                    }
                }
            }
            
            
            if (pr.HasTest && _settings.AutoTest) 
            { 
                var fp = Path.Combine(_settings.UserDataDir, "q.png"); 
                foreach (var ct in pr.Tests) 
                { 
                    await ct.GetTitleAndQuestions(); 
                    if (ct.IsCompleted)
                    { 
                        _logService.AddLog($"该章节测试已完成"); 
                    } 
                    if (ct.HasQuestion && !ct.IsCompleted) 
                    { 
                        foreach (var q in ct.Questions) 
                        { 
                            await q.GetAnswers(); 
                            if (_settings.UsedAiToOcr) 
                            { 
                                var bytes = await q.GetImageForQuestion(); 
                                await File.WriteAllBytesAsync(fp, bytes);
                                var r = await _aiControlService.GetTextFromImage(fp);
                                if (r==null)
                                {
                                    _settings.AutoTest = false;
                                    _logService.AddLog("AIOCR识图异常，已自动关闭自动答题");
                                    return;
                                }
                                _sbQuestion.Append(r); 
                            }
                            else 
                            { 
                                var bytes = await q.GetImageForQuestion(); 
                                await File.WriteAllBytesAsync(fp, bytes); 
                                var r =_ocrService.RunOCR(fp);
                                if (r==null)
                                {
                                    _settings.AutoTest = false;
                                    _logService.AddLog("OCR识图异常，已关闭自动答题并结束刷课");
                                    return;
                                }
                                _sbQuestion.Append(r); 
                            /*
                            _sbQuestion.Append($"标题：{q.Title}\n"); 
                            foreach (var i in q.Answers) 
                            { 
                                _sbQuestion.Append($"选项 {await i.Key.InnerTextAsync()} : {await i.Value.InnerTextAsync()}\n"); 
                            } 
                            */
                            } 
                            var ans = await _aiControlService.GetAnswer(_sbQuestion.ToString()); 
                            if (ans == null) 
                            { 
                                _settings.AutoTest = false;
                                _logService.AddLog("Ai配置异常！已关闭自动答题");
                                return;
                            } 
                            foreach (var a in q.Answers) 
                            { 
                                if (ans.Contains(await a.Key.InnerTextAsync())) 
                                { 
                                    await a.Key.ClickAsync(); 
                                } 
                            } 
                            _sbQuestion.Clear(); 
                            await Task.Delay(_settings.AiAnsweringInterval); 
                        } 
                        await ct.SubmitAnswer();
                        await pr.WaitForSubmitAgain();
                        _logService.AddLog($"该章节测试提交成功"); 
                        await Task.Delay(_settings.ChapterJumpInterval); 
                    } 
                } 
            } 
            _logService.AddLog("进入下一节..."); 
            await pr.NextPageAsync(); 
            await Task.Delay(_settings.ChapterJumpInterval);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
        
    }
    
    private string? GetDefaultBrowserPath()
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(@"http\shell\open\command");
            var rawValue = key?.GetValue("")?.ToString();
            if (string.IsNullOrWhiteSpace(rawValue))
                return null;
            return ParseExecutablePath(rawValue.Trim());
        }
        catch (UnauthorizedAccessException ex)
        {
            _logService.AddLog($"[注册表] 权限不足: {ex.Message}");
        }
        catch (SecurityException ex)
        {
            _logService.AddLog($"[注册表] 安全异常: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logService.AddLog($"[注册表] 读取失败 [{ex.GetType().Name}]: {ex.Message}");
        }
    
        return null;
    }

    private static string? ParseExecutablePath(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;
        if (command.StartsWith("\""))
        {
            var endIndex = command.IndexOf('"', 1);
            if (endIndex > 1)
            {
                var path = command.Substring(1, endIndex - 1);
                return File.Exists(path) ? path : null;
            }
        }
        else
        {
            var parts = command.Split(new[] { ' ', '\t' }, 2);
            if (parts.Length > 0)
            {
                var path = parts[0];
                if (path.Contains(".exe", StringComparison.OrdinalIgnoreCase))
                    return File.Exists(path) ? path : null;
            }
        }
        return null;
    }

    public void Dispose()
    {
        _playwright.Dispose();
        StopLoginHeartbeat();
    }


}