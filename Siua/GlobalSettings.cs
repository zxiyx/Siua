using System;
using System.IO;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using Siua.Common;
using Siua.Services;

namespace Siua;

/// <summary>管理应用运行设置及其持久化状态。</summary>
public partial class GlobalSettings : ObservableObject, IDisposable
{
    private const int SaveDelayMilliseconds = 750;

    private readonly object _saveScheduleLock = new();
    private readonly SettingsStorage _storage;
    private readonly Dictionary<string, ObservableCollection<string>> _coursesByPlatform =
        new(StringComparer.Ordinal);
    private CancellationTokenSource? _saveDelayCts;
    private bool _isLoading;
    private bool _isInitialized;
    private int _isDisposed;

    [ObservableProperty] private string _currentPlatform = "学习通";
    [ObservableProperty] private string _pix2TextExecutablePath = Path.Combine("Pix2TextRuntime", "Scripts", "p2t.exe");
    [ObservableProperty] private string _pix2TextHost = "127.0.0.1";
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
    [ObservableProperty] private bool _randomTest;

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
        _coursesByPlatform[CurrentPlatform] = Courses;

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

        var requiresCourseMigration = result.Snapshot?.Courses is { Length: > 0 } &&
                                      result.Snapshot.CoursesByPlatform is not { Count: > 0 };
        if (result.ShouldRewrite || requiresCourseMigration)
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
        value.CollectionChanged -= HandleCollectionChanged;
    }

    partial void OnCoursesChanged(ObservableCollection<string> value)
    {
        _coursesByPlatform[CurrentPlatform] = value;
        value.CollectionChanged += HandleCollectionChanged;
    }

    partial void OnCurrentPlatformChanged(string value)
    {
        Courses = GetOrCreateCourses(value);
    }

    partial void OnCurrentAiChanging(AiModelBase value)
    {
        CurrentAi.PropertyChanged -= HandleSettingsChanged;
    }

    partial void OnCurrentAiChanged(AiModelBase value)
    {
        value.PropertyChanged += HandleSettingsChanged;
    }

    partial void OnAutoTestChanged(bool value)
    {
        if (value)
            RandomTest = false;
    }

    partial void OnRandomTestChanged(bool value)
    {
        if (value)
            AutoTest = false;
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
        Pix2TextHost = Pix2TextHost,
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
        RandomTest = RandomTest,
        CoursesByPlatform = _coursesByPlatform.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.Ordinal),
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
        var currentPlatform = ValueOrDefault(snapshot.CurrentPlatform, LearningPlatformCatalog.XueXiTong);
        _coursesByPlatform.Clear();
        foreach (var pair in snapshot.CoursesByPlatform ?? [])
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                continue;

            _coursesByPlatform[pair.Key] = new ObservableCollection<string>(
                (pair.Value ?? []).Where(course => !string.IsNullOrWhiteSpace(course)));
        }

        if (snapshot.Courses is { Length: > 0 } &&
            !_coursesByPlatform.ContainsKey(currentPlatform))
        {
            _coursesByPlatform[currentPlatform] = new ObservableCollection<string>(
                snapshot.Courses.Where(course => !string.IsNullOrWhiteSpace(course)));
        }

        CurrentPlatform = currentPlatform;
        Courses = GetOrCreateCourses(currentPlatform);
        Pix2TextExecutablePath = ValueOrDefault(snapshot.Pix2TextExecutablePath,
            Path.Combine("Pix2TextRuntime", "Scripts", "p2t.exe"));
        Pix2TextHost = ValueOrDefault(snapshot.Pix2TextHost, "127.0.0.1");
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
        RandomTest = snapshot.RandomTest;

        var ai = snapshot.CurrentAi ?? new AiSettingsSnapshot();
        CurrentAi.AiProvider = ai.AiProvider;
        CurrentAi.Domain = ai.Domain;
        CurrentAi.ModelName = ai.ModelName;
        CurrentAi.ApiKey = ai.ApiKey;
    }

    private static string ValueOrDefault(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private ObservableCollection<string> GetOrCreateCourses(string platform)
    {
        if (_coursesByPlatform.TryGetValue(platform, out var courses))
            return courses;

        courses = [];
        _coursesByPlatform[platform] = courses;
        return courses;
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);
}
