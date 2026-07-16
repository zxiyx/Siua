using System;
using System.IO;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using Siua.Common;
using Siua.Services;

namespace Siua;

public partial class GlobalSettings : ObservableObject, IDisposable
{
    private const int SaveDelayMilliseconds = 750;

    private readonly object _saveScheduleLock = new();
    private readonly SettingsStorage _storage;
    private CancellationTokenSource? _saveDelayCts;
    private bool _isLoading;
    private bool _isInitialized;
    private int _isDisposed;

    [ObservableProperty] private string _currentPlatform = "学习通";
    [ObservableProperty] private string _pix2TextExecutablePath = Path.Combine("Pix2TextRuntime", "Scripts", "p2t.exe");
    [ObservableProperty] private int _pix2TextPort = 8503;
    [ObservableProperty] private string _browserCannel = "系统默认";
    [ObservableProperty] private bool _jumpCompleted = true;
    [ObservableProperty] private int _chapterJumpInterval = 2000;
    [ObservableProperty] private int _aiAnsweringInterval = 2000;
    [ObservableProperty] private bool _tryFinishVideo;
    [ObservableProperty] private bool _isMuted = true;
    [ObservableProperty] private int _popupTimeout = 1500;
    [ObservableProperty] private double _videoPlayRate = 1.0;
    [ObservableProperty] private bool _usedAiToOcr;
    [ObservableProperty] private bool _autoTest;

    [ObservableProperty]
    [property: JsonIgnore]
    private string _userDataDir = string.Empty;

    [ObservableProperty] private ObservableCollection<string> _courses = [];
    [ObservableProperty] private AiModelBase _currentAi = new();

    [JsonIgnore] public string SettingsFilePath => _storage.SettingsFilePath;
    [JsonIgnore] public string? LastSaveError => _storage.LastError;

    public GlobalSettings(string? storageDirectory = null)
    {
        _storage = new SettingsStorage(storageDirectory);
        UserDataDir = Path.Combine(_storage.StorageDirectory, "UserData");
        Directory.CreateDirectory(UserDataDir);

        PropertyChanged += HandleSettingsChanged;
        Courses.CollectionChanged += HandleCollectionChanged;
        CurrentAi.PropertyChanged += HandleSettingsChanged;
    }

    public void LoadFromJson()
    {
        ThrowIfDisposed();
        _isLoading = true;
        SettingsLoadResult result;
        try
        {
            result = _storage.Load();
            if (result.Snapshot is not null)
            {
                ApplySnapshot(result.Snapshot);
            }
        }
        finally
        {
            _isLoading = false;
            _isInitialized = true;
        }

        if (result.ShouldRewrite)
        {
            SaveToJson().GetAwaiter().GetResult();
        }
    }

    public Task SaveToJson()
    {
        ThrowIfDisposed();
        return _storage.SaveAsync(CreateSnapshot());
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        PropertyChanged -= HandleSettingsChanged;
        Courses.CollectionChanged -= HandleCollectionChanged;
        CurrentAi.PropertyChanged -= HandleSettingsChanged;
        CancelScheduledSave();

        // 确保防抖等待期间的最后一次修改也能在应用退出前落盘。
        Interlocked.Exchange(ref _isDisposed, 0);
        try
        {
            SaveToJson().GetAwaiter().GetResult();
        }
        finally
        {
            Interlocked.Exchange(ref _isDisposed, 1);
        }
    }

    partial void OnCoursesChanging(ObservableCollection<string> value)
    {
        Courses.CollectionChanged -= HandleCollectionChanged;
    }

    partial void OnCoursesChanged(ObservableCollection<string> value)
    {
        value.CollectionChanged += HandleCollectionChanged;
    }

    partial void OnCurrentAiChanging(AiModelBase value)
    {
        CurrentAi.PropertyChanged -= HandleSettingsChanged;
    }

    partial void OnCurrentAiChanged(AiModelBase value)
    {
        value.PropertyChanged += HandleSettingsChanged;
    }

