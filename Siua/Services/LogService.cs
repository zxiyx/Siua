using System;
using Siua.Common;
using Siua.Interfaces;
namespace Siua.Services;

/// <summary>集中记录并分发应用运行日志。</summary>
public class LogService:ILogService
{
    public event Action<LogEntry>? OnLogAdded;
    
    public event Action? OnLogCleared;
    public void AddLog(string message)
    {
        if (message.StartsWith("[Browser]", StringComparison.Ordinal))
            AddLog(LogLevel.Browser, "Browser", message);
        else
            AddLog(LogLevel.Info, "App", message);
    }
    
    public void AddLog(LogLevel level, string source, string message)
    {
        var entry = new LogEntry
        {
            Time = DateTime.Now,
            Level = level,
            Source = source,
            Message = message
        };
        OnLogAdded?.Invoke(entry);
    }
    public void Clear()
    {
        OnLogCleared?.Invoke();
    }
}
