using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace SpotifyTaskbarWidget;

public partial class MainWindow : Window
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "SpotifyTaskbarWidget";


    // Ícones do Spotify (paths 16x16 do leitor web)
    private static readonly Geometry PlayGeo = Geometry.Parse("M3 1.713a.7.7 0 0 1 1.05-.607l10.89 6.288a.7.7 0 0 1 0 1.212L4.05 14.894A.7.7 0 0 1 3 14.288V1.713z");
    private static readonly Geometry PauseGeo = Geometry.Parse("M2.7 1a.7.7 0 0 0-.7.7v12.6a.7.7 0 0 0 .7.7h2.6a.7.7 0 0 0 .7-.7V1.7a.7.7 0 0 0-.7-.7H2.7zm8 0a.7.7 0 0 0-.7.7v12.6a.7.7 0 0 0 .7.7h2.6a.7.7 0 0 0 .7-.7V1.7a.7.7 0 0 0-.7-.7h-2.6z");
    private static readonly Geometry AddCircleGeo = Geometry.Parse("M8 1.5a6.5 6.5 0 1 0 0 13 6.5 6.5 0 0 0 0-13zM0 8a8 8 0 1 1 16 0A8 8 0 0 1 0 8z M11.75 8a.75.75 0 0 1-.75.75H8.75V11a.75.75 0 0 1-1.5 0V8.75H5a.75.75 0 0 1 0-1.5h2.25V5a.75.75 0 0 1 1.5 0v2.25H11a.75.75 0 0 1 .75.75z");
    private static readonly Geometry CheckCircleGeo = Geometry.Parse("M0 8a8 8 0 1 1 16 0A8 8 0 0 1 0 8zm11.748-1.97a.75.75 0 0 0-1.06-1.06l-4.47 4.44-1.405-1.406a.75.75 0 1 0-1.061 1.06l2.466 2.467 5.53-5.5z");

    // Cores do Spotify; as neutras dependem do tema da barra (claro/escuro)
    private static readonly Color SpotifyGreenColor = Color.FromRgb(0x1E, 0xD7, 0x60);
    private static readonly Brush SpotifyGreen = new SolidColorBrush(SpotifyGreenColor);
    private readonly SolidColorBrush _accentBrush = new(SpotifyGreenColor);
    private readonly SolidColorBrush _accentHoverBrush = new(LightenColor(SpotifyGreenColor, 0.35));
    private Color _currentAccentColor = SpotifyGreenColor;
    private byte[]? _lastArtBytes;
    private int _trackSlideDirection;
    private Brush Subdued = new SolidColorBrush(Color.FromRgb(0xB3, 0xB3, 0xB3));
    private Brush DimWhite = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
    private Brush _progressFillNormal = Brushes.White;
    private bool? _lightTheme;

    private readonly MediaService _media = new();
    private readonly SpotifyUiaService _uia = new();
    private readonly WidgetSettings _settings = WidgetSettings.Shared;
    private bool? _liked;

    /// <summary>Barra desta janela (0 = principal, 1+ = secundárias). Cada
    /// monitor selecionado nas definições tem a sua própria instância.</summary>
    public int TrayIndex { get; set; }

    /// <summary>Fecho programático (sincronização de monitores) — não recriar.</summary>
    internal bool ClosedByApp;

    private bool _closed;
    private Action? _mediaChanged;
    private Action? _mediaTimeline;

    private static readonly List<MainWindow> Instances = new();
    private static bool _recreatePending;

    /// <summary>True se existe pelo menos uma janela de widget viva.</summary>
    public static bool HasWindows => Instances.Count > 0;

    /// <summary>Garante uma janela por barra selecionada nas definições:
    /// cria as que faltam, fecha as que sobram.</summary>
    public static void SyncToMonitors()
    {
        var wanted = WidgetSettings.Shared.Monitors;
        foreach (var win in Instances.Where(w => !wanted.Contains(w.TrayIndex)).ToList())
        {
            win.ClosedByApp = true;
            win.Close();
        }
        foreach (int idx in wanted)
        {
            if (Instances.Any(w => w.TrayIndex == idx)) continue;
            // Uma janela que rebente a construir (ex.: falha de XAML/arranque
            // específica de uma máquina) não pode derrubar as outras nem deixar
            // a app viva sem UI — registar e seguir
            try { new MainWindow { TrayIndex = idx }.Show(); }
            catch (Exception ex) { Diag.Log($"Widget window failed to create (tray {idx}): {ex}"); }
        }
        // A escolha de barra de cada janela pode ter mudado (ex.: o órfão que
        // tinha recuado para a principal tem de a largar JÁ, não daqui a 2s)
        foreach (var win in Instances)
            win._trayCache = IntPtr.Zero;
    }

    private readonly DispatcherTimer _positionTimer = new() { Interval = TimeSpan.FromMilliseconds(1000) };
    private readonly DispatcherTimer _trackTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private DateTime _lastTrimAt = DateTime.MinValue;

    private IntPtr _hwnd;
    private bool _moveMode;
    private bool _dragging;
    private bool _dragMoved;
    private bool _pressed;
    private Point _dragStartScreen;
    private double _dragStartLeft;

    private string _lastTrackKey = "";
    private bool _artDirty = true;
    private bool _refreshing;
    private bool _volLoading;
    private bool _spotifyPresent = true;
    private DateTime _sessionLostAt = DateTime.MinValue;
    private DateTime _trackNullSince = DateTime.MinValue;

    // Progresso: última posição conhecida + instante em que foi lida (interpolação)
    private TimeSpan _duration;
    private TimeSpan _basePosition;
    private DateTime _basePositionAt;
    private bool _isPlayingUi;

    // Suporte a letras sincronizadas (Lyrics)
    private LyricsWindow? _lyricsWindow;
    private SongLyrics? _currentLyrics;
    private string _lastLyricsTrackKey = "";
    private readonly DispatcherTimer _lyricsTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    // Estado via acessibilidade (favorito/aleatório/repetição): caro de ler,
    // por isso só em mudanças de faixa, após cliques, ou a cada 5 s
    private (bool? Liked, ShuffleMode Shuffle, RepeatMode Repeat) _uiaState =
        (null, ShuffleMode.Unknown, RepeatMode.Unknown);
    private DateTime _lastUiaStateAt = DateTime.MinValue;
    private bool _uiaDirty = true;

    // Depois de um clique otimista, leituras antigas (pré-clique) chegam durante
    // ~2 s e contradizem o novo estado — são ignoradas nesse intervalo
    private DateTime _playToggledAt = DateTime.MinValue;
    private DateTime _likedOptimisticAt = DateTime.MinValue;
    private DateTime _shuffleToggledAt = DateTime.MinValue;
    private DateTime _repeatToggledAt = DateTime.MinValue;
    private DateTime _seekAt = DateTime.MinValue;

    private RepeatMode _currentRepeatMode = RepeatMode.Off;
    private ShuffleMode _currentShuffleMode = ShuffleMode.Off;

    private bool AcceptPlayingState(bool incoming) =>
        incoming == _isPlayingUi || DateTime.UtcNow - _playToggledAt > TimeSpan.FromSeconds(2);

    // Âncoras da barra (píxeis físicos), atualizadas em background via UI Automation
    // Hook de foreground: o delegate tem de ficar referenciado (senão o GC apanha-o)
    // Um só hook de foreground para o processo inteiro: com N janelas, N hooks
    // davam N callbacks e N pipelines completos por CADA mudança de foco no OS
    private static Interop.WinEventDelegate? _fgProc;
    private static IntPtr _fgHook;

    private static void EnsureForegroundHook()
    {
        if (_fgHook != IntPtr.Zero) return;
        _fgProc = (_, _, _, _, _, _, _) =>
        {
            foreach (var win in Instances)
                win.Dispatcher.BeginInvoke((Action)win.UpdatePosition);
        };
        _fgHook = Interop.SetWinEventHook(
            Interop.EVENT_SYSTEM_FOREGROUND, Interop.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _fgProc, 0, 0, Interop.WINEVENT_OUTOFCONTEXT);
    }

    private readonly object _anchorLock = new();
    private double? _widgetsRightPx;
    private double? _startLeftPx;
    private double? _lastValidStartLeft;
    private double? _taskEndPx;
    private DateTime _lastAnchorQuery = DateTime.MinValue;
    private bool _anchorQueryRunning;
    private IntPtr _anchorsTray;
    private bool _lastKnownLeftAligned;

    private double MaxTextWidth => (_settings.ShowLyrics && _settings.UnifiedLyrics) ? 380 : 150;
    private const double MinTextWidth = 60;

    public MainWindow()
    {
        InitializeComponent();
        Instances.Add(this);
        Loaded += OnLoaded;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        // Não roubar o foco ao clicar e não aparecer no Alt+Tab
        int ex = Interop.GetWindowLong(_hwnd, Interop.GWL_EXSTYLE);
        Interop.SetWindowLong(_hwnd, Interop.GWL_EXSTYLE, ex | Interop.WS_EX_TOOLWINDOW | Interop.WS_EX_NOACTIVATE);

        if (HwndSource.FromHwnd(_hwnd) is HwndSource source)
        {
            source.AddHook(WndProc);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_SETTINGCHANGE = 0x001A;
        const int WM_THEMECHANGED = 0x031A;

        if (msg == WM_SETTINGCHANGE || msg == WM_THEMECHANGED)
        {
            _lastThemeCheck = DateTime.MinValue;
            ApplyThemeIfChanged();
        }
        return IntPtr.Zero;
    }

    /// <summary>Aplica os textos no idioma do Windows (PT ou EN).</summary>
    private void ApplyLanguage()
    {
        MoveMenu.Header = L.MoveWidget;
        MoveMenu.ToolTip = L.MoveWidgetTip;
        ResetPosMenu.Header = L.ResetAutoPos;
        MonitorMenu.Header = L.MonitorMenu;
        ButtonsMenuItem.Header = L.ButtonsMenu;
        BtnPlayMenu.Header = L.BtnPlay;
        BtnLikeMenu.Header = L.BtnLike;
        BtnShuffleMenu.Header = L.BtnShuffle;
        BtnPrevMenu.Header = L.BtnPrev;
        BtnNextMenu.Header = L.BtnNext;
        BtnRepeatMenu.Header = L.BtnRepeat;
        BtnVolumeMenu.Header = L.BtnVolume;
        ProgressMenu.Header = L.ProgressBar;
        DynamicColorMenu.Header = L.DynamicColor;
        LyricsMenuItem.Header = L.LyricsMenu;
        ShowLyricsMenu.Header = L.ShowLyrics;
        LyricsUnifiedMenu.Header = L.LyricsModeUnified;
        LyricsSeparateMenu.Header = L.LyricsModeSeparate;
        LyricsAlignMenu.Header = L.LyricsAlign;
        LyricsAlignLeftMenu.Header = L.LyricsAlignLeft;
        LyricsAlignCenterMenu.Header = L.LyricsAlignCenter;
        LyricsAlignRightMenu.Header = L.LyricsAlignRight;
        AutoStartMenu.Header = L.AutoStart;
        ExitMenu.Header = L.Exit;

        LauncherPanel.ToolTip = L.TipOpenSpotify;
        LauncherText.Text = L.OpenSpotify;
        ArtistText.Text = L.NothingPlaying;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyLanguage();
        if (PackagedApp.IsPackaged)
        {
            _ = InitStartupTaskStateAsync();
        }
        else
        {
            AutoStartMenu.IsChecked = IsAutoStartEnabled();
        }
        ApplyThemeIfChanged();
        RebuildMonitorMenu();
        _lastKnownLeftAligned = IsTaskbarLeftAligned();
        if (_settings.MarginsCenter.TryGetValue(TrayIndex, out var initSm) && initSm.LastStartLeft > 0)
        {
            _lastValidStartLeft = initSm.LastStartLeft;
            _startLeftPx = initSm.LastStartLeft;
        }
        ApplySettingsUi();
        // As definições são partilhadas: quando outra janela grava, re-aplicar
        WidgetSettings.Changed += OnSettingsChanged;

        // Captura de rato roubada a meio de um arrasto (menu, overlay do
        // sistema): sem isto _dragging ficava presa e o widget deixava de se
        // reposicionar; o próximo clique ainda gravava uma posição fantasma
        Root.LostMouseCapture += (_, _) =>
        {
            // Só quando roubada a MEIO do arrasto — no largar normal o
            // _dragging já está falso e a gravação da posição segue intacta
            if (_dragging)
            {
                _dragging = false;
                _dragMoved = false;
            }
        };

        // Re-afirmar o topmost por cima de um tooltip aberto empurra-o para
        // trás da barra (report da comunidade) — suspender enquanto durar
        AddHandler(ToolTipService.ToolTipOpeningEvent,
            new ToolTipEventHandler((_, _) => _tooltipOpen = true), true);
        AddHandler(ToolTipService.ToolTipClosingEvent,
            new ToolTipEventHandler((_, _) => _tooltipOpen = false), true);

        // O popup do volume tem de ganhar à barra (que também é topmost com a
        // ocultação automática): re-afirmá-lo no instante em que abre
        VolumePopup.Opened += (_, _) =>
        {
            _wheelAccum = 0;
            if (VolumePopup.Child != null &&
                PresentationSource.FromVisual(VolumePopup.Child) is HwndSource src)
                Interop.EnsureTopmost(src.Handle);

            // Aberto via roda, o rato pode nunca ENTRAR no popup — o MouseLeave
            // nunca dispararia e ele ficava aberto para sempre (suprimindo o
            // ReassertTopmost). Watchdog: fechar quando o rato não está nem no
            // popup nem no botão.
            _volPopupWatchdog?.Stop();
            _volPopupWatchdog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _volPopupWatchdog.Tick += (_, _) =>
            {
                if (!VolumePopup.IsOpen ||
                    (!VolumePopupBorder.IsMouseOver && !VolumeButton.IsMouseOver))
                {
                    _volPopupWatchdog?.Stop();
                    VolumePopup.IsOpen = false;
                }
            };
            _volPopupWatchdog.Start();
        };

        UpdatePosition();
        _positionTimer.Tick += (_, _) =>
        {
            UpdatePosition();
            UpdateProgressUi();
            ApplyThemeIfChanged();
            if ((DateTime.UtcNow - _lastTrimAt).TotalSeconds > 60)
            {
                _lastTrimAt = DateTime.UtcNow;
                Interop.TrimWorkingSet();
            }
        };
        _positionTimer.Start();

        _lyricsTimer.Tick += (_, _) => UpdateLyricsUi();
        _lyricsTimer.Start();

        // Clicar na taskbar põe a barra por cima do widget; re-afirmar o topmost
        // no instante da mudança de janela ativa (o timer sozinho deixava flicker)
        EnsureForegroundHook();
        Closed += (_, _) =>
        {
            if (_trayLocHook != IntPtr.Zero) Interop.UnhookWinEvent(_trayLocHook);
        };

        // Subscrever ANTES de aguardar a inicialização: com o retry do SMTC, o
        // init pode demorar minutos — o widget tem de reagir logo que ele pegue
        _mediaChanged = () =>
        {
            _artDirty = true;
            _uiaDirty = true;
            Dispatcher.InvokeAsync(() => _ = RefreshTrackAsync());
        };
        _mediaTimeline = () => Dispatcher.InvokeAsync(RefreshTimeline);
        _media.Changed += _mediaChanged;
        _media.TimelineChanged += _mediaTimeline;

        _trackTimer.Tick += (_, _) => _ = RefreshTrackAsync();
        _trackTimer.Start();

        var mediaInit = _media.InitializeAsync();

        await mediaInit;
        if (_closed)
            return; // fechada durante o await (sync de monitores / restart do Explorer)
        await RefreshTrackAsync();
        _ = Task.Delay(3000).ContinueWith(_ => Dispatcher.InvokeAsync(Interop.TrimWorkingSet));
    }

    /// <summary>Estado da UI que espelha as definições partilhadas (menus,
    /// escala) — chamado no arranque e sempre que qualquer janela grava.</summary>
    private void ApplySettingsUi()
    {
        ProgressMenu.IsChecked = _settings.ShowProgress;
        DynamicColorMenu.IsChecked = _settings.DynamicAlbumColor;
        UpdateProgressFillColor();
        ShowLyricsMenu.IsChecked = _settings.ShowLyrics;
        LyricsUnifiedMenu.IsChecked = _settings.ShowLyrics && _settings.UnifiedLyrics;
        LyricsSeparateMenu.IsChecked = _settings.ShowLyrics && !_settings.UnifiedLyrics;
        LyricsAlignMenu.Visibility = (!_settings.UnifiedLyrics && _settings.ShowLyrics) ? Visibility.Visible : Visibility.Collapsed;
        LyricsAlignLeftMenu.IsChecked = string.Equals(_settings.LyricsAlignment, "Left", StringComparison.OrdinalIgnoreCase);
        LyricsAlignCenterMenu.IsChecked = string.Equals(_settings.LyricsAlignment, "Center", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(_settings.LyricsAlignment);
        LyricsAlignRightMenu.IsChecked = string.Equals(_settings.LyricsAlignment, "Right", StringComparison.OrdinalIgnoreCase);
        _lyricsWindow?.ApplySettings();
        BtnPlayMenu.IsChecked = _settings.ShowPlay;
        BtnLikeMenu.IsChecked = _settings.ShowLike;
        BtnShuffleMenu.IsChecked = _settings.ShowShuffle;
        BtnPrevMenu.IsChecked = _settings.ShowPrev;
        BtnNextMenu.IsChecked = _settings.ShowNext;
        BtnRepeatMenu.IsChecked = _settings.ShowRepeat;
        BtnVolumeMenu.IsChecked = _settings.ShowVolume;
        ApplyScale();
        ApplyOpacity();
    }

    private void OnSettingsChanged()
    {
        ApplySettingsUi();
        // Reposicionar só DEPOIS do layout assentar: mudar tamanho/escala e
        // medir a janela no mesmo instante usava as dimensões antigas e o
        // widget aterrava desalinhado até ao tick seguinte
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)UpdatePosition);
        _ = RefreshTrackAsync();
    }

    // ---------- Posicionamento na barra de tarefas ----------

    private IntPtr _trayCache;
    private DateTime _trayCacheAt;

    /// <summary>Cache curta do handle da barra: enumerar e ordenar as barras
    /// todas em CADA evento de posição (dezenas/s durante um deslize) era
    /// trabalho repetido para rederivar um handle que quase nunca muda.</summary>
    private IntPtr GetTargetTray()
    {
        if (_trayCache != IntPtr.Zero && Interop.IsWindow(_trayCache)
            && (DateTime.UtcNow - _trayCacheAt).TotalSeconds < 2)
            return _trayCache;
        _trayCache = ResolveTargetTray();
        _trayCacheAt = DateTime.UtcNow;
        return _trayCache;
    }

    /// <summary>Barra de tarefas desta janela (principal ou secundária). Se a
    /// barra alvo não existir (monitor desligado), UMA janela órfã — a de menor
    /// índice — recua para a barra principal, desde que nenhuma outra lá viva;
    /// as restantes escondem-se. Garante que a app nunca fica toda invisível
    /// (sem widget não há menu de contexto para recuperar).</summary>
    private IntPtr ResolveTargetTray()
    {
        if (TrayIndex > 0)
        {
            var secondaries = Interop.GetSecondaryTrays();
            if (TrayIndex <= secondaries.Count)
                return secondaries[TrayIndex - 1];
            // O recuo só serve para a app não ficar TODA invisível: se outra
            // janela ainda tem barra (principal ou secundária viva), ou há um
            // órfão de índice menor que recua primeiro, esta esconde-se
            bool someoneVisible = Instances.Any(w => w != this &&
                (w.TrayIndex == 0 || w.TrayIndex <= secondaries.Count));
            bool lowerOrphan = Instances.Any(w => w != this &&
                w.TrayIndex > secondaries.Count && w.TrayIndex < TrayIndex);
            if (someoneVisible || lowerOrphan)
                return IntPtr.Zero;
        }
        return Interop.FindWindow("Shell_TrayWnd", null);
    }

    private static bool IsTaskbarLeftAligned()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            return key?.GetValue("TaskbarAl") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }

    private IntPtr _ownerTray;
    private DateTime _anchorsMissingSince = DateTime.MinValue;

    // Hook de movimento da barra: quando ela desliza (ocultação automática),
    // os eventos chegam ao milissegundo e o widget "cavalga" a animação
    private IntPtr _trayLocHook;
    private IntPtr _hookedTray;
    private Interop.WinEventDelegate? _trayLocProc;
    private bool _updateQueued;

    private void EnsureTrayLocationHook(IntPtr tray)
    {
        if (tray == _hookedTray) return;
        if (_trayLocHook != IntPtr.Zero)
        {
            Interop.UnhookWinEvent(_trayLocHook);
            _trayLocHook = IntPtr.Zero;
        }
        _hookedTray = tray;
        uint tid = Interop.GetWindowThreadProcessId(tray, out uint pid);
        _trayLocProc ??= (_, _, hwnd, idObject, _, _, _) =>
        {
            if (hwnd != _hookedTray || idObject != 0 || _updateQueued) return;
            _updateQueued = true;
            // Prioridade alta: cada ms conta para apanhar o início do deslize
            Dispatcher.BeginInvoke(DispatcherPriority.Send, (Action)UpdatePosition);
        };
        _trayLocHook = Interop.SetWinEventHook(
            Interop.EVENT_OBJECT_LOCATIONCHANGE, Interop.EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, _trayLocProc, pid, tid, Interop.WINEVENT_OUTOFCONTEXT);
    }

    private void UpdatePosition()
    {
        _updateQueued = false;
        if (_closed)
            return; // dispatch em atraso numa janela fechada: sem isto podia
                    // re-registar hooks num hwnd morto (crash em callback GC'd)
        IntPtr tray = GetTargetTray();
        if (tray == IntPtr.Zero || !Interop.GetWindowRect(tray, out var r))
        {
            // Barra alvo indisponível (monitor desligado / Explorer a reiniciar):
            // esconder e limpar o estado de deslize — sem isto, a reaparição
            // tocava uma animação de subida espúria
            CancelRide();
            _barWasHidden = false;
            HideWidget();
            return;
        }

        EnsureTrayLocationHook(tray);

        // Janela "owned" pela taskbar: o gestor de janelas mantém-na SEMPRE acima
        // do dono — elimina o flicker de z-order por construção. Se o Explorer
        // reiniciar, a janela morre com a barra e o OnClosed recria o widget.
        if (tray != _ownerTray && _hwnd != IntPtr.Zero)
        {
            Interop.SetWindowLongPtr(_hwnd, Interop.GWLP_HWNDPARENT, tray);
            _ownerTray = tray;
            ReassertTopmost();
        }

        // Ocultação automática: o widget "cavalga" a barra — o hook de movimento
        // chama isto a cada frame da animação e o Y segue o rect atual, por isso
        // ele desce e sobe colado à barra. Só se esconde quando ela assenta fora
        // do ecrã; âncoras ficam intactas (o X não muda num deslize vertical).
        int trayHeightPx = r.Bottom - r.Top;
        int visiblePx = Interop.GetTaskbarVisiblePx(r, out int monitorBottomPx, out int workAreaBottomPx);

        if (!Interop.GetWindowRect(_hwnd, out var w))
            return;
        int winWidth = w.Right - w.Left;
        int winHeight = w.Bottom - w.Top;

        // Centrar na banda DESENHADA da barra, não no rect da janela dela (no
        // 25H2 o rect é mais alto e o widget flutuava — issue #9). A reserva de
        // área de trabalho dá a altura real desenhada em qualquer barra
        // (incluindo multi-linha); com ocultação automática não há reserva e
        // vale a heurística dos 48 DIP do Win11.
        int reservedPx = monitorBottomPx - workAreaBottomPx;
        int barBandPx = reservedPx > 8
            ? Math.Min(trayHeightPx, reservedPx)
            : Math.Min(trayHeightPx, (int)Math.Round(48 * Interop.GetDpiForWindow(tray) / 96.0));
        // Onde o widget "estaria" com a barra assente fora do ecrã (mesmo offset
        // vertical dentro dela): ponto de partida da subida e destino da descida
        int belowEdgeTopPx = monitorBottomPx - 2 + (barBandPx - winHeight) / 2;

        if (_rideAnimating)
        {
            // A animação é dona da posição; se a barra inverter a meio,
            // invertemos também, a partir de onde o widget está
            if (_rideDown && visiblePx >= trayHeightPx - 4)
            {
                CancelRide();
                StartRide(w.Left, w.Top, r.Bottom - barBandPx + (barBandPx - winHeight) / 2,
                    down: false, winWidth, winHeight, monitorBottomPx);
            }
            else if (!_rideDown && visiblePx <= 8)
            {
                CancelRide();
                StartRide(w.Left, w.Top, belowEdgeTopPx,
                    down: true, winWidth, winHeight, monitorBottomPx);
            }
            return;
        }

        // Fração do caminho de esconder que a barra já percorreu — o widget
        // entra na animação neste ponto para ficar em sincronia com ela
        double hiddenPhase = 1.0 - Math.Clamp((double)visiblePx / trayHeightPx, 0, 1);

        // Interação em curso "pina" o widget: esconder a meio de um arrasto do
        // slider/menu fechava-lhe o popup nas mãos (e um arrasto de move-mode
        // escondido perdia a captura do rato e ficava _dragging preso). Quando
        // a interação acabar, o tick seguinte esconde normalmente.
        bool pinned = _dragging || VolumePopup.IsOpen ||
                      (Root.ContextMenu?.IsOpen ?? false);

        if (visiblePx <= 8)
        {
            if (pinned)
                return;
            // Assente fora do ecrã. Se o widget ainda está à vista, o esconder
            // aconteceu num salto único — animar a descida na mesma.
            if (Visibility == Visibility.Visible && Interop.IsAutoHideEnabled())
            {
                StartRide(w.Left, w.Top, belowEdgeTopPx, down: true, winWidth, winHeight, monitorBottomPx, hiddenPhase);
                return;
            }
            _barWasHidden = true;
            HideWidget();
            return;
        }

        if (visiblePx < trayHeightPx - 4)
        {
            // A deslizar. Widget visível = a barra começou a esconder-se: animar
            // a nossa descida (seguir os passos grossos da janela dela ficava
            // aos solavancos). Invisível = revelação em curso: esperar que
            // assente — a subida anima nessa altura.
            if (Visibility == Visibility.Visible && !pinned && Interop.IsAutoHideEnabled())
                StartRide(w.Left, w.Top, belowEdgeTopPx, down: true, winWidth, winHeight, monitorBottomPx, hiddenPhase);
            return;
        }

        if (tray != _anchorsTray)
        {
            // Mudou a barra alvo (outro monitor): descartar âncoras da anterior
            _anchorsTray = tray;
            _lastAnchorQuery = DateTime.MinValue;
            lock (_anchorLock)
            {
                _widgetsRightPx = null;
                _startLeftPx = null;
                _lastValidStartLeft = null;
                _taskEndPx = null;
            }
        }
        // Daqui para baixo a barra está assente no ecrã — âncoras fiáveis
        bool isLeft = IsTaskbarLeftAligned();
        if (isLeft != _lastKnownLeftAligned)
        {
            _lastKnownLeftAligned = isLeft;
            // Alignment mudou: invalidar âncoras para recalcular posição
            _lastAnchorQuery = DateTime.MinValue;
            lock (_anchorLock) { _widgetsRightPx = null; _startLeftPx = null; _lastValidStartLeft = null; _taskEndPx = null; }
        }
        RefreshAnchors(tray);
        double? widgetsRightPx, startLeftPx, taskEndPx;
        lock (_anchorLock)
        {
            widgetsRightPx = _widgetsRightPx;
            startLeftPx = _startLeftPx;
            taskEndPx = _taskEndPx;
        }

        // Toda a matemática de posicionamento é feita em PÍXEIS FÍSICOS da barra
        // alvo: converter para DIP usava a escala do monitor ATUAL da janela e,
        // ao mudar para um monitor com DPI diferente, a conta saía errada e o
        // widget aterrava a meio do ecrã.
        double windowScale = Interop.GetDpiForWindow(_hwnd) / 96.0; // px por DIP, no monitor atual

        if (_dragging)
            return;

        int topPx = r.Bottom - barBandPx + (barBandPx - winHeight) / 2;

        int rightAnchorLeftLimitPx = r.Left + 12;
        int leftPx, rightLimitPx;
        bool isCustomSlot = false;
        int availPx;

        var marginsDict = isLeft ? _settings.MarginsLeft : _settings.MarginsCenter;
        if (!_settings.AutoPosition && marginsDict.TryGetValue(TrayIndex, out var sm))
        {
            isCustomSlot = true;
            int slotLeft, slotRight;
            if (!isLeft)
            {
                if (sm.IsRightSlot)
                {
                    int? notifyLeftPx = Interop.GetTrayNotifyLeft(tray);
                    slotLeft = taskEndPx.HasValue ? (int)taskEndPx.Value + 8 : r.Left + (r.Right - r.Left) / 2 + 100;
                    slotRight = (notifyLeftPx ?? (r.Right - 220)) - 8;
                }
                else
                {
                    slotLeft = widgetsRightPx.HasValue ? (int)widgetsRightPx.Value + 8 : r.Left + 12;
                    double? effStartLeft = startLeftPx ?? _lastValidStartLeft;
                    if (!effStartLeft.HasValue && sm.LastStartLeft > 0) effStartLeft = sm.LastStartLeft;
                    slotRight = effStartLeft.HasValue ? (int)effStartLeft.Value - 8 : r.Left + (r.Right - r.Left) / 2 - 8;
                }
            }
            else
            {
                int? notifyLeftPx = Interop.GetTrayNotifyLeft(tray);
                slotLeft = taskEndPx.HasValue ? (int)taskEndPx.Value + 8 : r.Left + 300;
                slotRight = (notifyLeftPx ?? (r.Right - 220)) - 8;
            }

            leftPx = slotLeft + (int)sm.GapLeft;
            rightLimitPx = slotRight - (int)sm.GapRight;
            availPx = rightLimitPx - leftPx;
        }
        else
        {
            var manualDict = isLeft ? _settings.ManualXLeft : _settings.ManualX;
            if (!_settings.AutoPosition && manualDict.TryGetValue(TrayIndex, out double manualX))
            {
                if (!isLeft)
                {
                    double? effStart = startLeftPx ?? _lastValidStartLeft;
                    if (effStart.HasValue && manualX < effStart.Value)
                    {
                        // Widget posicionado à esquerda dos ícones centrados (entre tempo/widgets e Iniciar)
                        rightLimitPx = (int)effStart.Value - 8;
                        leftPx = (int)Math.Max(r.Left + 4, Math.Min(manualX, rightLimitPx - winWidth));
                    }
                    else
                    {
                        // Widget posicionado à direita dos ícones centrados
                        int? notifyLeftPx = Interop.GetTrayNotifyLeft(tray);
                        rightLimitPx = (notifyLeftPx ?? (r.Right - 220)) - 8;
                        int minLeft = taskEndPx.HasValue ? (int)taskEndPx.Value + 8 : r.Left + 4;
                        leftPx = (int)Math.Max(minLeft, Math.Min(manualX, rightLimitPx - winWidth));
                    }
                }
                else
                {
                    int? notifyLeftPx = Interop.GetTrayNotifyLeft(tray);
                    rightLimitPx = (notifyLeftPx ?? (r.Right - 220)) - 8;
                    int minLeft = taskEndPx.HasValue ? (int)taskEndPx.Value + 8 : r.Left + 4;
                    leftPx = (int)Math.Max(minLeft, Math.Min(manualX, rightLimitPx - winWidth));
                }
                availPx = rightLimitPx - leftPx;
            }
            else if (!isLeft)
            {
                double? effStart = startLeftPx ?? _lastValidStartLeft;
                // Numa barra centrada o botão Iniciar existe sempre — âncora nula
                // significa que a leitura ainda não chegou ou falhou.
                if (!effStart.HasValue && Visibility == Visibility.Visible)
                {
                    // Já estamos bem posicionados: FICAR QUIETO até as âncoras
                    // voltarem — esconder e reaparecer na borda esquerda (por cima
                    // do botão do tempo) era exatamente o salto reportado
                    leftPx = w.Left;
                    rightLimitPx = r.Right - 4;
                }
                else if (!effStart.HasValue)
                {
                    // Ainda sem posição (arranque / primeiro reveal): esperar em
                    // vez de posicionar às cegas; após o limite, fallback à esquerda
                    if (_anchorsMissingSince == DateTime.MinValue)
                        _anchorsMissingSince = DateTime.UtcNow;
                    if (DateTime.UtcNow - _anchorsMissingSince < TimeSpan.FromSeconds(4))
                    {
                        HideWidget();
                        return;
                    }
                    leftPx = widgetsRightPx.HasValue ? (int)widgetsRightPx.Value + 8 : r.Left + 12;
                    rightLimitPx = r.Right - 4;
                }
                else
                {
                    _anchorsMissingSince = DateTime.MinValue;
                    // Ícones centrados (em qualquer barra/monitor): o espaço livre
                    // está à esquerda — alinhar a seguir ao botão de widgets/tempo;
                    // sem ele, à borda esquerda. Nunca invadir o botão Iniciar.
                    leftPx = widgetsRightPx.HasValue ? (int)widgetsRightPx.Value + 8 : r.Left + 12;
                    rightLimitPx = (int)effStart.Value - 8;
                }
                availPx = rightLimitPx - leftPx;
            }
            else
            {
                // Ícones alinhados à esquerda: o espaço vazio está à direita —
                // encostar antes dos ícones do sistema/relógio, sem nunca tapar
                // a fila de ícones das apps
                int? notifyLeftPx = Interop.GetTrayNotifyLeft(tray);
                rightLimitPx = notifyLeftPx ?? (r.Right - 220);
                rightLimitPx -= 8;
                if (taskEndPx.HasValue)
                    rightAnchorLeftLimitPx = (int)taskEndPx.Value + 8;
                leftPx = Math.Max(rightAnchorLeftLimitPx, rightLimitPx - winWidth);
                availPx = rightLimitPx - rightAnchorLeftLimitPx;
            }
        }

        if (_spotifyPresent)
        {
            if (!ApplyResponsiveLayout(availPx / windowScale, isCustomSlot))
            {
                // Numa barra lotada nem a versão mínima cabe: esconder em vez
                // de transbordar para cima do relógio/ícones (issue #10)
                _barWasHidden = false;
                HideWidget();
                return;
            }
        }

        // Esconder quando: app em ecrã inteiro (irrelevante com auto-hide — as
        // janelas maximizadas ocupam o ecrã todo e dariam falsos positivos; a
        // visibilidade já segue a da barra); ou Spotify fechado sem botão de abrir.
        bool hide = (!Interop.IsAutoHideEnabled() && Interop.IsForegroundFullscreen(_hwnd, tray))
                    || (!_spotifyPresent && !_settings.ShowLauncher);
        if (hide)
        {
            _barWasHidden = false;
            HideWidget();
            return;
        }

        // Revelação da barra: o Windows teleporta a janela dela para o destino
        // e anima só o visual — não há frames para seguir. Animamos nós a
        // subida, a emergir da borda do ecrã em sincronia com a barra.
        // (Só faz sentido com ocultação automática — sem ela, um rect
        // transitório degenerado da barra armava uma subida espúria.)
        if (_barWasHidden && !_dragging && Interop.IsAutoHideEnabled())
        {
            _barWasHidden = false;
            StartRide(leftPx, belowEdgeTopPx, topPx, down: false, winWidth, winHeight, monitorBottomPx);
            return;
        }
        _barWasHidden = false;

        if (!_dragging && (Math.Abs(w.Left - leftPx) > 1 || Math.Abs(w.Top - topPx) > 1))
            Interop.MoveWindowTo(_hwnd, leftPx, topPx);

        // Durante o deslize, recortar a parte do widget que já saiu do ecrã —
        // sem isto, o excedente aparecia a atravessar um monitor disposto abaixo
        Interop.ClipWindowBottom(_hwnd, winWidth, winHeight, monitorBottomPx - topPx);

        if (Visibility != Visibility.Visible)
            Visibility = Visibility.Visible;
        ReassertTopmost();

        // Posicionamento do widget de letras (Lyrics) no espaço livre à direita da barra
        if (!_spotifyPresent || hide)
        {
            _lyricsWindow?.HideImmediate();
        }
        else if (!_settings.ShowLyrics || _settings.UnifiedLyrics)
        {
            _lyricsWindow?.FadeOutAndHide();
        }
        else
        {
            try
            {
                int? notifyLeft = Interop.GetTrayNotifyLeft(tray);
                int rightBoundPx = (notifyLeft ?? (r.Right - 220)) - 16;
                int leftBoundPx;

                if (!isLeft)
                {
                    // Barra centrada: o espaço livre à direita começa logo após os botões das apps
                    leftBoundPx = taskEndPx.HasValue
                        ? (int)taskEndPx.Value + 16
                        : (r.Left + (r.Right - r.Left) / 2 + 100);
                }
                else
                {
                    // Barra alinhada à esquerda: espaço entre os botões das apps e o widget principal
                    leftBoundPx = (int)(taskEndPx ?? (r.Left + 300)) + 16;
                    rightBoundPx = Math.Min(rightBoundPx, leftPx - 16);
                }

                int availableLyricsWidthPx = rightBoundPx - leftBoundPx;
                if (availableLyricsWidthPx >= 50 && visiblePx > 8)
                {
                    EnsureLyricsWindow();
                    if (_lyricsWindow != null)
                    {
                        _lyricsWindow.EnsureOwnerTray(tray);
                        _lyricsWindow.SetBounds(leftBoundPx, topPx, availableLyricsWidthPx, winHeight, monitorBottomPx - topPx, availableLyricsWidthPx);
                    }
                }
                else
                {
                    _lyricsWindow?.FadeOutAndHide();
                }
            }
            catch (Exception ex)
            {
                Diag.Log($"[Lyrics Position] {ex}");
                _lyricsWindow?.FadeOutAndHide();
            }
        }
    }

    private bool _tooltipOpen;

    /// <summary>Esconde o widget e fecha SEMPRE o popup de volume e o menu de
    /// contexto — todos os caminhos de esconder passam por aqui para não
    /// divergirem (havia caminhos que deixavam popups órfãos a flutuar).</summary>
    private void HideWidget()
    {
        _lyricsWindow?.HideImmediate();
        VolumePopup.IsOpen = false;
        if (Root.ContextMenu is { IsOpen: true } menu)
            menu.IsOpen = false;
        if (Visibility != Visibility.Hidden)
            Visibility = Visibility.Hidden;
    }

    /// <summary>Re-afirma o widget no topo, EXCETO com um tooltip/popup nosso
    /// aberto — re-afirmar por cima deles empurra-os para trás da barra (que
    /// em ocultação automática também vive na banda topmost).</summary>
    private void ReassertTopmost()
    {
        if (!_tooltipOpen && !VolumePopup.IsOpen)
        {
            Interop.EnsureTopmost(_hwnd);
            _lyricsWindow?.ReassertTopmost();
        }
    }

    // ---------- Animação de deslize (ocultação automática da barra) ----------

    private bool _barWasHidden;
    private bool _rideAnimating;
    private bool _rideDown;
    private DispatcherTimer? _rideTimer;

    /// <summary>Anima o widget entre a posição assente e o fundo do ecrã (nos
    /// dois sentidos), com o recorte a fazê-lo emergir/submergir na borda.
    /// Movimento Fluent: entradas desaceleram, saídas aceleram.
    /// startPhase (0..1): fração do caminho que a barra JÁ percorreu quando o
    /// gatilho chegou — o primeiro passo da janela dela vem atrasado, e entrar
    /// na curva a meio mantém o widget em sincronia em vez de a trás dela.</summary>
    private void StartRide(int leftPx, int fromTopPx, int toTopPx, bool down,
        int winWidth, int winHeight, int monitorBottomPx, double startPhase = 0)
    {
        var sw = Stopwatch.StartNew();
        const double DurationMs = 220; // aproxima a animação da própria barra
        startPhase = Math.Clamp(startPhase, 0, 1);
        // Instante da curva cujo easing corresponde à fase pedida
        double t0 = down ? Math.Cbrt(startPhase) : 1 - Math.Cbrt(1 - startPhase);
        double t0Ms = t0 * DurationMs;

        _rideAnimating = true;
        _rideDown = down;
        if (down)
            VolumePopup.IsOpen = false;

        double eased0 = down ? t0 * t0 * t0 : 1 - Math.Pow(1 - t0, 3);
        int startTopPx = (int)Math.Round(fromTopPx + (toTopPx - fromTopPx) * eased0);
        Interop.MoveWindowTo(_hwnd, leftPx, startTopPx);
        Interop.ClipWindowBottom(_hwnd, winWidth, winHeight, monitorBottomPx - startTopPx);
        if (Visibility != Visibility.Visible)
            Visibility = Visibility.Visible;
        ReassertTopmost();

        _rideTimer?.Stop();
        _rideTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(10)
        };
        _rideTimer.Tick += (_, _) =>
        {
            double t = Math.Min(1.0, (t0Ms + sw.ElapsedMilliseconds) / DurationMs);
            double eased = down ? t * t * t : 1 - Math.Pow(1 - t, 3);
            int topPx = (int)Math.Round(fromTopPx + (toTopPx - fromTopPx) * eased);
            Interop.MoveWindowTo(_hwnd, leftPx, topPx);
            Interop.ClipWindowBottom(_hwnd, winWidth, winHeight, monitorBottomPx - topPx);
            if (t >= 1.0)
            {
                _rideTimer?.Stop();
                _rideAnimating = false;
                if (down)
                {
                    _barWasHidden = true;
                    Visibility = Visibility.Hidden;
                }
            }
        };
        _rideTimer.Start();
    }

    private void CancelRide()
    {
        _rideTimer?.Stop();
        _rideAnimating = false;
    }

    /// <summary>
    /// Encaixa o widget no espaço disponível: primeiro encolhe o texto até um
    /// mínimo; se mesmo assim não couber, esconde os botões menos importantes
    /// (volume → aleatório → favoritos → seguinte → anterior).
    /// Devolve false quando nem a versão mínima cabe no espaço dado.
    /// </summary>
    private bool ApplyResponsiveLayout(double availableDip, bool isCustomSlot = false)
    {
        double s = _settings.Scale;
        double avail = availableDip / s; // trabalhar em unidades pré-escala

        const double IconBtn = 28;            // 26 + margens
        const double PlayBtn = 34;            // 30 + margens
        const double BasePart = 16 + 34 + 15; // padding + capa + margens do texto

        // O play deixou de ser obrigatório: quem só quer o mostrador de "a
        // tocar agora" pode escondê-lo (pedido da comunidade)
        double baseUsed = BasePart + (_settings.ShowPlay ? PlayBtn : 0);
        if (avail < baseUsed + MinTextWidth)
            return false;

        bool prev = false, next = false, like = false, shuffle = false, repeat = false, volume = false;
        double usedButtons = 0;

        // Botões essenciais de reprodução
        Take(ref prev, _settings.ShowPrev);
        Take(ref next, _settings.ShowNext);

        // Se houver botões secundários, certificar que só entram se houver espaço confortável
        // para o texto das letras (pelo menos 180 DIPs em modo Unified)
        double minComfortableText = (_settings.ShowLyrics && _settings.UnifiedLyrics) ? 180 : MinTextWidth;
        TakeComfortable(ref volume, _settings.ShowVolume);
        TakeComfortable(ref like, _settings.ShowLike);
        TakeComfortable(ref shuffle, _settings.ShowShuffle);
        TakeComfortable(ref repeat, _settings.ShowRepeat);

        // O texto ganha TODO o espaço restante até ao MaxTextWidth (380 DIPs) se automático,
        // ou todo o espaço do slot se o utilizador definiu margens personalizadas
        double text = isCustomSlot
            ? Math.Max(MinTextWidth, avail - (baseUsed + usedButtons))
            : Math.Max(MinTextWidth, Math.Min(MaxTextWidth, avail - (baseUsed + usedButtons)));

        SetVis(PlayPauseButton, _settings.ShowPlay);
        SetVis(PrevButton, prev);
        SetVis(NextButton, next);
        SetVis(LikeButton, like);
        SetVis(ShuffleButton, shuffle);
        SetVis(RepeatButton, repeat);
        SetVis(VolumeButton, volume);
        if (Math.Abs(TextStack.Width - text) > 1)
        {
            TextStack.Width = text;
            UpdateMarquee();
            UpdateUnifiedInfoMarquee();
        }
        return true;

        void Take(ref bool flag, bool wanted)
        {
            if (wanted && baseUsed + usedButtons + IconBtn + MinTextWidth <= avail)
            {
                flag = true;
                usedButtons += IconBtn;
            }
        }

        void TakeComfortable(ref bool flag, bool wanted)
        {
            if (wanted && baseUsed + usedButtons + IconBtn + minComfortableText <= avail)
            {
                flag = true;
                usedButtons += IconBtn;
            }
        }

        static void SetVis(UIElement el, bool show)
        {
            var v = show ? Visibility.Visible : Visibility.Collapsed;
            if (el.Visibility != v) el.Visibility = v;
        }
    }

    /// <summary>
    /// Atualiza as âncoras da barra (botão de widgets e botão Iniciar) em background,
    /// no máximo a cada 5 segundos — as consultas de UI Automation não são gratuitas.
    /// </summary>

    private void RefreshAnchors(IntPtr tray)
    {
        if ((DateTime.UtcNow - _lastAnchorQuery).TotalSeconds < 5)
            return;
        // Watchdog: se uma query pendurou (UIA contra um Explorer moribundo), a
        // flag não pode prender as âncoras para sempre — após 15s arranca outra
        if (_anchorQueryRunning && (DateTime.UtcNow - _lastAnchorQuery).TotalSeconds < 15)
            return;

        _anchorQueryRunning = true;
        _lastAnchorQuery = DateTime.UtcNow;
        Task.Run(() =>
        {
            try
            {
                var (ok, widgetsRight, startLeft, taskButtonsRight) = TaskbarAnchors.Get(tray);
                lock (_anchorLock)
                {
                    // A barra alvo mudou enquanto a query corria: estes valores
                    // são coordenadas do monitor errado — deitar fora
                    if (tray != _anchorsTray)
                        return;
                    // Leitura falhada → mantêm-se TODAS as âncoras anteriores
                    // (falha transitória de UIA ≠ layout da barra mudou; era
                    // isto que punha o widget em cima do botão do tempo)
                    if (!ok)
                        return;
                    // O Iniciar existe sempre — uma leitura OK sem ele é suspeita;
                    // mas aceitar à 3ª seguida, senão um Iniciar realmente
                    // escondido (shells modificadas) congelava as âncoras todas
                    if (widgetsRight.HasValue)
                        _widgetsRightPx = widgetsRight;
                    if (taskButtonsRight.HasValue)
                        _taskEndPx = taskButtonsRight;

                    if (startLeft.HasValue)
                    {
                        _startLeftPx = startLeft;
                        _lastValidStartLeft = startLeft.Value;
                        bool isLeftCur = IsTaskbarLeftAligned();
                        var marginsDictCur = isLeftCur ? _settings.MarginsLeft : _settings.MarginsCenter;
                        if (marginsDictCur.TryGetValue(TrayIndex, out var curSm) && !curSm.IsRightSlot)
                        {
                            curSm.LastStartLeft = startLeft.Value;
                        }
                    }
                    else if (_lastValidStartLeft.HasValue)
                    {
                        _startLeftPx = _lastValidStartLeft;
                    }
                }
            }
            finally
            {
                _anchorQueryRunning = false;
            }
        });
    }

    // ---------- Atualização da faixa ----------

    private async Task RefreshTrackAsync()
    {
        if (_refreshing || _closed) return;
        _refreshing = true;
        try
        {
            var procs = Process.GetProcessesByName("Spotify");
            bool processAlive = procs.Length > 0;
            foreach (var p in procs) p.Dispose();

            // As chamadas SMTC podem ficar penduradas para sempre numa sessão em
            // teardown (Spotify a fechar/reabrir) — sem timeout, a flag _refreshing
            // ficava presa e o widget congelava até reiniciar
            TrackInfo? track = null;
            if (processAlive)
            {
                try { track = await _media.GetTrackAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (TimeoutException) { }
            }

            // Nas mudanças de faixa, a sessão/título ficam vazios por umas centenas
            // de ms — manter o estado atual no ecrã e re-verificar já, em vez de
            // piscar placeholders ou esconder o widget
            bool noData = track == null || string.IsNullOrWhiteSpace(track.Title);
            if (processAlive && noData && _lastTrackKey.Length > 0)
            {
                if (_trackNullSince == DateTime.MinValue)
                    _trackNullSince = DateTime.UtcNow;
                if (DateTime.UtcNow - _trackNullSince < TimeSpan.FromSeconds(1.2))
                {
                    _ = QuickRecheckAsync();
                    return;
                }
            }
            else if (!noData)
            {
                _trackNullSince = DateTime.MinValue;
            }

            // Perder a sessão com o processo ainda vivo (de forma persistente) =
            // fecho do Spotify em curso. Esconder, sem estados intermédios.
            if (processAlive && track == null && _lastTrackKey.Length > 0)
            {
                _sessionLostAt = DateTime.UtcNow;
                _lastTrackKey = "";
            }
            bool closing = processAlive && track == null &&
                           DateTime.UtcNow - _sessionLostAt < TimeSpan.FromSeconds(6);
            _spotifyPresent = processAlive && !closing;

            var launcherWanted = !_spotifyPresent && _settings.ShowLauncher
                ? Visibility.Visible : Visibility.Collapsed;
            var contentWanted = _spotifyPresent ? Visibility.Visible : Visibility.Collapsed;
            if (LauncherPanel.Visibility != launcherWanted) LauncherPanel.Visibility = launcherWanted;
            if (ContentPanel.Visibility != contentWanted) ContentPanel.Visibility = contentWanted;

            if (!_spotifyPresent)
            {
                _liked = null;
                _duration = TimeSpan.Zero;
                _currentLyrics = null;
                _lastLyricsTrackKey = "";
                _lyricsWindow?.SetLyric("");
                VolumePopup.IsOpen = false;
                UpdateProgressUi();
                UpdatePosition(); // esconder/mostrar imediatamente, sem esperar o timer
                return;
            }

            _duration = track?.Duration ?? TimeSpan.Zero;
            if (AcceptPlayingState(track?.IsPlaying == true))
            {
                // Âncora no LastUpdatedTime do Windows (não no momento da leitura):
                // re-ler um snapshot antigo dá o mesmo valor interpolado — sem saltos
                TimeSpan pos = track?.Position ?? TimeSpan.Zero;
                DateTime posAt = track?.PositionAtUtc ?? DateTime.UtcNow;
                // Snapshot da faixa anterior (posição > duração, ou muito antigo
                // numa faixa acabada de mudar): mostrar do início até assentar
                bool stale = pos > _duration ||
                             (track != null && track.Title + "|" + track.Artist != _lastTrackKey &&
                              DateTime.UtcNow - posAt > TimeSpan.FromSeconds(5));
                if (stale)
                {
                    pos = TimeSpan.Zero;
                    posAt = DateTime.UtcNow;
                }
                // Após um seek, ignorar posições fotografadas ANTES do salto
                if (!(DateTime.UtcNow - _seekAt < TimeSpan.FromSeconds(3) && posAt < _seekAt))
                {
                    _basePosition = pos;
                    _basePositionAt = posAt;
                }
                _isPlayingUi = track?.IsPlaying == true;
            }
            UpdateProgressUi();

            if (track == null || string.IsNullOrWhiteSpace(track.Title))
            {
                _currentLyrics = null;
                _lastLyricsTrackKey = "";
                _lyricsWindow?.SetLyric("");
                TitleText.Text = "Spotify";
                ArtistText.Text = L.NothingPlaying;
                SetPlayPauseIcon(false);
                ShuffleIcon.Fill = DimWhite;
                ShuffleDot.Visibility = Visibility.Collapsed;
                ShuffleSmartStar.Visibility = Visibility.Collapsed;
                RepeatIcon.Fill = DimWhite;
                RepeatDot.Visibility = Visibility.Collapsed;
                RepeatOneBadge.Visibility = Visibility.Collapsed;
                LikeIcon.Data = AddCircleGeo;
                LikeIcon.Fill = DimWhite;
                _liked = null;
                SetAlbumArt(null);
                _lastTrackKey = "";
                UpdateMarquee();
                return;
            }

            TitleText.Text = track.Title;
            ArtistText.Text = track.Artist;
            UpdateMarquee();
            SetPlayPauseIcon(_isPlayingUi);

            // Estado real (favoritos + aleatório + repetição) da árvore de
            // acessibilidade do Spotify; o SMTC serve de rede de segurança.
            string key = track.Title + "|" + track.Artist;
            bool keyChanged = key != _lastTrackKey;
            if (keyChanged && _trackSlideDirection != 0)
            {
                double startX = _trackSlideDirection * 18.0;
                _trackSlideDirection = 0;
                TextStackShift.X = startX;
                TextStack.Opacity = 0.2;
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                var slide = new DoubleAnimation(startX, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease };
                var fade = new DoubleAnimation(0.2, 1.0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease };
                TextStackShift.BeginAnimation(TranslateTransform.XProperty, slide);
                TextStack.BeginAnimation(UIElement.OpacityProperty, fade);
            }

            if (keyChanged || _uiaDirty || DateTime.UtcNow - _lastUiaStateAt > TimeSpan.FromSeconds(5))
            {
                _uiaDirty = false;
                var state = (Liked: (bool?)null, Shuffle: ShuffleMode.Unknown, Repeat: RepeatMode.Unknown, Fresh: false);
                try { state = await Task.Run(() => _uia.GetState(track.Title)).WaitAsync(TimeSpan.FromSeconds(8)); }
                catch (TimeoutException) { }
                _lastStateFresh = state.Fresh;
                // Grupo ainda da faixa anterior (zombie): não mostrar o tick antigo
                _uiaState = (state.Fresh ? state.Liked : null, state.Shuffle, state.Repeat);
                _lastUiaStateAt = DateTime.UtcNow;
            }
            if (keyChanged || (_currentLyrics == null && !string.IsNullOrEmpty(track.Title)))
            {
                _ = LoadLyricsForTrackAsync(track.Title, track.Artist, track.Duration);
            }
            if (keyChanged)
            {
                _ = SettleStateAsync(); // re-ler até o Spotify renderizar a barra da faixa nova
                _ = Task.Delay(3000).ContinueWith(_ => Dispatcher.InvokeAsync(Interop.TrimWorkingSet));
            }
            var (liked, uiaMode, uiaRepeat) = _uiaState;
            // Depois de adicionar aos favoritos, ignorar "não gostado" antigo — o
            // texto do botão do Spotify pode demorar vários segundos a atualizar
            if (liked == false && DateTime.UtcNow - _likedOptimisticAt < TimeSpan.FromSeconds(8))
                liked = true;
            bool justLiked = liked == true && _liked != true;
            _liked = liked;
            if (justLiked)
            {
                AnimateLikePop();
            }

            // Repetição: mapear modo do SMTC (fonte da verdade em tempo real, inclusive minimizado)
            RepeatMode smtcRepeat = track.AutoRepeatMode switch
            {
                Windows.Media.MediaPlaybackAutoRepeatMode.None => RepeatMode.Off,
                Windows.Media.MediaPlaybackAutoRepeatMode.List => RepeatMode.Context,
                Windows.Media.MediaPlaybackAutoRepeatMode.Track => RepeatMode.Track,
                _ => RepeatMode.Unknown
            };

            RepeatMode repeatMode = _currentRepeatMode;
            if (DateTime.UtcNow - _repeatToggledAt > TimeSpan.FromMilliseconds(1500))
            {
                if (smtcRepeat != RepeatMode.Unknown)
                    repeatMode = smtcRepeat;
                else if (_uiaState.Repeat != RepeatMode.Unknown)
                    repeatMode = _uiaState.Repeat;
            }
            _currentRepeatMode = repeatMode;
            ApplyRepeatVisual(repeatMode);

            LikeIcon.Data = liked == true ? CheckCircleGeo : AddCircleGeo;
            LikeIcon.Fill = liked == true ? SpotifyGreen : (liked == false ? Subdued : DimWhite);

            ShuffleMode mode = _currentShuffleMode;
            if (DateTime.UtcNow - _shuffleToggledAt > TimeSpan.FromMilliseconds(1500))
            {
                if (Interop.IsSpotifyMinimized())
                {
                    if (track.IsShuffle.HasValue)
                        mode = track.IsShuffle.Value ? ShuffleMode.On : ShuffleMode.Off;
                }
                else
                {
                    if (uiaMode != ShuffleMode.Unknown)
                        mode = uiaMode;
                    else if (track.IsShuffle.HasValue)
                        mode = track.IsShuffle.Value ? ShuffleMode.On : ShuffleMode.Off;
                }
            }
            _currentShuffleMode = mode;
            ApplyShuffleVisual(mode);

            // Capa: só reler se mudou de faixa ou ainda não tinha sido carregada
            if (_artDirty || keyChanged)
            {
                _artDirty = false;
                _lastTrackKey = key;
                byte[]? bytes = null;
                try { bytes = await _media.GetThumbnailAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (TimeoutException) { }
                BitmapImage? art = null;
                if (bytes != null)
                {
                    // Miniaturas truncadas/corrompidas acontecem em transições
                    // de faixa — não podem rebentar o refresh inteiro
                    try { art = ToBitmap(bytes); } catch { }
                }
                _lastArtBytes = bytes;
                UpdateDynamicColor(bytes);
                SetAlbumArt(art);
            }
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>Troca a capa com um crossfade suave: a nova esbate para dentro
    /// por cima da atual (camada de topo) e depois passa a ser a base — em vez
    /// da troca seca, dá o toque "premium" pedido. Sem capa antes: fade simples
    /// a partir do placeholder; sem capa nova: volta ao placeholder.</summary>
    private void SetAlbumArt(BitmapImage? art)
    {
        const int FadeMs = 250;
        if (art == null)
        {
            ArtImageTop.BeginAnimation(OpacityProperty, null);
            ArtImageTop.Visibility = Visibility.Collapsed;
            ArtImage.Visibility = Visibility.Collapsed;
            ArtBrush.ImageSource = null;
            ArtBrushTop.ImageSource = null;
            ArtPlaceholder.Visibility = Visibility.Visible;
            return;
        }

        bool hadArt = ArtImage.Visibility == Visibility.Visible && ArtBrush.ImageSource != null;
        if (!hadArt)
        {
            // Vinha do placeholder: aparecer com um fade curto
            ArtBrush.ImageSource = art;
            ArtImage.BeginAnimation(OpacityProperty, null);
            ArtImage.Opacity = 1;
            ArtImage.Visibility = Visibility.Visible;
            ArtPlaceholder.Visibility = Visibility.Collapsed;
            ArtImage.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(FadeMs)));
            return;
        }

        // Já havia capa: crossfade real — a nova na camada de topo a esbater
        // para dentro por cima da atual
        ArtImage.BeginAnimation(OpacityProperty, null);
        ArtImage.Opacity = 1;
        ArtBrushTop.ImageSource = art;
        ArtImageTop.Visibility = Visibility.Visible;
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(FadeMs));
        fade.Completed += (_, _) =>
        {
            // A de topo passa a ser a base; a de topo esconde-se p/ a próxima
            ArtBrush.ImageSource = art;
            ArtImageTop.BeginAnimation(OpacityProperty, null);
            ArtImageTop.Opacity = 0;
            ArtImageTop.Visibility = Visibility.Collapsed;
        };
        ArtImageTop.BeginAnimation(OpacityProperty, fade);
    }

    private static BitmapImage ToBitmap(byte[] bytes)
    {
        var bmp = new BitmapImage();
        using (var ms = new MemoryStream(bytes))
        {
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 96;
            bmp.StreamSource = ms;
            bmp.EndInit();
        }
        bmp.Freeze();
        return bmp;
    }

    // ---------- Controlos ----------

    /// <summary>O triângulo de play centrado geometricamente parece deslocado à
    /// esquerda (a massa visual fica à esquerda) — compensação ótica de 1,5px.</summary>
    private void SetPlayPauseIcon(bool playing)
    {
        PlayPauseIcon.Data = playing ? PauseGeo : PlayGeo;
        PlayPauseIcon.Margin = playing ? new Thickness(0) : new Thickness(1.5, 0, 0, 0);
    }

    private void ApplyShuffleVisual(ShuffleMode mode)
    {
        ShuffleIcon.Fill = mode switch
        {
            ShuffleMode.On or ShuffleMode.Smart => SpotifyGreen,
            ShuffleMode.Off => Subdued,
            _ => DimWhite,
        };
        ShuffleDot.Visibility = mode is ShuffleMode.On or ShuffleMode.Smart
            ? Visibility.Visible : Visibility.Collapsed;
        ShuffleSmartStar.Visibility = mode == ShuffleMode.Smart ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyRepeatVisual(RepeatMode mode)
    {
        RepeatIcon.Fill = mode is RepeatMode.Context or RepeatMode.Track
            ? SpotifyGreen
            : (mode == RepeatMode.Off ? Subdued : DimWhite);
        RepeatDot.Visibility = mode is RepeatMode.Context or RepeatMode.Track
            ? Visibility.Visible : Visibility.Collapsed;
        RepeatOneBadge.Visibility = mode == RepeatMode.Track
            ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Atualização leve disparada pelos eventos de timeline (frequentes).
    /// Fora da thread de UI e com timeout: o SMTC pode pendurar em sessões mortas.</summary>
    private async void RefreshTimeline()
    {
        (TimeSpan Position, TimeSpan Duration, bool IsPlaying, DateTime PositionAtUtc)? tlMaybe = null;
        try { tlMaybe = await Task.Run(() => _media.GetTimeline()).WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (TimeoutException) { }
        if (tlMaybe is not { } tl) return;
        // Depois de um seek, snapshots ANTERIORES ao salto ainda chegam durante
        // uns segundos — aplicá-los fazia a barra recuar e voltar a saltar
        if (DateTime.UtcNow - _seekAt < TimeSpan.FromSeconds(3) && tl.PositionAtUtc < _seekAt)
            return;
        _duration = tl.Duration;
        if (AcceptPlayingState(tl.IsPlaying) && tl.Position <= tl.Duration)
        {
            _basePosition = tl.Position;
            _basePositionAt = tl.PositionAtUtc;
            _isPlayingUi = tl.IsPlaying;
            SetPlayPauseIcon(tl.IsPlaying);
        }
        UpdateProgressUi();
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        // Feedback imediato; leituras antigas são ignoradas por 2 s (grace) e
        // depois disso o estado real do SMTC volta a mandar
        _isPlayingUi = !_isPlayingUi;
        _playToggledAt = DateTime.UtcNow;
        SetPlayPauseIcon(_isPlayingUi);
        AnimateElementPop(PlayPauseButton, 1.06);
        if (!_isPlayingUi)
            _basePosition += DateTime.UtcNow - _basePositionAt; // congelar posição
        else
        {
            _lyricsTimer.Interval = TimeSpan.FromMilliseconds(250);
            UpdateLyricsUi();
        }
        _basePositionAt = DateTime.UtcNow;
        await _media.TogglePlayPauseAsync();
    }
    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        AnimateElementPop(NextButton, 1.08);
        _trackSlideDirection = 1;
        await _media.NextAsync();
    }
    private async void Prev_Click(object sender, RoutedEventArgs e)
    {
        AnimateElementPop(PrevButton, 1.08);
        _trackSlideDirection = -1;
        await _media.PreviousAsync();
    }

    private async void Shuffle_Click(object sender, RoutedEventArgs e)
    {
        AnimateElementPop(ShuffleButton, 1.08);
        bool isMin = Interop.IsSpotifyMinimized();
        if (isMin)
        {
            // Com o Spotify minimizado: UIA não processa cliques no Chromium. Usar o SMTC diretamente
            bool target = _currentShuffleMode != ShuffleMode.On && _currentShuffleMode != ShuffleMode.Smart;
            _currentShuffleMode = target ? ShuffleMode.On : ShuffleMode.Off;
            ApplyShuffleVisual(_currentShuffleMode);
            _shuffleToggledAt = DateTime.UtcNow;

            await _media.SetShuffleActiveAsync(target);
        }
        else
        {
            var next = _currentShuffleMode switch
            {
                ShuffleMode.Off => ShuffleMode.On,
                ShuffleMode.On => ShuffleMode.Smart,
                ShuffleMode.Smart => ShuffleMode.Off,
                _ => ShuffleMode.On,
            };
            _currentShuffleMode = next;
            ApplyShuffleVisual(next);
            _shuffleToggledAt = DateTime.UtcNow;

            bool ok = await Task.Run(() => _uia.CycleShuffle());
            if (!ok)
            {
                bool target = next is ShuffleMode.On or ShuffleMode.Smart;
                await _media.SetShuffleActiveAsync(target);
            }
        }

        await Task.Delay(200);
        _uiaDirty = true;
        await RefreshTrackAsync();
    }

    private async void Repeat_Click(object sender, RoutedEventArgs e)
    {
        AnimateElementPop(RepeatButton, 1.08);
        var next = _currentRepeatMode switch
        {
            RepeatMode.Off => RepeatMode.Context,
            RepeatMode.Context => RepeatMode.Track,
            RepeatMode.Track => RepeatMode.Off,
            _ => RepeatMode.Context,
        };

        _currentRepeatMode = next;
        ApplyRepeatVisual(next);
        _repeatToggledAt = DateTime.UtcNow;

        var targetSmtc = next switch
        {
            RepeatMode.Off => Windows.Media.MediaPlaybackAutoRepeatMode.None,
            RepeatMode.Context => Windows.Media.MediaPlaybackAutoRepeatMode.List,
            RepeatMode.Track => Windows.Media.MediaPlaybackAutoRepeatMode.Track,
            _ => Windows.Media.MediaPlaybackAutoRepeatMode.None,
        };

        bool ok = await _media.SetRepeatModeAsync(targetSmtc);
        if (!ok && !Interop.IsSpotifyMinimized())
            await Task.Run(() => _uia.CycleRepeat());

        await Task.Delay(200);
        _uiaDirty = true;
        await RefreshTrackAsync();
    }

    private async void Like_Click(object sender, RoutedEventArgs e)
    {
        if (_liked == true) return; // já está nos favoritos

        // Feedback imediato; se falhar, a leitura de estado seguinte corrige
        _liked = true;
        _likedOptimisticAt = DateTime.UtcNow;
        LikeIcon.Data = CheckCircleGeo;
        LikeIcon.Fill = SpotifyGreen;
        AnimateLikePop();

        bool ok = await Task.Run(() => _uia.AddToFavorites());
        if (!ok)
            await Task.Run(() => _uia.AddToFavoritesByClick()); // recurso raro: sem verbo disponível

        // O texto do botão do Spotify demora segundos a refletir a adição —
        // reconciliar mais tarde em vez de concluir já que falhou
        _ = ReconcileLikeLaterAsync();
    }

    private async Task ReconcileLikeLaterAsync()
    {
        await Task.Delay(4000);
        _uiaDirty = true;
        await RefreshTrackAsync();
    }

    /// <summary>Re-verificação rápida durante o vazio transitório das mudanças de faixa.</summary>
    private async Task QuickRecheckAsync()
    {
        await Task.Delay(350);
        await RefreshTrackAsync();
    }

    private int _settleToken;
    private bool _lastStateFresh = true;

    /// <summary>Depois de uma mudança de faixa, re-ler a cada 500 ms até o grupo
    /// do título no Spotify já ser o da faixa nova (validado pelo título do SMTC).
    /// Uma nova mudança de faixa cancela a série anterior.</summary>
    private async Task SettleStateAsync()
    {
        int token = ++_settleToken;
        for (int i = 0; i < 12; i++) // máx. ~6 s
        {
            await Task.Delay(500);
            if (token != _settleToken) return;
            _uiaDirty = true;
            await RefreshTrackAsync();
            if (_lastStateFresh) return;

            // Workaround para o Spotify minimizado (o Chromium congela a árvore de acessibilidade)
            if (i == 1 && await Task.Run(() => _uia.IsMinimized()))
            {
                await Task.Run(() => _uia.ForceUiaUpdate());
                _uiaDirty = true;
                await RefreshTrackAsync();
                if (_lastStateFresh) return;
            }
        }
    }

    private async void Volume_Click(object sender, RoutedEventArgs e)
    {
        if (VolumePopup.IsOpen)
        {
            VolumePopup.IsOpen = false;
            return;
        }
        if (_volLoading)
            return; // já há uma abertura em curso — outra sobrescreveria o
                    // ajuste do utilizador com o volume antigo ao completar

        CaptureForeground();
        _volLoading = true;
        try
        {
            // O recurso CoreAudio também fora da thread de UI — é uma RPC ao
            // serviço de áudio e chegava a bloquear a interface
            double? current = await Task.Run(() => Interop.IsSpotifyMinimized()
                ? (SpotifyVolume.GetVolume() ?? _uia.GetVolume())
                : (_uia.GetVolume() ?? SpotifyVolume.GetVolume()));
            if (_closed || Visibility != Visibility.Visible)
                return; // o widget escondeu-se durante a leitura: não abrir
                        // um popup órfão a flutuar sobre a barra
            // Atribuir COM _volLoading ainda ativo: senão o ValueChanged ecoava
            // a leitura de volta ao Spotify em cada abertura — e uma leitura
            // falhada (null → 100%) rebentava o volume só por abrir o popup
            if (current is double v)
                VolumeSlider.Value = v * 100;
        }
        finally
        {
            _volLoading = false;
        }
        VolumePopup.IsOpen = true;
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_volLoading) return;
        ApplyVolume(e.NewValue / 100.0);
    }

    private double? _pendingVolume;
    private bool _volApplying;

    /// <summary>
    /// Aplica o volume no slider do próprio Spotify (a UI dele acompanha),
    /// com o mixer do Windows como recurso. Serializa os pedidos para o
    /// arrastar do slider não acumular chamadas.
    /// </summary>
    private async void ApplyVolume(double fraction)
    {
        _pendingVolume = fraction;
        if (_volApplying) return;
        _volApplying = true;
        try
        {
            while (_pendingVolume is double v)
            {
                _pendingVolume = null;
                await Task.Run(() =>
                {
                    if (Interop.IsSpotifyMinimized() || !_uia.SetVolume(v))
                        SpotifyVolume.SetVolume((float)v);
                });
            }
        }
        finally
        {
            _volApplying = false;
        }
    }

    private void VolumePopup_MouseLeave(object sender, MouseEventArgs e) => VolumePopup.IsOpen = false;

    private int _wheelAccum;

    /// <summary>Roda do rato sobre o botão/popup de volume: fechado abre (com o
    /// volume atual carregado), aberto ajusta ±5 — como no próprio Spotify.</summary>
    private void Volume_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        if (!VolumePopup.IsOpen)
        {
            Volume_Click(sender, null!); // o guard de _volLoading evita reentradas
            return;
        }
        if (_volLoading) return;
        // Acumular o delta em vez de ±5 por EVENTO: touchpads de precisão
        // mandam dezenas de eventos pequenos por gesto (o volume ia de 50 a 0
        // num toque) e rodas rápidas juntam vários notches num evento só
        // Inverter o sentido descarta o resto acumulado — senão o primeiro
        // notch da direção contrária era "engolido" a cancelar o resíduo
        if (_wheelAccum != 0 && Math.Sign(_wheelAccum) != Math.Sign(e.Delta))
            _wheelAccum = 0;
        _wheelAccum += e.Delta;
        int steps = _wheelAccum / 120;
        if (steps == 0) return;
        _wheelAccum -= steps * 120;
        VolumeSlider.Value = Math.Clamp(VolumeSlider.Value + 5 * steps, 0, 100);
    }

    // ---------- Tema (barra clara/escura) ----------

    private static bool IsSystemLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int v && v == 1;
        }
        catch
        {
            return false;
        }
    }

    private DateTime _lastThemeCheck = DateTime.MinValue;

    /// <summary>A barra de tarefas segue o tema do SISTEMA (não o das apps);
    /// numa barra clara os textos/ícones têm de escurecer.</summary>
    private void ApplyThemeIfChanged()
    {
        if ((DateTime.UtcNow - _lastThemeCheck).TotalSeconds < 10) return;
        _lastThemeCheck = DateTime.UtcNow;

        bool light = IsSystemLightTheme();
        if (_lightTheme == light) return;
        _lightTheme = light;

        Subdued = new SolidColorBrush(light ? Color.FromRgb(0x48, 0x48, 0x48) : Color.FromRgb(0xB3, 0xB3, 0xB3));
        DimWhite = new SolidColorBrush(light ? Color.FromArgb(0x66, 0x00, 0x00, 0x00) : Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
        _progressFillNormal = light ? Brushes.Black : Brushes.White;

        TitleText.Foreground = light ? Brushes.Black : Brushes.White;
        ArtistText.Foreground = Subdued;
        LauncherText.Foreground = Subdued;
        PrevIcon.Fill = Subdued;
        NextIcon.Fill = Subdued;
        VolumeIcon.Fill = Subdued;
        ArtPlaceholder.Foreground = DimWhite;
        ProgressTrack.Background = new SolidColorBrush(light ? Color.FromArgb(0x2E, 0x00, 0x00, 0x00) : Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        UpdateProgressFillColor();

        // Botão play: círculo branco no escuro, preto no claro (como o Spotify)
        PlayPauseButton.Background = light ? Brushes.Black : Brushes.White;
        PlayPauseIcon.Fill = light ? Brushes.White : Brushes.Black;

        _marqueeKey = ""; // sem efeito no texto, mas força refresh coerente
        _ = RefreshTrackAsync();
    }

    // ---------- Marquee do título ----------

    private string _marqueeKey = "";

    /// <summary>Título maior do que a coluna de texto → scroll contínuo com pausas,
    /// como no Spotify; caso contrário fica estático.</summary>
    private void UpdateMarquee()
    {
        double clipWidth = TextStack.Width;
        // O DPI entra na chave: a largura renderizada muda com a escala do
        // monitor e a decisão de scroll ficava obsoleta ao mudar de ecrã
        string key = $"{TitleText.Text}|{clipWidth:0}|{VisualTreeHelper.GetDpi(this).PixelsPerDip:0.##}";
        if (key == _marqueeKey) return;
        _marqueeKey = key;

        // Medir a largura REALMENTE renderizada. A janela usa
        // TextFormattingMode=Display (avanços ajustados ao píxel) e o
        // FormattedText mede em modo Ideal — a diferença varia com a escala do
        // ecrã e o tipo de letra, e nalgumas máquinas passava da tolerância:
        // títulos que cabiam faziam scroll na mesma (report da comunidade).
        // Medir o próprio TextBlock sem restrições dá o valor exato do que se
        // desenha (está num Canvas, o layout já não o constrange).
        TitleText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double textWidth = Math.Ceiling(TitleText.DesiredSize.Width);

        TitleShift.BeginAnimation(TranslateTransform.XProperty, null);
        TitleShift.X = 0;

        double overflow = textWidth - clipWidth;
        if (overflow > 4)
        {
            double scrollSeconds = Math.Max(1.5, overflow / 25.0);
            double end = -(overflow + 12);
            var anim = new DoubleAnimationUsingKeyFrames();
            double t = 2.0; // pausa inicial (ler o início)
            anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));
            t += scrollSeconds;
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(end, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));
            t += 1.5; // pausa no fim (ler o resto)
            anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(end, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));
            t += 0.8; // regressar ao início e ficar estático (chạy 1 lần duy nhất)
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));
            anim.Duration = TimeSpan.FromSeconds(t);
            TitleShift.BeginAnimation(TranslateTransform.XProperty, anim);
        }
        // Sem overflow: texto parado em X=0 (o Canvas nunca trunca a renderização)
    }

    // ---------- Barra de progresso ----------

    /// <summary>O Spotify só publica a posição de vez em quando; entre leituras,
    /// a posição é interpolada com o relógio local enquanto está a tocar.</summary>
    private bool _isScrubbing = false;
    private TimeSpan _scrubTarget = TimeSpan.Zero;

    /// <summary>Desenha a barra de progresso. Chamado a cada segundo pelo timer;
    /// a posição é interpolada com o relógio local enquanto está a tocar.</summary>
    private void UpdateProgressUi()
    {
        bool show = _settings.ShowProgress && _spotifyPresent && _duration > TimeSpan.Zero;
        var wanted = show ? Visibility.Visible : Visibility.Collapsed;
        if (ProgressTrack.Visibility != wanted)
            ProgressTrack.Visibility = wanted;
        if (!show || _isScrubbing) return;

        TimeSpan pos = _basePosition;
        if (_isPlayingUi)
            pos += DateTime.UtcNow - _basePositionAt;

        double fraction = Math.Clamp(pos.TotalMilliseconds / _duration.TotalMilliseconds, 0, 1);
        ProgressFill.Width = fraction * ProgressTrack.ActualWidth;
        if (ProgressThumb != null)
            Canvas.SetLeft(ProgressThumb, Math.Clamp(ProgressFill.Width - 3, 0, Math.Max(0, ProgressTrack.ActualWidth - 6)));
    }

    private void Progress_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_moveMode) return; // em modo mover, o arrasto tem prioridade
        e.Handled = true;      // não tratar como clique para abrir o Spotify

        if (_duration <= TimeSpan.Zero || ProgressTrack.ActualWidth <= 0) return;
        _isScrubbing = true;
        ProgressTrack.CaptureMouse();
        AnimateThumbScale(1.3);
        UpdateScrubPosition(e.GetPosition(ProgressTrack).X);
    }

    private void Progress_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isScrubbing) return;
        UpdateScrubPosition(e.GetPosition(ProgressTrack).X);
    }

    private async void Progress_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isScrubbing) return;
        _isScrubbing = false;
        ProgressTrack.ReleaseMouseCapture();
        AnimateThumbScale(1.0);

        UpdateScrubPosition(e.GetPosition(ProgressTrack).X);

        _seekAt = DateTime.UtcNow;
        _basePosition = _scrubTarget;
        _basePositionAt = DateTime.UtcNow;
        UpdateProgressUi();

        if (!ProgressTrack.IsMouseOver)
        {
            ProgressFill.Background = _settings.DynamicAlbumColor ? _accentBrush : _progressFillNormal;
            var anim = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            ProgressThumb.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        await _media.SeekAsync(_scrubTarget);
    }

    private void Progress_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isScrubbing)
        {
            _isScrubbing = false;
            AnimateThumbScale(1.0);
            UpdateProgressUi();
        }
    }

    private void UpdateScrubPosition(double mouseX)
    {
        if (_duration <= TimeSpan.Zero || ProgressTrack.ActualWidth <= 0) return;
        double fraction = Math.Clamp(mouseX / ProgressTrack.ActualWidth, 0, 1);
        _scrubTarget = TimeSpan.FromTicks((long)(_duration.Ticks * fraction));
        ProgressFill.Width = fraction * ProgressTrack.ActualWidth;
        if (ProgressThumb != null)
        {
            Canvas.SetLeft(ProgressThumb, Math.Clamp(ProgressFill.Width - 3, 0, Math.Max(0, ProgressTrack.ActualWidth - 6)));
            ProgressThumb.Opacity = 1.0;
        }
    }

    private void AnimateThumbScale(double targetScale)
    {
        if (ProgressThumbScale == null) return;
        var anim = new DoubleAnimation(targetScale, TimeSpan.FromMilliseconds(100))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ProgressThumbScale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        ProgressThumbScale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    private void Progress_MouseEnter(object sender, MouseEventArgs e)
    {
        ProgressFill.Background = _settings.DynamicAlbumColor ? _accentHoverBrush : SpotifyGreen;
        var anim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ProgressThumb.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    private void Progress_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isScrubbing) return;
        ProgressFill.Background = _settings.DynamicAlbumColor ? _accentBrush : _progressFillNormal;
        var anim = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ProgressThumb.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    private void DynamicColor_Click(object sender, RoutedEventArgs e)
    {
        _settings.DynamicAlbumColor = DynamicColorMenu.IsChecked;
        _settings.Save();
        if (!_settings.DynamicAlbumColor)
            SetAccentColor(SpotifyGreenColor);
        else
            UpdateDynamicColor(_lastArtBytes);

        UpdateProgressFillColor();
    }

    private void UpdateProgressFillColor()
    {
        if (ProgressTrack.IsMouseOver)
            ProgressFill.Background = _settings.DynamicAlbumColor ? _accentHoverBrush : SpotifyGreen;
        else
            ProgressFill.Background = _settings.DynamicAlbumColor ? _accentBrush : _progressFillNormal;
    }

    private static void AnimateElementPop(FrameworkElement element, double peakScale = 1.08)
    {
        var scale = new ScaleTransform(1.0, 1.0);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = scale;
        var anim = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        anim.KeyFrames.Add(new SplineDoubleKeyFrame(peakScale, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90)), new KeySpline(0.1, 0.9, 0.2, 1.0)));
        anim.KeyFrames.Add(new SplineDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200)), new KeySpline(0.1, 0.9, 0.2, 1.0)));
        anim.Completed += (_, _) =>
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            element.RenderTransform = Transform.Identity;
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    private void AnimateLikePop() => AnimateElementPop(LikeIcon, 1.15);

    private void UpdateDynamicColor(byte[]? bytes)
    {
        if (!_settings.DynamicAlbumColor || bytes == null || bytes.Length == 0)
        {
            SetAccentColor(SpotifyGreenColor);
            return;
        }

        Task.Run(() =>
        {
            try
            {
                Color c = ExtractVibrantColor(bytes);
                Dispatcher.InvokeAsync(() => SetAccentColor(c));
            }
            catch
            {
                Dispatcher.InvokeAsync(() => SetAccentColor(SpotifyGreenColor));
            }
        });
    }

    private static Color ExtractVibrantColor(byte[] bytes)
    {
        try
        {
            var bmp = new BitmapImage();
            using (var ms = new MemoryStream(bytes))
            {
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 24;
                bmp.StreamSource = ms;
                bmp.EndInit();
            }
            bmp.Freeze();

            var converted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            converted.Freeze();

            int width = converted.PixelWidth;
            int height = converted.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            converted.CopyPixels(pixels, stride, 0);

            Color bestColor = SpotifyGreenColor;
            double bestScore = -1;

            for (int y = 0; y < height; y += 2)
            {
                for (int x = 0; x < width; x += 2)
                {
                    int idx = y * stride + x * 4;
                    byte b = pixels[idx];
                    byte g = pixels[idx + 1];
                    byte r = pixels[idx + 2];

                    double rf = r / 255.0;
                    double gf = g / 255.0;
                    double bf = b / 255.0;

                    double max = Math.Max(rf, Math.Max(gf, bf));
                    double min = Math.Min(rf, Math.Min(gf, bf));
                    double l = (max + min) / 2.0;

                    if (l < 0.20 || l > 0.85) continue;

                    double delta = max - min;
                    double s = delta == 0 ? 0 : delta / (1.0 - Math.Abs(2.0 * l - 1.0));
                    if (s < 0.25) continue;

                    double score = s * 1.5 + (1.0 - Math.Abs(l - 0.55));
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestColor = Color.FromRgb(r, g, b);
                    }
                }
            }
            return bestColor;
        }
        catch
        {
            return SpotifyGreenColor;
        }
    }

    private static Color LightenColor(Color c, double factor = 0.35)
    {
        byte r = (byte)Math.Clamp(c.R + (255 - c.R) * factor, 0, 255);
        byte g = (byte)Math.Clamp(c.G + (255 - c.G) * factor, 0, 255);
        byte b = (byte)Math.Clamp(c.B + (255 - c.B) * factor, 0, 255);
        return Color.FromRgb(r, g, b);
    }

    private void SetAccentColor(Color target)
    {
        if (_currentAccentColor == target) return;
        Color oldHover = LightenColor(_currentAccentColor, 0.35);
        Color targetHover = LightenColor(target, 0.35);

        var anim = new ColorAnimation(_currentAccentColor, target, TimeSpan.FromMilliseconds(350))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var hoverAnim = new ColorAnimation(oldHover, targetHover, TimeSpan.FromMilliseconds(350))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        _currentAccentColor = target;
        _accentBrush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        _accentHoverBrush.BeginAnimation(SolidColorBrush.ColorProperty, hoverAnim);
    }

    private void Progress_MenuClick(object sender, RoutedEventArgs e)
    {
        _settings.ShowProgress = ProgressMenu.IsChecked;
        _settings.Save();
        UpdateProgressUi();
    }

    private void ShowLyrics_Click(object sender, RoutedEventArgs e)
    {
        bool oldShow = _settings.ShowLyrics;
        _settings.ShowLyrics = ShowLyricsMenu.IsChecked;
        _settings.Save();
        ApplySettingsUi();
        if (oldShow != _settings.ShowLyrics && _settings.UnifiedLyrics)
        {
            AnimateLyricsModeSwitch(toUnified: _settings.ShowLyrics);
        }
        else
        {
            UpdatePosition();
            UpdateLyricsUi();
        }
    }

    private void LyricsUnified_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.UnifiedLyrics && _settings.ShowLyrics) return;
        _settings.UnifiedLyrics = true;
        _settings.ShowLyrics = true;
        _settings.Save();
        ApplySettingsUi();
        AnimateLyricsModeSwitch(toUnified: true);
    }

    private void LyricsSeparate_Click(object sender, RoutedEventArgs e)
    {
        if (!_settings.UnifiedLyrics && _settings.ShowLyrics) return;
        _settings.UnifiedLyrics = false;
        _settings.ShowLyrics = true;
        _settings.Save();
        ApplySettingsUi();
        AnimateLyricsModeSwitch(toUnified: false);
    }

    private void LyricsAlign_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.Tag is string align)
        {
            _settings.LyricsAlignment = align;
            _settings.Save();
            ApplySettingsUi();
        }
    }

    private void EnsureLyricsWindow()
    {
        if (_lyricsWindow == null && !_closed)
        {
            _lyricsWindow = new LyricsWindow { TrayIndex = TrayIndex };
            _lyricsWindow.ApplySettings();
            new WindowInteropHelper(_lyricsWindow).EnsureHandle();
        }
    }

    private DateTime _lastLyricsFetchAttempt = DateTime.MinValue;

    private async Task LoadLyricsForTrackAsync(string title, string artist, TimeSpan duration)
    {
        string key = $"{artist} - {title}".Trim();
        if (string.IsNullOrEmpty(key) || key == " - ")
        {
            _currentLyrics = null;
            _lastLyricsTrackKey = "";
            UpdateLyricsUi();
            return;
        }

        if (_lastLyricsTrackKey == key && _currentLyrics != null) return;
        if (_lastLyricsTrackKey == key && (DateTime.UtcNow - _lastLyricsFetchAttempt).TotalSeconds < 5) return;

        _lastLyricsTrackKey = key;
        _lastLyricsFetchAttempt = DateTime.UtcNow;

        try
        {
            var lyrics = await LyricsService.GetLyricsAsync(title, artist, duration);
            if (_lastLyricsTrackKey == key)
            {
                _currentLyrics = lyrics;
                UpdateLyricsUi();
            }
        }
        catch
        {
            if (_lastLyricsTrackKey == key)
            {
                _currentLyrics = null;
                UpdateLyricsUi();
            }
        }
    }

    private bool _modeTransitioning;
    private static readonly IEasingFunction ModeEase = new CubicEase { EasingMode = EasingMode.EaseOut };

    private void AnimateLyricsModeSwitch(bool toUnified)
    {
        _modeTransitioning = true;
        const int OutMs = 180;
        const int InMs = 240;

        // Limpar animações anteriores para transição limpa
        StandardTextPanel.BeginAnimation(UIElement.OpacityProperty, null);
        StandardTextShift.BeginAnimation(TranslateTransform.YProperty, null);
        UnifiedTextPanel.BeginAnimation(UIElement.OpacityProperty, null);
        UnifiedTextShift.BeginAnimation(TranslateTransform.YProperty, null);

        if (toUnified)
        {
            _lyricsWindow?.FadeOutAndHide(200);

            // Pré-preencher o texto combinado antes de esbater para dentro
            string t = TitleText.Text ?? "";
            string a = ArtistText.Text ?? "";
            _lastUnifiedTitle = t;
            _lastUnifiedArtist = a;
            SetUnifiedInfoText(t, a);

            if (_currentLyrics == null)
            {
                SetUnifiedLyricLine(_isPlayingUi ? "♪" : "", 0, false, TimeSpan.Zero);
            }
            else if (_currentLyrics.IsInstrumental)
            {
                SetUnifiedLyricLine("♪ Instrumental", 0, false, TimeSpan.Zero);
            }
            else if (_currentLyrics.Lines.Count == 0)
            {
                SetUnifiedLyricLine(_isPlayingUi ? "♪" : "", 0, false, TimeSpan.Zero);
            }
            else
            {
                TimeSpan pos = GetCurrentLyricPosition();
                var lineInfo = _currentLyrics.GetLineInfoAt(pos);
                string text = string.IsNullOrWhiteSpace(lineInfo.Text) ? "♪" : lineInfo.Text;
                TimeSpan lineDuration = lineInfo.EndTime > lineInfo.StartTime ? (lineInfo.EndTime - lineInfo.StartTime) : TimeSpan.FromSeconds(4);
                SetUnifiedLyricLine(text, lineInfo.Progress, lineInfo.HasMatch, lineDuration);
            }

            // Ambos os painéis visíveis durante o crossfade
            StandardTextPanel.Visibility = Visibility.Visible;
            UnifiedTextPanel.Visibility = Visibility.Visible;

            UnifiedTextPanel.Opacity = 0;
            UnifiedTextShift.Y = 6;

            var outFade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(OutMs)) { EasingFunction = ModeEase };
            var outSlide = new DoubleAnimation(-6, TimeSpan.FromMilliseconds(OutMs)) { EasingFunction = ModeEase };
            outFade.Completed += (_, _) =>
            {
                StandardTextPanel.Visibility = Visibility.Collapsed;
                StandardTextPanel.BeginAnimation(UIElement.OpacityProperty, null);
                StandardTextShift.BeginAnimation(TranslateTransform.YProperty, null);
                StandardTextPanel.Opacity = 1;
                StandardTextShift.Y = 0;
            };

            var inFade = new DoubleAnimation(1, TimeSpan.FromMilliseconds(InMs)) { EasingFunction = ModeEase };
            var inSlide = new DoubleAnimation(0, TimeSpan.FromMilliseconds(InMs)) { EasingFunction = ModeEase };
            inFade.Completed += (_, _) =>
            {
                _modeTransitioning = false;
                UnifiedTextPanel.BeginAnimation(UIElement.OpacityProperty, null);
                UnifiedTextShift.BeginAnimation(TranslateTransform.YProperty, null);
                UnifiedTextPanel.Opacity = 1;
                UnifiedTextShift.Y = 0;
                UpdateUnifiedInfoMarquee();
            };

            StandardTextPanel.BeginAnimation(UIElement.OpacityProperty, outFade);
            StandardTextShift.BeginAnimation(TranslateTransform.YProperty, outSlide);

            UnifiedTextPanel.BeginAnimation(UIElement.OpacityProperty, inFade);
            UnifiedTextShift.BeginAnimation(TranslateTransform.YProperty, inSlide);
        }
        else
        {
            // Transição para modo separado (2 janelas)
            StandardTextPanel.Visibility = Visibility.Visible;
            UnifiedTextPanel.Visibility = Visibility.Visible;

            StandardTextPanel.Opacity = 0;
            StandardTextShift.Y = -6;

            var outFade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(OutMs)) { EasingFunction = ModeEase };
            var outSlide = new DoubleAnimation(6, TimeSpan.FromMilliseconds(OutMs)) { EasingFunction = ModeEase };
            outFade.Completed += (_, _) =>
            {
                UnifiedTextPanel.Visibility = Visibility.Collapsed;
                UnifiedTextPanel.BeginAnimation(UIElement.OpacityProperty, null);
                UnifiedTextShift.BeginAnimation(TranslateTransform.YProperty, null);
                UnifiedTextPanel.Opacity = 1;
                UnifiedTextShift.Y = 0;
            };

            var inFade = new DoubleAnimation(1, TimeSpan.FromMilliseconds(InMs)) { EasingFunction = ModeEase };
            var inSlide = new DoubleAnimation(0, TimeSpan.FromMilliseconds(InMs)) { EasingFunction = ModeEase };
            inFade.Completed += (_, _) =>
            {
                _modeTransitioning = false;
                StandardTextPanel.BeginAnimation(UIElement.OpacityProperty, null);
                StandardTextShift.BeginAnimation(TranslateTransform.YProperty, null);
                StandardTextPanel.Opacity = 1;
                StandardTextShift.Y = 0;
                UpdateMarquee();
            };

            UnifiedTextPanel.BeginAnimation(UIElement.OpacityProperty, outFade);
            UnifiedTextShift.BeginAnimation(TranslateTransform.YProperty, outSlide);

            StandardTextPanel.BeginAnimation(UIElement.OpacityProperty, inFade);
            StandardTextShift.BeginAnimation(TranslateTransform.YProperty, inSlide);
        }

        UpdatePosition();
        UpdateLyricsUi();
    }

    /// <summary>
    /// Đón đầu hiển thị lyric tự động (~250ms).
    /// Bù trừ thời gian animation chuyển dòng (240ms) và độ trễ phản xạ tự nhiên
    /// của các file LRC cộng đồng, giúp câu hát hiện rõ nét 100% đúng thời điểm ca sĩ cất giọng.
    /// </summary>
    private static readonly TimeSpan LyricLeadIn = TimeSpan.FromMilliseconds(250);

    private TimeSpan GetCurrentLyricPosition()
    {
        TimeSpan pos = _basePosition;
        if (_isPlayingUi)
            pos += DateTime.UtcNow - _basePositionAt;
        return pos + LyricLeadIn;
    }

    private void UpdateLyricsUi()
    {
        try
        {
            bool showLyrics = _settings.ShowLyrics && _spotifyPresent;

            if (!showLyrics || !_isPlayingUi || _currentLyrics == null || _currentLyrics.Lines.Count == 0)
            {
                if (_lyricsTimer.Interval.TotalMilliseconds < 800)
                    _lyricsTimer.Interval = TimeSpan.FromMilliseconds(1000);
            }
            else
            {
                if (_lyricsTimer.Interval.TotalMilliseconds > 300)
                    _lyricsTimer.Interval = TimeSpan.FromMilliseconds(250);
            }

            if (!showLyrics)
            {
                _lyricsWindow?.FadeOutAndHide();
                if (!_modeTransitioning && StandardTextPanel.Visibility != Visibility.Visible)
                {
                    StandardTextPanel.Visibility = Visibility.Visible;
                    StandardTextPanel.Opacity = 1;
                    StandardTextShift.Y = 0;
                    UnifiedTextPanel.Visibility = Visibility.Collapsed;
                    UpdateMarquee();
                }
                return;
            }

            if (_settings.UnifiedLyrics)
            {
                // Modo Combinado (1 Janela)
                _lyricsWindow?.FadeOutAndHide();

                if (!_modeTransitioning && StandardTextPanel.Visibility != Visibility.Collapsed)
                {
                    StandardTextPanel.Visibility = Visibility.Collapsed;
                    UnifiedTextPanel.Visibility = Visibility.Visible;
                    UnifiedTextPanel.Opacity = 1;
                    UnifiedTextShift.Y = 0;
                }

                // Linha 1: Título (branco) • Artista (cinzento suave)
                string t = TitleText.Text ?? "";
                string a = ArtistText.Text ?? "";
                if (t != _lastUnifiedTitle || a != _lastUnifiedArtist)
                {
                    _lastUnifiedTitle = t;
                    _lastUnifiedArtist = a;
                    SetUnifiedInfoText(t, a);
                    UpdateUnifiedInfoMarquee();
                }

                // Linha 2: Lời bài hát với cuộn nhịp thời gian thực
                if (_currentLyrics == null)
                {
                    SetUnifiedLyricLine(_isPlayingUi ? "♪" : "", 0, false, TimeSpan.Zero);
                    return;
                }

                if (_currentLyrics.IsInstrumental)
                {
                    SetUnifiedLyricLine("♪ Instrumental", 0, false, TimeSpan.Zero);
                    return;
                }

                if (_currentLyrics.Lines.Count == 0)
                {
                    SetUnifiedLyricLine(_isPlayingUi ? "♪" : "", 0, false, TimeSpan.Zero);
                    return;
                }

                TimeSpan pos = GetCurrentLyricPosition();

                var lineInfo = _currentLyrics.GetLineInfoAt(pos);
                string text = lineInfo.Text;
                if (string.IsNullOrWhiteSpace(text))
                    text = "♪";

                TimeSpan lineDuration = lineInfo.EndTime > lineInfo.StartTime ? (lineInfo.EndTime - lineInfo.StartTime) : TimeSpan.FromSeconds(4);
                SetUnifiedLyricLine(text, lineInfo.Progress, lineInfo.HasMatch, lineDuration);
            }
            else
            {
                // Modo Separado (2 Janelas)
                if (!_modeTransitioning && StandardTextPanel.Visibility != Visibility.Visible)
                {
                    StandardTextPanel.Visibility = Visibility.Visible;
                    StandardTextPanel.Opacity = 1;
                    StandardTextShift.Y = 0;
                    UnifiedTextPanel.Visibility = Visibility.Collapsed;
                    UpdateMarquee();
                }

                if (_currentLyrics == null)
                {
                    _lyricsWindow?.SetLyric(_isPlayingUi ? "♪" : "");
                    return;
                }

                if (_currentLyrics.IsInstrumental)
                {
                    _lyricsWindow?.SetLyric("♪ Instrumental");
                    return;
                }

                if (_currentLyrics.Lines.Count == 0)
                {
                    _lyricsWindow?.SetLyric(_isPlayingUi ? "♪" : "");
                    return;
                }

                TimeSpan pos = GetCurrentLyricPosition();

                string line = _currentLyrics.GetLineAt(pos);
                if (string.IsNullOrWhiteSpace(line))
                    line = "♪";

                _lyricsWindow?.SetLyric(line);
            }
        }
        catch (Exception ex)
        {
            Diag.Log($"[UpdateLyricsUi] {ex}");
        }
    }

    private static readonly SolidColorBrush ArtistDimBrush = new(Color.FromRgb(0xB3, 0xB3, 0xB3));
    private static readonly SolidColorBrush DotDimBrush = new(Color.FromRgb(0x80, 0x80, 0x80));
    private string _lastUnifiedTitle = "";
    private string _lastUnifiedArtist = "";

    private void SetUnifiedInfoText(string title, string artist)
    {
        if (UnifiedInfoText == null) return;
        UnifiedInfoText.Inlines.Clear();
        if (string.IsNullOrWhiteSpace(artist) || artist == L.NothingPlaying)
        {
            UnifiedInfoText.Inlines.Add(new System.Windows.Documents.Run(title)
            {
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold
            });
        }
        else
        {
            // Tên bài hát: màu trắng nổi bật
            UnifiedInfoText.Inlines.Add(new System.Windows.Documents.Run(title)
            {
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold
            });
            // Dấu chấm phân cách
            UnifiedInfoText.Inlines.Add(new System.Windows.Documents.Run(" • ")
            {
                Foreground = DotDimBrush,
                FontWeight = FontWeights.Normal
            });
            // Tên tác giả: màu xám dịu
            UnifiedInfoText.Inlines.Add(new System.Windows.Documents.Run(artist)
            {
                Foreground = ArtistDimBrush,
                FontWeight = FontWeights.Normal
            });
        }
    }

    private string _unifiedMarqueeKey = "";

    private void UpdateUnifiedInfoMarquee()
    {
        if (UnifiedInfoText == null || UnifiedInfoShift == null) return;
        double clipWidth = Math.Max(10, TextStack.Width);
        double dpi = 1.0;
        try { dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { }
        string fullText = string.Concat(UnifiedInfoText.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text));
        if (string.IsNullOrEmpty(fullText))
            fullText = UnifiedInfoText.Text ?? "";

        string key = $"{fullText}|{clipWidth:0}|{dpi:0.##}";
        if (key == _unifiedMarqueeKey) return;
        _unifiedMarqueeKey = key;

        var ft = new FormattedText(
            fullText,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(UnifiedInfoText.FontFamily, UnifiedInfoText.FontStyle, UnifiedInfoText.FontWeight, UnifiedInfoText.FontStretch),
            UnifiedInfoText.FontSize,
            Brushes.White,
            dpi);
        double textWidth = ft.Width;

        UnifiedInfoShift.BeginAnimation(TranslateTransform.XProperty, null);
        UnifiedInfoShift.X = 0;

        double overflow = textWidth - clipWidth;
        if (overflow > 1.0)
        {
            double scrollSeconds = Math.Max(1.5, overflow / 25.0);
            double end = -(overflow + 10);
            var anim = new DoubleAnimationUsingKeyFrames();
            double t = 2.0; // pausa inicial
            anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));
            t += scrollSeconds;
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(end, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));
            t += 1.5; // pausa no fim
            anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(end, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));
            t += 0.8; // regressar ao início e parar definitivamente (chạy 1 lần duy nhất)
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));
            anim.Duration = TimeSpan.FromSeconds(t);

            UnifiedInfoShift.BeginAnimation(TranslateTransform.XProperty, anim);
        }
    }

    private int _unifiedActiveLayer = 0;
    private string _currentUnifiedLyric = "";
    private double _lastLyricClipWidth = 0;
    private double _lastLyricOverflow = 0;
    private bool _isLyricScrollingActive = false;
    private static readonly IEasingFunction LyricTransitionEase = new CubicEase { EasingMode = EasingMode.EaseOut };

    private static double CalculateLyricScrollTarget(double overflow, double progress, bool hasMatch, bool isPlaying)
    {
        if (overflow <= 1.0 || !hasMatch || !isPlaying)
            return 0;

        double maxScroll = overflow + 6;
        if (progress <= 0.12)
            return 0;

        if (progress >= 0.88)
            return -maxScroll;

        double ratio = (progress - 0.12) / 0.76;
        return -maxScroll * ratio;
    }

    private static void StartContinuousLyricScroll(TranslateTransform transform, double overflow, double progress, TimeSpan lineDuration)
    {
        if (overflow <= 1.0 || lineDuration <= TimeSpan.Zero)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.X = 0;
            return;
        }

        double maxScroll = overflow + 6;
        if (progress >= 0.88)
            return;

        double totalSec = Math.Max(0.5, lineDuration.TotalSeconds);
        var anim = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd };

        if (progress < 0.12)
        {
            double delaySec = (0.12 - progress) * totalSec;
            double scrollSec = 0.76 * totalSec;
            anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(delaySec))));
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(-maxScroll, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(delaySec + scrollSec))));
        }
        else
        {
            double startX = -maxScroll * ((progress - 0.12) / 0.76);
            double remainingSec = (0.88 - progress) * totalSec;
            anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(startX, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(-maxScroll, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(Math.Max(0.05, remainingSec)))));
        }

        transform.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    private void SetUnifiedLyricLine(string text, double progress, bool hasMatch, TimeSpan lineDuration)
    {
        text = text?.Trim() ?? "";

        var currentTb = _unifiedActiveLayer == 0 ? UnifiedLyricText : UnifiedLyricTextTop;
        var currentTr = _unifiedActiveLayer == 0 ? UnifiedLyricShift : UnifiedLyricShiftTop;

        var nextTb = _unifiedActiveLayer == 0 ? UnifiedLyricTextTop : UnifiedLyricText;
        var nextTr = _unifiedActiveLayer == 0 ? UnifiedLyricShiftTop : UnifiedLyricShift;

        double clipWidth = Math.Max(10, TextStack.Width);

        // 1. Khi câu hát đổi mới
        if (_currentUnifiedLyric != text)
        {
            _currentUnifiedLyric = text;
            _lastLyricClipWidth = clipWidth;
            const int TransitionMs = 240;

            if (string.IsNullOrEmpty(text))
            {
                _isLyricScrollingActive = false;
                _lastLyricOverflow = 0;
                var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(TransitionMs)) { EasingFunction = LyricTransitionEase };
                currentTb.BeginAnimation(OpacityProperty, fadeOut);
                nextTb.BeginAnimation(OpacityProperty, null);
                nextTb.Opacity = 0;
                currentTr.BeginAnimation(TranslateTransform.XProperty, null);
                nextTr.BeginAnimation(TranslateTransform.XProperty, null);
                return;
            }

            // Đo độ dài câu hát
            double dpi = 1.0;
            try { dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { }

            var ft = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(nextTb.FontFamily, nextTb.FontStyle, nextTb.FontWeight, nextTb.FontStretch),
                nextTb.FontSize,
                Brushes.White,
                dpi);

            double textWidth = ft.Width;
            _lastLyricOverflow = Math.Max(0, textWidth - clipWidth);

            // Bắt đầu scroll animation mượt mà 120 FPS
            if (_isPlayingUi && _lastLyricOverflow > 1.0)
            {
                StartContinuousLyricScroll(nextTr, _lastLyricOverflow, progress, lineDuration);
                _isLyricScrollingActive = true;
            }
            else
            {
                _isLyricScrollingActive = false;
                double initialTargetX = CalculateLyricScrollTarget(_lastLyricOverflow, progress, hasMatch, _isPlayingUi);
                nextTr.BeginAnimation(TranslateTransform.XProperty, null);
                nextTr.X = initialTargetX;
            }

            // Crossfade & trượt Y giữa 2 layer (tự nhiên theo tần số quét 120Hz của màn hình)
            if (string.IsNullOrEmpty(currentTb.Text) || currentTb.Opacity < 0.05)
            {
                nextTb.Text = text;
                nextTr.BeginAnimation(TranslateTransform.YProperty, null);
                nextTr.Y = 0;
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(TransitionMs)) { EasingFunction = LyricTransitionEase };
                nextTb.BeginAnimation(OpacityProperty, fadeIn);
                currentTb.BeginAnimation(OpacityProperty, null);
                currentTb.Opacity = 0;
                _unifiedActiveLayer = 1 - _unifiedActiveLayer;
            }
            else
            {
                // Linha de saída: sobe ligeiramente (-4px) e esbate para fora
                var fadeOutAnim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(TransitionMs)) { EasingFunction = LyricTransitionEase };
                var slideOutAnim = new DoubleAnimation(-4, TimeSpan.FromMilliseconds(TransitionMs)) { EasingFunction = LyricTransitionEase };
                currentTb.BeginAnimation(OpacityProperty, fadeOutAnim);
                currentTr.BeginAnimation(TranslateTransform.YProperty, slideOutAnim);

                // Linha de entrada: sobe de +5px até 0px enquanto faz fade in
                nextTb.Text = text;
                nextTr.BeginAnimation(TranslateTransform.YProperty, null);
                nextTr.Y = 5;

                var fadeInAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(TransitionMs)) { EasingFunction = LyricTransitionEase };
                var slideInAnim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(TransitionMs)) { EasingFunction = LyricTransitionEase };

                nextTb.BeginAnimation(OpacityProperty, fadeInAnim);
                nextTr.BeginAnimation(TranslateTransform.YProperty, slideInAnim);

                _unifiedActiveLayer = 1 - _unifiedActiveLayer;
            }
            return;
        }

        // 2. Câu hát không đổi: liên tục kiểm tra và đồng bộ vị trí
        var activeTb = _unifiedActiveLayer == 0 ? UnifiedLyricText : UnifiedLyricTextTop;
        var activeTr = _unifiedActiveLayer == 0 ? UnifiedLyricShift : UnifiedLyricShiftTop;

        if (string.IsNullOrEmpty(text) || text == "♪" || !hasMatch || _lastLyricOverflow <= 1.0)
        {
            _isLyricScrollingActive = false;
            if (activeTr.X != 0)
            {
                activeTr.BeginAnimation(TranslateTransform.XProperty, null);
                activeTr.X = 0;
            }
            return;
        }

        // Cập nhật lại overflow nếu độ rộng widget thay đổi
        if (Math.Abs(clipWidth - _lastLyricClipWidth) > 3)
        {
            _lastLyricClipWidth = clipWidth;
            double dpi = 1.0;
            try { dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { }

            var ft = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(activeTb.FontFamily, activeTb.FontStyle, activeTb.FontWeight, activeTb.FontStretch),
                activeTb.FontSize,
                Brushes.White,
                dpi);

            _lastLyricOverflow = Math.Max(0, ft.Width - clipWidth);
            if (_isPlayingUi)
            {
                StartContinuousLyricScroll(activeTr, _lastLyricOverflow, progress, lineDuration);
                _isLyricScrollingActive = true;
            }
        }

        if (!_isPlayingUi)
        {
            // Tạm dừng: cố định vị trí hiện tại
            if (_isLyricScrollingActive)
            {
                _isLyricScrollingActive = false;
                double currentX = (double)activeTr.GetValue(TranslateTransform.XProperty);
                activeTr.BeginAnimation(TranslateTransform.XProperty, null);
                activeTr.X = currentX;
            }
            return;
        }

        if (!_isLyricScrollingActive)
        {
            // Vừa bấm Play tiếp tục phát
            _isLyricScrollingActive = true;
            StartContinuousLyricScroll(activeTr, _lastLyricOverflow, progress, lineDuration);
            return;
        }

        // Kiểm tra lệch nhịp / tua: chỉ tái kích hoạt animation nếu lệch quá 8 pixel
        double expectedX = CalculateLyricScrollTarget(_lastLyricOverflow, progress, hasMatch, _isPlayingUi);
        double curX = (double)activeTr.GetValue(TranslateTransform.XProperty);
        if (Math.Abs(curX - expectedX) > 8.0)
        {
            StartContinuousLyricScroll(activeTr, _lastLyricOverflow, progress, lineDuration);
        }
    }

    private void VolumePopup_Closed(object sender, EventArgs e)
    {
        _volPopupWatchdog?.Stop();
        RestoreForeground();
    }

    private DispatcherTimer? _volPopupWatchdog;

    // ---------- Preservação do foco ----------
    // O popup do volume e o menu de contexto ativam janelas próprias do WPF;
    // quando fecham, o foco pode ficar órfão e atalhos globais (PrintScreen,
    // Win+Shift+S) deixam de responder até se clicar noutra janela.

    private IntPtr _fgBeforeUi;

    private void Root_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        CaptureForeground();
        RebuildMonitorMenu();
        // Estado global — outra janela (ou o Gestor de Tarefas) pode tê-lo mudado
        if (!PackagedApp.IsPackaged)
            AutoStartMenu.IsChecked = IsAutoStartEnabled();
    }

    /// <summary>Um item POR CADA barra de tarefas existente, com seleção
    /// múltipla — cada monitor marcado tem a sua instância do widget
    /// (pedido da comunidade). Pelo menos um fica sempre marcado.</summary>
    private void RebuildMonitorMenu()
    {
        MonitorMenu.Items.Clear();
        int count = Interop.GetSecondaryTrays().Count;
        var monitors = _settings.Monitors;
        for (int i = 0; i <= count; i++)
        {
            int index = i;
            var item = new MenuItem
            {
                Header = i == 0 ? L.MonitorPrimary : L.MonitorN(i + 1),
                IsCheckable = true,
                IsChecked = monitors.Contains(i),
                StaysOpenOnClick = true, // marcar vários sem o menu fechar
            };
            item.Click += (s, _) =>
            {
                if (monitors.Contains(index))
                {
                    // Tem de sobrar pelo menos um monitor QUE EXISTA — entradas
                    // de monitores desligados não contam, senão a seleção podia
                    // ficar só com barras inexistentes e a app toda invisível
                    if (!monitors.Any(m => m != index && m <= count))
                    {
                        ((MenuItem)s).IsChecked = true;
                        return;
                    }
                    monitors.Remove(index);
                }
                else
                {
                    monitors.Add(index);
                    monitors.Sort();
                }
                _settings.Save();
                // Se esta própria janela vai ser removida, fechar o menu antes —
                // um menu StaysOpen órfão numa janela destruída fica pendurado
                if (!monitors.Contains(TrayIndex) && Root.ContextMenu is { IsOpen: true } cm)
                    cm.IsOpen = false;
                SyncToMonitors();
            };
            MonitorMenu.Items.Add(item);
        }
        if (count == 0)
        {
            // Sem barras secundárias o Windows não tem onde ancorar o widget
            // noutro ecrã — explicar como ativar em vez de esconder o menu
            // (utilizadores achavam que a funcionalidade não existia, issue #11)
            MonitorMenu.Items.Add(new MenuItem
            {
                Header = L.MonitorHint,
                IsEnabled = false,
            });
        }
    }

    private void ContextMenu_Closed(object sender, RoutedEventArgs e) => RestoreForeground();

    private void CaptureForeground()
    {
        IntPtr fg = Interop.GetForegroundWindow();
        _fgBeforeUi = fg == _hwnd ? IntPtr.Zero : fg;
    }

    private void RestoreForeground()
    {
        if (_fgBeforeUi == IntPtr.Zero) return;
        IntPtr target = _fgBeforeUi;
        _fgBeforeUi = IntPtr.Zero;
        if (Interop.GetForegroundWindow() != target)
            Interop.SetForegroundWindow(target);
    }

    // ---------- Mover (só quando ativado no menu) / clique para abrir o Spotify ----------

    private enum DragAction { None, Move, ResizeLeft, ResizeRight }
    private DragAction _dragAction = DragAction.None;
    private double _dragStartTextWidth;

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_moveMode)
        {
            Point p = e.GetPosition(Root);
            if (p.X <= 14)
                _dragAction = DragAction.ResizeLeft;
            else if (p.X >= Root.ActualWidth - 14)
                _dragAction = DragAction.ResizeRight;
            else
                _dragAction = DragAction.Move;

            _dragging = true;
            _dragMoved = false;
            _dragStartLeft = Left;
            _dragStartTextWidth = TextStack.Width;
            _dragStartScreen = PointToScreen(e.GetPosition(this));
            Root.CaptureMouse();
        }
        else
        {
            _pressed = true;
        }
    }

    private void Root_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            if (_moveMode)
            {
                Point p = e.GetPosition(Root);
                if (p.X <= 14 || p.X >= Root.ActualWidth - 14)
                    Root.Cursor = Cursors.SizeWE;
                else
                    Root.Cursor = Cursors.SizeAll;
            }
            return;
        }

        Point cur = PointToScreen(e.GetPosition(this));
        double dxDevice = cur.X - _dragStartScreen.X;

        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget == null) return;
        double dx = source.CompositionTarget.TransformFromDevice.Transform(new Vector(dxDevice, 0)).X;

        if (Math.Abs(dx) > 3) _dragMoved = true;
        if (!_dragMoved) return;

        double scale = Math.Max(0.1, _settings.Scale);

        if (_dragAction == DragAction.Move)
        {
            Left = _dragStartLeft + dx;
        }
        else if (_dragAction == DragAction.ResizeRight)
        {
            double dWidth = dx / scale;
            double newWidth = Math.Max(MinTextWidth, _dragStartTextWidth + dWidth);
            if (Math.Abs(TextStack.Width - newWidth) > 0.5)
            {
                TextStack.Width = newWidth;
                UpdateMarquee();
                UpdateUnifiedInfoMarquee();
            }
        }
        else if (_dragAction == DragAction.ResizeLeft)
        {
            double dWidth = -dx / scale;
            double newWidth = Math.Max(MinTextWidth, _dragStartTextWidth + dWidth);
            if (Math.Abs(TextStack.Width - newWidth) > 0.5)
            {
                TextStack.Width = newWidth;
                Left = _dragStartLeft - (newWidth - _dragStartTextWidth) * scale;
                UpdateMarquee();
                UpdateUnifiedInfoMarquee();
            }
        }
    }

    private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            _dragAction = DragAction.None;
            Root.ReleaseMouseCapture();
            if (_dragMoved)
            {
                UpdateLayout();
                IntPtr tray = GetTargetTray();
                if (tray != IntPtr.Zero && Interop.GetWindowRect(tray, out var r) && Interop.GetWindowRect(_hwnd, out var w))
                {
                    bool isLeft = IsTaskbarLeftAligned();
                    double? widgetsRightPx, startLeftPx, taskEndPx;
                    lock (_anchorLock)
                    {
                        widgetsRightPx = _widgetsRightPx;
                        startLeftPx = _startLeftPx;
                        taskEndPx = _taskEndPx;
                    }

                    int slotLeft, slotRight;
                    bool isRightSlot = false;
                    if (!isLeft)
                    {
                        double? effStart = startLeftPx ?? _lastValidStartLeft;
                        int centerDivider = effStart.HasValue 
                            ? (int)effStart.Value 
                            : (r.Left + (r.Right - r.Left) / 2);
                        if (w.Left < centerDivider)
                        {
                            isRightSlot = false;
                            slotLeft = widgetsRightPx.HasValue ? (int)widgetsRightPx.Value + 8 : r.Left + 12;
                            slotRight = effStart.HasValue ? (int)effStart.Value - 8 : r.Left + (r.Right - r.Left) / 2 - 8;
                        }
                        else
                        {
                            isRightSlot = true;
                            int? notifyLeftPx = Interop.GetTrayNotifyLeft(tray);
                            slotLeft = taskEndPx.HasValue ? (int)taskEndPx.Value + 8 : r.Left + (r.Right - r.Left) / 2 + 100;
                            slotRight = (notifyLeftPx ?? (r.Right - 220)) - 8;
                        }
                    }
                    else
                    {
                        int? notifyLeftPx = Interop.GetTrayNotifyLeft(tray);
                        slotLeft = taskEndPx.HasValue ? (int)taskEndPx.Value + 8 : r.Left + 300;
                        slotRight = (notifyLeftPx ?? (r.Right - 220)) - 8;
                    }

                    double gapLeft = Math.Max(0, w.Left - slotLeft);
                    double gapRight = Math.Max(0, slotRight - w.Right);
                    var sm = new SlotMargins
                    {
                        GapLeft = gapLeft,
                        GapRight = gapRight,
                        IsRightSlot = isRightSlot,
                        CustomTextWidth = TextStack.Width,
                        LastStartLeft = (!isLeft && (startLeftPx ?? _lastValidStartLeft).HasValue) ? (startLeftPx ?? _lastValidStartLeft)!.Value : 0
                    };

                    var marginsDict = isLeft ? _settings.MarginsLeft : _settings.MarginsCenter;
                    marginsDict[TrayIndex] = sm;

                    var manualDict = isLeft ? _settings.ManualXLeft : _settings.ManualX;
                    manualDict[TrayIndex] = w.Left;

                    _settings.AutoPosition = false;
                    _settings.Save();
                }
            }
        }
        else if (_pressed)
        {
            _pressed = false;
            SpotifyActions.OpenSpotifyWindow();
        }
    }

    // ---------- Menu de contexto ----------

    private void MoveMode_Click(object sender, RoutedEventArgs e)
    {
        _moveMode = MoveMenu.IsChecked;
        ContentPanel.IsHitTestVisible = !_moveMode;
        Root.Cursor = _moveMode ? Cursors.SizeAll : Cursors.Hand;
    }

    private void ResetPos_Click(object sender, RoutedEventArgs e)
    {
        _settings.AutoPosition = true;
        _settings.ManualX.Clear();
        _settings.ManualXLeft.Clear();
        _settings.MarginsCenter.Clear();
        _settings.MarginsLeft.Clear();
        _settings.Save();
        MoveMenu.IsChecked = false;
        _moveMode = false;
        ContentPanel.IsHitTestVisible = true;
        Root.Cursor = Cursors.Hand;
        UpdatePosition();
    }

    private void ApplyScale() => Root.LayoutTransform = new ScaleTransform(_settings.Scale, _settings.Scale);

    private void ApplyOpacity() => Root.Opacity = _settings.Opacity;

    private void Buttons_Click(object sender, RoutedEventArgs e)
    {
        _settings.ShowPlay = BtnPlayMenu.IsChecked;
        _settings.ShowLike = BtnLikeMenu.IsChecked;
        _settings.ShowShuffle = BtnShuffleMenu.IsChecked;
        _settings.ShowPrev = BtnPrevMenu.IsChecked;
        _settings.ShowNext = BtnNextMenu.IsChecked;
        _settings.ShowRepeat = BtnRepeatMenu.IsChecked;
        _settings.ShowVolume = BtnVolumeMenu.IsChecked;
        _settings.Save();
        UpdatePosition();
    }

    private const string StartupTaskId = "SpotifyTaskbarWidgetStartup";

    private async Task InitStartupTaskStateAsync()
    {
        try
        {
            var task = await Windows.ApplicationModel.StartupTask.GetAsync(StartupTaskId);
            AutoStartMenu.IsChecked = task.State is Windows.ApplicationModel.StartupTaskState.Enabled
                or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
        }
        catch { }
    }

    private async void AutoStart_Click(object sender, RoutedEventArgs e)
    {
        if (PackagedApp.IsPackaged)
        {
            try
            {
                var task = await Windows.ApplicationModel.StartupTask.GetAsync(StartupTaskId);
                if (AutoStartMenu.IsChecked)
                {
                    var state = await task.RequestEnableAsync();
                    AutoStartMenu.IsChecked = state == Windows.ApplicationModel.StartupTaskState.Enabled;
                }
                else
                {
                    task.Disable();
                }
            }
            catch { }
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (AutoStartMenu.IsChecked)
                key.SetValue(RunValueName, $"\"{Environment.ProcessPath}\"");
            else
                key.DeleteValue(RunValueName, false);
        }
        catch { }
    }

    private static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(RunValueName) != null;
        }
        catch
        {
            return false;
        }
    }


    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        App.IntentionalExit = true;
        Application.Current.Shutdown();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _closed = true; // trava a continuação do OnLoaded se ainda estiver no await
        _positionTimer.Stop();
        _trackTimer.Stop();
        _lyricsTimer.Stop();
        if (_lyricsWindow != null)
        {
            _lyricsWindow.ClosedByApp = true;
            _lyricsWindow.Close();
            _lyricsWindow = null;
        }
        CancelRide(); // o tick do ride não pode continuar num hwnd morto
        if (_mediaChanged != null) _media.Changed -= _mediaChanged;
        if (_mediaTimeline != null) _media.TimelineChanged -= _mediaTimeline;
        _media.Shutdown(); // solta as subscrições WinRT que prendiam a janela
        WidgetSettings.Changed -= OnSettingsChanged;
        Instances.Remove(this);
        if (!App.IntentionalExit && !ClosedByApp && !_recreatePending)
        {
            // Explorer reiniciou e levou as janelas (são owned pelas barras):
            // um só waiter recria o conjunto todo quando a barra voltar
            _recreatePending = true;
            _ = RecreateAfterTaskbarRestartAsync();
        }
    }

    /// <summary>Como o widget é owned pela taskbar, um reinício do Explorer
    /// destrói a(s) janela(s) — esperar pela barra nova e recriar o conjunto.</summary>
    private static async Task RecreateAfterTaskbarRestartAsync()
    {
        try
        {
            for (int i = 0; i < 120; i++)
            {
                await Task.Delay(1000);
                if (Interop.FindWindow("Shell_TrayWnd", null) != IntPtr.Zero)
                {
                    await Task.Delay(2000); // deixar a barra (e as secundárias) assentar
                    SyncToMonitors();
                    return;
                }
            }
            App.IntentionalExit = true;
            Application.Current.Shutdown();
        }
        finally
        {
            _recreatePending = false;
        }
    }
}
