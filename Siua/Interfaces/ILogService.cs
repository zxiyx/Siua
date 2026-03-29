using System;

namespace Siua.Interfaces;

public interface ILogService
{
    event Action<string>? OnLogAdded;
    event Action? OnLogCleared;
    void AddLog(string message);
    void Clear();
}