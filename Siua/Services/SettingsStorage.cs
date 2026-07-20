using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Siua.Common;

namespace Siua.Services;

/// <summary>负责应用设置文件的读取、写入与恢复。</summary>
internal sealed class SettingsStorage
{
    private const string SettingsFileName = "settings.json";
    private const string ApplicationDirectoryName = "Siua";
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public SettingsStorage(string? storageDirectory)
    {
        StorageDirectory = string.IsNullOrWhiteSpace(storageDirectory)
            ? GetDefaultStorageDirectory()
            : Path.GetFullPath(storageDirectory);
        SettingsFilePath = Path.Combine(StorageDirectory, SettingsFileName);
        Directory.CreateDirectory(StorageDirectory);
    }

    public string StorageDirectory { get; }
    public string SettingsFilePath { get; }
    public string? LastError { get; private set; }

    private string BackupFilePath => SettingsFilePath + ".bak";
    private string TemporaryFilePath => SettingsFilePath + ".tmp";

    public SettingsLoadResult Load()
    {
        foreach (var candidate in GetLoadCandidates())
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            if (TryRead(candidate, out var snapshot))
            {
                var shouldRewrite = !PathsEqual(candidate, SettingsFilePath);
                return new SettingsLoadResult(snapshot, shouldRewrite);
            }

            if (PathsEqual(candidate, SettingsFilePath))
            {
                ArchiveCorruptFile(candidate);
            }
        }

        return new SettingsLoadResult(null, false);
    }

    public async Task SaveAsync(SettingsSnapshot snapshot)
    {
        var json = JsonConvert.SerializeObject(snapshot, Formatting.Indented);
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await File.WriteAllTextAsync(
                    TemporaryFilePath,
                    json,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                .ConfigureAwait(false);

            if (File.Exists(SettingsFilePath))
            {
                File.Replace(TemporaryFilePath, SettingsFilePath, BackupFilePath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(TemporaryFilePath, SettingsFilePath);
            }

            LastError = null;
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            Debug.WriteLine($"Save settings failed: {exception}");
        }
        finally
        {
            DeleteTemporaryFile();
            _writeLock.Release();
        }
    }

    private IEnumerable<string> GetLoadCandidates()
    {
        return new[]
        {
            SettingsFilePath,
            TemporaryFilePath,
            BackupFilePath,
            Path.Combine(Directory.GetCurrentDirectory(), SettingsFileName),
            Path.Combine(AppContext.BaseDirectory, SettingsFileName)
        }.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryRead(string path, out SettingsSnapshot? snapshot)
    {
        try
        {
            snapshot = JsonConvert.DeserializeObject<SettingsSnapshot>(
                File.ReadAllText(path, Encoding.UTF8));
            return snapshot is not null;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Load settings failed from '{path}': {exception}");
            snapshot = null;
            return false;
        }
    }

    private static void ArchiveCorruptFile(string path)
    {
        try
        {
            var archivePath = Path.Combine(
                Path.GetDirectoryName(path)!,
                $"settings.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Move(path, archivePath, overwrite: true);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Archive corrupt settings failed: {exception}");
        }
    }

    private void DeleteTemporaryFile()
    {
        try
        {
            if (File.Exists(TemporaryFilePath))
            {
                File.Delete(TemporaryFilePath);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Delete temporary settings failed: {exception}");
        }
    }

    private static bool PathsEqual(string left, string right) =>
        Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static string GetDefaultStorageDirectory()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = string.IsNullOrWhiteSpace(localApplicationData)
            ? AppContext.BaseDirectory
            : localApplicationData;
        return Path.Combine(root, ApplicationDirectoryName);
    }
}
