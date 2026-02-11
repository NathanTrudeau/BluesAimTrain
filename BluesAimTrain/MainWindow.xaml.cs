using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using IOPath = System.IO.Path;
using System.Windows.Media.Imaging;
using BluesShared;

namespace BluesAimTrain
{
    // =========================================================
    // Argument Parsing & Logging
    // =========================================================
    public static class AppBootstrap
    {
        public static string? ProfileDirOverride { get; private set; }
        public static string LogDir { get; private set; } = "";
        public static string LogPath { get; private set; } = "";

        public static void Init(string[] args)
        {
            var parsed = ParseArgs(args);

            // profileDir override
            if (parsed.TryGetValue("profileDir", out var dir) && !string.IsNullOrWhiteSpace(dir))
            {
                ProfileDirOverride = dir.Trim().Trim('"');
            }

            // Log location: default under master app folder
            // If profileDir override exists, keep logs next to it for debugging.
            var baseDir = ProfileDirOverride ?? GetDefaultBluesBarAppDataDir();
            LogDir = IOPath.Combine(baseDir, "logs");
            Directory.CreateDirectory(LogDir);

            LogPath = IOPath.Combine(LogDir, "aimtrain.log");

            Log($"=== START AimTrain {GetVersionString()} ===");
            Log($"Args: {string.Join(" ", args ?? Array.Empty<string>())}");
            Log($"ProfileDirOverride: {ProfileDirOverride ?? "(none)"}");
            Log($"LogPath: {LogPath}");

            // Catch crashes so beta debugging is not a séance
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                try { Log("UnhandledException: " + e.ExceptionObject); } catch { }
            };
        }

        public static void Log(string msg)
        {
            try
            {
                var line = $"[{DateTime.UtcNow:O}] {msg}{Environment.NewLine}";
                File.AppendAllText(LogPath, line, Encoding.UTF8);
                Debug.WriteLine(line);
            }
            catch
            {
                // swallow logging errors
            }
        }

        private static string GetDefaultBluesBarAppDataDir()
        {
            return IOPath.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BluesBar");
        }

        private static string GetVersionString()
        {
            try
            {
                var asm = typeof(AppBootstrap).Assembly;
                var v = asm.GetName().Version?.ToString() ?? "?.?.?";
                return $"v{v}";
            }
            catch
            {
                return "v?.?.?";
            }
        }

        private static Dictionary<string, string> ParseArgs(string[] args)
        {
            // Supports: --key value, --key="value", --key=value
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (args == null) return dict;

            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i] ?? "";
                if (!a.StartsWith("--")) continue;

                var raw = a.Substring(2);

                string key;
                string value;

                int eq = raw.IndexOf('=');
                if (eq >= 0)
                {
                    key = raw.Substring(0, eq).Trim();
                    value = raw.Substring(eq + 1).Trim().Trim('"');
                }
                else
                {
                    key = raw.Trim();
                    if (i + 1 < args.Length && !(args[i + 1] ?? "").StartsWith("--"))
                    {
                        value = (args[i + 1] ?? "").Trim().Trim('"');
                        i++;
                    }
                    else
                    {
                        value = "true";
                    }
                }

