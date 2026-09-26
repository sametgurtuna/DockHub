using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CustomDock.Core;

/// <summary>
/// Text size of settings, widget panels and menus: the user's choice, or Windows' "Text size" accessibility setting.
/// The dock itself keeps its height (its size setting makes it larger); everything that opens from it scales.
/// </summary>
public static class TextScale
{
    private static Windows.UI.ViewManagement.UISettings? s_uiSettings;
    private static bool s_hooked;

    /// <summary>1.0 = 100 %.</summary>
    public static double Factor { get; private set; } = 1;

    public static event Action? Changed;

    public static double SystemFactor
    {
        get
        {
            try { return Math.Clamp((s_uiSettings ??= new Windows.UI.ViewManagement.UISettings()).TextScaleFactor, 1, 2.25); }
            catch { return 1; }
        }
    }

    public static void Initialize()
    {
        if (!s_hooked)
        {
            s_hooked = true;
            try
            {
                s_uiSettings ??= new Windows.UI.ViewManagement.UISettings();
                s_uiSettings.TextScaleFactorChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(Update);
            }
            catch (Exception ex)
            {
                Log.Debug($"Text scale changes are not followed: {ex.Message}");
            }
            // Context menus are created all over the app; scale them as they open.
            EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler((s, _) => Apply((ContextMenu)s)));
        }
        Update();
    }

    public static void Update()
    {
        double configured = AppServices.Config.TextScale;
        double factor = configured > 0 ? configured : SystemFactor;
        if (Math.Abs(factor - Factor) < 0.001) return;
        Factor = factor;
        Changed?.Invoke();
    }

    /// <summary>Scales an element's layout (text and everything around it) by the current factor.</summary>
    public static void Apply(FrameworkElement element)
    {
        if (Math.Abs(Factor - 1) < 0.001)
        {
            if (element.LayoutTransform is ScaleTransform) element.LayoutTransform = Transform.Identity;
            return;
        }
        element.LayoutTransform = new ScaleTransform(Factor, Factor);
    }
}
