using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace BluesAimTrain
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        //------------------------------------------- FIELDS & MODELS -------------------------------------------//

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

        //------------------------------------------- INITIALIZATION -------------------------------------------//

        public MainWindow()
        {
            InitializeComponent();

            InitializeColors();
            InitializeTimers();
            InitializeEventHandlers();
            InitializePresets();

            StartupMessage.Visibility = Visibility.Visible;
        }

        private void InitializeColors()
        {
            targetColor = GetRandomTargetColor();
            targetColorName = targetColor.ToString();

            foreach (PropertyInfo prop in typeof(Colors).GetProperties(BindingFlags.Public | BindingFlags.Static))
                ColorPicker.Items.Add(new ComboBoxItem { Content = prop.Name });

            ColorPicker.SelectedIndex = 0;
        }

        private void InitializeTimers()
        {
            spawnTimer.Interval = TimeSpan.FromMilliseconds(spawnDelay);
            spawnTimer.Tick += SpawnTarget;
        }

        private void InitializeEventHandlers()
        {
            PreviewKeyDown += MainWindow_PreviewKeyDown;

            ResumeButton.Click += ResumeButton_Click;
            ResetButton.Click += ResetButton_Click;

            CenterToggleButton.Checked += (_, _) => ToggleCenter(true);
            CenterToggleButton.Unchecked += (_, _) => ToggleCenter(false);

            GameCanvas.MouseLeftButtonDown += HandleMissClick;
        }

        //------------------------------------------- GAME FLOW -------------------------------------------//

        private void StartGame()
        {
            isRunning = true;
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
        }

        private void ResetGame()
        {
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

        //------------------------------------------- INPUT -------------------------------------------//

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                // Toggle pause/resume
                if (PauseOverlayBorder.Visibility == Visibility.Visible)
                {
                    // Overlay visible → resume game
                    ResumeGame();
                }
                else
                {
                    // Overlay hidden → either start game or pause
                    if (!isRunning)
                    {
                        // Game not running → start game
                        StartGame();
                    }
                    else
                    {
                        // Game running → pause
                        PauseGame();
                    }
                }

                e.Handled = true;
            }
        }

        private void HandleMissClick(object sender, MouseButtonEventArgs e)
        {
            if (!isRunning) return;
            if (e.OriginalSource is Ellipse) return;

            totalShots++;
            UpdateAccuracy();
            HideStartupMessage();
        }

        //------------------------------------------- TARGET LOGIC -------------------------------------------//

        private void SpawnTarget(object sender, EventArgs e)
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

            AttachHitHandler(circle, centerDot);
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
                IsHitTestVisible = false
            };

            Canvas.SetLeft(dot, x + targetSize / 2 - 3);
            Canvas.SetTop(dot, y + targetSize / 2 - 3);
            return dot;
        }

        private void AttachHitHandler(Ellipse circle, Ellipse centerDot)
        {
            circle.MouseLeftButtonDown += (_, ev) =>
            {
                score++;
                totalShots++;

                Point clickPos = ev.GetPosition(GameCanvas);

                double cx = Canvas.GetLeft(circle) + targetSize / 2;
                double cy = Canvas.GetTop(circle) + targetSize / 2;

                HitShots.Add(new ShotRecord
                {
                    OffsetX = clickPos.X - cx,
                    OffsetY = clickPos.Y - cy,
                    Hit = true
                });

                ScoreTextBlock.Text = $"Score: {score}";
                UpdateAccuracy();

                GameCanvas.Children.Remove(circle);
                if (centerDot != null) GameCanvas.Children.Remove(centerDot);
            };
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
            };

            despawnTimer.Start();
        }

        //------------------------------------------- STATS -------------------------------------------//

        private void UpdateAccuracy()
        {
            AccuracyTextBlock.Text =
                $"Accuracy: {((double)score / totalShots * 100):F2}%";
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
                SetPreset(70, 500, 3000, GetRandomTargetColorName(out targetColor), true);

            PresetBeginner.Click += (_, _) =>
                SetPreset(100, 700, 1800, "Aquamarine", true);

            PresetCasual.Click += (_, _) =>
                SetPreset(60, 500, 1200, "Orange", true);

            PresetPro.Click += (_, _) =>
                SetPreset(40, 500, 1000, "Red", true);
        }

        private void SetPreset(double size, int delay, int lifetime, string colorName, bool showCenter)
        {
            SizeSlider.Value = size;
            DelaySlider.Value = delay;
            LifetimeSlider.Value = lifetime;

            targetSize = size;
            spawnDelay = delay;
            targetLifetime = lifetime;

            spawnTimer.Interval = TimeSpan.FromMilliseconds(spawnDelay);

            targetColor = (Color)ColorConverter.ConvertFromString(colorName);
            targetColorName = colorName;

            CenterToggleButton.IsChecked = showCenter;
            ToggleCenter(showCenter);
        }

        private void ToggleCenter(bool enabled)
        {
            showCenterMark = enabled;
        }

        //------------------------------------------- BUTTONS -------------------------------------------//

        private void ResumeButton_Click(object sender, RoutedEventArgs e)
        {
            string colorName = (ColorPicker.SelectedItem as ComboBoxItem)?.Content.ToString();
            targetColor = (Color)ColorConverter.ConvertFromString(colorName);
            targetColorName = colorName;

            targetSize = SizeSlider.Value;
            spawnDelay = (int)DelaySlider.Value;
            targetLifetime = (int)LifetimeSlider.Value;

            spawnTimer.Interval = TimeSpan.FromMilliseconds(spawnDelay);

            ResumeGame();
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

        private void RandomColorButton_Click(object sender, RoutedEventArgs e)
        {
            string colorName = GetRandomTargetColorName(out Color randomColor);

            targetColor = randomColor;
            targetColorName = colorName;

            var colorItem = ColorPicker.Items
                .Cast<ComboBoxItem>()
                .FirstOrDefault(c => c.Content.ToString() == colorName);

            if (colorItem != null)
                ColorPicker.SelectedItem = colorItem;
        }

        //------------------------------------------- UTILITIES -------------------------------------------//

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
