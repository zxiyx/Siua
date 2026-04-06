using System;
using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using Siua.Common;

namespace Siua;

public partial class GlobalSettings:ObservableObject
{
    [ObservableProperty]
    private string _browserCannel = "系统默认";
    [ObservableProperty]
    private bool _jumpCompleted  = true;
    [ObservableProperty]
    private int _chapterJumpInterval  = 2000;
    [ObservableProperty]
    private int _aiAnsweringInterval  = 2000;
    [ObservableProperty]
    private bool _tryFinishVideo  = false;
    [ObservableProperty]
    private bool _isMuted  = true;
    
    [ObservableProperty]
    private double _videoPlayRate  = 1.0;
    
    [ObservableProperty]
    private bool _usedAiToOcr  = false;
    
    [ObservableProperty]
    private bool _autoTest = false;
    
    [ObservableProperty]
    private bool _saveCookies  = true;

    [ObservableProperty]
    [JsonIgnore]
    private string _userDataDir = Path.Combine(Directory.GetCurrentDirectory(), "UserData");
    
    [ObservableProperty]
    private ObservableCollection<string> _courses  = new ();
    
    [ObservableProperty]
    private AiModelBase _currentAi = new()
    {
        ApiKey = null,
        Domain = null,
        ModelName = null
    };
    
    [JsonIgnore]
    private string JsonPath  => Path.Combine(Directory.GetCurrentDirectory(), "settings.json");

    [JsonIgnore] private bool IsLoading { get; set; } = false;
    [JsonIgnore] private System.Timers.Timer? _saveTimer;
    [JsonIgnore] private bool _isInitialized = false;
    public GlobalSettings()
    {
        _saveTimer = new System.Timers.Timer(1200) { AutoReset = false };
        _saveTimer.Elapsed += async(s, e) => await SaveToJson();
        this.PropertyChanged += OnPropertyChanged;
        _courses.CollectionChanged += OnCollectionChanged;
    }
    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Courses) or nameof(CurrentAi) or nameof(JsonPath))
            return;
        if (!_isInitialized || IsLoading) return;
        TriggerAutoSave();
    }
    
    private void OnCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (!_isInitialized || IsLoading) return;
        TriggerAutoSave();
    }
    private void TriggerAutoSave()
    {
        _saveTimer?.Stop();
        _saveTimer?.Start();
    }
    public void LoadFromJson()
    {
        if (!File.Exists(JsonPath))
        {
            _isInitialized = true;
            return;
        }
        try
        {
            IsLoading = true;
            var json = File.ReadAllText(JsonPath);
            JsonConvert.PopulateObject(json, this); 
             //var config = JsonConvert.DeserializeObject<GlobalSettings>(json);
             //if (config != null) CopyProperties(config, this);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Load settings failed: {ex.Message}");
        }
        finally
        {
            _isInitialized = true;
            IsLoading = false;
        }
    }
    public async Task SaveToJson()
    {
        try
        {
            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            var tempPath = JsonPath + ".tmp";
            await File.WriteAllTextAsync(JsonPath, json);
            File.Replace(tempPath, JsonPath, null);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Save settings failed: {ex.Message}");
        }
    }
    public static void CopyProperties<T>(T source, T target) where T : class
    {
        if (source == null || target == null) return;
        
        var properties = typeof(T).GetProperties()
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0);
        
        foreach (var prop in properties)
        {
            try
            {
                var value = prop.GetValue(source);
                prop.SetValue(target, value);
            }
            catch { /* 忽略无法复制的属性 */ }
        }
    }

        


}
