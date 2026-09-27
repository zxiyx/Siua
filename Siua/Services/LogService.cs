using System;
using Siua.Common;
using System.IO;
using System.Security;
using System.Text;
using Siua.Interfaces;
namespace Siua.Services;

/// <summary>集中记录并分发应用运行日志。</summary>
public class LogService:ILogService
{
    private static readonly object ErrorFileLock = new();
    private readonly string _errorLogPath;

    public LogService() : this(AppContext.BaseDirectory) { }

    internal LogService(string runtimeDirectory)
    {
        _errorLogPath = Path.Combine(runtimeDirectory, "Log", "Log.txt");
    }

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
        var time = DateTime.Now;
        if (level == LogLevel.Error)
        {
            try
            {
                // 先保存完整详情，再缩短界面文案；关闭日志筛选或清空界面不影响文件。
                lock (ErrorFileLock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_errorLogPath)!);
                    File.AppendAllText(_errorLogPath,
                        $"[{time:yyyy-MM-dd HH:mm:ss.fff}] [Error] [{source}]{Environment.NewLine}" +
                        message + Environment.NewLine + new string('-', 72) + Environment.NewLine,
                        new UTF8Encoding(false));
                }
                message = SummarizeError(message);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
            {
                // 日志目录不可写时保留原错误，不能让用户去检查一个未成功写入的文件。
                message += $"{Environment.NewLine}本地错误日志写入失败：{exception.Message}";
            }
        }

        var entry = new LogEntry
        {
            Time = time,
            Level = level,
            Source = source,
            Message = message
        };
        OnLogAdded?.Invoke(entry);
    }

    private static string SummarizeError(string message)
    {
        var firstLine = message.Split(['\r', '\n'], 2)[0];
        var colon = firstLine.IndexOfAny(['：', ':']);
        var summary = colon >= 0 ? firstLine[..colon] : firstLine;
        return $"{summary.TrimEnd()}：检查Log.txt文件";
    }
    public void Clear()
    {
        OnLogCleared?.Invoke();
    }
}
