using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Siua.Common;
using Siua.Services;

namespace Siua.ViewModels;

public partial class AccountEditViewModel : ViewModelBase
{
    public Action? RequestClose;
    public GlobalSettings Settings { get; }
    public ObservableCollection<LoginAccount> Accounts { get; } = [];
    [ObservableProperty] private LoginAccount? _selectedAccount;
    [ObservableProperty] private string _accountName = "";
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private bool _isInfoOpen;
    [ObservableProperty] private string _infoMessage = "";
    [ObservableProperty] private NotificationType _infoSeverity = NotificationType.Information;
    public string EditorTitle => SelectedAccount is null ? "添加账号" : "编辑账号";
    public string PasswordHint => SelectedAccount is null ? "填写登录密码" : "留空则保留已保存的密码";
    public string PlatformHint => Settings.CurrentPlatform == LearningPlatformCatalog.XueXiTong
        ? "手机号或超星号登录；验证码、二次验证需在浏览器中完成。"
        : "可先保存账号；当前智慧树仍需在浏览器中手动登录。";

    public AccountEditViewModel(GlobalSettings settings)
    {
        Settings = settings;
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
    partial void OnUsernameChanged(string value) => SaveAccountCommand.NotifyCanExecuteChanged();
    partial void OnPasswordChanged(string value) => SaveAccountCommand.NotifyCanExecuteChanged();
    partial void OnIsSavingChanged(bool value)
    {
        SaveAccountCommand.NotifyCanExecuteChanged();
        DeleteAccountCommand.NotifyCanExecuteChanged();
        NewAccountCommand.NotifyCanExecuteChanged();
        CloseCommand.NotifyCanExecuteChanged();
    }
    private bool CanEdit() => !IsSaving;
    private bool CanSave() => !IsSaving && !string.IsNullOrWhiteSpace(Username) &&
        (Password.Length > 0 || SelectedAccount is not null);
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
        var username = Username.Trim();
        if (Settings.Accounts.Any(a => a.Platform == Settings.CurrentPlatform && a.Id != SelectedAccount?.Id &&
            string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase)))
        {
            ShowInfo("当前平台已保存此账号，请在列表中选择后编辑。", NotificationType.Warning);
            return;
        }
        IsSaving = true;
        try
        {
            var account = new LoginAccount
            {
                Id = SelectedAccount?.Id ?? Guid.NewGuid().ToString("N"),
                Platform = Settings.CurrentPlatform, Name = AccountName.Trim(), Username = username,
                EncryptedPassword = Password.Length > 0 ? AccountPasswordProtector.Protect(Password) : SelectedAccount!.EncryptedPassword
            };
            var index = SelectedAccount is null ? -1 : Settings.Accounts.IndexOf(SelectedAccount);
            if (index < 0) Settings.Accounts.Add(account);
            else Settings.Accounts[index] = account;
            Settings.Login.SelectedAccountId ??= account.Id;
            await Settings.SaveToJson();
            SelectedAccount = account;
            Password = "";
            ShowInfo(Settings.LastSaveError is null ? "账号已保存" : "写入失败，请检查设置目录权限后重试。",
                Settings.LastSaveError is null ? NotificationType.Success : NotificationType.Error);
        }
        catch
        {
            // 加密异常和输入内容不写入普通日志，避免泄露凭据。
            ShowInfo("密码保存失败，请在当前 Windows 用户下重试。", NotificationType.Error);
        }
        finally { IsSaving = false; }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAccount()
    {
        if (SelectedAccount is null) return;
        IsSaving = true;
        try
        {
            Settings.Accounts.Remove(SelectedAccount);
            NewAccount();
            await Settings.SaveToJson();
            ShowInfo(Settings.LastSaveError is null ? "账号已删除" : "删除尚未写入磁盘，请检查设置目录权限后重试。",
                Settings.LastSaveError is null ? NotificationType.Success : NotificationType.Error);
        }
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
}
