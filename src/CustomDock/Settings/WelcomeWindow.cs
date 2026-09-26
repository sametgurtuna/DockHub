using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using CustomDock.Controls;
using CustomDock.Core;
using CustomDock.Native;

namespace CustomDock.Settings;

/// <summary>
/// First-run welcome: explains what happened to the taskbar, lets the user choose the mode, the edge and a look,
/// and ends with a few tips. Changes apply live; closing the window keeps the defaults.
/// </summary>
public sealed class WelcomeWindow : Window
{
    private readonly AppConfig _config = AppServices.Config;
    private readonly ContentControl _page = new();
    private readonly Button _back;
    private readonly Button _next;
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private int _step;
    private TaskbarMode _chosenMode;

    public WelcomeWindow()
    {
        Title = "Welcome to DockHub";
        Width = 680;
        Height = 540;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/DockHub;component/Assets/DockHub.ico"));
        SetResourceReference(BackgroundProperty, "WindowBackgroundBrush");
        SetResourceReference(ForegroundProperty, "TextPrimaryBrush");
        FontFamily = (FontFamily)FindResource("UiFont");
        FontSize = 13;
        _chosenMode = _config.TaskbarMode;

        _back = new Button { Content = "Back", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        _next = new Button { MinWidth = 110 };
        _next.SetResourceReference(StyleProperty, "AccentButton");
        _back.Click += (_, _) => Go(_step - 1);
        _next.Click += (_, _) => { if (_step == 3) Finish(openGallery: false); else Go(_step + 1); };

        var footer = new DockPanel { Margin = new Thickness(32, 0, 32, 24), LastChildFill = false };
        DockPanel.SetDock(_dots, System.Windows.Controls.Dock.Left);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { _back, _next } };
        DockPanel.SetDock(buttons, System.Windows.Controls.Dock.Right);
        footer.Children.Add(_dots);
        footer.Children.Add(buttons);

        var root = new DockPanel();
        DockPanel.SetDock(footer, System.Windows.Controls.Dock.Bottom);
        root.Children.Add(footer);
        _page.Margin = new Thickness(32, 28, 32, 12);
        root.Children.Add(_page);
        Content = root;

        SourceInitialized += (_, _) => WindowEffects.SetDarkMode(new WindowInteropHelper(this).Handle, ThemeManager.IsDark);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Closed += (_, _) => { _config.WelcomeShown = true; AppServices.ConfigService.SaveNow(); };
        Go(0);
    }

    private void Go(int step)
    {
        _step = Math.Clamp(step, 0, 3);
        _page.Content = _step switch
        {
            0 => WelcomePage(),
            1 => ModePage(),
            2 => LookPage(),
            _ => TipsPage(),
        };
        _back.Visibility = _step == 0 ? Visibility.Hidden : Visibility.Visible;
        _next.Content = _step switch { 0 => "Get started", 3 => "Done", _ => "Next" };

        _dots.Children.Clear();
        for (int i = 0; i < 4; i++)
        {
            var dot = new Border { Width = i == _step ? 18 : 7, Height = 7, CornerRadius = new CornerRadius(3.5), Margin = new Thickness(0, 0, 6, 0) };
            dot.SetResourceReference(Border.BackgroundProperty, i == _step ? "AccentBrush" : "SubtleFillBrush");
            _dots.Children.Add(dot);
        }
        if (_page.Content is FrameworkElement page) Motion.Appear(page, fromScale: 0.98, milliseconds: 200);
    }

    // ------------------------------------------------------------------ Pages

    private static TextBlock Heading(string text) => new()
    {
        Text = text, FontSize = 26, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
        FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"),
    };

