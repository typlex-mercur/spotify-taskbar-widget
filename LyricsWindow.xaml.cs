using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SpotifyTaskbarWidget;

public partial class LyricsWindow : Window
{
    public int TrayIndex { get; set; }
    public bool ClosedByApp { get; set; }

    private IntPtr _hwnd;
    private IntPtr _ownerTray;
    private string _currentLyric = "";
    private readonly WidgetSettings _settings = WidgetSettings.Shared;

    public LyricsWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        int ex = Interop.GetWindowLong(_hwnd, Interop.GWL_EXSTYLE);
        Interop.SetWindowLong(_hwnd, Interop.GWL_EXSTYLE, ex | Interop.WS_EX_TOOLWINDOW | Interop.WS_EX_NOACTIVATE);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplySettings();
    }

    public void ApplySettings()
    {
        var tAlign = _settings.LyricsAlignment?.ToLowerInvariant() switch
        {
            "left" => TextAlignment.Left,
            "right" => TextAlignment.Right,
            _ => TextAlignment.Center
        };

        LyricText.HorizontalAlignment = HorizontalAlignment.Stretch;
        LyricTextTop.HorizontalAlignment = HorizontalAlignment.Stretch;
        LyricText.TextAlignment = tAlign;
        LyricTextTop.TextAlignment = tAlign;

        double baseFontSize = _settings.LyricsFontSize > 0 ? _settings.LyricsFontSize : 11.5;
        ApplyFontSize(baseFontSize);

        Root.Opacity = Math.Clamp(_settings.Opacity, 0.2, 1.0);
        if (_hwnd != IntPtr.Zero)
        {
            Interop.EnsureTopmost(_hwnd);
        }
    }

    private double _baseFontSize = 11.5;

    private void ApplyFontSize(double size)
    {
        _baseFontSize = size;
        LyricText.FontSize = size;
        LyricTextTop.FontSize = size;
        // Allow up to 2 lines: lineHeight ≈ fontSize × 1.35, so 2 lines ≈ fontSize × 2.7
        double maxH = Math.Ceiling(size * 2.7);
        LyricText.MaxHeight = maxH;
        LyricTextTop.MaxHeight = maxH;
    }

    public void ReassertTopmost()
    {
        if (_hwnd != IntPtr.Zero)
            Interop.EnsureTopmost(_hwnd);
    }

    public void EnsureOwnerTray(IntPtr tray)
    {
        if (tray != _ownerTray && _hwnd != IntPtr.Zero && tray != IntPtr.Zero)
        {
            Interop.SetWindowLongPtr(_hwnd, Interop.GWLP_HWNDPARENT, tray);
            _ownerTray = tray;
            Interop.EnsureTopmost(_hwnd);
        }
    }

    public void SetBounds(int leftPx, int topPx, int widthPx, int heightPx, int clipBottomPx, int availableWidthPx)
    {
        if (_hwnd == IntPtr.Zero) return;

        double scale = Interop.GetDpiForWindow(_hwnd) / 96.0;
        if (scale <= 0) scale = 1.0;

        double logicalWidth = Math.Max(50, widthPx / scale);
        double logicalHeight = Math.Max(20, heightPx / scale);

        Width = logicalWidth;
        Height = logicalHeight;

        // Auto-scale font when area is narrow (< 220 DIPs)
        double baseFont = _settings.LyricsFontSize > 0 ? _settings.LyricsFontSize : 11.5;
        double targetFont = baseFont;
        if (logicalWidth < 220 && logicalWidth >= 60)
        {
            double ratio = (logicalWidth - 60) / 160.0;
            targetFont = 8.5 + (baseFont - 8.5) * Math.Clamp(ratio, 0.0, 1.0);
        }
        else if (logicalWidth < 60)
        {
            targetFont = 8.5;
        }

        if (Math.Abs(targetFont - _baseFontSize) > 0.2)
        {
            ApplyFontSize(targetFont);
        }

        // Physically move, resize, and maintain topmost on Windows 11 Taskbar
        Interop.SetBoundsPhysical(_hwnd, leftPx, topPx, widthPx, heightPx);
        Interop.ClipWindowBottom(_hwnd, widthPx, heightPx, clipBottomPx);
        Interop.EnsureTopmost(_hwnd);

        if (Visibility != Visibility.Visible)
            Visibility = Visibility.Visible;
    }

    private int _activeLayer = 0;
    private static readonly IEasingFunction LyricEase = new CubicEase { EasingMode = EasingMode.EaseOut };

    public void SetLyric(string text)
    {
        text = text?.Trim() ?? "";
        if (_currentLyric == text) return;

        _currentLyric = text;
        const int TransitionMs = 240;

        var currentTb = _activeLayer == 0 ? LyricText : LyricTextTop;
        var currentTr = _activeLayer == 0 ? LyricTransform : LyricTransformTop;

        var nextTb = _activeLayer == 0 ? LyricTextTop : LyricText;
        var nextTr = _activeLayer == 0 ? LyricTransformTop : LyricTransform;

        if (string.IsNullOrEmpty(text))
        {
            var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(TransitionMs)) { EasingFunction = LyricEase };
            currentTb.BeginAnimation(OpacityProperty, fadeOut);
            nextTb.BeginAnimation(OpacityProperty, null);
            nextTb.Opacity = 0;
            return;
        }

        // Initial or coming from empty: simple smooth fade in place
        if (string.IsNullOrEmpty(currentTb.Text) || currentTb.Opacity < 0.05)
        {
            nextTb.Text = text;
            nextTr.BeginAnimation(TranslateTransform.YProperty, null);
            nextTr.Y = 0;
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(TransitionMs)) { EasingFunction = LyricEase };
            nextTb.BeginAnimation(OpacityProperty, fadeIn);
            currentTb.BeginAnimation(OpacityProperty, null);
            currentTb.Opacity = 0;
            _activeLayer = 1 - _activeLayer;
            return;
        }

        // Outgoing line: floats slightly up (-4px) and fades out smoothly
        var fadeOutAnim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(TransitionMs)) { EasingFunction = LyricEase };
        var slideOutAnim = new DoubleAnimation(-4, TimeSpan.FromMilliseconds(TransitionMs)) { EasingFunction = LyricEase };
        currentTb.BeginAnimation(OpacityProperty, fadeOutAnim);
        currentTr.BeginAnimation(TranslateTransform.YProperty, slideOutAnim);

        // Incoming line: floats up from +5px into position (0px) while fading in smoothly
        nextTb.Text = text;
        nextTr.BeginAnimation(TranslateTransform.YProperty, null);
        nextTr.Y = 5;

        var fadeInAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(TransitionMs)) { EasingFunction = LyricEase };
        var slideInAnim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(TransitionMs)) { EasingFunction = LyricEase };

        nextTb.BeginAnimation(OpacityProperty, fadeInAnim);
        nextTr.BeginAnimation(TranslateTransform.YProperty, slideInAnim);

        _activeLayer = 1 - _activeLayer;
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Click on lyrics opens/focuses Spotify
        SpotifyActions.OpenSpotifyWindow();
    }
}