                if (!string.IsNullOrWhiteSpace(key))
                    dict[key] = value;
            }

            return dict;
        }
    }
    // =========================================================
    // STATS SCENE RECORDING
    // =========================================================
    public static class HeatmapThumbRenderer
    {
        public static BitmapSource RenderCircular(HeatmapSnapshot h, int sizePx = 96)
        {
            int w = Math.Max(1, h.W);
            int hh = Math.Max(1, h.H);
            var counts = h.Counts ?? Array.Empty<int>();

            // find max
            int max = 1;
            for (int i = 0; i < counts.Length; i++)
                if (counts[i] > max) max = counts[i];

            var bmp = new WriteableBitmap(sizePx, sizePx, 96, 96, PixelFormats.Bgra32, null);
            int stride = sizePx * 4;
            byte[] px = new byte[sizePx * stride];

            // map pixels -> grid cell
            for (int py = 0; py < sizePx; py++)
            {
                double ny = (py / (double)(sizePx - 1)) * 2 - 1;
                for (int pxX = 0; pxX < sizePx; pxX++)
                {
                    double nx = (pxX / (double)(sizePx - 1)) * 2 - 1;

                    // circular mask
                    if (nx * nx + ny * ny > 1.0)
                        continue;

                    int gx = (int)Math.Floor((nx * 0.5 + 0.5) * (w - 1));
                    int gy = (int)Math.Floor((ny * 0.5 + 0.5) * (hh - 1));
                    int idx = gy * w + gx;

                    int c = (idx >= 0 && idx < counts.Length) ? counts[idx] : 0;

                    // intensity 0..255
                    byte t = (byte)(255.0 * c / max);

                    // Gold-ish heat (subtle -> bright)
                    // BGRA
                    byte b = (byte)(20 + (t * 40 / 255));
                    byte g = (byte)(40 + (t * 140 / 255));
                    byte r = (byte)(80 + (t * 175 / 255));

                    int off = py * stride + pxX * 4;
                    px[off + 0] = b;
                    px[off + 1] = g;
                    px[off + 2] = r;
                    px[off + 3] = 255;
                }
            }

            bmp.WritePixels(new System.Windows.Int32Rect(0, 0, sizePx, sizePx), px, stride, 0);
            return bmp;
        }
    }
    public sealed class ChallengeRunEntry
    {
        public DateTime Utc { get; set; } = DateTime.UtcNow;

        public string ModeId { get; set; } = "Unknown";      // e.g. "Flick_60s", "Tracking_30s"
        public string ModeName { get; set; } = "Unknown";

        public int Score { get; set; }
        public int Hits { get; set; }
        public int Misses { get; set; }
        public double Accuracy { get; set; }

        public long CoinsAwarded { get; set; }               // total coins earned this run

        // Heatmap for this run
        public HeatmapSnapshot Heatmap { get; set; } = new HeatmapSnapshot();
    }

    public sealed class HeatmapSnapshot
    {
        public int W { get; set; } = 16;                     // grid width
        public int H { get; set; } = 9;                      // grid height

        // Flattened row-major counts: index = y*W + x
        public int[] Counts { get; set; } = Array.Empty<int>();
    }
    public sealed class CircularHeatmapRecorder
    {
        private readonly int _n;          // grid size (NxN)
        private readonly float[] _accum;  // flattened, row-major

        // Blob radius in grid cells (tweak to match your free-mode look)
        private readonly int _splatRadius;

        public CircularHeatmapRecorder(int gridSize = 64, int splatRadius = 4)
        {
            _n = Math.Max(8, gridSize);
            _splatRadius = Math.Max(1, splatRadius);
            _accum = new float[_n * _n];
        }

        public int N => _n;

        public void Clear() => Array.Clear(_accum, 0, _accum.Length);

        /// <summary>
        /// Record a HIT at normalized coords inside the target circle:
        /// nx,ny in [-1..1] where (0,0) is target center and radius=1.
        /// </summary>
        public void RecordHitNormalized(double nx, double ny)
        {
            // ignore anything outside the circle
            double r2 = nx * nx + ny * ny;
            if (r2 > 1.0) return;

            // Map [-1..1] to grid [0..N-1]
            int cx = (int)Math.Round((nx * 0.5 + 0.5) * (_n - 1));
            int cy = (int)Math.Round((ny * 0.5 + 0.5) * (_n - 1));

            // Splat a small gaussian-ish blob
            int r = _splatRadius;
            float invSigma2 = 1.0f / (r * r * 0.5f); // tweak softness

            for (int y = cy - r; y <= cy + r; y++)
            {
                if ((uint)y >= (uint)_n) continue;
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if ((uint)x >= (uint)_n) continue;

                    // circle mask in grid space (keeps heatmap circular)
                    double gx = (x / (double)(_n - 1)) * 2 - 1;
                    double gy = (y / (double)(_n - 1)) * 2 - 1;
                    if (gx * gx + gy * gy > 1.0) continue;

                    int dx = x - cx;
                    int dy = y - cy;
                    float d2 = dx * dx + dy * dy;

                    float w = (float)Math.Exp(-d2 * invSigma2);
                    _accum[y * _n + x] += w;
                }
            }
        }

        /// <summary>
        /// Snapshot for saving: scale to ints to keep JSON small/stable.
        /// </summary>
        public int[] SnapshotCounts(int scale = 1000)
        {
            int[] outArr = new int[_accum.Length];

            // Find max to normalize (optional). If you want raw density, remove max scaling.
            float max = 1e-6f;
            for (int i = 0; i < _accum.Length; i++)
                if (_accum[i] > max) max = _accum[i];

            for (int i = 0; i < _accum.Length; i++)
                outArr[i] = (int)Math.Round((_accum[i] / max) * scale);

            return outArr;
        }
    }

    public partial class MainWindow : Window
    {
        // =========================================================
        // TYPES (ready to split)
        // =========================================================
        private enum Scene { MainMenu, FreeRun, Challenge, Stats }

        private sealed class ShotRecord
        {
            public double OffsetX;
            public double OffsetY;
            public bool Hit;
        }

        private sealed class RunMetrics
        {
            public int Hits;
            public int Misses;

            public double OverallAcc01;   // 0..1
            public double InTargetAcc01;  // 0..1 (distance-based)
            public double AvgDistPx;      // avg distance from center on hits
        }

        private sealed class ChallengeRunRecord
        {
            public DateTime When { get; set; }
            public string Mode { get; set; } = "";
            public string Setting { get; set; } = "";      // "30" / "60" / "100" or "30s" in tracking
            public int Targets { get; set; }
            public int Hits { get; set; }
            public int Misses { get; set; }
            public double Seconds { get; set; }
            public double Pace { get; set; }              // sec/target (or sec per kill)
            public double Acc { get; set; }               // 0..1
            public double InTgt { get; set; }             // 0..1
            public double AvgDist { get; set; }
            public int Score { get; set; }
            public int Coins { get; set; }
            public HeatmapSnapshot Heatmap { get; set; } = new HeatmapSnapshot();
            public ImageSource? HeatmapImage { get; set; }


            // convenience strings for display
            public string WhenText => When.ToString("MM-dd  HH:mm");
            public string AccText => $"{Acc * 100:F1}%";
            public string InTgtText => $"{InTgt * 100:F1}%";
            public string SecondsText => $"{Seconds:F2}s";
            public string PaceText => (Mode == "Tracking") ? "" : $"{Pace:F2}s/t";
            public bool Acc80Plus => Acc >= 0.80;
            public bool InTgt80Plus => InTgt >= 0.80;
        }

        private enum ChallengeType { Speed, Accuracy, Tracking }
        private enum ChallengeColorChoice { Red, Yellow, Green }

        private struct ScoreBreakdown
        {
            public int BaseMax;
            public int BaseScore;

            // bonuses shown on stat lines
            public int BonusTimeOrSpeed;     // time and/or speed reward
            public int BonusPace;            // pace-specific bonus
            public int BonusMisses;          // misses bonus (low misses)
            public int BonusOverallAcc;      // overall accuracy bonus
            public int BonusInTargetAcc;     // in-target bonus
            public int BonusAvgDist;         // avg distance bonus

            public int FinalScore => Math.Max(0,
                BaseScore +
                BonusTimeOrSpeed +
                BonusPace +
                BonusMisses +
                BonusOverallAcc +
                BonusInTargetAcc +
                BonusAvgDist
            );
        }

        // =========================================================
        // CORE STATE / SERVICES (ready to split)
        // =========================================================
        private readonly Random rng = new Random();
        private readonly DispatcherTimer spawnTimer = new DispatcherTimer();
        private readonly List<DispatcherTimer> _activeDespawnTimers = new();

        private Cursor _customCursor;
        private Scene _currentScene = Scene.MainMenu;

        // =========================================================
        // FREE RUN STATE (bug-fixed + clean)
        // =========================================================
        private bool _freeRunActive = false;

        private readonly List<ShotRecord> _freeRunHitShots = new(); // hits only (offsets)
        private RunMetrics _freeRunCurrent = new RunMetrics();
        private RunMetrics _freeRunLast = new RunMetrics();
        private List<ShotRecord> _freeRunLastHitShots = new();

        // Free run settings
        private Color targetColor = Colors.Aquamarine;
        private string targetColorName = "Aquamarine";
        private double targetSize = 50;
        private int spawnDelay = 800;
        private int targetLifetime = 1000;
        private bool showCenterMark = true;

        // =========================================================
        // CHALLENGE STATE
        // =========================================================
        private CircularHeatmapRecorder? _challengeHitHeatmap;   // circular, hits-only, per run
        private HeatmapSnapshot? _challengeHeatmapSnapshot;      // frozen snapshot for this run (for saving later)

        private bool _challengeScorecardVisible = false;

        private bool challengeModeActive = false;
        private bool challengeArmed = false;
        private bool challengeCompleted = false;
        private bool challengeInputLocked = false;

        private int ChallengeTotalTargets = 30;
        private int challengeHits = 0;
        private int challengeTargetsRemaining;
        private int challengeTargetsSpawned = 0;

        private DateTime challengeStartTime;

        private readonly Dictionary<Ellipse, Ellipse> challengeTargetToDot = new();
        private readonly List<ShotRecord> ChallengeHits_List = new();

        private ChallengeType _challengeType = ChallengeType.Speed;
        private int _challengeTargetCount = 30;
        private ChallengeColorChoice _challengeColor = ChallengeColorChoice.Green;

        private const double ChallengeSpawnPadding = 2;
        private const int ChallengeSpawnAttempts = 150;

        // Scorecard UI layout
        private const double ScorecardRowWidth = 420;
        private const double ColLabel = 110;
        private const double ColValue = 220;
        private const double ColBonus = 90;

        private static readonly Brush BrushDim = new SolidColorBrush(Color.FromRgb(200, 200, 200));

        private readonly System.Collections.ObjectModel.ObservableCollection<ChallengeRunRecord> _challengeRuns
            = new System.Collections.ObjectModel.ObservableCollection<ChallengeRunRecord>();

        // =========================================================
        // TRACKING MODE
        // =========================================================
        private readonly Dictionary<Ellipse, System.Windows.Shapes.Path> _trackingHealthArc = new();

        private readonly DispatcherTimer _trackingMoveTimer = new DispatcherTimer();
        private readonly DispatcherTimer _trackingCountdownTimer = new DispatcherTimer();

        private double _trackingSecondsRemaining = 0;
        private int _trackingDurationSeconds = 30;
        private const double TrackingKillSeconds = 0.9;

        private Point _lastMousePos = new Point(-9999, -9999);
        private bool _mouseInsideCanvas = false;

        private readonly Dictionary<Ellipse, Vector> _trackingVel = new();
        private readonly Dictionary<Ellipse, double> _trackingDwellSeconds = new();

        // =========================================================
        // PROFILE SYNC
        // =========================================================

        private ProfileSync _profileSync = null!;
        private long _coinsCached = 0;
        int _coinsAwarded = 0;

        // =========================================================
        // MENU WINDOW LOCK
        // =========================================================
        private const double MenuWidth = 1000;
        private const double MenuHeight = 660;

        private const double GameDefaultWidth = 1200;
        private const double GameDefaultHeight = 800;

        private bool _enteredGameFromMenu = false;

        // =========================================================
        // INIT
        // =========================================================
        public MainWindow()
        {
            InitializeComponent();
            Loaded += (_, __) => InitProfileCoinSync();
            UpdateCoinsUI();

//#if !DEBUG
//            BtnDev4.Visibility = Visibility.Collapsed;
//#endif

            // Data
            ChallengeHistoryList.ItemsSource = _challengeRuns;
            DataContext = this;
         
            // Cursor
            _customCursor = new Cursor(System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "Assets", "blue_crosshair_default.cur"));

            // Challenge initial count
            challengeTargetsRemaining = ChallengeTotalTargets;

            // Timers & events
            InitializeTimers();
            InitializeEventHandlers();
            InitializePresets();
            InitializeColors();

            StartupMessage.Visibility = Visibility.Collapsed;
            SetScene(Scene.MainMenu);
        }

        private void InitializeTimers()
        {
            // FreeRun spawn timer
            spawnTimer.Interval = TimeSpan.FromMilliseconds(spawnDelay);
            spawnTimer.Tick += SpawnFreeRunTarget;

            // Tracking
            _trackingMoveTimer.Interval = TimeSpan.FromMilliseconds(16);
            _trackingMoveTimer.Tick += TrackingMoveTimer_Tick;

            _trackingCountdownTimer.Interval = TimeSpan.FromMilliseconds(100);
            _trackingCountdownTimer.Tick += TrackingCountdownTimer_Tick;
        }

        private void InitializeEventHandlers()
        {
            PreviewKeyDown += MainWindow_PreviewKeyDown;

            ResetButton.Click += ResetButton_Click;

            CenterToggleButton.Checked += (_, _) => ToggleCenter(true);
            CenterToggleButton.Unchecked += (_, _) => ToggleCenter(false);

            GameCanvas.MouseLeftButtonDown += GameCanvas_MouseLeftButtonDown;
            GameCanvas.MouseMove += GameCanvas_MouseMove;
            GameCanvas.MouseEnter += (_, _) => _mouseInsideCanvas = true;
            GameCanvas.MouseLeave += (_, _) => _mouseInsideCanvas = false;
        }

        // =========================================================
        // SCENE NAVIGATION
        // =========================================================
        private void BtnFreerun_Click(object sender, RoutedEventArgs e) => EnterFreeRun();
        private void BtnChallenge_Click(object sender, RoutedEventArgs e) => EnterChallenge();
        private void BtnStats_Click(object sender, RoutedEventArgs e) => EnterStats();
        private void MenuButton_Click(object sender, RoutedEventArgs e) => GoToMainMenu();

        private void SetScene(Scene scene)
        {
            bool wasMenu = (_currentScene == Scene.MainMenu);
            _currentScene = scene;

            Scene_MainMenu.Visibility = (scene == Scene.MainMenu) ? Visibility.Visible : Visibility.Collapsed;
            Scene_Stats.Visibility = (scene == Scene.Stats) ? Visibility.Visible : Visibility.Collapsed;

            Scene_GameHost.Visibility =
                (scene == Scene.FreeRun || scene == Scene.Challenge)
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (scene == Scene.MainMenu)
            {
                LockWindowForMenu();
                _enteredGameFromMenu = false;
            }
            else
            {
                UnlockWindowFromMenu();
                _enteredGameFromMenu = wasMenu;
                if (_enteredGameFromMenu)
                    ApplyDefaultGameWindowSize();
            }
        }

        private void EnterFreeRun()
        {
            StopAllActivity();
            PrepareFreeRunDefaults();

            ShowFreeRunOverlay();
            SetScene(Scene.FreeRun);
        }

        private void EnterChallenge()
        {
            StopAllActivity();
            SetScene(Scene.Challenge);

            HideAllPauseMenus();
            ChallengePauseOverlayBorder.Visibility = Visibility.Visible;
            StartupMessage.Visibility = Visibility.Collapsed;

            RefreshChallengeMenuVisuals();

            challengeArmed = false;
            challengeModeActive = false;
        }

        private void EnterStats()
        {
            StopAllActivity();
            HideAllPauseMenus();
            StartupMessage.Visibility = Visibility.Collapsed;
            SetScene(Scene.Stats);
        }

        private void GoToMainMenu()
        {
            StopAllActivity();
            HideAllPauseMenus();
            StartupMessage.Visibility = Visibility.Collapsed;
            SetScene(Scene.MainMenu);
        }

        // =========================================================
        // INPUT (ESC handling)
        // =========================================================
        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape && e.Key != Key.Space) return;

            if (e.Key == Key.Escape)
            {
                if (_currentScene == Scene.FreeRun)
                {
                    if (PauseOverlayBorder.Visibility == Visibility.Visible)
                    {
                        HideAllPauseMenus();
                        StartupMessage.Visibility = Visibility.Collapsed;
                        StartFreeRunSpawning();
                    }
                    else
                    {
                        SnapshotFreeRunToLastRun();
                        StopFreeRunSpawning();
                        ShowFreeRunOverlay();
                    }

                    e.Handled = true;
                    return;
                }

                if (_currentScene == Scene.Challenge)
                {
                    bool menuOpen = (ChallengePauseOverlayBorder.Visibility == Visibility.Visible);

                    if (menuOpen)
                    {
                        CloseChallengeMenuAndArm();
                    }
                    else
                    {
                        ResetChallengeCompletely();
                        HideAllPauseMenus();
                        ChallengePauseOverlayBorder.Visibility = Visibility.Visible;
                        StartupMessage.Visibility = Visibility.Collapsed;
                    }

                    e.Handled = true;
                    return;
                }

                if (_currentScene == Scene.Stats)
                {
                    GoToMainMenu();
                    e.Handled = true;
                    return;
                }

                e.Handled = true;
                return;
            }

            // Space: currently no-op
            e.Handled = true;
        }

        private void GameCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            _lastMousePos = e.GetPosition(GameCanvas);
        }

        // =========================================================
        // CLICK ROUTING
        // =========================================================
        private void GameCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Challenge: armed click starts, scorecard click consumes, tracking ignores clicks during run
            if (_currentScene == Scene.Challenge)
            {
                if (_challengeScorecardVisible)
                {
                    e.Handled = true;
                    return;
                }

                if (challengeArmed)
                {
                    StartChallengeIfArmed();
                    e.Handled = true;
                    return;
                }

                if (_challengeType == ChallengeType.Tracking && challengeModeActive)
                {
                    e.Handled = true;
                    return;
                }

                if (ChallengePauseOverlayBorder.Visibility == Visibility.Visible)
                {
                    e.Handled = true;
                    return;
                }

                HandleChallengeClick(e);
                e.Handled = true;
                return;
            }

            // FreeRun
            if (_currentScene == Scene.FreeRun && _freeRunActive)
            {
                HandleFreeRunClick(e);
                e.Handled = true;
                return;
            }
        }

        // =========================================================
        // FREE RUN (bug-fixed)
        // =========================================================
        private void PrepareFreeRunDefaults()
        {
            _freeRunActive = false;
            spawnTimer.Stop();
            StopAllDespawnTimers();

            GameCanvas.Children.Clear();
            HeatmapCanvas.Children.Clear();

            _freeRunHitShots.Clear();
            _freeRunCurrent = new RunMetrics();

            spawnTimer.Interval = TimeSpan.FromMilliseconds(spawnDelay);
            StartupMessage.Visibility = Visibility.Visible;
        }

        private void StartFreeRunSpawning()
        {
            if (_currentScene != Scene.FreeRun) return;

            ApplySettingsFromTextboxes();
            HideStartupMessage();

            _freeRunActive = true;
            spawnTimer.Stop();
            spawnTimer.Interval = TimeSpan.FromMilliseconds(spawnDelay);
            spawnTimer.Start();
        }

        private void StopFreeRunSpawning()
        {
            _freeRunActive = false;
            spawnTimer.Stop();
        }

        private void StartFreerunFromOverlay()
        {
            if (_currentScene != Scene.FreeRun) return;

            ApplySettingsFromTextboxes();
            HideAllPauseMenus();
            StartupMessage.Visibility = Visibility.Collapsed;

            StartFreeRunSpawning();
        }

        private void BtnPlayFreerun_Click(object sender, RoutedEventArgs e)
        {
            StartFreerunFromOverlay();
        }

        private void BtnClosePauseMenu_Click(object sender, RoutedEventArgs e)
        {
            HideAllPauseMenus();
            StartupMessage.Visibility = Visibility.Collapsed;
            StartFreeRunSpawning();
        }

        private void BtnMainMenuFromPause_Click(object sender, RoutedEventArgs e)
        {
            GoToMainMenu();
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentScene != Scene.FreeRun) return;

            StopFreeRunSpawning();
            ClearAllTargets_NoPenalty();

            _freeRunCurrent = new RunMetrics();
            _freeRunHitShots.Clear();

            UpdateFreeRunLastRunPanel();
        }

        private void HandleFreeRunClick(MouseButtonEventArgs e)
        {
            var clickedEllipse = e.OriginalSource as Ellipse;

            // Find the corresponding center dot if it exists (only for removal)
            Ellipse centerDot = FindCenterDotForClickedEllipse(clickedEllipse);

            if (clickedEllipse != null)
            {
                _freeRunCurrent.Hits++;

                Point clickPos = e.GetPosition(GameCanvas);
                double cx = Canvas.GetLeft(clickedEllipse) + targetSize / 2;
                double cy = Canvas.GetTop(clickedEllipse) + targetSize / 2;

                _freeRunHitShots.Add(new ShotRecord
                {
                    OffsetX = clickPos.X - cx,
                    OffsetY = clickPos.Y - cy,
                    Hit = true
                });

                GameCanvas.Children.Remove(clickedEllipse);
                if (centerDot != null) GameCanvas.Children.Remove(centerDot);
            }
            else
            {
                _freeRunCurrent.Misses++;
            }

            RecomputeFreeRunMetrics();
        }

        private void SpawnFreeRunTarget(object? sender, EventArgs e)
        {
            if (_currentScene != Scene.FreeRun || !_freeRunActive) return;

            HideStartupMessage();

            double w = GameCanvas.ActualWidth;
            double h = GameCanvas.ActualHeight;
            if (w < targetSize + 2 || h < targetSize + 2) return;

            double x = rng.NextDouble() * (w - targetSize);
            double y = rng.NextDouble() * (h - targetSize);

            var circle = new Ellipse
            {
                Width = targetSize,
                Height = targetSize,
                Fill = new SolidColorBrush(targetColor)
            };

            Canvas.SetLeft(circle, x);
            Canvas.SetTop(circle, y);
            GameCanvas.Children.Add(circle);

            Ellipse centerDot = null;
            if (showCenterMark)
            {
                centerDot = CreateCenterDot(x, y);
                GameCanvas.Children.Add(centerDot);
            }

            StartDespawnTimer_FreeRun(circle, centerDot);
        }

        private void StartDespawnTimer_FreeRun(Ellipse circle, Ellipse? centerDot)
        {
            var despawnTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(targetLifetime) };
            _activeDespawnTimers.Add(despawnTimer);

            despawnTimer.Tick += (_, _) =>
            {
                despawnTimer.Stop();
                _activeDespawnTimers.Remove(despawnTimer);

                // Already removed by hit? then do nothing.
                if (!GameCanvas.Children.Contains(circle)) return;

                // Despawn counts as ONE miss (bug fix: no double-increment)
                if (_currentScene == Scene.FreeRun && _freeRunActive)
                {
                    _freeRunCurrent.Misses++;
                    RecomputeFreeRunMetrics();
                }

                GameCanvas.Children.Remove(circle);
                if (centerDot != null) GameCanvas.Children.Remove(centerDot);
            };

            despawnTimer.Start();
        }

        private void RecomputeFreeRunMetrics()
        {
            int hits = _freeRunCurrent.Hits;
            int misses = _freeRunCurrent.Misses;
            int attempts = hits + misses;

            _freeRunCurrent.OverallAcc01 = attempts > 0 ? (double)hits / attempts : 1.0;

            if (_freeRunHitShots.Count == 0)
            {
                _freeRunCurrent.AvgDistPx = targetSize;
                _freeRunCurrent.InTargetAcc01 = 0.0;
                return;
            }

            double avgDist = _freeRunHitShots
                .Select(s => Math.Sqrt(s.OffsetX * s.OffsetX + s.OffsetY * s.OffsetY))
                .DefaultIfEmpty(targetSize)
                .Average();

            _freeRunCurrent.AvgDistPx = avgDist;

            double radius = targetSize / 2.0;
            _freeRunCurrent.InTargetAcc01 = radius > 0.001
                ? Math.Max(0.0, 1.0 - (avgDist / radius))
                : 0.0;
        }

        private void SnapshotFreeRunToLastRun()
        {
            // Ensure current run numbers are consistent
            RecomputeFreeRunMetrics();

            _freeRunLast = new RunMetrics
            {
                Hits = _freeRunCurrent.Hits,
                Misses = _freeRunCurrent.Misses,
                OverallAcc01 = _freeRunCurrent.OverallAcc01,
                InTargetAcc01 = _freeRunCurrent.InTargetAcc01,
                AvgDistPx = _freeRunCurrent.AvgDistPx
            };

            _freeRunLastHitShots = _freeRunHitShots.ToList();
        }

        private void UpdateFreeRunLastRunPanel()
        {
            // Show "current run so far" (this panel in your UI behaves like a live summary)
            PrevScoreTextBlock.Text = $"Score: {_freeRunCurrent.Hits}";

            int attempts = _freeRunCurrent.Hits + _freeRunCurrent.Misses;
            double overallAcc01 = attempts > 0 ? (double)_freeRunCurrent.Hits / attempts : 1.0;

            double avgDist = _freeRunHitShots.Count > 0
                ? _freeRunHitShots.Select(s => Math.Sqrt(s.OffsetX * s.OffsetX + s.OffsetY * s.OffsetY)).Average()
                : 0.0;

            double radius = targetSize / 2.0;
            double inTargetAcc01 = (_freeRunHitShots.Count > 0 && radius > 0.001)
                ? Math.Max(0.0, 1.0 - (avgDist / radius))
                : 0.0;

            _freeRunCurrent.OverallAcc01 = overallAcc01;
            _freeRunCurrent.InTargetAcc01 = inTargetAcc01;
            _freeRunCurrent.AvgDistPx = avgDist;

            PrevAccuracyTextBlock.Text =
                $"Accuracy: {overallAcc01 * 100:F1}%\n" +
                $"In-Tgt : {inTargetAcc01 * 100:F1}%\n" +
                $"AvgDist: {avgDist:F1}px";

            PrevTargetSettingsTextBlock.Text =
                $"Settings:\n" +
                $"Color = {targetColorName}\n" +
                $"Size = {targetSize}px\n" +
                $"Delay = {spawnDelay}ms\n" +
                $"Lifetime = {targetLifetime}ms";

            DrawFreeRunHeatmap(_freeRunHitShots);
        }

        private void DrawFreeRunHeatmap(List<ShotRecord> hitShots)
        {
            HeatmapCanvas.Children.Clear();
            if (hitShots == null || hitShots.Count == 0) return;

            double centerX = HeatmapCanvas.Width / 2;
            double centerY = HeatmapCanvas.Height / 2;

            double maxRadius = Math.Min(centerX, centerY) - 5;
            double maxShotDistance = targetSize * 0.75;

            // Center marker
            var centerDot = new Ellipse
            {
                Width = 6,
                Height = 6,
                Stroke = Brushes.White,
                StrokeThickness = 1,
                Fill = Brushes.Transparent,
                Opacity = 0.9
            };
            Canvas.SetLeft(centerDot, centerX - 3);
            Canvas.SetTop(centerDot, centerY - 3);
            HeatmapCanvas.Children.Add(centerDot);

            // Target outline ring
            double targetRealRadius = targetSize / 2.0;
            double targetHeatmapRadius = (targetRealRadius / maxShotDistance) * maxRadius;

            var targetOutline = new Ellipse
            {
                Width = targetHeatmapRadius * 2,
                Height = targetHeatmapRadius * 2,
                Stroke = Brushes.White,
                StrokeThickness = 1,
                Opacity = 0.6,
                Fill = Brushes.Transparent
            };
            Canvas.SetLeft(targetOutline, centerX - targetHeatmapRadius);
            Canvas.SetTop(targetOutline, centerY - targetHeatmapRadius);
            HeatmapCanvas.Children.Add(targetOutline);

            foreach (var shot in hitShots)
            {
                double distance = Math.Sqrt(shot.OffsetX * shot.OffsetX + shot.OffsetY * shot.OffsetY);
                double normalized = Math.Min(distance / maxShotDistance, 1.0);
                double r = normalized * maxRadius;

                double angle = Math.Atan2(shot.OffsetY, shot.OffsetX);

                double x = centerX + r * Math.Cos(angle) - 3;
                double y = centerY + r * Math.Sin(angle) - 3;

                var dot = new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = Brushes.Lime,
                    Opacity = 0.7
                };

                Canvas.SetLeft(dot, x);
                Canvas.SetTop(dot, y);
                HeatmapCanvas.Children.Add(dot);
            }
        }

        // =========================================================
        // SHARED TARGET HELPERS
        // =========================================================
        private void ClearAllTargets_NoPenalty()
        {
            StopAllDespawnTimers();

            var ellipses = GameCanvas.Children.OfType<Ellipse>().ToList();
            foreach (var e in ellipses)
                GameCanvas.Children.Remove(e);
        }

        private void StopAllDespawnTimers()
        {
            foreach (var t in _activeDespawnTimers.ToList())
                t.Stop();
            _activeDespawnTimers.Clear();
        }

        private Ellipse CreateCenterDot(double x, double y)
        {
            var dot = new Ellipse
            {
                Width = 6,
                Height = 6,
                Stroke = Brushes.Black,
                StrokeThickness = 1,
                Fill = Brushes.Transparent,
                IsHitTestVisible = false,
                Tag = "CenterDot"
            };

            Canvas.SetLeft(dot, x + targetSize / 2 - 3);
            Canvas.SetTop(dot, y + targetSize / 2 - 3);
            return dot;
        }

        private Ellipse? FindCenterDotForClickedEllipse(Ellipse? clickedEllipse)
        {
            if (clickedEllipse == null) return null;

            foreach (var child in GameCanvas.Children.OfType<Ellipse>())
            {
                if (child.Tag is string tag && tag == "CenterDot")
                {
                    double cx = Canvas.GetLeft(child) + child.Width / 2;
                    double cy = Canvas.GetTop(child) + child.Height / 2;

                    double tx = Canvas.GetLeft(clickedEllipse) + clickedEllipse.Width / 2;
                    double ty = Canvas.GetTop(clickedEllipse) + clickedEllipse.Height / 2;

                    if (Math.Abs(tx - cx) < 0.1 && Math.Abs(ty - cy) < 0.1)
                        return child;
                }
            }

            return null;
        }

        private void HideStartupMessage()
        {
            if (StartupMessage.Visibility == Visibility.Visible)
                StartupMessage.Visibility = Visibility.Collapsed;
        }

        // =========================================================
        // UI OVERLAYS
        // =========================================================
        private void ShowFreeRunOverlay()
        {
            HideAllPauseMenus();

            StopFreeRunSpawning();
            ClearAllTargets_NoPenalty();

            PauseOverlayBorder.Visibility = Visibility.Visible;
            PreviousStatsGrid.Visibility = Visibility.Visible;
            ColorPickerPanel.Visibility = Visibility.Collapsed;

            UpdateFreeRunLastRunPanel();
        }

        private void HideAllPauseMenus()
        {
            PauseOverlayBorder.Visibility = Visibility.Collapsed;
            ChallengePauseOverlayBorder.Visibility = Visibility.Collapsed;
            PreviousStatsGrid.Visibility = Visibility.Collapsed;
            ColorPickerPanel.Visibility = Visibility.Collapsed;
        }

        // =========================================================
        // CHALLENGE MENU UI (toggles)
        // =========================================================
        private void RefreshChallengeMenuVisuals()
        {
            if (BtnTypeSpeed == null) return;

            SetSelectedStyle(BtnTypeSpeed, _challengeType == ChallengeType.Speed);
            SetSelectedStyle(BtnTypeAccuracy, _challengeType == ChallengeType.Accuracy);
            SetSelectedStyle(BtnTypeTracking, _challengeType == ChallengeType.Tracking);

            SetSelectedStyle(BtnCount30, _challengeTargetCount == 30);
            SetSelectedStyle(BtnCount60, _challengeTargetCount == 60);
            SetSelectedStyle(BtnCount100, _challengeTargetCount == 100);

            SetSelectedStyle(BtnColorRed, _challengeColor == ChallengeColorChoice.Red);
            SetSelectedStyle(BtnColorYellow, _challengeColor == ChallengeColorChoice.Yellow);
            SetSelectedStyle(BtnColorGreen, _challengeColor == ChallengeColorChoice.Green);
        }

        private void SetSelectedStyle(Button btn, bool selected)
        {
            if (selected)
            {
                btn.Background = new SolidColorBrush(Color.FromRgb(30, 160, 90));
                btn.BorderBrush = new SolidColorBrush(Color.FromRgb(160, 255, 200));
                btn.Opacity = 1.0;
            }
            else
            {
                btn.Background = new SolidColorBrush(Color.FromRgb(42, 42, 42));
                btn.BorderBrush = new SolidColorBrush(Color.FromRgb(85, 85, 85));
                btn.Opacity = 0.85;
            }
        }

        private void BtnTypeSpeed_Click(object sender, RoutedEventArgs e) { _challengeType = ChallengeType.Speed; RefreshChallengeMenuVisuals(); }
        private void BtnTypeAccuracy_Click(object sender, RoutedEventArgs e) { _challengeType = ChallengeType.Accuracy; RefreshChallengeMenuVisuals(); }
        private void BtnTypeTracking_Click(object sender, RoutedEventArgs e) { _challengeType = ChallengeType.Tracking; RefreshChallengeMenuVisuals(); }

        private void BtnCount30_Click(object sender, RoutedEventArgs e) { _challengeTargetCount = 30; RefreshChallengeMenuVisuals(); }
        private void BtnCount60_Click(object sender, RoutedEventArgs e) { _challengeTargetCount = 60; RefreshChallengeMenuVisuals(); }
        private void BtnCount100_Click(object sender, RoutedEventArgs e) { _challengeTargetCount = 100; RefreshChallengeMenuVisuals(); }

        private void BtnColorRed_Click(object sender, RoutedEventArgs e) { _challengeColor = ChallengeColorChoice.Red; RefreshChallengeMenuVisuals(); }
        private void BtnColorYellow_Click(object sender, RoutedEventArgs e) { _challengeColor = ChallengeColorChoice.Yellow; RefreshChallengeMenuVisuals(); }
        private void BtnColorGreen_Click(object sender, RoutedEventArgs e) { _challengeColor = ChallengeColorChoice.Green; RefreshChallengeMenuVisuals(); }

        private void BtnPlayChallenge_Click(object sender, RoutedEventArgs e) => CloseChallengeMenuAndArm();
        private void BtnCloseChallengeMenu_Click(object sender, RoutedEventArgs e) => CloseChallengeMenuAndArm();

        // =========================================================
        // CHALLENGE FLOW
        // =========================================================

        private void RecordChallengeHeatmapHit(double hitX, double hitY, double centerX, double centerY, double diameter)
        {
            if (_challengeHitHeatmap == null) return;

            double radius = diameter / 2.0;
            if (radius <= 0.001) return;

            double dx = hitX - centerX;
            double dy = hitY - centerY;

            // Normalize to target radius: [-1..1]
            double nx = dx / radius;
            double ny = dy / radius;

            _challengeHitHeatmap.RecordHitNormalized(nx, ny);
        }
        private void CloseChallengeMenuAndArm()
        {
            HideAllPauseMenus();
            ChallengePauseOverlayBorder.Visibility = Visibility.Collapsed;
            ArmChallenge();
        }

        private void ArmChallenge()
        {
            ApplyChallengeConfigToRuntime();
            ResetChallengeCompletely();

            challengeArmed = true;
            challengeModeActive = false;
            challengeCompleted = false;
            challengeInputLocked = false;

            StartupMessage.Visibility = Visibility.Visible;
            SetStartupText("Click ONCE to begin...");
        }

        private void StartChallengeIfArmed()
        {
            if (_currentScene != Scene.Challenge) return;
            if (!challengeArmed) return;
            if (challengeInputLocked) return;

            challengeArmed = false;
            StartupMessage.Visibility = Visibility.Collapsed;

            HideAllPauseMenus();
            ApplyChallengeConfigToRuntime();
            GameCanvas.UpdateLayout();

            StartChallengeMode_NewScoring();
        }

        private void ResetChallengeCompletely()
        {
            _freeRunActive = false;
            spawnTimer.Stop();
            StopAllDespawnTimers();

            challengeModeActive = false;
            challengeCompleted = false;
            challengeInputLocked = false;

            challengeHits = 0;
            challengeTargetsSpawned = 0;
            challengeTargetsRemaining = ChallengeTotalTargets;

            // Clear visuals + records
            GameCanvas.Children.Clear();
            HeatmapCanvas.Children.Clear();
            challengeTargetToDot.Clear();
            ChallengeHits_List.Clear();
            _trackingHealthArc.Clear();

            _challengeHitHeatmap = null;
            _challengeHeatmapSnapshot = null;

            // Tracking reset
            _trackingMoveTimer.Stop();
            _trackingCountdownTimer.Stop();
            _trackingVel.Clear();
            _trackingDwellSeconds.Clear();
            _trackingSecondsRemaining = 0;

            StartupMessage.Visibility = Visibility.Collapsed;
        }

        private void ApplyChallengeConfigToRuntime()
        {
            targetSize = 80;

            targetColor = _challengeColor switch
            {
                ChallengeColorChoice.Red => Color.FromRgb(255, 170, 170),
                ChallengeColorChoice.Yellow => Color.FromRgb(255, 244, 170),
                _ => Color.FromRgb(180, 255, 190),
            };
            targetColorName = _challengeColor.ToString();

            if (_challengeType == ChallengeType.Tracking)
            {
                _trackingDurationSeconds = _challengeTargetCount;                 // 30/60/100 seconds
                ChallengeTotalTargets = Math.Max(1, _challengeTargetCount / 2);   // 15/30/50 targets
            }
            else
            {
                ChallengeTotalTargets = _challengeTargetCount;                   // 30/60/100 targets
            }

            challengeTargetsRemaining = ChallengeTotalTargets;
        }

        private void StartChallengeMode_NewScoring()
        {
            challengeModeActive = true;
            challengeCompleted = false;
            challengeInputLocked = false;

            _challengeHitHeatmap = new CircularHeatmapRecorder(gridSize: 64, splatRadius: 4);
            _challengeHeatmapSnapshot = null;

            challengeHits = 0;
            challengeTargetsSpawned = 0;
            challengeTargetsRemaining = ChallengeTotalTargets;

            GameCanvas.Children.Clear();
            challengeTargetToDot.Clear();
            ChallengeHits_List.Clear();

            _trackingVel.Clear();
            _trackingDwellSeconds.Clear();

            challengeStartTime = DateTime.Now;

            if (_challengeType == ChallengeType.Tracking)
            {
                StartTrackingRuntime();
                return;
            }

            // Speed/Accuracy: spawn initial set
            for (int i = 0; i < Math.Min(4, ChallengeTotalTargets); i++)
                SpawnChallengeTarget();
        }

        // =========================================================
        // CHALLENGE CLICK HANDLING
        // =========================================================
        private void HandleChallengeClick(MouseButtonEventArgs e)
        {
            if (!challengeModeActive || challengeInputLocked) return;

            var clickedEllipse = e.OriginalSource as Ellipse;

            if (clickedEllipse != null && challengeTargetToDot.ContainsKey(clickedEllipse))
            {
                challengeHits++;
                challengeTargetsRemaining--;

                Point clickPos = e.GetPosition(GameCanvas);
                double cx = Canvas.GetLeft(clickedEllipse) + targetSize / 2;
                double cy = Canvas.GetTop(clickedEllipse) + targetSize / 2;

                RecordChallengeHeatmapHit(
                    hitX: clickPos.X,
                    hitY: clickPos.Y,
                    centerX: cx,
                    centerY: cy,
                    diameter: targetSize
                );

                RegisterChallengeHit(clickPos.X - cx, clickPos.Y - cy);

                GameCanvas.Children.Remove(clickedEllipse);
                if (challengeTargetToDot[clickedEllipse] != null)
                    GameCanvas.Children.Remove(challengeTargetToDot[clickedEllipse]);
                challengeTargetToDot.Remove(clickedEllipse);

                if (challengeTargetsSpawned < ChallengeTotalTargets)
                    SpawnChallengeTarget();

                if (challengeTargetsRemaining <= 0)
                {
                    challengeInputLocked = true;
                    EndChallengeMode_WithPlaceholderScorecard();
                }
            }
            else
            {
                // Miss
                // (Tracking is handled by dwell, so misses here are only for click-based modes)
                // Keep a single miss counter source: reuse totalMisses semantics via local variable not needed.
                // We'll store misses as: (targets - hits) at end? No, you want click misses too:
                _challengeClickMisses++;
            }
        }

        // Clean miss counter for challenge only (click-based misses and tracking leftover targets)
        private int _challengeClickMisses = 0;

        private void RegisterChallengeHit(double offsetX, double offsetY)
        {
            if (!challengeModeActive) return;

            ChallengeHits_List.Add(new ShotRecord
            {
                OffsetX = offsetX,
                OffsetY = offsetY,
                Hit = true
            });
        }

        // =========================================================
        // CHALLENGE TARGET SPAWNING
        // =========================================================
        private void SpawnChallengeTarget()
        {
            if (!challengeModeActive || challengeTargetsSpawned >= ChallengeTotalTargets) return;

            if (!TryFindNonOverlappingPosition(out double x, out double y))
                return;

            var circle = new Ellipse
            {
                Width = targetSize,
                Height = targetSize,
                Fill = new SolidColorBrush(targetColor)
            };

            Canvas.SetLeft(circle, x);
            Canvas.SetTop(circle, y);
            GameCanvas.Children.Add(circle);

            Ellipse? centerDot = null;
            if (showCenterMark)
            {
                centerDot = CreateCenterDot(x, y);
                GameCanvas.Children.Add(centerDot);
            }

            challengeTargetToDot[circle] = centerDot;
            challengeTargetsSpawned++;
        }

        private bool TryFindNonOverlappingPosition(out double x, out double y)
        {
            x = 0;
            y = 0;

            double w = GameCanvas.ActualWidth;
            double h = GameCanvas.ActualHeight;
            if (w < targetSize + 2 || h < targetSize + 2) return false;

            double minDist = targetSize + ChallengeSpawnPadding;

            var existing = challengeTargetToDot.Keys.ToList();

            for (int attempt = 0; attempt < ChallengeSpawnAttempts; attempt++)
            {
                double candidateX = rng.NextDouble() * (w - targetSize);
                double candidateY = rng.NextDouble() * (h - targetSize);

                double cx = candidateX + targetSize / 2.0;
                double cy = candidateY + targetSize / 2.0;

                bool overlaps = false;

                foreach (var t in existing)
                {
                    if (!GameCanvas.Children.Contains(t)) continue;

                    double tx = Canvas.GetLeft(t) + t.Width / 2.0;
                    double ty = Canvas.GetTop(t) + t.Height / 2.0;

                    double dx = cx - tx;
                    double dy = cy - ty;

                    if ((dx * dx + dy * dy) < (minDist * minDist))
                    {
                        overlaps = true;
                        break;
                    }
                }

                if (!overlaps)
                {
                    x = candidateX;
                    y = candidateY;
                    return true;
                }
            }

            return false;
        }

        // =========================================================
        // TRACKING MODE
        // =========================================================
        private System.Windows.Shapes.Path CreateHealthArc()
        {
            return new System.Windows.Shapes.Path
            {
                Stroke = Brushes.LimeGreen,
                StrokeThickness = 6,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Opacity = 0.95,
                IsHitTestVisible = false
            };
        }

        private void UpdateHealthArc(System.Windows.Shapes.Path arc, double centerX, double centerY, double progress01)
        {
            progress01 = Math.Max(0, Math.Min(1, progress01));

            // Ring radius: slightly larger than target radius
            double r = targetSize / 2.0 + 6.0;

            // Start at top (-90 degrees), sweep clockwise
            double startAngle = -90.0;
            double sweepAngle = 360.0 * progress01;

            if (progress01 <= 0.001)
            {
                arc.Data = Geometry.Empty;
                return;
            }

            if (progress01 >= 0.999)
            {
                // Full ring: draw as ellipse geometry (nice and clean)
                arc.Data = new EllipseGeometry(new Point(centerX, centerY), r, r);
                return;
            }

            Point start = PointOnCircle(centerX, centerY, r, startAngle);
            Point end = PointOnCircle(centerX, centerY, r, startAngle + sweepAngle);

            bool isLargeArc = sweepAngle > 180.0;

            var fig = new PathFigure
            {
                StartPoint = start,
                IsClosed = false,
                IsFilled = false
            };

            fig.Segments.Add(new ArcSegment
            {
                Point = end,
                Size = new Size(r, r),
                RotationAngle = 0,
                IsLargeArc = isLargeArc,
                SweepDirection = SweepDirection.Clockwise,
                IsStroked = true
            });

            var geo = new PathGeometry();
            geo.Figures.Add(fig);
            arc.Data = geo;

            arc.Stroke = HealthGradient01(progress01);
        }

        private Brush HealthGradient01(double t)
        {
            t = Math.Max(0, Math.Min(1, t));

            // Smooth tri-blend: red -> amber -> green
            Color low = Color.FromRgb(220, 60, 60);    // deep red
            Color mid = Color.FromRgb(255, 190, 80);   // soft amber
            Color high = Color.FromRgb(90, 220, 140);  // emerald green

            if (t < 0.5)
            {
                // low -> mid
                double u = t / 0.5;
                return new SolidColorBrush(Lerp(low, mid, u));
            }
            else
            {
                // mid -> high
                double u = (t - 0.5) / 0.5;
                return new SolidColorBrush(Lerp(mid, high, u));
            }
        }

        private static Point PointOnCircle(double cx, double cy, double r, double angleDeg)
        {
            double a = angleDeg * Math.PI / 180.0;
            return new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
        }

        private void StartTrackingRuntime()
        {
            _trackingSecondsRemaining = _trackingDurationSeconds;
            _challengeClickMisses = 0; // clicks ignored in tracking

            for (int i = 0; i < Math.Min(4, ChallengeTotalTargets); i++)
                SpawnTrackingTarget();

            _trackingMoveTimer.Start();
            _trackingCountdownTimer.Start();
        }

        private void SpawnTrackingTarget()
        {
            if (!challengeModeActive || challengeTargetsSpawned >= ChallengeTotalTargets) return;
            if (!TryFindNonOverlappingPosition(out double x, out double y)) return;

            var circle = new Ellipse
            {
                Width = targetSize,
                Height = targetSize,
                Fill = new SolidColorBrush(targetColor),
                Tag = "TrackingTarget"
            };

            Canvas.SetLeft(circle, x);
            Canvas.SetTop(circle, y);
            GameCanvas.Children.Add(circle);

            Ellipse? centerDot = null;
            if (showCenterMark)
            {
                centerDot = CreateCenterDot(x, y);
                GameCanvas.Children.Add(centerDot);
            }

            challengeTargetToDot[circle] = centerDot;
            challengeTargetsSpawned++;

            _trackingDwellSeconds[circle] = 0.0;

            var arc = CreateHealthArc();
            GameCanvas.Children.Add(arc);
            _trackingHealthArc[circle] = arc;

            double cx = x + targetSize / 2.0;
            double cy = y + targetSize / 2.0;
            UpdateHealthArc(arc, cx, cy, 0.0);

            double speed = 220.0;
            double ang = rng.NextDouble() * Math.PI * 2;
            var v = new Vector(Math.Cos(ang), Math.Sin(ang));

            double mult = 0.75 + rng.NextDouble() * 0.65;
            v *= (speed * mult);

            _trackingVel[circle] = v;
        }

        private void TrackingMoveTimer_Tick(object? sender, EventArgs e)
        {
            if (!challengeModeActive || challengeInputLocked) return;
            if (_challengeType != ChallengeType.Tracking) return;

            double dt = _trackingMoveTimer.Interval.TotalSeconds;
            double w = GameCanvas.ActualWidth;
            double h = GameCanvas.ActualHeight;
            if (w <= 2 || h <= 2) return;

            var targets = challengeTargetToDot.Keys.ToList();

            foreach (var t in targets)
            {
                if (!GameCanvas.Children.Contains(t)) continue;

                var v = _trackingVel.TryGetValue(t, out var vel) ? vel : new Vector(0, 0);

                double x = Canvas.GetLeft(t);
                double y = Canvas.GetTop(t);

                double nx = x + v.X * dt;
                double ny = y + v.Y * dt;

                double maxX = Math.Max(0, w - targetSize);
                double maxY = Math.Max(0, h - targetSize);

                // bounce
                if (nx < 0) { nx = 0; v.X = -v.X; }
                else if (nx > maxX) { nx = maxX; v.X = -v.X; }

                if (ny < 0) { ny = 0; v.Y = -v.Y; }
                else if (ny > maxY) { ny = maxY; v.Y = -v.Y; }

                _trackingVel[t] = v;

                Canvas.SetLeft(t, nx);
                Canvas.SetTop(t, ny);

                if (challengeTargetToDot.TryGetValue(t, out var dot) && dot != null)
                    UpdateCenterDotPosition(dot, nx, ny);

                // center after final position
                double cx = nx + targetSize / 2.0;
                double cy = ny + targetSize / 2.0;

                // --- SINGLE SOURCE OF TRUTH FOR "HEALTH" ---
                if (!_trackingDwellSeconds.TryGetValue(t, out double cur))
                    cur = 0.0;

                bool inside = _mouseInsideCanvas && PointInsideEllipse(_lastMousePos, nx, ny, targetSize);

                if (inside)
                {
                    cur += dt;

                    if (cur >= TrackingKillSeconds)
                    {
                        // clamp before kill so health ring shows full
                        cur = TrackingKillSeconds;
                        _trackingDwellSeconds[t] = cur;

                        KillTrackingTarget(t);
                        continue;
                    }
                }

                // permanent chip damage: DO NOT reset when leaving
                if (cur > TrackingKillSeconds)
                    cur = TrackingKillSeconds;

                _trackingDwellSeconds[t] = cur;

                // Update arc AFTER cur is finalized (so it matches actual health)
                if (_trackingHealthArc.TryGetValue(t, out var arc))
                {
                    double progress = cur / TrackingKillSeconds; // 0..1
                    UpdateHealthArc(arc, cx, cy, progress);
                }
            }
        }


        private void TrackingCountdownTimer_Tick(object? sender, EventArgs e)
        {
            if (!challengeModeActive || challengeInputLocked) return;
            if (_challengeType != ChallengeType.Tracking) return;

            _trackingSecondsRemaining -= _trackingCountdownTimer.Interval.TotalSeconds;

            if (_trackingSecondsRemaining <= 0)
            {
                _trackingSecondsRemaining = 0;

                _trackingMoveTimer.Stop();
                _trackingCountdownTimer.Stop();

                CountRemainingTrackingTargetsAsMisses_AndClear();

                challengeInputLocked = true;
                EndChallengeMode_WithPlaceholderScorecard();
            }
        }

        private void CountRemainingTrackingTargetsAsMisses_AndClear()
        {
            // Remaining (not killed) count as misses.
            if (challengeTargetsRemaining > 0)
                _challengeClickMisses += challengeTargetsRemaining;

            challengeTargetsRemaining = 0;

            GameCanvas.Children.Clear();

            challengeTargetToDot.Clear();
            _trackingVel.Clear();
            _trackingDwellSeconds.Clear();
            _trackingHealthArc.Clear();
        }

        private void UpdateCenterDotPosition(Ellipse dot, double targetLeft, double targetTop)
        {
            Canvas.SetLeft(dot, targetLeft + targetSize / 2 - dot.Width / 2);
            Canvas.SetTop(dot, targetTop + targetSize / 2 - dot.Height / 2);
        }

        private static bool PointInsideEllipse(Point p, double ellipseLeft, double ellipseTop, double diameter)
        {
            double r = diameter / 2.0;
            double cx = ellipseLeft + r;
            double cy = ellipseTop + r;

            double dx = p.X - cx;
            double dy = p.Y - cy;

            return (dx * dx + dy * dy) <= (r * r);
        }

        private void KillTrackingTarget(Ellipse target)
        {
            if (!challengeModeActive) return;
            if (!challengeTargetToDot.ContainsKey(target)) return;

            challengeHits++;
            challengeTargetsRemaining--;

            double cx = Canvas.GetLeft(target) + targetSize / 2;
            double cy = Canvas.GetTop(target) + targetSize / 2;

            RecordChallengeHeatmapHit(
                hitX: _lastMousePos.X,
                hitY: _lastMousePos.Y,
                centerX: cx,
                centerY: cy,
                diameter: targetSize
            );

            RegisterChallengeHit(_lastMousePos.X - cx, _lastMousePos.Y - cy);

            if (_trackingHealthArc.TryGetValue(target, out var arc))
            {
                GameCanvas.Children.Remove(arc);
                _trackingHealthArc.Remove(target);
            }

            GameCanvas.Children.Remove(target);
            if (challengeTargetToDot[target] != null)
                GameCanvas.Children.Remove(challengeTargetToDot[target]);
            challengeTargetToDot.Remove(target);

            _trackingVel.Remove(target);
            _trackingDwellSeconds.Remove(target);

            if (challengeTargetsSpawned < ChallengeTotalTargets)
                SpawnTrackingTarget();

            if (challengeTargetsRemaining <= 0)
            {
                _trackingMoveTimer.Stop();
                _trackingCountdownTimer.Stop();

                challengeInputLocked = true;
                EndChallengeMode_WithPlaceholderScorecard();
            }
        }

        // =========================================================
        // SCORING / METRICS
        // =========================================================

        private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

        private static double Ramp(double value, double good, double amazing)
        {
            if (amazing <= good)
                return value >= good ? 1.0 : 0.0;

            return Clamp01((value - good) / (amazing - good));
        }

        private static double PowEase(double t, double power)
        {
            t = Clamp01(t);
            return Math.Pow(t, power);
        }

        /// <summary>
        /// Insane bonus curve:
        /// - Below ok => 0
        /// - At ok => >= minBonus
        /// - Approaching 1.0 => rockets toward maxBonus
        /// </summary>
        private static int InsaneBonus01(double value01, double ok, double good, int minBonus, int maxBonus, double power = 3.5)
        {
            value01 = Clamp01(value01);

            // Map [ok..good] into [0..1] (so "good" is already paying hard)
            double t = Ramp(value01, good: ok, amazing: good); // uses your Ramp()

            if (t <= 0) return 0;

            double u = PowEase(t, power); // make it explode near 1
            int bonus = (int)Math.Round(minBonus + (maxBonus - minBonus) * u);

            // Guarantee minimum once you qualify as "good-ish"
            return Math.Max(minBonus, bonus);
        }

        /// <summary>
        /// Similar but for "lower is better" metrics (like seconds per target).
        /// Provide a "goodMax" threshold and "amazingMax" threshold.
        /// </summary>
        private static int InsaneBonusLowerBetter(double value, double goodMax, double amazingMax, int minBonus, int maxBonus, double power = 3.5)
        {
            // Convert into 0..1 where 1 = amazing (small value)
            // If value <= amazingMax => 1
            // If value >= goodMax => 0
            if (goodMax <= amazingMax) return 0;

            double t = Clamp01((goodMax - value) / (goodMax - amazingMax));
            if (t <= 0) return 0;

            double u = PowEase(t, power);
            int bonus = (int)Math.Round(minBonus + (maxBonus - minBonus) * u);
            return Math.Max(minBonus, bonus);
        }

        private (double overallAcc, double inTargetAcc, double avgDist, int misses) ComputeAccuracyMetrics()
        {
            // misses: clicks + (tracking leftover targets)
            int misses = _challengeClickMisses;

            int attempts = challengeHits + misses;
            double overallAcc = attempts > 0 ? (double)challengeHits / attempts : 1.0;

            if (ChallengeHits_List.Count == 0)
                return (overallAcc, 0.0, targetSize, misses);

            double avgDist = ChallengeHits_List
                .Select(h => Math.Sqrt(h.OffsetX * h.OffsetX + h.OffsetY * h.OffsetY))
                .DefaultIfEmpty(targetSize)
                .Average();

            double radius = targetSize / 2.0;
            double inTargetAcc = Math.Max(0.0, 1.0 - (avgDist / radius));

            return (overallAcc, inTargetAcc, avgDist, misses);
        }


        private int GetBaseMaxScoreForTargetCount(int targets)
        {
            return targets switch
            {
                30 => 30_000,
                60 => 60_000,
                100 => 100_000,
                _ => (int)Math.Round(targets * 1000.0)
            };
        }

        private int GetBaseMaxScoreForTrackingDuration(int durationSeconds)
        {
            // Per your spec: 30s -> 30,000; 60s -> 60,000; 100s -> 100,000
            // So it's 1,000 coins per second.
            return Math.Max(1, durationSeconds) * 1000;
        }

        private ScoreBreakdown Score_SpeedMode_Breakdown(int targetCount, double elapsedSeconds, double overallAcc, double inTargetAcc, double avgDist)
        {
            var b = new ScoreBreakdown();
            b.BaseMax = GetBaseMaxScoreForTargetCount(targetCount);

            double spt = elapsedSeconds / Math.Max(1, targetCount);

            double speedFactor = Ramp(2.00 - spt, good: 0.0, amazing: 2.00 - 0.60);
            int baseScore = (int)Math.Round(b.BaseMax * speedFactor);
            b.BaseScore = baseScore;

            // --- Accuracy bonuses ---
            b.BonusOverallAcc = InsaneBonus01(
                value01: overallAcc,
                ok: 0.80,
                good: 0.93,
                minBonus: 500,
                maxBonus: (int)(b.BaseMax * 0.35),
                power: 4.0
            );

            b.BonusInTargetAcc = InsaneBonus01(
                value01: inTargetAcc,
                ok: 0.65,
                good: 0.86,
                minBonus: 500,
                maxBonus: (int)(b.BaseMax * 0.25),
                power: 4.0
            );

            // --- Pace bonus (lower is better) ---
            b.BonusPace = InsaneBonusLowerBetter(
                value: spt,
                goodMax: 1.05,
                amazingMax: 0.60,
                minBonus: 500,
                maxBonus: (int)(b.BaseMax * 0.45),
                power: 4.2
            );

            // --- Misses bonus (use miss-rate so it scales with run size) ---
            double attempts = Math.Max(1.0, targetCount + 0.0); // speed has fixed targets
            double missRate = Clamp01(1.0 - overallAcc);       // same thing, but explicit
            b.BonusMisses = InsaneBonusLowerBetter(
                value: missRate,
                goodMax: 0.12,       // <=12% misses is "good"
                amazingMax: 0.02,    // <=2% misses is "insane"
                minBonus: 500,
                maxBonus: (int)(b.BaseMax * 0.25),
                power: 4.0
            );

            // --- Avg distance bonus (lower is better) ---
            b.BonusAvgDist = InsaneBonusLowerBetter(
                value: avgDist,
                goodMax: targetSize * 0.28,   // tweak if you want stricter/looser
                amazingMax: targetSize * 0.12,
                minBonus: 500,
                maxBonus: (int)(b.BaseMax * 0.25),
                power: 4.2
            );

            b.BonusTimeOrSpeed = 0; // keep as 0 for speed; pace is the "speed bonus" now

            return b;
        }

        private ScoreBreakdown Score_AccuracyMode_Breakdown(int targetCount, double elapsedSeconds, double overallAcc, double inTargetAcc, double avgDist)
        {
            var b = new ScoreBreakdown();
            b.BaseMax = GetBaseMaxScoreForTargetCount(targetCount);

            double overallT = Ramp(overallAcc, 0.85, 0.95);
            double inTargetT = Ramp(inTargetAcc, 0.75, 0.90);

            double accuracyFactor = Clamp01(0.70 * overallT + 0.30 * inTargetT);
            int baseScore = (int)Math.Round(b.BaseMax * accuracyFactor);
            b.BaseScore = baseScore;

            double spt = elapsedSeconds / Math.Max(1, targetCount);

            // Give bonuses for ALL good fields (lenient but explosive near perfect)
            b.BonusOverallAcc = InsaneBonus01(
                value01: overallAcc,
                ok: 0.82,
                good: 0.95,
                minBonus: 500,
                maxBonus: (int)(b.BaseMax * 0.30),
                power: 4.2
            );

            b.BonusInTargetAcc = InsaneBonus01(
                value01: inTargetAcc,
                ok: 0.72,
                good: 0.90,
                minBonus: 500,
                maxBonus: (int)(b.BaseMax * 0.35),
                power: 4.5
            );

            // Speed bonus exists here: map spt into BonusTimeOrSpeed + BonusPace (both can pay)

            b.BonusTimeOrSpeed = InsaneBonusLowerBetter(
                value: spt,
                goodMax: 1.10,
                amazingMax: 0.65,
                minBonus: 500,
                maxBonus: (int)(b.BaseMax * 0.30),
                power: 4.0
            );

            b.BonusPace = InsaneBonusLowerBetter(
                value: spt,
                goodMax: 0.95,
                amazingMax: 0.60,
                minBonus: 500,
                maxBonus: (int)(b.BaseMax * 0.35),
                power: 4.3
            );

            // Miss-rate + AvgDist bonuses
            double missRate = Clamp01(1.0 - overallAcc);

            b.BonusMisses = InsaneBonusLowerBetter(
                value: missRate,
                goodMax: 0.10,
                amazingMax: 0.015,
                minBonus: 500,
                maxBonus: (int)(b.BaseMax * 0.20),
                power: 4.0
            );

            b.BonusAvgDist = InsaneBonusLowerBetter(
                value: avgDist,
                goodMax: targetSize * 0.26,
                amazingMax: targetSize * 0.10,
                minBonus: 500,
                maxBonus: (int)(b.BaseMax * 0.30),
                power: 4.6
            );

            return b;
        }

        private ScoreBreakdown Score_TrackingMode_Breakdown(int targetCount, int durationSeconds, double elapsedSeconds, double overallAcc, double inTargetAcc, double avgDist) { 

            var b = new ScoreBreakdown();

            // Base is duration-based, not target-based
            b.BaseMax = GetBaseMaxScoreForTrackingDuration(durationSeconds);

            // Scale base by completion (if time runs out and you didn't kill all)
            // In your tracking mode, Misses includes leftovers when timer ends,
            // so overallAcc already represents "kills / (kills+leftovers)".
            // We'll use that as completion proxy.
            double completion01 = Clamp01(overallAcc);
            b.BaseScore = (int)Math.Round(b.BaseMax * completion01);

            // Center bonus (big)
            b.BonusInTargetAcc = InsaneBonus01(
                value01: inTargetAcc,
                ok: 0.65,
                good: 0.90,
                minBonus: 500,
                maxBonus: (int)(b.BaseMax * 0.50),
                power: 4.8
            );

            // Completion bonus (overallAcc is basically completion in tracking)
            b.BonusOverallAcc = InsaneBonus01(
                value01: overallAcc,
                ok: 0.80,
                good: 0.98,
                minBonus: 500,
                maxBonus: (int)(b.BaseMax * 0.35),
                power: 5.0
            );

            // Miss bonus (same thing but expressed as miss-rate, pays separately)
            double missRate = Clamp01(1.0 - overallAcc);
            b.BonusMisses = InsaneBonusLowerBetter(
                value: missRate,
                goodMax: 0.15,
                amazingMax: 0.02,
                minBonus: 500,
                maxBonus: (int)(b.BaseMax * 0.25),
                power: 4.4
            );

            // Avg distance bonus (laser tracking)
            b.BonusAvgDist = InsaneBonusLowerBetter(
                value: avgDist,
                goodMax: targetSize * 0.30,
                amazingMax: targetSize * 0.12,
                minBonus: 500,
                maxBonus: (int)(b.BaseMax * 0.35),
                power: 4.8
            );

            // Time-under-duration bonus (only if finished early)
            double dur = Math.Max(0.001, durationSeconds);
            double under01 = Clamp01((dur - elapsedSeconds) / dur);
            double timeGate01 = Ramp(overallAcc, good: 0.95, amazing: 1.00);

            int rawTimeBonus = (int)Math.Round((b.BaseMax * 0.65) * Math.Pow(under01, 3.0) * timeGate01);
            b.BonusTimeOrSpeed = (under01 > 0.001 && timeGate01 > 0.0) ? Math.Max(500, rawTimeBonus) : 0;

            return b;
        }

        // =========================================================
        // END CHALLENGE + SCORECARD
        // =========================================================
        private async void EndChallengeMode_WithPlaceholderScorecard()
        {
            challengeModeActive = false;
            challengeCompleted = true;
            challengeInputLocked = true;

            // Freeze heatmap snapshot for this completed run
            if (_challengeHitHeatmap != null)
            {
                _challengeHeatmapSnapshot = new HeatmapSnapshot
                {
                    W = _challengeHitHeatmap.N,
                    H = _challengeHitHeatmap.N,
                    Counts = _challengeHitHeatmap.SnapshotCounts(scale: 1000)
                };
            }
            else
            {
                _challengeHeatmapSnapshot = new HeatmapSnapshot();
            }

            GameCanvas.Children.Clear();

            var elapsed = DateTime.Now - challengeStartTime;
            double elapsedSeconds = Math.Max(0.001, elapsed.TotalSeconds);

            if (_challengeType == ChallengeType.Tracking && _trackingSecondsRemaining <= 0)
                elapsedSeconds = _trackingDurationSeconds;

            var (overallAcc, inTargetAcc, avgDist, misses) = ComputeAccuracyMetrics();

            ScoreBreakdown sb = _challengeType switch
            {
                ChallengeType.Speed => Score_SpeedMode_Breakdown(ChallengeTotalTargets, elapsedSeconds, overallAcc, inTargetAcc, avgDist),
                ChallengeType.Accuracy => Score_AccuracyMode_Breakdown(ChallengeTotalTargets, elapsedSeconds, overallAcc, inTargetAcc, avgDist),
                ChallengeType.Tracking => Score_TrackingMode_Breakdown(ChallengeTotalTargets, _trackingDurationSeconds, elapsedSeconds, overallAcc, inTargetAcc, avgDist),
                _ => new ScoreBreakdown()
            };

            int finalScore = sb.FinalScore;
            _coinsAwarded = finalScore;

            double secondsPerTarget = elapsedSeconds / Math.Max(1, ChallengeTotalTargets);
            string formattedTime = $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}:{elapsed.Milliseconds:D3}";

            RecordChallengeRun(
                score: finalScore,
                coins: finalScore,
                elapsedSeconds: elapsedSeconds,
                pace: secondsPerTarget,
                acc: overallAcc,
                inTgt: inTargetAcc,
                avgDist: avgDist,
                misses: misses,
                heatmap: _challengeHeatmapSnapshot ?? new HeatmapSnapshot()
            );

            // Build scorecard UI
            StartupMessage.Visibility = Visibility.Visible;
            StartupMessageStack.Children.Clear();
            _challengeScorecardVisible = true;

            challengeArmed = false;
            challengeInputLocked = false;

            string title = _challengeType switch
            {
                ChallengeType.Speed => "SPEED CHALLENGE COMPLETE",
                ChallengeType.Accuracy => "ACCURACY CHALLENGE COMPLETE",
                ChallengeType.Tracking => "TRACKING CHALLENGE COMPLETE",
                _ => "CHALLENGE COMPLETE"
            };

            AddBorderLine("║╔══════════════════════════════════════╗║");
            AddLine($"║║{CenterInBox(title, 36)}       ║║", 16, FontWeights.Bold, Brushes.White, align: TextAlignment.Left);
            AddBorderLine("║╠══════════════════════════════════════╣║");

            var paceBrush = Grade_LowerBetter(secondsPerTarget, goodMax: 1.00, okMax: 1.40, gradient: true);
            var accBrush = Grade_HigherBetter(overallAcc, ok: 0.80, good: 0.92, gradient: true);
            var inTBrush = Grade_HigherBetter(inTargetAcc, ok: 0.65, good: 0.80, gradient: true);
            var missBrush = Grade_LowerBetter(misses, goodMax: 2, okMax: 6, gradient: true);
            var distBrush = Grade_LowerBetter(avgDist, goodMax: 10, okMax: 20, gradient: true);

            AddStatLine("Targets :", $"{ChallengeTotalTargets}", BrushDim, 0);
            AddStatLine("Misses  :", $"{misses}", missBrush, sb.BonusMisses, bold: (_challengeType == ChallengeType.Speed));

            AddStatLine("Time    :", $"{formattedTime}", (_challengeType == ChallengeType.Speed) ? Brushes.White : BrushDim, sb.BaseScore, bold: (_challengeType == ChallengeType.Speed));
            if (_challengeType != ChallengeType.Tracking)
            {
                AddStatLine("Pace    :", $"{secondsPerTarget:F2} sec/target", paceBrush, sb.BonusPace, bold: (_challengeType == ChallengeType.Speed));
            }

            AddLine("");

            AddStatLine("Acc     :", $"{overallAcc * 100:F1}%", accBrush, sb.BonusOverallAcc, bold: (_challengeType != ChallengeType.Speed));
            AddStatLine("In-Tgt  :", $"{inTargetAcc * 100:F1}%", inTBrush, sb.BonusInTargetAcc, bold: (_challengeType != ChallengeType.Speed));
            AddStatLine("AvgDist :", $"{avgDist:F1}px", distBrush, sb.BonusAvgDist, bold: (_challengeType != ChallengeType.Speed));

            AddLine("");
            AddLine($"FINAL SCORE: {finalScore}", 14, FontWeights.ExtraBold, Brushes.Gold, font: new FontFamily("Consolas"));
            AddLine($"Coins: +{_coinsAwarded}", 18, FontWeights.Bold, Brushes.Gold, font: new FontFamily("Consolas"));

            AddLine("");
            AddLine("Click to REDEEM & Play Again", 14, FontWeights.Bold, Brushes.White);
            AddLine("ESC for Challenge Menu", 12, FontWeights.Normal, BrushDim);

            AddLine("");
            AddBorderLine("╚══════════════════════════════════════╝");

            StartupMessage.IsHitTestVisible = true;

            // Let UI breathe
            await Task.Delay(1);
        }

        private void RecordChallengeRun(int score, int coins, double elapsedSeconds, double pace, double acc, double inTgt, double avgDist, int misses, HeatmapSnapshot heatmap)
        {
            var rec = new ChallengeRunRecord
            {
                When = DateTime.Now,
                Mode = _challengeType.ToString(),
                Setting = (_challengeType == ChallengeType.Tracking) ? $"{_trackingDurationSeconds}s" : $"{_challengeTargetCount}",
                Targets = ChallengeTotalTargets,
                Hits = challengeHits,
                Misses = misses,
                Seconds = elapsedSeconds,
                Pace = pace,
                Acc = acc,
                InTgt = inTgt,
                AvgDist = avgDist,
                Score = score,
                Coins = coins,
                Heatmap = heatmap,
                HeatmapImage = HeatmapThumbRenderer.RenderCircular(heatmap, sizePx: 96)
            };

            _challengeRuns.Insert(0, rec);

            while (_challengeRuns.Count > 10)
                _challengeRuns.RemoveAt(_challengeRuns.Count - 1);
        }

        // Scorecard helpers
        private void SetStartupText(string msg)
        {
            StartupMessageStack.Children.Clear();

            StartupMessageStack.Children.Add(new TextBlock
            {
                Text = msg,
                Foreground = Brushes.White,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0)
            });

            StartupMessageText.Inlines.Clear();
        }

        private string CenterInBox(string text, int innerWidth)
        {
            if (text.Length >= innerWidth)
                return text.Substring(0, innerWidth);

            int totalPadding = innerWidth - text.Length;
            int left = totalPadding / 2;
            int right = totalPadding - left;

            return new string(' ', left) + text + new string(' ', right);
        }

        private void AddBorderLine(string text, double size = 18)
        {
            AddLine(text, size, FontWeights.Bold, Brushes.White, font: new FontFamily("Consolas"));
        }

        private void AddLine(string text, double size = 14, FontWeight? weight = null, Brush? color = null, TextAlignment align = TextAlignment.Left, Thickness? margin = null, FontFamily? font = null)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = size,
                FontWeight = weight ?? FontWeights.Normal,
                Foreground = color ?? Brushes.White,
                TextAlignment = align,
                Margin = margin ?? new Thickness(0, 0, 0, 0),
                TextWrapping = TextWrapping.NoWrap,
                HorizontalAlignment = HorizontalAlignment.Left,
                FontFamily = font ?? new FontFamily("Consolas")
            };

            StartupMessageStack.Children.Add(tb);
        }

        private void AddStatLine(
            string label,
            string value,
            Brush valueBrush,
            int bonusPoints,
            bool bold = false,
            Brush? labelBrush = null,
            Brush? zeroBonusBrush = null)
        {
            labelBrush ??= BrushDim;
            zeroBonusBrush ??= new SolidColorBrush(Color.FromRgb(140, 140, 140));

            var row = new Grid
            {
                Width = ScorecardRowWidth,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 2, 0, 2)
            };

            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ColLabel) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ColValue) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ColBonus) });

            var labelTb = new TextBlock
            {
                Text = label,
                Foreground = labelBrush,
                FontSize = 14,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                FontFamily = new FontFamily("Consolas"),
                VerticalAlignment = VerticalAlignment.Center
            };

            var valueTb = new TextBlock
            {
                Text = value,
                Foreground = valueBrush,
                FontSize = 14,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                FontFamily = new FontFamily("Consolas"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            bool isNegative = bonusPoints < 0;
            string sign = isNegative ? "-" : "+";
            int abs = Math.Abs(bonusPoints);

            var bonusTb = new TextBlock
            {
                Text = $"({sign}{abs})",
                Foreground = (bonusPoints == 0)
                    ? zeroBonusBrush
                    : (isNegative ? Brushes.OrangeRed : Brushes.LimeGreen),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Consolas"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = (bonusPoints == 0) ? 0.75 : 1.0
            };

            Grid.SetColumn(labelTb, 0);
            Grid.SetColumn(valueTb, 1);
            Grid.SetColumn(bonusTb, 2);

            row.Children.Add(labelTb);
            row.Children.Add(valueTb);
            row.Children.Add(bonusTb);

            StartupMessageStack.Children.Add(row);
        }

        private Brush Spectrum_0to1(double t)
        {
            t = Math.Max(0, Math.Min(1, t));

            Color c;
            if (t < 0.5)
            {
                double u = t / 0.5;
                c = Lerp(Color.FromRgb(255, 80, 80), Color.FromRgb(255, 215, 0), u);
            }
            else
            {
                double u = (t - 0.5) / 0.5;
                c = Lerp(Color.FromRgb(255, 215, 0), Color.FromRgb(80, 255, 120), u);
            }

            return new SolidColorBrush(c);
        }

        private static Color Lerp(Color a, Color b, double t)
        {
            byte r = (byte)(a.R + (b.R - a.R) * t);
            byte g = (byte)(a.G + (b.G - a.G) * t);
            byte bl = (byte)(a.B + (b.B - a.B) * t);
            return Color.FromRgb(r, g, bl);
        }

        private Brush Grade_HigherBetter(double value01, double ok, double good, bool gradient = true)
        {
            value01 = Math.Max(0, Math.Min(1, value01));
            return gradient ? Spectrum_0to1(value01)
                : (value01 >= good ? Brushes.LimeGreen : (value01 >= ok ? Brushes.Gold : Brushes.OrangeRed));
        }

        private Brush Grade_LowerBetter(double value, double goodMax, double okMax, bool gradient = true)
        {
            if (!gradient)
            {
                if (value <= goodMax) return Brushes.LimeGreen;
                if (value <= okMax) return Brushes.Gold;
                return Brushes.OrangeRed;
            }

            double t = 1.0 - ((value - goodMax) / Math.Max(0.0001, (okMax - goodMax)));
            t = Math.Max(0, Math.Min(1, t));
            return Spectrum_0to1(t);
        }

        // =========================================================
        // CHALLENGE SCORECARD CLICK ROUTE (redeem)
        // =========================================================
        private void Scene_GameHost_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_currentScene != Scene.Challenge) return;

            if (_challengeScorecardVisible)
            {
                // Redeem coins now
                RedeemScorecardCoinsAndRearm();
                e.Handled = true;
                return;
            }
        }

        private void Scene_GameHost_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_currentScene != Scene.Challenge) return;

            if (_challengeScorecardVisible)
            {
                RedeemScorecardCoinsAndRearm();
                e.Handled = true;
                return;
            }
        }

        private void RedeemScorecardCoinsAndRearm()
        {
            _challengeScorecardVisible = false;

            AwardCoins(_coinsAwarded);
            _coinsAwarded = 0;
            UpdateCoinsUI();

            ResetChallengeCompletely();

            ApplyChallengeConfigToRuntime();
            RefreshChallengeMenuVisuals();

            HideAllPauseMenus();
            ChallengePauseOverlayBorder.Visibility = Visibility.Collapsed;

            challengeArmed = true;

            SetStartupText("Click ONCE to begin...");
            StartupMessage.Visibility = Visibility.Visible;
            StartupMessage.IsHitTestVisible = true;

            // Reset miss counter for next run
            _challengeClickMisses = 0;
        }

        // =========================================================
        // SETTINGS / PRESETS
        // =========================================================
        private void InitializePresets()
        {
            PresetDefault.Click += (_, _) => SetPreset(70, 500, 3000, true);
            PresetBeginner.Click += (_, _) => SetPreset(100, 700, 1800, true);
            PresetCasual.Click += (_, _) => SetPreset(60, 500, 1200, true);
            PresetPro.Click += (_, _) => SetPreset(40, 500, 1000, true);
        }

        private void SetPreset(double size, int delay, int lifetime, bool showCenter)
        {
            SizeTextBox.Text = ((int)size).ToString();
            DelayTextBox.Text = delay.ToString();
            LifetimeTextBox.Text = lifetime.ToString();

            targetSize = size;
            spawnDelay = delay;
            targetLifetime = lifetime;

            spawnTimer.Interval = TimeSpan.FromMilliseconds(spawnDelay);

            CenterToggleButton.IsChecked = showCenter;
            ToggleCenter(showCenter);
        }

        private void ToggleCenter(bool enabled) => showCenterMark = enabled;

        private static int ParseIntOrDefault(string s, int fallback)
            => int.TryParse(s, out int v) ? v : fallback;

        private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
            => e.Handled = e.Text.Any(ch => !char.IsDigit(ch));

        private void ApplySettingsButton_Click(object sender, RoutedEventArgs e)
            => ApplySettingsFromTextboxes();

        private void ApplySettingsFromTextboxes()
        {
            int size = ParseIntOrDefault(SizeTextBox.Text, (int)targetSize);
            int delay = ParseIntOrDefault(DelayTextBox.Text, spawnDelay);
            int lifetime = ParseIntOrDefault(LifetimeTextBox.Text, targetLifetime);

            targetSize = size;
            spawnDelay = delay;
            targetLifetime = lifetime;

            spawnTimer.Interval = TimeSpan.FromMilliseconds(spawnDelay);

            bool showCenter = CenterToggleButton.IsChecked ?? true;
            ToggleCenter(showCenter);

            SizeTextBox.Text = size.ToString();
            DelayTextBox.Text = delay.ToString();
            LifetimeTextBox.Text = lifetime.ToString();
        }

        // =========================================================
        // COLORS
        // =========================================================
        private void InitializeColors()
        {
            ColorBoxesPanel.Children.Clear();

            var extraColors = new List<(string Name, Color Color)>
            {
                ("Glimmerfang", Color.FromRgb(203, 11, 156)),
                ("Moonblight", Color.FromRgb(14, 189, 124)),
                ("Cinderveil", Color.FromRgb(247, 33, 77)),
                ("Duskwraith", Color.FromRgb(6, 84, 91)),
                ("Frostmorrow", Color.FromRgb(212, 241, 255)),
                ("Bloodthorn", Color.FromRgb(185, 17, 42)),
                ("Goldenhex", Color.FromRgb(248, 198, 22)),
                ("Bluepack ⌨🖱", Color.FromRgb(111, 195, 226)),
                ("Gloomolive", Color.FromRgb(97, 97, 14)),
                ("Crimsonbane", Color.FromRgb(188, 24, 64)),
                ("Aquamyst", Color.FromRgb(41, 193, 174)),
                ("Violetrune", Color.FromRgb(192, 72, 194)),
                ("Chocospell", Color.FromRgb(157, 84, 29)),
                ("⌖ Carolinkle", Color.FromRgb(171, 168, 255)),
                ("Smaragdwisp", Color.FromRgb(34, 153, 92)),
                ("Infernospark", Color.FromRgb(145, 24, 28)),
                ("Plummancy", Color.FromRgb(201, 110, 215)),
                ("Sandstormgrip", Color.FromRgb(227, 147, 77)),
                ("Indigomancer", Color.FromRgb(68, 5, 129))
            };

            foreach (PropertyInfo prop in typeof(Colors).GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                var color = (Color)prop.GetValue(null);

                var border = new Border
                {
                    Width = 16,
                    Height = 16,
                    Margin = new Thickness(1),
                    Background = new SolidColorBrush(color),
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(1),
                    Tag = (prop.Name, color),
                    Cursor = Cursors.Hand
                };

                border.MouseLeftButtonDown += (_, _) =>
                {
                    targetColor = color;
                    targetColorName = prop.Name;
                    UpdateColorPreviewAndPanel(targetColor);
                };

                ColorBoxesPanel.Children.Add(border);
            }

            foreach (var (name, color) in extraColors)
            {
                var border = new Border
                {
                    Width = 16,
                    Height = 16,
                    Margin = new Thickness(1),
                    Background = new SolidColorBrush(color),
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(1),
                    Tag = (name, color),
                    Cursor = Cursors.Hand
                };

                border.MouseLeftButtonDown += (_, _) =>
                {
                    targetColor = color;
                    targetColorName = name;
                    UpdateColorPreviewAndPanel(targetColor);
                };

                ColorBoxesPanel.Children.Add(border);
            }

            if (ColorBoxesPanel.Children.Count > 0 && ColorBoxesPanel.Children[0] is Border first)
            {
                var tuple = ((string, Color))first.Tag;
                targetColor = tuple.Item2;
                targetColorName = tuple.Item1;
                UpdateColorPreviewAndPanel(targetColor);
            }
        }

        private void ToggleColorPickerButton_Click(object sender, RoutedEventArgs e)
        {
            ColorPickerPanel.Visibility =
                ColorPickerPanel.Visibility == Visibility.Visible
                    ? Visibility.Collapsed
                    : Visibility.Visible;
        }

        private void UpdateColorPreviewAndPanel(Color color)
        {
            CurrentColorPreview.Background = new SolidColorBrush(color);
            CurrentColorPreview2.Background = new SolidColorBrush(color);

            foreach (var child in ColorBoxesPanel.Children)
            {
                if (child is Border border && border.Tag is ValueTuple<string, Color> t)
                {
                    border.BorderThickness = (t.Item2 == color) ? new Thickness(3) : new Thickness(1);
                }
            }
        }

        private void RandomColorButton_Click(object sender, RoutedEventArgs e)
        {
            string colorName = GetRandomTargetColorName(out Color randomColor);
            targetColor = randomColor;
            targetColorName = colorName;
            UpdateColorPreviewAndPanel(targetColor);
        }

        private string GetRandomTargetColorName(out Color color)
        {
            var props = typeof(Colors).GetProperties(BindingFlags.Public | BindingFlags.Static);
            var prop = props[rng.Next(props.Length)];
            color = (Color)prop.GetValue(null);
            return prop.Name;
        }

        // =========================================================
        // WINDOW CHROME
        // =========================================================
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                MaxRestoreButton_Click(sender, e);
                return;
            }

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MaxRestoreButton_Click(object sender, RoutedEventArgs e)
            => WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;

        private void Window_QueryCursor(object sender, QueryCursorEventArgs e)
        {
            e.Cursor = _customCursor;
            e.Handled = true;
        }

        // =========================================================
        // MENU WINDOW LOCK / RESTORE
        // =========================================================
        private void LockWindowForMenu()
        {
            WindowState = WindowState.Normal;
            ResizeMode = ResizeMode.NoResize;

            Width = MenuWidth;
            Height = MenuHeight;

            MinWidth = MaxWidth = MenuWidth;
            MinHeight = MaxHeight = MenuHeight;
        }

        private void UnlockWindowFromMenu()
        {
            ResizeMode = ResizeMode.CanResize;

            MinWidth = 600;
            MinHeight = 600;
            MaxWidth = double.PositiveInfinity;
            MaxHeight = double.PositiveInfinity;
        }

        private void ApplyDefaultGameWindowSize()
        {
            ResizeMode = ResizeMode.CanResize;

            MinWidth = 600;
            MinHeight = 600;
            MaxWidth = double.PositiveInfinity;
            MaxHeight = double.PositiveInfinity;

            WindowState = WindowState.Normal;
            Width = GameDefaultWidth;
            Height = GameDefaultHeight;
        }

        // =========================================================
        // STOP EVERYTHING (shared)
        // =========================================================
        private void StopAllActivity()
        {
            // FreeRun
            spawnTimer.Stop();
            StopAllDespawnTimers();
            _freeRunActive = false;

            // Challenge
            challengeModeActive = false;
            challengeArmed = false;
            challengeCompleted = false;
            challengeInputLocked = false;

            challengeHits = 0;
            challengeTargetsRemaining = ChallengeTotalTargets;
            challengeTargetsSpawned = 0;
            _challengeClickMisses = 0;

            challengeTargetToDot.Clear();
            ChallengeHits_List.Clear();

            // Visuals
            GameCanvas.Children.Clear();
            HeatmapCanvas.Children.Clear();

            // Tracking
            _trackingMoveTimer.Stop();
            _trackingCountdownTimer.Stop();
            _trackingVel.Clear();
            _trackingDwellSeconds.Clear();
            _trackingSecondsRemaining = 0;

            // FreeRun stats store
            _freeRunHitShots.Clear();
            _freeRunCurrent = new RunMetrics();
        }

        // =========================================================
        // PROFILE SYNC
        // =========================================================

        private void InitProfileCoinSync()
        {
            _profileSync = ProfileSync.CreateDefault(AppBootstrap.ProfileDirOverride);
            AppBootstrap.Log($"ProfilePath used: {_profileSync.ProfilePath}");
            AppBootstrap.Log(typeof(ProfileSync).Assembly.Location);

            // Initial read (ensures profile exists too)
            _coinsCached = _profileSync.ReadCoinsLocked();
            UpdateCoinsUI();

            // Live updates (BluesBar spends, AimTrain earns, etc.)
            _profileSync.CoinsChanged += coins =>
            {
                Dispatcher.Invoke(() =>
                {
                    _coinsCached = coins;
                    UpdateCoinsUI();
                });
            };

            _profileSync.StartWatching();
        }

        public void AwardCoins(int amount)
        {
            if (amount <= 0) return;

            var newTotal = _profileSync.EarnLocked(amount, reason: "AimTrain");
            _coinsCached = newTotal;
            UpdateCoinsUI();

            AppBootstrap.Log($"EarnLocked +{amount} => {newTotal}");
        }

        private void UpdateCoinsUI()
        {
            if (CoinsTextBlock == null) return;
            CoinsTextBlock.Text = $"Coins: {_coinsCached:N0}";
        }

        protected override void OnClosed(EventArgs e)
        {
            _profileSync?.Dispose();
            base.OnClosed(e);
        }

        // =========================================================
        // DEV TOOL
        // =========================================================
        private void BtnDev4_Click(object sender, RoutedEventArgs e)
        {
#if DEBUG
            _challengeTargetCount = 4;
            ChallengeTotalTargets = 4;

            RefreshChallengeMenuVisuals();
            ApplyChallengeConfigToRuntime();

            challengeArmed = true;
            challengeCompleted = false;
            challengeModeActive = false;

            StartupMessage.Visibility = Visibility.Visible;
            SetStartupText("Click ONCE to begin...");
#endif
        }

        //add this to xaml for dev tool button
        /*
        <Button x:Name="BtnDev4"
                                        Content="DEV: 4"
                                        Width="64"
                                        Height="26"
                                        Margin="0,8,0,0"
                                        FontSize="11"
                                        FontWeight="SemiBold"
                                        Foreground="White"
                                        Click="BtnDev4_Click">

                                        <Button.Style>
                                            <Style TargetType="Button" BasedOn="{StaticResource FancyButton}">

                                                <!-- Base look -->
                                                <Setter Property="Background" Value="#FF69B4"/>
                                                <!-- Hot pink -->
                                                <Setter Property="BorderBrush" Value="#9B4DCA"/>
                                                <!-- Purple -->
                                                <Setter Property="BorderThickness" Value="2"/>
                                                <Setter Property="Opacity" Value="0.85"/>
                                                <Setter Property="Padding" Value="6,2"/>

                                                <!-- Very subtle hover -->
                                                <Style.Triggers>

                                                    <Trigger Property="IsMouseOver" Value="True">
                                                        <Setter Property="Background" Value="#FF85C8"/>
                                                    </Trigger>

                                                    <Trigger Property="IsPressed" Value="True">
                                                        <Setter Property="Background" Value="#E0559F"/>
                                                    </Trigger>

                                                    <Trigger Property="IsEnabled" Value="False">
                                                        <Setter Property="Opacity" Value="0.4"/>
                                                    </Trigger>

                                                </Style.Triggers>

                                            </Style>
                                        </Button.Style>

                                    </Button>
        

         */
    }
}
