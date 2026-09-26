using Avalonia;
using Avalonia.Controls.Notifications;
using SukiUI.Controls;

namespace Siua.Common;

/// <summary>让动态绑定触发 SukiUI 内置的状态图标和颜色更新。</summary>
public sealed class InfoBarExtensions : AvaloniaObject
{
    public static readonly AttachedProperty<NotificationType> SeverityProperty =
        AvaloniaProperty.RegisterAttached<InfoBarExtensions, InfoBar, NotificationType>(
            "Severity", NotificationType.Information);

    static InfoBarExtensions()
    {
        SeverityProperty.Changed.AddClassHandler<InfoBar>((bar, _) =>
            bar.Severity = GetSeverity(bar));
    }

    public static NotificationType GetSeverity(InfoBar bar) => bar.GetValue(SeverityProperty);

    public static void SetSeverity(InfoBar bar, NotificationType value) => bar.SetValue(SeverityProperty, value);
}
