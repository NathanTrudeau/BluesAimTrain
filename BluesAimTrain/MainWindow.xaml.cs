using System.Diagnostics;
using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using static System.Formats.Asn1.AsnWriter;

namespace BluesAimTrain
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        //------------------------------------------- CONSTANTS & CLASSES ----------------------------------------//

        private Cursor _customCursor;

        private readonly Random rng = new Random();
        private readonly DispatcherTimer spawnTimer = new DispatcherTimer();

        private bool isRunning = false;
        private int score = 0;
        private int totalShots = 0;

        private Color targetColor = Colors.Aquamarine;
        private string targetColorName = "Aquamarine";

        private double targetSize = 50;
        private int spawnDelay = 800;
        private int targetLifetime = 1000;

        private bool showCenterMark = true;

        private readonly List<ShotRecord> HitShots = new();

        private class ShotRecord
        {
            public double OffsetX;
            public double OffsetY;
            public bool Hit;
        }

        // ---------- Challenge Mode ----------

        private int ChallengeTotalTargets = 30;      // Total targets for this challenge

        private bool challengeModeActive = false; // Is challenge mode currently running
        private bool challengeArmed = false;        // Challenge mode selected but not started
        private int challengeHits = 0;              // How many targets the player hit
        private int challengeTargetsRemaining;      // Counts down as targets are hit
        private int challengeTargetsSpawned = 0;    // How many targets have been spawned so far
        private DateTime challengeStartTime;        // Start time for the challenge timer
        private TimeSpan challengeEndTime;          // End time for the challenge timer
        private int totalMisses = 0;                // Missed shots
        private bool challengeCompleted = false;    // Challenge finished
        private bool challengeInputLocked = false;  // Temporarily block input after challenge ends
        private readonly Dictionary<Ellipse, Ellipse> challengeTargetToDot = new();  // Track center dots per target
        private const int MaxConcurrentChallengeTargets = 3;                         // Max concurrent targets on screen
        private List<ShotRecord> ChallengeHits_List = new List<ShotRecord>();

        int ChallengeAccuracyFinalPoints = 0;  // The integer points contribution from accuracy
        double ChallengeAccuracyPercent = 0.0; // Accuracy as a 0–100% value
        private int ChallengeTimeScore;
        private int ChallengeMissPenalty;
        private int ChallengeFinalScore;
        private double ChallengeAvgDistance;
        private double ChallengeElapsedSeconds;

        //------------------------------------------- INITIALIZATION -------------------------------------------//

        public MainWindow()
        {
            InitializeComponent();
            _customCursor = new Cursor(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Assets", "blue_crosshair_default.cur"));
            challengeTargetsRemaining = ChallengeTotalTargets;

            InitializeColors();
            InitializeTimers();
            InitializeEventHandlers();
            InitializePresets();

            StartupMessage.Visibility = Visibility.Visible;
        }

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

            //add default colors
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
                    Tag = color,
                    Cursor = Cursors.Hand
                };

                border.MouseLeftButtonDown += (s, e) =>
                {
                    targetColor = color;
                    targetColorName = prop.Name;
                    UpdateColorPreviewAndPanel(targetColor);
                };

                ColorBoxesPanel.Children.Add(border);
            }

            //add extra colors
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
                    Tag = color,
                    Cursor = Cursors.Hand
                };

                border.MouseLeftButtonDown += (s, e) =>
                {
                    targetColor = color;
                    targetColorName = name;
                    UpdateColorPreviewAndPanel(targetColor);
                };

                ColorBoxesPanel.Children.Add(border);
            }

            // Initialize preview to the first color
            if (ColorBoxesPanel.Children.Count > 0 && ColorBoxesPanel.Children[0] is Border first)
            {
                targetColor = (Color)first.Tag;
                targetColorName = "Default";
                UpdateColorPreviewAndPanel(targetColor);
            }
        }


        private void InitializeTimers()
        {
            spawnTimer.Interval = TimeSpan.FromMilliseconds(spawnDelay);
            spawnTimer.Tick += SpawnTarget;
        }

        private void InitializeEventHandlers()
        {
            PreviewKeyDown += MainWindow_PreviewKeyDown;

            ResetButton.Click += ResetButton_Click;

            CenterToggleButton.Checked += (_, _) => ToggleCenter(true);
            CenterToggleButton.Unchecked += (_, _) => ToggleCenter(false);

            GameCanvas.MouseLeftButtonDown += GameCanvas_MouseLeftButtonDown;
        }

        //------------------------------------------- GAME FLOW -------------------------------------------//

        private void StartGame()
        {
            isRunning = true;
            if (!challengeModeActive) // only start normal timer if NOT in challenge
                spawnTimer.Start();

            PauseOverlayBorder.Visibility = Visibility.Collapsed;
            PreviousStatsGrid.Visibility = Visibility.Collapsed;
        }

        private void ResumeGame()
        {
            if (!isRunning)
            {
                isRunning = true;
                spawnTimer.Start();

                PauseOverlayBorder.Visibility = Visibility.Collapsed; // HIDE outer border
                PreviousStatsGrid.Visibility = Visibility.Collapsed;
            }
        }

        private void PauseGame()
        {
            if (isRunning)
            {
                isRunning = false;
                spawnTimer.Stop();

                PauseOverlayBorder.Visibility = Visibility.Visible; // SHOW outer border
                PreviousStatsGrid.Visibility = Visibility.Visible;

                SavePreviousStatus();
                DrawRunHeatmap();
            }

            // Show resume message
            StartupMessage.Visibility = Visibility.Visible;
            StartupMessageText.Text = "Press SPACE to resume";

            PauseOverlayBorder.Visibility = Visibility.Visible;
            PreviousStatsGrid.Visibility = Visibility.Visible;

            SavePreviousStatus();
            DrawRunHeatmap();
        }

        private void ResetGame()
        {
            challengeCompleted = false;
            challengeModeActive = false;
            // Pause the game if running
            if (isRunning)
                PauseGame();

            // ---------------- RESET CURRENT RUN ----------------
            score = 0;
            totalShots = 0;
            HitShots.Clear();

            GameCanvas.Children.Clear();
            HeatmapCanvas.Children.Clear();

            ScoreTextBlock.Text = "Score: 0";
            AccuracyTextBlock.Text = "Accuracy: 0%";

            // ---------------- RESET PREVIOUS STATS PANEL ----------------
            PrevScoreTextBlock.Text = "Score: 0";
            PrevAccuracyTextBlock.Text = "Accuracy: 0%";
        }

        //------------------------------ CHALLENGE MODE  lOGIC ------------------------------
        private void StartChallengeMode()
        {
            challengeModeActive = true;
            challengeCompleted = false;
            challengeHits = 0;
            challengeTargetsRemaining = ChallengeTotalTargets;
            challengeTargetsSpawned = 0;
            challengeInputLocked = false;

            GameCanvas.Children.Clear();
            challengeTargetToDot.Clear();

            isRunning = true;

            ScoreTextBlock.Text = "Score: ----";
            AccuracyTextBlock.Text = "Accuracy: ----";

            // Spawn initial 3 targets (or fewer if total < 3)
            for (int i = 0; i < Math.Min(4, ChallengeTotalTargets); i++)
                SpawnChallengeTarget();

            challengeStartTime = DateTime.Now; // reset the start time here
        }

        private async void EndChallengeModeWithStats()
        {
            double challengeScore = CalculateChallengeScore();
            isRunning = false;
            challengeModeActive = false;
            challengeCompleted = true;
            challengeInputLocked = true;
            GameCanvas.Children.Clear();

            /* DEBUG
             * 
            Debug.WriteLine($"ChallengeHits count: {ChallengeHits.Count}");
            foreach (var h in ChallengeHits_List)
                Debug.WriteLine($"OffsetX: {h.OffsetX}, OffsetY: {h.OffsetY}");
            */

            StartupMessage.Visibility = Visibility.Visible;

            // Clear canvas
            GameCanvas.Children.Clear();

            // Calculate elapsed time
            TimeSpan elapsed = DateTime.Now - challengeStartTime;
            challengeEndTime = elapsed;
            string formattedTime = $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}:{elapsed.Milliseconds:D3}";

            double accuracy = (challengeHits + totalMisses) > 0
                ? (double)challengeHits / (challengeHits + totalMisses) * 100
                : 100;

            // Update the stats text
            StartupMessageText.Text =
                $"╔═══════════════ CHALLENGE COMPLETE ═══════════════╗\n\n" +
                "\n\n                                             \r\n" +
                "                                            \r\n" +
                $" Calculating...\n" +
                "\n\n                                             \r\n" +
                "                                             \r\n" +
                $"═════════════════════════════════════════════════════\r\n";

            // Wait 2 seconds before allowing input again
            await Task.Delay(1337);
            StartupMessageText.Text = "";

            // Format elapsed time nicely
            string formattedChallengeTime = $"{(int)challengeEndTime.TotalMinutes:D2}:{(int)challengeEndTime.Seconds:D2}:{challengeEndTime.Milliseconds:D3}";

            AddLine("╔═══════════════ CHALLENGE COMPLETE ═══════════════╗", 20, FontWeights.Bold);

            AddLine("");

            AddLine("┌──────────────     OVERVIEW     ──────────────┐", 14, FontWeights.Bold);

            AddLine($" Total Targets   : {ChallengeTotalTargets}");
            AddLine($" Misses          : {totalMisses}");
            AddLine($" Overall Accuracy: {accuracy:F2}%");
            AddLine($" Time            : {formattedTime}");

            AddLine("└─────────────────────────────────────────────┘");
            AddLine("");

            AddLine("┌──────────── ACCURACY DETAILS ────────────┐", 14, FontWeights.Bold);

            AddLine($" Avg Dist to Center : {ChallengeAvgDistance:F1}px");
            AddLine($" In-target Accuracy : {ChallengeAccuracyPercent:F1}%",
                    14, FontWeights.Bold,
                    Brushes.LimeGreen);
            AddLine($" Accuracy Score     : {ChallengeAccuracyFinalPoints}",
                    14, FontWeights.Bold,
                    Brushes.LimeGreen);

            AddLine("└─────────────────────────────────────────────┘");
            AddLine("");

            AddLine("┌──────────── TIME & PENALTIES ────────────┐", 14, FontWeights.Bold);

            AddLine($" Time Taken  : {formattedChallengeTime}");

            AddLine($" Time Bonus  : +{ChallengeTimeScore}",
                    14, FontWeights.Bold,
                    Brushes.LimeGreen);

            AddLine($" Miss Penalty: -{ChallengeMissPenalty}",
                    14, FontWeights.Bold,
                    Brushes.OrangeRed);

            AddLine("└────────────────────────────────────────────┘");
            AddLine("");

            AddLine("════════════════════════════════════════════════", 16, FontWeights.Bold);

            AddLine($" FINAL SCORE: {ChallengeFinalScore:F0}",
                    28, FontWeights.ExtraBold,
                    Brushes.Gold);

            AddLine("════════════════════════════════════════════════",
                    16, FontWeights.Bold);

            string rank = GetChallengeRank(ChallengeFinalScore);

            AddLine(
                    $" RANK: {rank}",
                    24,
                    FontWeights.ExtraBold,
                    rank == "S+" ? Brushes.MediumPurple :
                    rank == "S" ? Brushes.Gold :
                    rank == "A" ? Brushes.LimeGreen :
                    rank == "B" ? Brushes.DeepSkyBlue :
                    rank == "C" ? Brushes.Gray :
                   Brushes.DarkSlateGray
            );

            AddLine("");
            AddLine("Press SPACE to play again");
            AddLine("ESC for pause menu");

            AddLine(
                "                                             \r\n" +
                "                                             \r\n" +
                "╚══════════════════════════════════════════════════╝", 20, FontWeights.Bold);
            // Clear all challenge stats immediately
            ChallengeHits_List.Clear();
            totalMisses = 0;
            ChallengeElapsedSeconds = 0;
            ChallengeAvgDistance = 0;
            ChallengeAccuracyFinalPoints = 0;
            ChallengeAccuracyPercent = 0;
            ChallengeTimeScore = 0;
            ChallengeMissPenalty = 0;
            ChallengeFinalScore = 0;
            challengeInputLocked = false;
        }
        private string GetChallengeRank(int score)
        {
            if (score >= 220_500) return "S+";
            if (score >= 205_500) return "S";
            if (score >= 193_275) return "A";
            if (score >= 185_300) return "B";
            if (score >= 142_500) return "C";
            return "D";
        }

        void AddLine( string text, double size = 14, FontWeight? weight = null, Brush color = null)
         {
            StartupMessageText.Inlines.Add(new Run(text + "\n")
            {
                FontSize = size,
                FontWeight = weight ?? FontWeights.Normal,
                Foreground = color ?? Brushes.White
            });
        }

        private double CalculateChallengeScore()
        {
            if (ChallengeHits_List.Count == 0)
            {
                // No hits, everything is zero
                ChallengeAccuracyFinalPoints = 0;
                ChallengeAccuracyPercent = 0.0;
                ChallengeTimeScore = 0;
                ChallengeMissPenalty = 0;
                ChallengeFinalScore = 0;
                ChallengeAvgDistance = 0;
                ChallengeElapsedSeconds = 0;
                return 0;
            }

            //---------------- Hits Only ----------------//
            var hitsOnTarget = ChallengeHits_List.Where(h => h.Hit).ToList();
            int hitCount = hitsOnTarget.Count;

            //---------------- Average Accuracy to Center ----------------//
            double totalDistance = hitsOnTarget.Sum(hit => Math.Sqrt(hit.OffsetX * hit.OffsetX + hit.OffsetY * hit.OffsetY));
            ChallengeAvgDistance = totalDistance / hitCount;

            // Use full target radius for normalization to prevent factor=0
            double maxDistance = targetSize;
            double accuracyFactor = Math.Max(0.0, 1.0 - (ChallengeAvgDistance / maxDistance));

            // Accuracy is the biggest impact: scale by total targets for bonus
            ChallengeAccuracyFinalPoints = (int)(accuracyFactor * 133700);
            ChallengeAccuracyPercent = accuracyFactor * 100.0;

            //---------------- Elapsed Time ----------------//
            ChallengeElapsedSeconds = challengeEndTime.TotalSeconds;

            //---------------- Time Score ----------------//
            // Clamp accuracy to 0–1 just in case
            double safeAccuracy = Math.Max(0, Math.Min(1, accuracyFactor));
            // Time bonus scales inversely with elapsed time
            // Every second shaved matters a lot; multiply by accuracy to reward good hits
            double timeRatio = Math.Max(0.0, 1.0 - (ChallengeElapsedSeconds / 60.0));
            double timeCurve = Math.Pow(timeRatio, 1.7); // exponent controls intensity

            ChallengeTimeScore = (int)(timeCurve * 120_000 * (0.7 + 0.3 * safeAccuracy));

            //---------------- Miss Penalty ----------------//
            ChallengeMissPenalty = totalMisses * 20000; // minor penalty per miss

            //---------------- Final Score ----------------//
            ChallengeFinalScore = (ChallengeAccuracyFinalPoints + ChallengeTimeScore - ChallengeMissPenalty);

            return Math.Max(0, ChallengeFinalScore); // prevent negatives
        }

        private void RegisterChallengeHit(double offsetX, double offsetY)
        {
            if (!challengeModeActive) return;

            // Record the offset from the target center
            ChallengeHits_List.Add(new ShotRecord
            {
                OffsetX = offsetX,
                OffsetY = offsetY,
                Hit = true
            });
        }

        //------------------------------------------- INPUT -------------------------------------------//

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // ---------- SPACE: Start / Pause Game ----------
            if (e.Key == Key.Space)
            {
                // If the pause menu is currently visible → close it and show resume message
                if (PauseOverlayBorder.Visibility == Visibility.Visible)
                {
                    PauseOverlayBorder.Visibility = Visibility.Collapsed;
                    PreviousStatsGrid.Visibility = Visibility.Collapsed;

                    // Show the startup/resume overlay
                    StartupMessage.Visibility = Visibility.Visible;
                    StartupMessageText.Text = "Press SPACE to play";

                    // Reset challenge mode armed state
                    challengeArmed = false;

                    e.Handled = true;
                    return;
                }

                // Otherwise, normal space bar behavior (start/resume the game)
                if (!isRunning)
                {
                    isRunning = true;
                    StartupMessage.Visibility = Visibility.Collapsed;

                    if (challengeArmed)
                        StartChallengeMode();
                    else
                        spawnTimer.Start();
                }
                else
                {
                    isRunning = false;
                    spawnTimer.Stop();

                    StartupMessage.Visibility = Visibility.Visible;
                    StartupMessageText.Text = "Game paused\nPress SPACE to resume";
                }

                e.Handled = true;
            }

            // ---------- ESCAPE: Pause / Challenge Mode Toggle ----------
            if (e.Key == Key.Escape)
            {
                // ---------- 1) Exit challenge mode if active or armed ----------
                if (challengeModeActive || challengeArmed)
                {
                    // Stop challenge mode immediately
                    challengeModeActive = false;
                    challengeArmed = false;
                    challengeCompleted = false;
                    isRunning = false;
                    challengeInputLocked = false;

                    challengeHits = 0;
                    challengeTargetsRemaining = ChallengeTotalTargets;
                    challengeTargetsSpawned = 0;
                    totalMisses = 0;

                    GameCanvas.Children.Clear();
                    challengeTargetToDot.Clear();

                    // Restore settings from normal mode UI
                    targetSize = ParseIntOrDefault(SizeTextBox.Text, (int)targetSize);
                    spawnDelay = ParseIntOrDefault(DelayTextBox.Text, spawnDelay);
                    targetLifetime = ParseIntOrDefault(LifetimeTextBox.Text, targetLifetime);
                    spawnTimer.Interval = TimeSpan.FromMilliseconds(spawnDelay);
                    showCenterMark = CenterToggleButton.IsChecked ?? true;
                    targetSize = 120;
                    targetColor = Color.FromRgb(171, 168, 255);
                    targetColorName = "Carolinkle";

                    // Reset HUD
                    ScoreTextBlock.Text = "Score: 0";
                    AccuracyTextBlock.Text = "Accuracy: 0%";
                }

                // ---------- 2) Toggle pause menu ----------
                if (PauseOverlayBorder.Visibility == Visibility.Visible)
                {
                    // Pause menu is open → close it
                    PauseOverlayBorder.Visibility = Visibility.Collapsed;
                    PreviousStatsGrid.Visibility = Visibility.Collapsed;

                    // Show resume message
                    StartupMessage.Visibility = Visibility.Visible;
                    StartupMessageText.Text = "Press SPACE to resume";
                }
                else
                {
                    // Pause menu is closed → open it
                    PauseGame();
                }
                UpdateColorPreviewAndPanel(targetColor);

                e.Handled = true;
                return;
            }
        }
        private void GameCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!isRunning && !challengeModeActive) return;

            var clickedEllipse = e.OriginalSource as Ellipse;

            // Find the corresponding center dot if it exists
            Ellipse centerDot = null;
            foreach (var child in GameCanvas.Children.OfType<Ellipse>())
            {
                if (child.Tag is string tag && tag == "CenterDot")
                {
                    double cx = Canvas.GetLeft(child) + child.Width / 2;
                    double cy = Canvas.GetTop(child) + child.Height / 2;

                    if (clickedEllipse != null &&
                        Math.Abs(Canvas.GetLeft(clickedEllipse) + clickedEllipse.Width / 2 - cx) < 0.1 &&
                        Math.Abs(Canvas.GetTop(clickedEllipse) + clickedEllipse.Height / 2 - cy) < 0.1)
                    {
                        centerDot = child;
                        break;
                    }
                }
            }

            // ---------- CHALLENGE MODE ----------
            if (challengeModeActive && !challengeInputLocked)
            {
                if (clickedEllipse != null && challengeTargetToDot.ContainsKey(clickedEllipse)) // HIT
                {
                    challengeHits++;
                    challengeTargetsRemaining--;

                    Point clickPos = e.GetPosition(GameCanvas);
                    double cx = Canvas.GetLeft(clickedEllipse) + targetSize / 2;
                    double cy = Canvas.GetTop(clickedEllipse) + targetSize / 2;

                    var record = new ShotRecord
                    {
                        OffsetX = clickPos.X - cx,
                        OffsetY = clickPos.Y - cy,
                        Hit = true
                    };


                    RegisterChallengeHit(clickPos.X - cx, clickPos.Y - cy);
                    // Remove target + center dot
                    GameCanvas.Children.Remove(clickedEllipse);
                    if (challengeTargetToDot[clickedEllipse] != null)
                        GameCanvas.Children.Remove(challengeTargetToDot[clickedEllipse]);
                    challengeTargetToDot.Remove(clickedEllipse);

                    UpdateAccuracy();

                    // Spawn new target if total spawned < ChallengeTotalTargets
                    if (challengeTargetsSpawned < ChallengeTotalTargets)
                        SpawnChallengeTarget();

                    // ✅ Check if all targets have been hit
                    if (challengeTargetsRemaining <= 0)
                        EndChallengeModeWithStats();
                }
                else // MISS
                {
                    totalMisses++;
                }

                e.Handled = true;
                return;
            }

            // ---------- NORMAL MODE ----------
            if (clickedEllipse != null)
            {
                score++;
                totalShots++;

                Point clickPos = e.GetPosition(GameCanvas);
                double cx = Canvas.GetLeft(clickedEllipse) + targetSize / 2;
                double cy = Canvas.GetTop(clickedEllipse) + targetSize / 2;

                HitShots.Add(new ShotRecord
                {
                    OffsetX = clickPos.X - cx,
                    OffsetY = clickPos.Y - cy,
                    Hit = true
                });

                ScoreTextBlock.Text = $"Score: {score}";
                UpdateAccuracy();

                GameCanvas.Children.Remove(clickedEllipse);
                if (centerDot != null) GameCanvas.Children.Remove(centerDot);
            }
            else
            {
                totalMisses++;
                totalShots++;
                UpdateAccuracy();
            }
        }

        //------------------------------------------- TARGET LOGIC -------------------------------------------//

        private void SpawnChallengeTarget()
        {
            if (!challengeModeActive || challengeTargetsSpawned >= ChallengeTotalTargets) return;

            double x = rng.NextDouble() * (GameCanvas.ActualWidth - targetSize);
            double y = rng.NextDouble() * (GameCanvas.ActualHeight - targetSize);

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

            challengeTargetToDot[circle] = centerDot;
            challengeTargetsSpawned++;
        }
        private void SpawnTarget(object? sender, EventArgs e)
        {
            HideStartupMessage();

            double x = rng.NextDouble() * (GameCanvas.ActualWidth - targetSize);
            double y = rng.NextDouble() * (GameCanvas.ActualHeight - targetSize);

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
            StartDespawnTimer(circle, centerDot);
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

        private void StartDespawnTimer(Ellipse circle, Ellipse centerDot)
        {
            var despawnTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(targetLifetime)
            };

            despawnTimer.Tick += (_, _) =>
            {
                despawnTimer.Stop();

                if (!GameCanvas.Children.Contains(circle)) return;

                totalShots++;
                UpdateAccuracy();

                GameCanvas.Children.Remove(circle);
                if (centerDot != null) GameCanvas.Children.Remove(centerDot);

                // Challenge mode
                if (challengeModeActive && challengeTargetToDot.ContainsKey(circle))
                {
                    challengeTargetsRemaining--;
                    challengeTargetToDot.Remove(circle);

                    // End challenge if all targets are gone
                    if (challengeTargetsRemaining <= 0)
                    {
                        StartupMessage.Visibility = Visibility.Visible;
                        EndChallengeModeWithStats();
                    }
                }
            };

            despawnTimer.Start();
        }

        //------------------------------------------- STATS -------------------------------------------//

        private void UpdateAccuracy()
        {
            if (challengeModeActive)
            {
                AccuracyTextBlock.Text = "Accuracy: ----";
                ScoreTextBlock.Text = "Score: ----";
                return;
            }

            int totalAttempts = score + totalMisses;
            double accuracy = totalAttempts > 0 ? (double)score / totalAttempts * 100 : 100;
            AccuracyTextBlock.Text = $"Accuracy: {accuracy:F2}%";
        }

        private void SavePreviousStatus()
        {
            PrevScoreTextBlock.Text = $"Score: {score}";
            PrevAccuracyTextBlock.Text =
                $"Accuracy: {((double)score / totalShots * 100):F2}%";

            PrevTargetSettingsTextBlock.Text =
                $"Settings:\n" +
                $"Color = {targetColorName}\n" +
                $"Size = {targetSize}px\n" +
                $"Delay = {spawnDelay}ms\n" +
                $"Lifetime = {targetLifetime}ms";
        }

        //------------------------------------------- HEATMAP -------------------------------------------//

        private void DrawRunHeatmap()
        {
            if (HitShots.Count == 0) return;

            HeatmapCanvas.Children.Clear();

            double centerX = HeatmapCanvas.Width / 2;
            double centerY = HeatmapCanvas.Height / 2;

            double maxRadius = Math.Min(centerX, centerY) - 5;
            double maxShotDistance = targetSize * 0.75;

            //---------------- CENTER DOT ----------------//

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

            //---------------- TARGET SIZE OUTLINE ----------------//

            double targetRealRadius = targetSize / 2;
            double targetHeatmapRadius =
                (targetRealRadius / maxShotDistance) * maxRadius;

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

            //---------------- SHOT DOTS ----------------//

            foreach (var shot in HitShots.Where(s => s.Hit))
            {
                double distance = Math.Sqrt(
                    shot.OffsetX * shot.OffsetX +
                    shot.OffsetY * shot.OffsetY);

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

        //------------------------------------------- PRESETS & SETTINGS -------------------------------------------//

        private void InitializePresets()
        {
            PresetDefault.Click += (_, _) =>
                SetPreset(70, 500, 3000, true);

            PresetBeginner.Click += (_, _) =>
                SetPreset(100, 700, 1800, true);

            PresetCasual.Click += (_, _) =>
                SetPreset(60, 500, 1200, true);

            PresetPro.Click += (_, _) =>
                SetPreset(40, 500, 1000, true);

            PresetChallenge.Click += (_, _) =>
            {
                // Set challenge mode settings
                targetSize = 80;
                targetColor = Color.FromRgb(111, 195, 226);
                targetColorName = "Bluepack";

                // Close pause menu UI
                PauseOverlayBorder.Visibility = Visibility.Collapsed;
                PreviousStatsGrid.Visibility = Visibility.Collapsed;

                // Arm challenge mode without starting it
                challengeModeActive = false;   // Not running yet
                challengeArmed = true;         // custom flag for "ready to start"

                // Reset counters
                challengeHits = 0;
                challengeTargetsRemaining = ChallengeTotalTargets;
                challengeTargetsSpawned = 0;

                // Reset HUD for challenge
                ScoreTextBlock.Text = "Score: ----";
                AccuracyTextBlock.Text = "Accuracy: ----";

                // Clear any existing targets
                GameCanvas.Children.Clear();

                // Show a small "Press SPACE to start challenge" message
                StartupMessage.Visibility = Visibility.Visible;
                StartupMessageText.Text = "Press SPACE to start Challenge Mode";
            };
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

        private void ToggleCenter(bool enabled)
        {
            showCenterMark = enabled;
        }

        private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // allow only digits
            e.Handled = e.Text.Any(ch => !char.IsDigit(ch));
        }

        //------------------------------------------- BUTTONS -------------------------------------------//

        private void ColorBox_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border b && b.Tag is Color selectedColor)
            {
                targetColor = selectedColor;
                targetColorName = selectedColor.ToString();
            }
        }

        private void ToggleColorPickerButton_Click(object sender, RoutedEventArgs e)
        {
            // Toggle the new color picker panel visibility
            ColorPickerPanel.Visibility =
                ColorPickerPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
            
        }

        private void ResumeButton_Click(object sender, RoutedEventArgs e) //unused
        {

            spawnTimer.Interval = TimeSpan.FromMilliseconds(spawnDelay);

            ResumeGame();
        }

        private static int ParseIntOrDefault(string s, int fallback)
        {
            return int.TryParse(s, out int v) ? v : fallback;
        }

        private void CloseMenuButton_Click(object sender, RoutedEventArgs e) //unused
        {
            // Simulate pressing ESC
            MainWindow_PreviewKeyDown(
                this,
                new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(this),
                    0,
                    Key.Escape
                )
                {
                    RoutedEvent = Keyboard.KeyDownEvent
                }
            );
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            ResetGame();
        }

        //------------------------------------------- WINDOW CHROME -------------------------------------------//

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

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaxRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        //------------------------------------------- COLOR UTILS -------------------------------------------//

        private void UpdateColorPreviewAndPanel(Color color)
        {
            // Update the preview swatch
            CurrentColorPreview.Background = new SolidColorBrush(color);
            CurrentColorPreview2.Background = new SolidColorBrush(color);

            // Highlight the corresponding color in the ColorBoxesPanel
            foreach (var child in ColorBoxesPanel.Children)
            {
                if (child is Border border && border.Tag is Color c)
                {
                    border.BorderThickness = (c == color) ? new Thickness(3) : new Thickness(1);
                }
            }
        }

        private void HighlightSelectedColor(string colorName)
        {
            foreach (Border box in ColorBoxesPanel.Children)
            {
                box.BorderThickness = new Thickness(1);
                box.BorderBrush = Brushes.Black;

                if ((string)box.Tag == colorName)
                {
                    box.BorderThickness = new Thickness(3);
                    box.BorderBrush = Brushes.White;
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

        //------------------------------------------- UTILITIES -------------------------------------------//
        private void ApplySettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ApplySettingsFromTextboxes();
        }

        private void ApplySettingsFromTextboxes()
        {
            // Parse from textboxes (fallbacks use current values)
            int size = ParseIntOrDefault(SizeTextBox.Text, (int)targetSize);
            int delay = ParseIntOrDefault(DelayTextBox.Text, spawnDelay);
            int lifetime = ParseIntOrDefault(LifetimeTextBox.Text, targetLifetime);

            // Apply to live settings
            targetSize = size;
            spawnDelay = delay;
            targetLifetime = lifetime;

            spawnTimer.Interval = TimeSpan.FromMilliseconds(spawnDelay);

            // Apply center toggle immediately too (optional but feels right)
            bool showCenter = CenterToggleButton.IsChecked ?? true;
            ToggleCenter(showCenter);

            // Optional: reflect normalized values back in the UI
            SizeTextBox.Text = size.ToString();
            DelayTextBox.Text = delay.ToString();
            LifetimeTextBox.Text = lifetime.ToString();

            // Optional: if a preset was active, you can mark it "Custom" here
            // activePresetName = "Custom";
        }

        private void Window_QueryCursor(object sender, QueryCursorEventArgs e)
        {
            e.Cursor = _customCursor;   // or Cursors.None
            e.Handled = true;
        }

        private void HideStartupMessage()
        {
            if (StartupMessage.Visibility == Visibility.Visible)
                StartupMessage.Visibility = Visibility.Collapsed;
        }

        private Color GetRandomTargetColor()
        {
            var props = typeof(Colors).GetProperties(BindingFlags.Public | BindingFlags.Static);
            return (Color)props[rng.Next(props.Length)].GetValue(null);
        }

        private string GetRandomTargetColorName(out Color color)
        {
            var props = typeof(Colors).GetProperties(BindingFlags.Public | BindingFlags.Static);
            var prop = props[rng.Next(props.Length)];
            color = (Color)prop.GetValue(null);
            return prop.Name;
        }
    }
}
