using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Forms = System.Windows.Forms;

namespace ICloudReminderSticky;

public partial class MainWindow : Window
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const string ReminderUrl = "https://www.icloud.com.cn/reminders/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AppSettings _settings;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly OverlayWindow _overlay;
    private readonly DispatcherTimer _syncTimer;
    private bool _reallyClose;
    private bool _syncing;
    private bool _desktopViewActive;
    private bool _initialDesktopModeHandled;
    private DateTime _lastPageReloadUtc = DateTime.MinValue;
    private readonly Dictionary<string, DateTime> _pendingCompletions = [];

    public MainWindow()
    {
        InitializeComponent();
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });

        _settings = AppSettings.Load();
        Width = Math.Max(MinWidth, _settings.Width);
        Height = Math.Max(MinHeight, _settings.Height);
        Topmost = _settings.Topmost;
        UpdatePinButton();

        _overlay = new OverlayWindow();
        _overlay.CompleteRequested += Overlay_CompleteRequested;
        _overlay.OpenWebRequested += (_, _) => OpenWebView();
        _overlay.SyncRequested += (_, _) => ForcePageRefresh();
        _overlay.AddRequested += async (_, _) => await OpenAddReminderEditorAsync();

        _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _syncTimer.Tick += async (_, _) =>
        {
            if (_desktopViewActive && DateTime.UtcNow - _lastPageReloadUtc >= TimeSpan.FromSeconds(20))
                ForcePageRefresh();
            else
                await RefreshDesktopRemindersAsync();
        };

        _trayIcon = CreateTrayIcon();
        Loaded += MainWindow_Loaded;
        SourceInitialized += MainWindow_SourceInitialized;
        Closing += MainWindow_Closing;
        SizeChanged += (_, _) => SaveWindowSize();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        SnapToTopRight();
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: AppSettings.BrowserDataFolder);
            await Browser.EnsureCoreWebView2Async(environment);
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            Browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            Browser.CoreWebView2.Settings.IsZoomControlEnabled = false;
            Browser.CoreWebView2.NewWindowRequested += Browser_NewWindowRequested;
            Browser.CoreWebView2.NavigationCompleted += Browser_NavigationCompleted;
            Browser.ZoomFactor = _settings.ZoomFactor;
            Browser.CoreWebView2.Navigate(ReminderUrl);
        }
        catch (Exception ex)
        {
            ShowLoadingError("无法打开 iCloud", "请确认网络和 Microsoft Edge WebView2 Runtime 正常");
            System.Windows.MessageBox.Show($"无法打开 iCloud：\n{ex.Message}",
                "iCloud 提醒便利贴", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Browser_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            ShowLoadingError("页面加载失败", "请检查网络连接，然后点击顶部刷新按钮");
            return;
        }

        LoadingPanel.Visibility = Visibility.Collapsed;
        _lastPageReloadUtc = DateTime.UtcNow;
        _syncTimer.Start();
        await Task.Delay(2200);
        await RefreshDesktopRemindersAsync(allowInitialDesktopMode: true);
    }

    private async Task RefreshDesktopRemindersAsync(bool allowInitialDesktopMode = false)
    {
        if (_syncing || Browser.CoreWebView2 is null) return;
        _syncing = true;
        try
        {
            var json = await Browser.ExecuteScriptAsync("""
                (() => {
                  const frame = document.querySelector('iframe[src*="/applications/reminders"]');
                  let doc = null;
                  try { doc = frame?.contentDocument || null; } catch { return { ready: false, items: [] }; }
                  if (!doc?.body) return { ready: false, items: [] };

                  const occurrences = new Map();
                  const items = [...doc.querySelectorAll('button.mark-completed')].map((button, index) => {
                    const label = button.getAttribute('aria-label') || '';
                    const match = label.match(/^将“([\s\S]*)”标记为已完成$/);
                    const title = (match?.[1] || label).trim();
                    const occurrence = occurrences.get(label) || 0;
                    occurrences.set(label, occurrence + 1);
                    return { index, occurrence, title, label };
                  }).filter(item => item.title.length > 0);

                  return { ready: true, items };
                })()
                """);

            var payload = JsonSerializer.Deserialize<ReminderPayload>(json, JsonOptions);
            if (payload?.Ready != true) return;

            var nowUtc = DateTime.UtcNow;
            var rawKeys = payload.Items.Select(ReminderKey).ToHashSet(StringComparer.Ordinal);
            foreach (var pending in _pendingCompletions.ToArray())
            {
                var confirmedByPage = !rawKeys.Contains(pending.Key);
                var timedOut = nowUtc - pending.Value >= TimeSpan.FromSeconds(30);
                if (confirmedByPage || timedOut) _pendingCompletions.Remove(pending.Key);
            }
            var visibleItems = payload.Items
                .Where(item => !_pendingCompletions.ContainsKey(ReminderKey(item)))
                .ToList();
            _overlay.SetItems(visibleItems);
            var shouldPersistSyncState = _settings.LastSyncedCount != payload.Items.Count ||
                _settings.LastSyncUtc is null || nowUtc - _settings.LastSyncUtc.Value >= TimeSpan.FromSeconds(30);
            if (shouldPersistSyncState)
            {
                _settings.LastSyncedCount = payload.Items.Count;
                _settings.LastSyncUtc = nowUtc;
                _settings.Save();
            }
            _trayIcon.Text = $"iCloud 提醒 · {payload.Items.Count} 项 · {DateTime.Now:HH:mm:ss}";
            if (allowInitialDesktopMode && !_initialDesktopModeHandled)
            {
                _initialDesktopModeHandled = true;
                if (_settings.DesktopMode && payload.Items.Count > 0)
                    ShowDesktopOverlay();
            }
        }
        catch
        {
            // Keep the last good desktop list if iCloud is temporarily unavailable.
        }
        finally
        {
            _syncing = false;
        }
    }

    private async void Overlay_CompleteRequested(object? sender, ReminderSnapshot reminder)
    {
        var reminderKey = ReminderKey(reminder);
        _pendingCompletions[reminderKey] = DateTime.UtcNow;
        var success = false;
        try
        {
            success = await DispatchCompletionClickAsync(reminder);
            if (success)
            {
                await Task.Delay(700);
                if (await IsReminderStillPresentAsync(reminder))
                {
                    // DOM click is a fallback for WebView builds where CDP input is unavailable.
                    await DispatchDomCompletionClickAsync(reminder);
                    await Task.Delay(450);
                }
                ForcePageRefresh();
            }
        }
        catch
        {
            success = false;
        }

        if (!success)
        {
            _pendingCompletions.Remove(reminderKey);
            _overlay.SetCompletionResult(reminder, false);
            await RefreshDesktopRemindersAsync();
        }
    }

    private async Task<bool> DispatchCompletionClickAsync(ReminderSnapshot reminder)
    {
        if (Browser.CoreWebView2 is null) return false;
        var labelJson = JsonSerializer.Serialize(reminder.Label);
        var coordinateScript = $$"""
            (() => {
              const label = {{labelJson}};
              const occurrence = {{reminder.Occurrence}};
              const frame = document.querySelector('iframe[src*="/applications/reminders"]');
              let doc = null;
              try { doc = frame?.contentDocument || null; } catch { return { found: false, x: 0, y: 0 }; }
              if (!doc || !frame) return { found: false, x: 0, y: 0 };
              const matches = [...doc.querySelectorAll('button.mark-completed')]
                .filter(button => (button.getAttribute('aria-label') || '') === label);
              const target = matches[occurrence];
              if (!target) return { found: false, x: 0, y: 0 };
              const frameRect = frame.getBoundingClientRect();
              const targetRect = target.getBoundingClientRect();
              return {
                found: true,
                x: frameRect.left + targetRect.left + targetRect.width / 2,
                y: frameRect.top + targetRect.top + targetRect.height / 2
              };
            })()
            """;
        var coordinateJson = await Browser.ExecuteScriptAsync(coordinateScript);
        var target = JsonSerializer.Deserialize<CompletionTarget>(coordinateJson, JsonOptions);
        if (target?.Found != true) return false;

        var pressed = JsonSerializer.Serialize(new
        {
            type = "mousePressed",
            x = target.X,
            y = target.Y,
            button = "left",
            clickCount = 1
        });
        var released = JsonSerializer.Serialize(new
        {
            type = "mouseReleased",
            x = target.X,
            y = target.Y,
            button = "left",
            clickCount = 1
        });
        await Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", pressed);
        await Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", released);
        return true;
    }

    private async Task DispatchDomCompletionClickAsync(ReminderSnapshot reminder)
    {
        var labelJson = JsonSerializer.Serialize(reminder.Label);
        await Browser.ExecuteScriptAsync($$"""
                (() => {
                  const label = {{labelJson}};
                  const occurrence = {{reminder.Occurrence}};
                  const frame = document.querySelector('iframe[src*="/applications/reminders"]');
                  let doc = null;
                  try { doc = frame?.contentDocument || null; } catch { return false; }
                  if (!doc) return false;
                  const matches = [...doc.querySelectorAll('button.mark-completed')]
                    .filter(button => (button.getAttribute('aria-label') || '') === label);
                  const target = matches[occurrence];
                  if (!target) return false;
                  const options = { bubbles: true, cancelable: true, view: window, button: 0 };
                  target.dispatchEvent(new PointerEvent('pointerdown', options));
                  target.dispatchEvent(new MouseEvent('mousedown', options));
                  target.dispatchEvent(new PointerEvent('pointerup', options));
                  target.dispatchEvent(new MouseEvent('mouseup', options));
                  target.click();
                  return true;
                })()
                """);
    }

    private async Task<bool> IsReminderStillPresentAsync(ReminderSnapshot reminder)
    {
        var labelJson = JsonSerializer.Serialize(reminder.Label);
        var result = await Browser.ExecuteScriptAsync($$"""
            (() => {
              const label = {{labelJson}};
              const occurrence = {{reminder.Occurrence}};
              const frame = document.querySelector('iframe[src*="/applications/reminders"]');
              let doc = null;
              try { doc = frame?.contentDocument || null; } catch { return true; }
              if (!doc) return true;
              const matches = [...doc.querySelectorAll('button.mark-completed')]
                .filter(button => (button.getAttribute('aria-label') || '') === label);
              return Boolean(matches[occurrence]);
            })()
            """);
        return string.Equals(result, "true", StringComparison.OrdinalIgnoreCase);
    }

    private async Task OpenAddReminderEditorAsync()
    {
        OpenWebView();
        if (Browser.CoreWebView2 is null) return;
        await Task.Delay(250);
        try
        {
            await Browser.ExecuteScriptAsync("""
                (() => {
                  const frame = document.querySelector('iframe[src*="/applications/reminders"]');
                  let doc = null;
                  try { doc = frame?.contentDocument || null; } catch { return false; }
                  if (!doc) return false;
                  const candidates = [...doc.querySelectorAll('[aria-label="添加新提醒事项"]')];
                  const target = candidates.find(element => {
                    const rect = element.getBoundingClientRect();
                    const style = getComputedStyle(element);
                    return rect.width > 0 && rect.height > 0 && style.visibility !== 'hidden' && style.display !== 'none';
                  }) || candidates.at(-1);
                  if (!target) return false;
                  target.click();
                  return true;
                })()
                """);
        }
        catch
        {
            // The web view remains open so the user can add the item manually.
        }
    }

    private void Browser_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (!string.IsNullOrWhiteSpace(e.Uri)) Browser.CoreWebView2.Navigate(e.Uri);
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示桌面清单", null, (_, _) => Dispatcher.Invoke(ShowDesktopOverlay));
        menu.Items.Add("打开 iCloud 网页", null, (_, _) => Dispatcher.Invoke(OpenWebView));
        menu.Items.Add("添加提醒事项", null, (_, _) => Dispatcher.Invoke(async () => await OpenAddReminderEditorAsync()));
        menu.Items.Add("立即同步", null, (_, _) => Dispatcher.Invoke(ForcePageRefresh));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("放大网页", null, (_, _) => Dispatcher.Invoke(() => ChangeZoom(0.1)));
        menu.Items.Add("缩小网页", null, (_, _) => Dispatcher.Invoke(() => ChangeZoom(-0.1)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        var icon = new Forms.NotifyIcon
        {
            Text = "iCloud 提醒便利贴",
            Icon = System.Drawing.SystemIcons.Information,
            Visible = true,
            ContextMenuStrip = menu
        };
        icon.DoubleClick += (_, _) => Dispatcher.Invoke(ToggleVisibility);
        return icon;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        RemoveSystemFrame(handle);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) SnapToTopRight();
        else if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        _settings.Topmost = Topmost;
        _settings.Save();
        UpdatePinButton();
    }

    private void UpdatePinButton()
    {
        PinButton.Content = "\uE718";
        PinButton.ToolTip = Topmost ? "取消置顶" : "置顶";
        PinButton.Foreground = Topmost
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 159, 10))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(199, 199, 204));
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        ForcePageRefresh(showLoading: true);
    }

    private void SnapButton_Click(object sender, RoutedEventArgs e) => SnapToTopRight();
    private void HideButton_Click(object sender, RoutedEventArgs e) => ShowDesktopOverlay();
    private void CloseButton_Click(object sender, RoutedEventArgs e) => ExitApplication();

    private void ChangeZoom(double delta)
    {
        _settings.ZoomFactor = Math.Clamp(_settings.ZoomFactor + delta, 0.5, 1.5);
        if (Browser.CoreWebView2 is not null) Browser.ZoomFactor = _settings.ZoomFactor;
        _settings.Save();
    }

    private void ForcePageRefresh(bool showLoading = false)
    {
        if (Browser.CoreWebView2 is null) return;
        _lastPageReloadUtc = DateTime.UtcNow;
        if (showLoading)
        {
            LoadingTitle.Text = "正在刷新 iCloud…";
            LoadingSubtitle.Text = "";
            LoadingPanel.Visibility = Visibility.Visible;
        }
        Browser.Reload();
    }

    private void ShowDesktopOverlay()
    {
        _desktopViewActive = true;
        _settings.DesktopMode = true;
        _settings.Save();
        Hide();
        _overlay.Topmost = false;
        _overlay.SnapToTopRight();
        _overlay.Show();
    }

    private void OpenWebView()
    {
        _desktopViewActive = false;
        _overlay.Hide();
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = _settings.Topmost;
    }

    private void SnapToTopRight()
    {
        const double margin = 16;
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - margin;
        Top = area.Top + margin;
    }

    private void ToggleVisibility()
    {
        if (_desktopViewActive)
        {
            if (_overlay.IsVisible) _overlay.Hide();
            else
            {
                _overlay.SnapToTopRight();
                _overlay.Show();
            }
            return;
        }

        if (IsVisible && WindowState != WindowState.Minimized && IsActive) Hide();
        else OpenWebView();
    }

    private void SaveWindowSize()
    {
        if (WindowState != WindowState.Normal || ActualWidth <= 0 || ActualHeight <= 0) return;
        _settings.Width = ActualWidth;
        _settings.Height = ActualHeight;
        _settings.Save();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_reallyClose)
        {
            e.Cancel = true;
            ShowDesktopOverlay();
            return;
        }

        _syncTimer.Stop();
        _overlay.Close();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _settings.Save();
    }

    private void ExitApplication()
    {
        _reallyClose = true;
        Close();
    }

    private void ShowLoadingError(string title, string subtitle)
    {
        LoadingTitle.Text = title;
        LoadingSubtitle.Text = subtitle;
        LoadingPanel.Visibility = Visibility.Visible;
    }

    private static string ReminderKey(ReminderSnapshot reminder) =>
        $"{reminder.Label}\u001f{reminder.Occurrence}";

    private static void RemoveSystemFrame(IntPtr handle)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        var squareCorners = 1;
        DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref squareCorners, sizeof(int));
        var noBorder = unchecked((int)0xFFFFFFFE);
        DwmSetWindowAttribute(handle, DwmwaBorderColor, ref noBorder, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);
}
