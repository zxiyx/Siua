using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Siua.Common;
using Siua.Interfaces;
using Siua.Services;

namespace Siua.ViewModels;

public partial class AccountEditViewModel : ViewModelBase
{
    public Action? RequestClose;
    private readonly ILogService _logService;
    public GlobalSettings Settings { get; }
    public ObservableCollection<LoginAccount> Accounts { get; } = [];
    [ObservableProperty] private LoginAccount? _selectedAccount;
    // TextBox 清空时可回传 null，不能仅依赖字段的初始空字符串。
    [ObservableProperty] private string? _accountName = "";
    [ObservableProperty] private string? _username = "";
    [ObservableProperty] private string? _password = "";
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private bool _isInfoOpen;
    [ObservableProperty] private string _infoMessage = "";
    [ObservableProperty] private NotificationType _infoSeverity = NotificationType.Information;
    public string EditorTitle => SelectedAccount is null ? "添加账号" : "编辑账号";
    public string PasswordHint => SelectedAccount is null ? "填写登录密码" : "留空则保留已保存的密码";
    public string PlatformHint => Settings.CurrentPlatform == LearningPlatformCatalog.XueXiTong
        ? "手机号或超星号登录；验证码、二次验证需在浏览器中完成。"
        : "可先保存账号；当前智慧树仍需在浏览器中手动登录。";

    public AccountEditViewModel(GlobalSettings settings, ILogService? logService = null)
    {
        Settings = settings;
        _logService = logService ?? new LogService();
        Settings.Accounts.CollectionChanged += (_, _) => RefreshAccounts();
        Settings.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(GlobalSettings.CurrentPlatform)) return;
            NewAccount();
            RefreshAccounts();
            OnPropertyChanged(nameof(PlatformHint));
        };
        RefreshAccounts();
    }

    private void RefreshAccounts()
    {
        var id = SelectedAccount?.Id;
        Accounts.Clear();
        foreach (var account in Settings.Accounts.Where(a => a.Platform == Settings.CurrentPlatform)) Accounts.Add(account);
        SelectedAccount = Accounts.FirstOrDefault(a => a.Id == id);
    }

    partial void OnSelectedAccountChanged(LoginAccount? value)
    {
        AccountName = value?.Name ?? "";
        Username = value?.Username ?? "";
        Password = "";
        IsInfoOpen = false;
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(PasswordHint));
        SaveAccountCommand.NotifyCanExecuteChanged();
        DeleteAccountCommand.NotifyCanExecuteChanged();
    }
    partial void OnUsernameChanged(string? value) => SaveAccountCommand.NotifyCanExecuteChanged();
    partial void OnPasswordChanged(string? value) => SaveAccountCommand.NotifyCanExecuteChanged();
    partial void OnIsSavingChanged(bool value)
    {
        SaveAccountCommand.NotifyCanExecuteChanged();
        DeleteAccountCommand.NotifyCanExecuteChanged();
        NewAccountCommand.NotifyCanExecuteChanged();
        CloseCommand.NotifyCanExecuteChanged();
    }
    private bool CanEdit() => !IsSaving;
    private bool CanSave() => !IsSaving && !string.IsNullOrWhiteSpace(Username) &&
        (!string.IsNullOrEmpty(Password) || SelectedAccount is not null);
    private bool CanDelete() => !IsSaving && SelectedAccount is not null;

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void NewAccount()
    {
        SelectedAccount = null;
        AccountName = Username = Password = "";
        IsInfoOpen = false;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAccount()
    {
        if (!CanSave()) return;
        var username = Username!.Trim();
        var name = AccountName?.Trim() ?? "";
        var password = Password ?? "";
        var selected = SelectedAccount;
        var platform = Settings.CurrentPlatform;
        if (Settings.Accounts.Any(a => a.Platform == platform && a.Id != selected?.Id &&
            string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase)))
        {
            ShowInfo("当前平台已保存此账号，请在列表中选择后编辑。", NotificationType.Warning);
            return;
        }
        IsSaving = true;
        var stage = "密码加密";
        try
        {
            var encryptedPassword = password.Length > 0
                ? AccountPasswordProtector.Protect(password) : selected!.EncryptedPassword;
            stage = "账号保存";
            var account = new LoginAccount
            {
                Id = selected?.Id ?? Guid.NewGuid().ToString("N"),
                Platform = platform, Name = name, Username = username,
                EncryptedPassword = encryptedPassword
            };
            var index = selected is null ? -1 : Settings.Accounts.IndexOf(selected);
            if (index < 0) Settings.Accounts.Add(account);
            else Settings.Accounts[index] = account;
            Settings.Login.SelectedAccountId ??= account.Id;
            stage = "账号配置写入";
            await Settings.SaveToJson();
            if (Settings.LastSaveException is { } saveException)
            {
                ReportFailure(stage, saveException, username, password, name);
                return;
            }
            stage = "保存结果更新";
            if (Settings.CurrentPlatform == platform)
            {
                SelectedAccount = account;
                Password = "";
            }
            ShowInfo("账号已保存", NotificationType.Success);
        }
        catch (Exception exception)
        {
            ReportFailure(stage, exception, username, password, name, selected?.Username, selected?.Name);
        }
        finally { IsSaving = false; }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAccount()
    {
        if (SelectedAccount is null) return;
        var account = SelectedAccount;
        var password = Password;
        IsSaving = true;
        try
        {
            Settings.Accounts.Remove(account);
            NewAccount();
            await Settings.SaveToJson();
            if (Settings.LastSaveException is { } exception)
                ReportFailure("账号删除写入", exception, account.Username, account.Name, password);
            else ShowInfo("账号已删除", NotificationType.Success);
        }
        catch (Exception exception) { ReportFailure("账号删除", exception, account.Username, account.Name, password); }
        finally { IsSaving = false; }
    }

    public void ResetEditor() => NewAccount();

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Close()
    {
        NewAccount();
        RequestClose?.Invoke();
    }

    private void ShowInfo(string message, NotificationType severity)
    {
        InfoMessage = message;
        InfoSeverity = severity;
        IsInfoOpen = true;
    }

    private void ReportFailure(string stage, Exception exception, params string?[] sensitiveValues)
    {
        // 使用保存前捕获的输入脱敏，列表刷新清空表单后也不能漏掉凭据。
        var details = "";
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var message = current.Message;
            foreach (var value in sensitiveValues.Where(value => !string.IsNullOrEmpty(value)).Distinct().OrderByDescending(value => value!.Length))
                message = message.Replace(value!, "[已隐藏]", StringComparison.Ordinal);
            details += $"{current.GetType().FullName} (0x{current.HResult:X8})：{message}{Environment.NewLine}{current.StackTrace}{Environment.NewLine}";
        }
        _logService.AddLog(LogLevel.Error, "Account", $"{stage}失败：{Environment.NewLine}{details}");
        ShowInfo($"{stage}失败，请检查 Log/Log.txt 文件。", NotificationType.Error);
    }
}
