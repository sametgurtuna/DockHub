using System.Windows;
using CustomDock.Controls;
using CustomDock.Native;
using CustomDock.Services;

namespace CustomDock.Dock;

public sealed partial class AppButton
{
    private void OnFileDragEnter(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        Motion.Fade(_hover, 1, 100);
        AnimatePress(HoverScale);
        _dragActivateTimer.Stop();
        _dragActivateTimer.Start();
    }

    private void OnFileDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnFileDragLeave(object sender, DragEventArgs e)
    {
        _dragActivateTimer.Stop();
        Motion.Fade(_hover, 0, 160);
        AnimatePress(1);
    }

    private void OnFileDrop(object sender, DragEventArgs e)
    {
        _dragActivateTimer.Stop();
        Motion.Fade(_hover, 0, 160);
        AnimatePress(1);

        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;

        e.Handled = true;
        string? targetPath = LaunchPath;
        if (string.IsNullOrWhiteSpace(targetPath)) return;

        string? exeToRun = targetPath;
        if (exeToRun.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            exeToRun = ShellIcons.ResolveShortcut(exeToRun) ?? exeToRun;

        string args = string.Join(" ", files.Select(f => $"\"{f}\""));
        AppLauncher.Launch(exeToRun, args);
    }
}
