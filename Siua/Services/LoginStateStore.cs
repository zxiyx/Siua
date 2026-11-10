using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Siua.Common;

namespace Siua.Services;

/// <summary>仅保存加密的浏览器状态。撤销代次防止清除后旧任务重新写回。</summary>
public sealed class LoginStateStore
{
    private sealed class Entry
    {
        public readonly object Sync = new();
        public readonly SemaphoreSlim LoginGate = new(1, 1);
        public long Epoch;
        public long Version;
    }
    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly string _directory;
    public event Action? Changed;
    public LoginStateStore(string directory) => _directory = Path.Combine(directory, "LoginStates");
    private static string Key(LoginAccount account) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(account.Platform + "\n" + account.Id)));
    private Entry Get(LoginAccount account) => _entries.GetOrAdd(Key(account), _ => new Entry());
    private string FilePath(LoginAccount account) => Path.Combine(_directory, Key(account) + ".dat");
    public SemaphoreSlim LoginGate(LoginAccount account) => Get(account).LoginGate;
    public long Epoch(LoginAccount account) { var e = Get(account); lock (e.Sync) return e.Epoch; }
    public DateTime? SavedAt(LoginAccount account) => File.Exists(FilePath(account)) ? File.GetLastWriteTime(FilePath(account)) : null;

    public string? Read(LoginAccount account, long epoch, out long version)
    {
        var e = Get(account);
        lock (e.Sync)
        {
            version = e.Version;
            if (epoch != e.Epoch || !File.Exists(FilePath(account))) return null;
            var envelope = JObject.Parse(LoginStateProtector.Unprotect(File.ReadAllText(FilePath(account))));
            if ((string?)envelope["platform"] != account.Platform || (string?)envelope["account"] != account.Id)
                throw new InvalidDataException("登录状态与账号不匹配");
            var state = (string?)envelope["state"] ?? throw new InvalidDataException("登录状态为空");
            var parsed = JObject.Parse(state);
            if (parsed["cookies"] is not JArray || parsed["origins"] is not JArray)
                throw new InvalidDataException("登录状态格式无效");
            return state;
        }
    }

    public bool Save(LoginAccount account, long epoch, ref long version, string state)
    {
        var e = Get(account);
        lock (e.Sync)
        {
            if (!account.RememberLogin || epoch != e.Epoch || version != e.Version) return false;
            var encrypted = LoginStateProtector.Protect(JsonConvert.SerializeObject(new
                { platform = account.Platform, account = account.Id, state }));
            Directory.CreateDirectory(_directory);
            var path = FilePath(account);
            var temp = path + ".tmp";
            try
            {
                File.WriteAllText(temp, encrypted, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
                version = ++e.Version;
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        Changed?.Invoke();
        return true;
    }

    public void Clear(LoginAccount account)
    {
        var e = Get(account);
        lock (e.Sync)
        {
            e.Epoch++;
            e.Version++;
            var path = FilePath(account);
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
        }
        Changed?.Invoke();
    }
}
