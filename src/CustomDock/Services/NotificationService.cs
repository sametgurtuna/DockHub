using System.Windows;
using CustomDock.Core;
using Microsoft.Toolkit.Uwp.Notifications;

namespace CustomDock.Services;

public sealed record ToastAction(string Label, string Action, string? Id = null);

/// <summary>
/// Windows toast notifications. For unpackaged desktop applications,
/// Microsoft.Toolkit.Uwp.Notifications automatically handles AUMID registration and COM activation.
/// </summary>
public sealed class NotificationService
{
    public const string ActionHydrationDrink = "hydration-drink";
    public const string ActionReminderSnooze = "reminder-snooze";
    public const string ActionReminderDone = "reminder-done";
    public const string ActionOpenUpdate = "open-update";
    public const string ActionSkipUpdate = "skip-update";

    private bool _initialized;

    /// <summary>Fallback to use if toast cannot be shown (tray balloon notification).</summary>
    public Action<string, string>? Fallback { get; set; }

    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            ToastNotificationManagerCompat.OnActivated += e =>
            {
                var args = ToastArguments.Parse(e.Argument);
                Application.Current?.Dispatcher.BeginInvoke(() => HandleActivation(args));
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to register toast activation");
        }
    }

    public void Show(string title, string body, string? tag = null, params ToastAction[] actions)
    {
        try
        {
            var builder = new ToastContentBuilder()
                .AddArgument("action", "open")
                .AddText(title)
                .AddText(body);

            foreach (var action in actions)
            {
                var button = new ToastButton()
                    .SetContent(action.Label)
                    .AddArgument("action", action.Action)
                    .SetBackgroundActivation();
                if (action.Id is not null)
                    button.AddArgument("id", action.Id);
                builder.AddButton(button);
            }

            builder.Show(toast =>
            {
                if (tag is null) return;
                toast.Tag = tag;
                toast.Group = "customdock";
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to show toast notification");
            Fallback?.Invoke(title, body);
        }
    }

    /// <summary>Alarm scenario notification (stays on screen until dismissed by user, plays alarm sound).</summary>
    public void ShowAlarm(string title, string body, string tag)
    {
        try
        {
            new ToastContentBuilder()
                .SetToastScenario(ToastScenario.Alarm)
                .AddArgument("action", "open")
                .AddText(title)
                .AddText(body)
                .AddAudio(new Uri("ms-winsoundevent:Notification.Looping.Alarm"), loop: true)
                .AddButton(new ToastButtonDismiss("Dismiss"))
                .Show(toast =>
                {
                    toast.Tag = tag;
                    toast.Group = "customdock";
                });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to show alarm notification");
            Show(title, body, tag);
        }
    }

    private static void HandleActivation(ToastArguments args)
    {
        if (!args.TryGetValue("action", out string? action)) return;
        args.TryGetValue("id", out string? id);

        switch (action)
        {
            case ActionHydrationDrink:
                AppServices.Hydration.AddGlass();
                break;
            case ActionReminderSnooze when id is not null:
                AppServices.Reminders.Snooze(id, TimeSpan.FromMinutes(10));
                break;
            case ActionOpenUpdate:
                App.Instance.ShowSettings("about");
                break;
            case ActionSkipUpdate when AppServices.Updates.Available is { } skipped:
                AppServices.Updates.Skip(skipped);
                break;
        }
    }

    public static void Cleanup()
    {
        try
        {
            ToastNotificationManagerCompat.History.Clear();
        }
        catch
        {
            // ignore
        }
    }
}
