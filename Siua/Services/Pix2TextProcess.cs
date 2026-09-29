using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Siua.Services;

/// <summary>只保留启动期的有限输出；就绪后清空并丢弃输出，避免记录 OCR 正文。</summary>
internal sealed class Pix2TextProcess : IDisposable
{
    private readonly Process _process;
    private readonly object _sync = new();
    private readonly Queue<string> _lines = new();
    private int _characters;
    private bool _capture = true;
    private bool _disposed;
    private Task _output = Task.CompletedTask;
    private Task _error = Task.CompletedTask;

    public Pix2TextProcess(Process process, string executable)
    {
        _process = process;
        Executable = executable;
    }

    public string Executable { get; }
    public bool WasReady { get; private set; }
    public bool HasExited
    {
        get
        {
            lock (_sync)
            {
                try { return _disposed || _process.HasExited; }
                catch (InvalidOperationException) { return true; }
            }
        }
    }
    public int? ExitCode
    {
        get
        {
            lock (_sync)
            {
                try { return !_disposed && _process.HasExited ? _process.ExitCode : null; }
                catch (InvalidOperationException) { return null; }
            }
        }
    }

    public void Start(Action onExit)
    {
        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) => onExit();
        _process.Start();
        _output = DrainAsync(_process.StandardOutput, "stdout");
        _error = DrainAsync(_process.StandardError, "stderr");
    }

    private async Task DrainAsync(StreamReader reader, string source)
    {
        try
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                lock (_sync)
                {
                    if (!_capture || string.IsNullOrWhiteSpace(line)) continue;
                    // 只记录末尾最多 65536 字符 / 200 行，不无限积累下载进度或 Python 输出。
                    var entry = $"[{source}] " + (line.Length > 8192 ? line[..8192] + " [本行已截断]" : line);
                    _lines.Enqueue(entry);
                    _characters += entry.Length;
                    while (_characters > 65536 || _lines.Count > 200)
                        _characters -= _lines.Dequeue().Length;
                }
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    public void MarkReady()
    {
        lock (_sync)
        {
            WasReady = true;
            _capture = false;
            _lines.Clear();
            _characters = 0;
        }
    }

    public async Task<string> GetStartupDetailsAsync()
    {
        if (HasExited)
        {
            try { await Task.WhenAll(_output, _error).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
            catch (TimeoutException) { }
        }
        lock (_sync)
        {
            return $"Python：{Executable}{Environment.NewLine}启动输出（末尾，最多 65536 字符 / 200 行）：{Environment.NewLine}" +
                (_lines.Count == 0 ? "进程没有提供启动输出。" : string.Join(Environment.NewLine, _lines));
        }
    }

    public bool Stop()
    {
        lock (_sync)
        {
            if (_disposed || HasExited) return false;
            _process.Kill(entireProcessTree: true);
            if (!_process.WaitForExit(5000))
                throw new TimeoutException("Pix2Text 进程未在 5 秒内停止。");
            return true;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            Stop();
            _disposed = true;
            _capture = false;
            _process.Dispose();
        }
    }
}
