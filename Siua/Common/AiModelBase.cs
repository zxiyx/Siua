using CommunityToolkit.Mvvm.ComponentModel;

namespace Siua.Common;

/// <summary>保存当前 AI 模型的连接参数。</summary>
public partial class AiModelBase:ObservableObject
{
    [ObservableProperty] private string? _aiProvider;

    [ObservableProperty] private string? _domain;

    [ObservableProperty] private string? _modelName;

    [ObservableProperty] private string? _apiKey;
}
