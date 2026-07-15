using System;

namespace Siua.Common;

public enum LogLevel
{
    Info,
    Error,
    Browser
}

public sealed class LogEntry
{
    public DateTime Time { get; init; } = DateTime.Now;
    public LogLevel Level { get; init; } = LogLevel.Info;
    public string Source { get; init; } = "App";
    public string Message { get; init; } = string.Empty;

    public string LevelText => Level switch
    {
        LogLevel.Info => "信息",
        LogLevel.Error => "错误",
        LogLevel.Browser => "浏览器",
        _ => Level.ToString()
    };
}
