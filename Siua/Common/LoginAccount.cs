using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

namespace Siua.Common;

/// <summary>只持久化加密后的密码；编辑时以新对象替换，避免半成品参与登录。</summary>
public sealed class LoginAccount
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Platform { get; init; } = LearningPlatformCatalog.XueXiTong;
    public string Name { get; init; } = "";
    public string Username { get; init; } = "";
    public string EncryptedPassword { get; init; } = "";

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
