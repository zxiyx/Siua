using System;
using System.Security.Cryptography;
using System.Text;

namespace Siua.Services;

/// <summary>浏览器登录状态绑定当前 Windows 用户；不用于账号密码。</summary>
public static class LoginStateProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Siua.LoginState.v1");

    public static string Protect(string password)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("登录状态保存仅支持 Windows。");
        var bytes = Encoding.UTF8.GetBytes(password);
        try { return Convert.ToBase64String(ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public static string Unprotect(string encryptedPassword)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("登录状态读取仅支持 Windows。");
        var bytes = ProtectedData.Unprotect(Convert.FromBase64String(encryptedPassword), Entropy, DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
