using System;
using Siua.Common;

namespace Siua.Interfaces;

public interface ILogService
{
    event Action<LogEntry>? OnLogAdded;
    event Action? OnLogCleared;
    void AddLog(string message);
    void AddLog(LogLevel level, string source, string message);
    void Clear();
}