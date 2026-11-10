using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

namespace Siua.Common;

/// <summary>账号密码按用户选择明文保存，登录状态单独管理。</summary>
public sealed class LoginAccount
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Platform { get; init; } = LearningPlatformCatalog.XueXiTong;
    public string Name { get; init; } = "";
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
    public bool RememberLogin { get; init; }

    [JsonIgnore] public string MaskedUsername => Username.Length > 4
        ? Username[..2] + "••••" + Username[^2..] : "••••";
    [JsonIgnore] public string DisplayName => string.IsNullOrWhiteSpace(Name)
        ? MaskedUsername : $"{Name} · {MaskedUsername}";
    public override string ToString() => DisplayName;
}

public partial class LoginPreferences : ObservableObject
{
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string? _selectedAccountId;
}