    private static TextBlock Paragraph(string text, double top = 10)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0), LineHeight = 20, FontSize = 13.5 };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        return block;
    }

    private UIElement WelcomePage() => new StackPanel
    {
        Children =
        {
            new Image
            {
                Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/DockHub;component/Assets/DockHub.ico")),
                Width = 72, Height = 72, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 18),
            },
            Heading("Welcome to DockHub"),
            Paragraph("DockHub has taken the place of your Windows taskbar: Start, search, your apps, the tray and live widgets now live in one dock."),
            Paragraph("Your taskbar isn't gone. It comes back whenever DockHub closes, crashes or is uninstalled, and Settings › General has a button to bring it back at any time."),
            Paragraph("Let's set it up in three quick steps. You can change everything later in Settings."),
        },
    };

    private UIElement ModePage()
    {
        var replace = Choice("Replace the taskbar", "Recommended. The dock takes over Start, running apps and the tray; the Windows taskbar hides.",
            "", _chosenMode == TaskbarMode.Replace, () => _chosenMode = TaskbarMode.Replace);
        var both = Choice("Keep both", "The dock sits above the Windows taskbar, which stays as it is.",
            "", _chosenMode == TaskbarMode.ShowBoth, () => _chosenMode = TaskbarMode.ShowBoth);
        return new StackPanel
        {
            Children =
            {
                Heading("How should DockHub work?"),
                Paragraph("Switching modes later restarts DockHub.", 6),
                new UniformGrid { Columns = 2, Margin = new Thickness(0, 22, 0, 0), Children = { replace, both } },
            },
        };
    }

    private UIElement LookPage()
    {
        var edges = new UniformGrid { Columns = 4, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var (edge, name, glyph) in new[] { (DockEdge.Bottom, "Bottom", ""), (DockEdge.Top, "Top", ""), (DockEdge.Left, "Left", ""), (DockEdge.Right, "Right", "") })
        {
            var e = edge;
            edges.Children.Add(Choice(name, "", glyph, _config.Edge == edge, () => _config.Edge = e, compact: true));
        }

        var presets = new UniformGrid { Columns = 2, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var preset in LayoutPresets.All)
        {
            var p = preset;
            presets.Children.Add(Choice(preset.Name, preset.Description, "", false, () => LayoutPresets.Apply(p, AppServices.ConfigService), group: "preset"));
        }

        return new StackPanel
        {
            Children =
            {
                Heading("Make it yours"),
                Paragraph("Changes show on the dock right away.", 6),
                new TextBlock { Text = "Screen edge", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 0) },
                edges,
                new TextBlock { Text = "Start from a layout (optional)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 0) },
                presets,
            },
        };
    }

    private UIElement TipsPage()
    {
        var gallery = new Button { Content = "Open the widget gallery", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 20, 0, 0) };
        gallery.Click += (_, _) => Finish(openGallery: true);
        return new StackPanel
        {
            Children =
            {
                Heading("You're all set"),
                Tip("", "Right-click the dock", "Add widgets, pin apps, change the position, or undo your last change."),
                Tip("", "Win + 1…9", "Open or switch to your dock's apps, like on the Windows taskbar. Ctrl+Alt+D brings the dock up."),
                Tip("", "Folders", "Drag one app onto another to make a folder."),
                Tip("", "Widgets", "Every widget can be added more than once and has its own settings."),
                gallery,
            },
        };
    }

    private static UIElement Tip(string glyph, string title, string text)
    {
        var icon = new TextBlock { Text = glyph, FontSize = 18, Width = 34, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var body = new StackPanel { Children = { new TextBlock { Text = title, FontWeight = FontWeights.SemiBold }, Paragraph(text, 2) } };
        return new DockPanel { Margin = new Thickness(0, 16, 0, 0), Children = { icon, body } };
    }

    /// <summary>A selectable card (radio button look).</summary>
    private static FrameworkElement Choice(string title, string description, string glyph, bool selected, Action onSelect, bool compact = false, string group = "choice")
    {
        var icon = new TextBlock { Text = glyph, FontSize = compact ? 18 : 22, Margin = new Thickness(0, 0, 0, compact ? 6 : 10) };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        var content = new StackPanel { Children = { icon, new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap } } };
        if (description.Length > 0) content.Children.Add(Paragraph(description, 4));

        var card = new RadioButton
        {
            GroupName = group + Guid.NewGuid().ToString("N")[..4],
            IsChecked = selected,
            Content = content,
            Margin = new Thickness(0, 0, 10, 10),
            Padding = new Thickness(compact ? 12 : 16),
            Cursor = Cursors.Hand,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        card.Template = CardTemplate();
        card.Checked += (_, _) =>
        {
            // Uncheck siblings (group names are unique per page build).
            if (card.Parent is Panel panel)
                foreach (var other in panel.Children.OfType<RadioButton>().Where(r => r != card)) other.IsChecked = false;
            onSelect();
        };
        return card;
    }

    private static ControlTemplate CardTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border), "Card");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
        border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "SurfaceBorderBrush");
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        border.AppendChild(presenter);

        var template = new ControlTemplate(typeof(RadioButton)) { VisualTree = border };
        var checkedTrigger = new Trigger { Property = System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, Value = true };
        checkedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("AccentBrush"), "Card"));
        template.Triggers.Add(checkedTrigger);
        var hoverTrigger = new Trigger { Property = IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("SurfaceHoverBrush"), "Card"));
        template.Triggers.Add(hoverTrigger);
        return template;
    }

    private void Finish(bool openGallery)
    {
        var mode = _chosenMode;
        Close();
        if (openGallery) App.Instance.ShowSettings("gallery");
        // Changing the mode restarts DockHub (after its own confirmation), so it goes last.
        if (mode != _config.TaskbarMode) _config.TaskbarMode = mode;
    }
}
