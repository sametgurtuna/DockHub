using System.Windows;
using CustomDock.Core;
using CustomDock.Shell;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace CustomDock.Dock;

/// <summary>Sistem tepsisi ikonu ve menüsü (WinForms NotifyIcon).</summary>
public sealed class TrayIconManager : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ContextMenuStrip _menu;

    public TrayIconManager()
    {
        _menu = new Forms.ContextMenuStrip { ShowImageMargin = false };
        _menu.Items.Add("Ayarlar…", null, (_, _) => App.Instance.ShowSettings());
        _menu.Items.Add("Dock öğeleri…", null, (_, _) => App.Instance.ShowSettings("items"));
        _menu.Items.Add("Widget ekle…", null, (_, _) => App.Instance.ShowSettings("gallery"));
        _menu.Items.Add("Dock'u göster", null, (_, _) => App.Instance.RevealDock());
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("Görev çubuğunu geri getir", null, (_, _) =>
        {
            TaskbarController.ForceShow();
            AppServices.Config.TaskbarMode = TaskbarMode.ShowBoth;
        });
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("Çıkış", null, (_, _) => App.Instance.ExitApplication());
        _menu.Opening += (_, _) => ApplyMenuTheme();

        _icon = new Forms.NotifyIcon
        {
            Text = "DockHub",
            Icon = LoadIcon(),
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                App.Instance.ShowSettings();
        };
    }

    public void ShowBalloon(string title, string text)
        => _icon.ShowBalloonTip(5000, title, text, Forms.ToolTipIcon.Info);

    private static Drawing.Icon LoadIcon()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/DockHub;component/Assets/DockHub.ico"));
            if (info is not null)
            {
                using var stream = info.Stream;
                return new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Tray ikonu yüklenemedi");
        }
        return Drawing.SystemIcons.Application;
    }

    private void ApplyMenuTheme()
    {
        if (ThemeManager.IsDark)
        {
            _menu.Renderer = new Forms.ToolStripProfessionalRenderer(new DarkMenuColors()) { RoundedEdges = true };
            _menu.ForeColor = Drawing.Color.FromArgb(240, 240, 240);
        }
        else
        {
            _menu.Renderer = new Forms.ToolStripProfessionalRenderer();
            _menu.ForeColor = Drawing.SystemColors.ControlText;
        }
        foreach (Forms.ToolStripItem item in _menu.Items)
            item.ForeColor = _menu.ForeColor;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }

    private sealed class DarkMenuColors : Forms.ProfessionalColorTable
    {
        private static readonly Drawing.Color Back = Drawing.Color.FromArgb(32, 32, 36);
        private static readonly Drawing.Color Hover = Drawing.Color.FromArgb(55, 55, 60);
        private static readonly Drawing.Color Line = Drawing.Color.FromArgb(62, 62, 68);

        public override Drawing.Color ToolStripDropDownBackground => Back;
        public override Drawing.Color MenuBorder => Line;
        public override Drawing.Color MenuItemBorder => Hover;
        public override Drawing.Color MenuItemSelected => Hover;
        public override Drawing.Color MenuItemSelectedGradientBegin => Hover;
        public override Drawing.Color MenuItemSelectedGradientEnd => Hover;
        public override Drawing.Color SeparatorDark => Line;
        public override Drawing.Color SeparatorLight => Back;
        public override Drawing.Color ImageMarginGradientBegin => Back;
        public override Drawing.Color ImageMarginGradientMiddle => Back;
        public override Drawing.Color ImageMarginGradientEnd => Back;
    }
}
