using System;
using System.Security.Cryptography;
using System.Text;

namespace Siua.Services;

/// <summary>密码绑定当前 Windows 用户，不在配置文件中保存明文。</summary>
public static class AccountPasswordProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Siua.LoginAccount.v1");

    public static string Protect(string password)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("账号密码保存仅支持 Windows。");
        var bytes = Encoding.UTF8.GetBytes(password);
        try { return Convert.ToBase64String(ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public static string Unprotect(string encryptedPassword)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("账号密码读取仅支持 Windows。");
        var bytes = ProtectedData.Unprotect(Convert.FromBase64String(encryptedPassword), Entropy, DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
