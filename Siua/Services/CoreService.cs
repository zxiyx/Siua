using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Microsoft.Win32;
using Newtonsoft.Json;
using Siua.Interfaces;
using Siua.Core;

namespace Siua.Services;

public class CoreService : ICoreService
{
    private IPlaywright _playwright;
    private IBrowserContext _context;
    private IPage _page;
    private StringBuilder _sbQuestion = new();
    private readonly ILogService _logService;
    private readonly GlobalSettings _settings;
    private readonly PaddleOcrService _ocrService;
    private readonly AiControlService _aiControlService;

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
        var op = new BrowserTypeLaunchPersistentContextOptions()
        {
            Headless = false,
            Args = new[]
            {
                "--disable-blink-features=AutomationControlled",
                "--disable-site-isolation-trials"
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
        _context = await _playwright.Chromium.LaunchPersistentContextAsync(_settings.UserDataDir, op);
        _page = _context.Pages.Count > 0 ? _context.Pages[0] : await _context.NewPageAsync();
        _page.Console += async (sender, e) =>
        {
    
            /*
            var details = new List<string>();
            foreach (var arg in e.Args)
            {
                try
                {
                    var json = await arg.JsonValueAsync<string>();
                    details.Add(json?.ToString() ?? "null");
                }
                catch
                {
                    details.Add(arg.ToString());
                }
            }
            */
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
        await _page.WaitForLoadStateAsync(LoadState.Load);
        if (_page.Url.Contains("passport", StringComparison.OrdinalIgnoreCase))
        {
            _logService.AddLog("检测到登录界面，请先登录.");
        }
        _logService.AddLog("等待登录中...");
        await _page.WaitForURLAsync("**/mooc1.chaoxing.com/**", new() { Timeout = 60000 });
        _logService.AddLog("登录成功");
        if (_settings.SaveCookies)
        {
            await SaveCookies();
        }
    }

    private async Task SaveCookies()
    {
        var cookies = await _page.Context.CookiesAsync();
        await File.WriteAllTextAsync(Path.Combine(_settings.UserDataDir, "cookies.json"), System.Text.Json.JsonSerializer.Serialize(cookies));
    }

    private async Task LoadCookies()
    {
        try
        {
            if (!_settings.SaveCookies) return;
            var fp = Path.Combine(_settings.UserDataDir, "cookies.json");
            if (!File.Exists(fp))
            {
                _logService?.AddLog("Cookies 文件不存在，跳过加载");
                return;
            }
            
            var json = await File.ReadAllTextAsync(fp);
            var cookies = JsonConvert.DeserializeObject<List<Cookie>>(json);
            
            if (cookies != null && cookies.Count > 0)
            {
                await _context.AddCookiesAsync(cookies);
                _logService?.AddLog($"Cookies 已加载");
            }
        }
        catch (Exception ex)
        {
            _logService?.AddLog($"加载 Cookies 失败：{ex.Message}");
        }
    }

    public async Task ParsePage()
    {
        _sbQuestion.Clear();
        var pr = new PageResolver(_page, _settings);
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
                _logService.AddLog($"搜索到章节测试 : {ct.ChapterTitle}"); 
                if (ct.IsCompleted)
                { 
                    //await ct.WaitFotGetTotalScore(); 
                    _logService.AddLog($"该章节测试已完成"); 
                } 
                if (ct.HasQuestion && !ct.IsCompleted) 
                { 
                    foreach (var q in ct.Questions) 
                    { 
                        await q.GetAnswers(); 
                        if (_settings.UsedOcr) 
                        { 
                            var bytes = await q.GetImageForQuestion(); 
                            await File.WriteAllBytesAsync(fp, bytes); 
                            _sbQuestion.Append(_ocrService.RunOCR(fp)); 
                        }
                        else 
                        { 
                            _sbQuestion.Append($"标题：{q.Title}\n"); 
                            foreach (var i in q.Answers) 
                            { 
                                _sbQuestion.Append($"选项 {await i.Key.InnerTextAsync()} : {await i.Value.InnerTextAsync()}\n"); 
                            } 
                        } 
                        var ans = await _aiControlService.GetAnswer(_sbQuestion.ToString()); 
                        if (ans == null) 
                        { 
                            _logService.AddLog("Ai配置异常！程序已暂停"); 
                            break; 
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
    }


}