    private void HandleSettingsChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (_isInitialized && !_isLoading)
        {
            ScheduleSave();
        }
    }

    private void HandleCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        if (_isInitialized && !_isLoading)
        {
            ScheduleSave();
        }
    }

    private void ScheduleSave()
    {
        if (Volatile.Read(ref _isDisposed) != 0)
        {
            return;
        }

        CancellationTokenSource current;
        CancellationTokenSource? previous;
        lock (_saveScheduleLock)
        {
            previous = _saveDelayCts;
            current = new CancellationTokenSource();
            _saveDelayCts = current;
        }

        previous?.Cancel();
        previous?.Dispose();
        _ = SaveAfterDelayAsync(current);
    }

    private async Task SaveAfterDelayAsync(CancellationTokenSource delaySource)
    {
        try
        {
            await Task.Delay(SaveDelayMilliseconds, delaySource.Token).ConfigureAwait(false);
            await SaveToJson().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (delaySource.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            lock (_saveScheduleLock)
            {
                if (ReferenceEquals(_saveDelayCts, delaySource))
                {
                    _saveDelayCts = null;
                }
            }

            delaySource.Dispose();
        }
    }

    private void CancelScheduledSave()
    {
        CancellationTokenSource? pending;
        lock (_saveScheduleLock)
        {
            pending = _saveDelayCts;
            _saveDelayCts = null;
        }

        pending?.Cancel();
    }

    private SettingsSnapshot CreateSnapshot() => new()
    {
        CurrentPlatform = CurrentPlatform,
        Pix2TextExecutablePath = Pix2TextExecutablePath,
        Pix2TextPort = Pix2TextPort,
        BrowserCannel = BrowserCannel,
        JumpCompleted = JumpCompleted,
        ChapterJumpInterval = ChapterJumpInterval,
        AiAnsweringInterval = AiAnsweringInterval,
        TryFinishVideo = TryFinishVideo,
        IsMuted = IsMuted,
        PopupTimeout = PopupTimeout,
        VideoPlayRate = VideoPlayRate,
        UsedAiToOcr = UsedAiToOcr,
        AutoTest = AutoTest,
        Courses = Courses.ToArray(),
        CurrentAi = new AiSettingsSnapshot
        {
            AiProvider = CurrentAi.AiProvider,
            Domain = CurrentAi.Domain,
            ModelName = CurrentAi.ModelName,
            ApiKey = CurrentAi.ApiKey
        }
    };

    private void ApplySnapshot(SettingsSnapshot snapshot)
    {
        CurrentPlatform = ValueOrDefault(snapshot.CurrentPlatform, "学习通");
        Pix2TextExecutablePath = ValueOrDefault(snapshot.Pix2TextExecutablePath,
            Path.Combine("Pix2TextRuntime", "Scripts", "p2t.exe"));
        Pix2TextPort = snapshot.Pix2TextPort is > 0 and <= 65535 ? snapshot.Pix2TextPort : 8503;
        BrowserCannel = ValueOrDefault(snapshot.BrowserCannel, "系统默认");
        JumpCompleted = snapshot.JumpCompleted;
        ChapterJumpInterval = Math.Max(0, snapshot.ChapterJumpInterval);
        AiAnsweringInterval = Math.Max(0, snapshot.AiAnsweringInterval);
        TryFinishVideo = snapshot.TryFinishVideo;
        IsMuted = snapshot.IsMuted;
        PopupTimeout = Math.Max(0, snapshot.PopupTimeout);
        VideoPlayRate = double.IsFinite(snapshot.VideoPlayRate) && snapshot.VideoPlayRate > 0
            ? snapshot.VideoPlayRate
            : 1.0;
        UsedAiToOcr = snapshot.UsedAiToOcr;
        AutoTest = snapshot.AutoTest;

        Courses.Clear();
        foreach (var course in snapshot.Courses ?? [])
        {
            if (!string.IsNullOrWhiteSpace(course))
            {
                Courses.Add(course);
            }
        }

        var ai = snapshot.CurrentAi ?? new AiSettingsSnapshot();
        CurrentAi.AiProvider = ai.AiProvider;
        CurrentAi.Domain = ai.Domain;
        CurrentAi.ModelName = ai.ModelName;
        CurrentAi.ApiKey = ai.ApiKey;
    }

    private static string ValueOrDefault(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);
}
