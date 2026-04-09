using CommunityToolkit.Mvvm.ComponentModel;

namespace Siua.Common;

public partial class AiModelBase:ObservableObject
{
    [ObservableProperty] private string? _aiProvider;

    [ObservableProperty] private string? _domain;

    [ObservableProperty] private string? _modelName;

    [ObservableProperty] private string? _apiKey;
}
