using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace BluesAimTrain
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Random rng = new Random();
        private DispatcherTimer spawnTimer = new DispatcherTimer();
        private bool isRunning = false;
        private int score = 0;
        private int totalShots = 0;
        private Color targetColor = Colors.Red;
        private double targetSize = 50;        // in pixels
        private int spawnDelay = 800;          // ms
        private int targetLifetime = 1000;     // ms
        private string targetColorName = "Red"; // store the NAME of the color

        public MainWindow()
        {
            InitializeComponent();

            // Populate ColorPicker with all Colors dynamically
            foreach (PropertyInfo prop in typeof(Colors).GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                ColorPicker.Items.Add(new ComboBoxItem { Content = prop.Name });
            }

            ColorPicker.SelectedIndex = 0; // optional: select the first color by default

            spawnTimer.Interval = TimeSpan.FromMilliseconds(800);
            spawnTimer.Tick += SpawnTarget;

            StartPauseButton.Click += StartPauseButton_Click;
            ResumeButton.Click += ResumeButton_Click;

            GameCanvas.MouseLeftButtonDown += (s, ev) =>
            {
                // Only count clicks that are NOT on a circle
                if (!(ev.OriginalSource is Ellipse))
                {
                    totalShots++;
                    AccuracyTextBlock.Text = $"Accuracy: {((double)score / totalShots * 100):F2}%";
                }
            };
        }

        private void SavePreviousStatus()
        {
            PrevScoreTextBlock.Text = $"Score: {score}";
            PrevAccuracyTextBlock.Text = $"Accuracy: {((double)score / totalShots * 100):F2}%";
            PrevTargetSettingsTextBlock.Text =
                                $"Settings:\n" +
                                $"Color = {targetColorName}\n" +
                                $"Size = {targetSize}px\n" +
                                $"Delay = {spawnDelay}ms\n" +
                                $"Lifetime = {targetLifetime}ms";
        }

        private void StartPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (isRunning)
            {
                // Pause the game
                spawnTimer.Stop();
                StartPauseButton.Content = "Start";
                isRunning = false;

                // Update previous stats
                PrevScoreTextBlock.Text = $"Score: {score}";
                PrevAccuracyTextBlock.Text = $"Accuracy: {((double)score / totalShots * 100):F2}%";
                PrevTargetSettingsTextBlock.Text =
                    $"Settings:\n" +
                    $"Color = {targetColorName}\n" +
                    $"Size = {targetSize}px\n" +
                    $"Delay = {spawnDelay}ms\n" +
                    $"Lifetime = {targetLifetime}ms";

                // Show the previous stats panel
                PreviousStatsGrid.Visibility = Visibility.Visible;

                // Show pause overlay
                PauseOverlay.Visibility = Visibility.Visible;

                // Reset counters for new game
                score = 0;
                totalShots = 0;
                ScoreTextBlock.Text = "Score: 0";
                AccuracyTextBlock.Text = "Accuracy: 0%";

                GameCanvas.Children.Clear();
            }
            else
            {
                // Start game
                spawnTimer.Start();
                StartPauseButton.Content = "Pause";
                isRunning = true;
            }
        }

        // Resume game from pause overlay
        private void ResumeButton_Click(object sender, RoutedEventArgs e)
        {

            // Get selected color name from ComboBox
            string colorName = (ColorPicker.SelectedItem as ComboBoxItem).Content.ToString();

            // Convert string to Color dynamically
            targetColor = (Color)ColorConverter.ConvertFromString(colorName);

            // Store for previous stats
            targetColorName = colorName; 

            // Apply size, spawn delay, lifetime
            targetSize = SizeSlider.Value;
            spawnDelay = (int)DelaySlider.Value;
            targetLifetime = (int)LifetimeSlider.Value;

            spawnTimer.Interval = TimeSpan.FromMilliseconds(spawnDelay);

            // Hide overlay and resume game
            PauseOverlay.Visibility = Visibility.Collapsed;
            PreviousStatsGrid.Visibility = Visibility.Collapsed;
            spawnTimer.Start();
            StartPauseButton.Content = "Pause";
            isRunning = true;

        }

        private void SpawnTarget(object sender, EventArgs e)
        {
            double x = rng.NextDouble() * (GameCanvas.ActualWidth - targetSize);
            double y = rng.NextDouble() * (GameCanvas.ActualHeight - targetSize);

            Ellipse circle = new Ellipse
            {
                Width = targetSize,
                Height = targetSize,
                Fill = new SolidColorBrush(targetColor),
                StrokeThickness = 0
            };

            Canvas.SetLeft(circle, x);
            Canvas.SetTop(circle, y);
            GameCanvas.Children.Add(circle);

            // Click handler for hit
            circle.MouseLeftButtonDown += (s, ev) =>
            {
                score++;
                totalShots++;
                ScoreTextBlock.Text = $"Score: {score}";
                AccuracyTextBlock.Text = $"Accuracy: {((double)score / totalShots * 100):F2}%";
                GameCanvas.Children.Remove(circle);
            };

            // Despawn timer (for misses)
            var despawnTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(targetLifetime)
            };
            despawnTimer.Tick += (s, ev) =>
            {
                despawnTimer.Stop();
                if (GameCanvas.Children.Contains(circle))
                {
                    totalShots++;
                    AccuracyTextBlock.Text = $"Accuracy: {((double)score / totalShots * 100):F2}%";
                    GameCanvas.Children.Remove(circle);
                }
            };
            despawnTimer.Start();
        }
    }
}