using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using WpfButton = System.Windows.Controls.Button;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfCursors = System.Windows.Input.Cursors;
using WpfFontFamily = System.Windows.Media.FontFamily;

namespace ICloudReminderSticky;

public partial class OverlayWindow : Window
{
    public event EventHandler<ReminderSnapshot>? CompleteRequested;
    public event EventHandler? OpenWebRequested;
    public event EventHandler? SyncRequested;
    public event EventHandler? AddRequested;

    private readonly Dictionary<string, WpfButton> _buttons = [];
    private readonly Dictionary<string, Grid> _rows = [];

    public OverlayWindow()
    {
        InitializeComponent();
    }

    public void SetItems(IReadOnlyList<ReminderSnapshot> reminders)
    {
        ReminderPanel.Children.Clear();
        _buttons.Clear();
        _rows.Clear();

        foreach (var reminder in reminders)
        {
            var key = KeyFor(reminder);
            var completeButton = new WpfButton
            {
                Content = "○",
                Margin = new Thickness(0, 0, 5, 0),
                Style = (Style)FindResource("CompletionButton"),
                Tag = reminder,
                ToolTip = "标记为已完成"
            };
            completeButton.Click += CompleteButton_Click;
            _buttons[key] = completeButton;

            var title = new TextBlock
            {
                Text = reminder.Title,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(250, 250, 250)),
                FontFamily = new WpfFontFamily("Microsoft YaHei UI"),
                FontSize = 17,
                FontWeight = FontWeights.Medium,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 5,
                    ShadowDepth = 1,
                    Opacity = 0.95
                }
            };

            var row = new Grid
            {
                Margin = new Thickness(4, 2, 8, 3),
                MinHeight = 38,
                Background = WpfBrushes.Transparent
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(completeButton, 0);
            Grid.SetColumn(title, 1);
            row.Children.Add(completeButton);
            row.Children.Add(title);
            ReminderPanel.Children.Add(row);
            _rows[key] = row;
        }

        Height = reminders.Count == 0
            ? 36
            : Math.Min(MaxHeight, 36 + reminders.Count * 48);
    }

    public void SetCompletionResult(ReminderSnapshot reminder, bool success)
    {
        if (!_buttons.TryGetValue(KeyFor(reminder), out var button)) return;
        button.IsEnabled = true;
        button.Content = success ? "✓" : "○";
    }

    public void SnapToTopRight()
    {
        const double margin = 22;
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - margin;
        Top = area.Top + margin;
    }

    private void CompleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: ReminderSnapshot reminder } button) return;
        button.IsEnabled = false;
        button.Content = "✓";
        CompleteRequested?.Invoke(this, reminder);
        AnimateCompletedRow(reminder);
    }

    private void AnimateCompletedRow(ReminderSnapshot reminder)
    {
        var key = KeyFor(reminder);
        if (!_rows.TryGetValue(key, out var row)) return;

        var duration = new Duration(TimeSpan.FromMilliseconds(320));
        var shift = new TranslateTransform();
        row.RenderTransform = shift;

        var fade = new DoubleAnimation(1, 0, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        var slide = new DoubleAnimation(0, 24, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        var collapse = new DoubleAnimation(Math.Max(38, row.ActualHeight), 0, duration)
        {
            BeginTime = TimeSpan.FromMilliseconds(80),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };

        fade.Completed += (_, _) =>
        {
            ReminderPanel.Children.Remove(row);
            _rows.Remove(key);
            _buttons.Remove(key);
            Height = Math.Max(36, Math.Min(MaxHeight, 36 + ReminderPanel.Children.Count * 48));
        };

        row.BeginAnimation(OpacityProperty, fade);
        shift.BeginAnimation(TranslateTransform.XProperty, slide);
        row.BeginAnimation(HeightProperty, collapse);
    }

    private void DragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OpenWebMenuItem_Click(object sender, RoutedEventArgs e) => OpenWebRequested?.Invoke(this, EventArgs.Empty);
    private void SyncMenuItem_Click(object sender, RoutedEventArgs e) => SyncRequested?.Invoke(this, EventArgs.Empty);
    private void AddMenuItem_Click(object sender, RoutedEventArgs e) => AddRequested?.Invoke(this, EventArgs.Empty);
    private void AddButton_Click(object sender, RoutedEventArgs e) => AddRequested?.Invoke(this, EventArgs.Empty);
    private void HideButton_Click(object sender, RoutedEventArgs e) => Hide();
    private void HideMenuItem_Click(object sender, RoutedEventArgs e) => Hide();

    private static string KeyFor(ReminderSnapshot reminder) => $"{reminder.Label}\u001f{reminder.Occurrence}";
}
