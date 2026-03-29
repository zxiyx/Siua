using System;
using Siua.Interfaces;
namespace Siua.Services;

public class LogService:ILogService
{
    public event Action<string>? OnLogAdded;
    
    public event Action? OnLogCleared;
    public void AddLog(string message)
    {
        OnLogAdded?.Invoke($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
    }
    public void Clear()
    {
        OnLogCleared?.Invoke();
    }
}