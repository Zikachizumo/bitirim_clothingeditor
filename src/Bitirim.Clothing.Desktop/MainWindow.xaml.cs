using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Bitirim.Clothing.Desktop.Host;
using Bitirim.Clothing.Editor.Infrastructure;
using Microsoft.Web.WebView2.Core;

namespace Bitirim.Clothing.Desktop;

public partial class MainWindow : Window
{
    private const string VirtualHost = "app.bitirim.local";

    private readonly AppSession _session;
    private readonly HostBridge _bridge;
    private readonly string? _startupFile;
    private readonly DispatcherTimer _autosave = new();

    private bool _uiReady;

    public MainWindow(AppSession session, string? startupFile)
    {
        _session = session;
        _startupFile = startupFile;

        InitializeComponent();

        _bridge = new HostBridge(session.Log);
        _bridge.Send += PostToUiAsync;
        HostOperations.Register(_bridge, session, () => this);
        RegisterWindowOperations();

        SplashVersion.Text = "v" + typeof(MainWindow).Assembly.GetName().Version!.ToString(3);

        Loaded += OnLoaded;
        Closing += OnClosing;
        Drop += OnDrop;
        DragOver += OnDragOver;
    }

    // ------------------------------------------------------------------
    // startup
    // ------------------------------------------------------------------

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            SetSplash("Loading Asset Engine…", 0.25);

            // Start the asset backend while the WebView initialises. Failure is
            // not fatal: the UI opens and reports what is unavailable.
            var backendTask = _session.TryCapabilitiesAsync();

            SetSplash("Loading Editor…", 0.55);
            await InitialiseWebViewAsync();

            var capabilities = await backendTask;
            if (capabilities is null)
                _session.Log.Warn("Asset engine unavailable; real GTA assets cannot be read or written.");

            SetSplash("Ready", 1.0);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowFatal(
                "The Microsoft Edge WebView2 Runtime is required and was not found on this computer.\n\n"
                + "It ships with Windows 11 and with current Windows 10 installs. "
                + "If it is missing, it can be installed for free from Microsoft.");
        }
        catch (Exception ex)
        {
            var reference = _session.Log.Error("Startup failed", ex);
            ShowFatal($"The editor could not be started.\n\nReference: {reference}\n"
                      + $"Details are in:\n{Path.Combine(Paths.Logs, "errors.log")}");
        }
    }

    private async Task InitialiseWebViewAsync()
    {
        var environment = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: Path.Combine(Paths.Cache, "webview"),
            options: new CoreWebView2EnvironmentOptions
            {
                // The UI is local and trusted; autoplay and background throttling
                // policies only get in the way of a desktop editor.
                AdditionalBrowserArguments = "--disable-features=msWebOOUI,msPdfOOUI --autoplay-policy=no-user-gesture-required",
            });

        await WebView.EnsureCoreWebView2Async(environment);
        var core = WebView.CoreWebView2;

        var settings = core.Settings;
        settings.AreDefaultContextMenusEnabled = _session.Settings.Current.DeveloperMode;
        settings.AreDevToolsEnabled = _session.Settings.Current.DeveloperMode;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsSwipeNavigationEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;

        core.WebMessageReceived += OnWebMessage;

        // Block navigation away from the bundled UI. A stray link must never
        // turn the editor window into a browser.
        core.NavigationStarting += (_, args) =>
        {
            if (!args.Uri.StartsWith($"https://{VirtualHost}/", StringComparison.OrdinalIgnoreCase))
                args.Cancel = true;
        };
        core.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            {
                Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
            }
        };

        var webRoot = Paths.WebRoot;
        if (!Directory.Exists(webRoot))
        {
            throw new EditorException("ui_missing",
                $"The user interface files are missing from this installation ({webRoot}).");
        }

        core.SetVirtualHostNameToFolderMapping(
            VirtualHost, webRoot, CoreWebView2HostResourceAccessKind.Allow);

        core.Navigate($"https://{VirtualHost}/index.html");
    }

    private void RegisterWindowOperations()
    {
        // The UI signals it has painted; only then does the splash retire.
        _bridge.On("ui.ready", _ =>
        {
            Dispatcher.Invoke(RevealUi);
            return new
            {
                startupFile = _startupFile,
                protocol = HostBridge.ProtocolVersion,
            };
        });

        _bridge.On("window.minimize", _ =>
        {
            Dispatcher.Invoke(() => WindowState = WindowState.Minimized);
            return true;
        });

        _bridge.On("window.toggleMaximize", _ =>
        {
            Dispatcher.Invoke(() =>
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized);
            return true;
        });

        _bridge.On("window.close", _ =>
        {
            Dispatcher.Invoke(Close);
            return true;
        });

        _bridge.On("window.setTitle", args =>
        {
            var suffix = HostBridge.OptionalString(args, "title");
            Dispatcher.Invoke(() => Title = string.IsNullOrWhiteSpace(suffix)
                ? "Bitirim Clothing Creator"
                : $"{suffix} - Bitirim Clothing Creator");
            return true;
        });

        _bridge.On("devtools.open", _ =>
        {
            Dispatcher.Invoke(() =>
            {
                WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                WebView.CoreWebView2.OpenDevToolsWindow();
            });
            return true;
        });
    }

    private void RevealUi()
    {
        if (_uiReady) return;
        _uiReady = true;

        WebView.Visibility = Visibility.Visible;

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        fade.Completed += (_, _) => Splash.Visibility = Visibility.Collapsed;
        Splash.BeginAnimation(OpacityProperty, fade);

        StartAutosave();
        _session.Log.Info("Editor ready.");
    }

    // ------------------------------------------------------------------
    // messaging
    // ------------------------------------------------------------------

    private async void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string raw;
        try { raw = e.TryGetWebMessageAsString(); }
        catch { raw = e.WebMessageAsJson; }

        await _bridge.HandleAsync(raw);
    }

    private Task PostToUiAsync(string payload)
    {
        if (!Dispatcher.CheckAccess())
            return Dispatcher.InvokeAsync(() => PostToUiAsync(payload)).Task.Unwrap();

        if (WebView.CoreWebView2 is not null)
            WebView.CoreWebView2.PostWebMessageAsString(payload);

        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------
    // autosave, drag & drop, shutdown
    // ------------------------------------------------------------------

    private void StartAutosave()
    {
        _autosave.Interval = TimeSpan.FromMinutes(
            Math.Clamp(_session.Settings.Current.AutosaveMinutes, 1, 120));
        _autosave.Tick += async (_, _) =>
        {
            if (!_session.Settings.Current.AutosaveEnabled) return;
            if (_session.Current is null || !_session.Dirty) return;

            try
            {
                _session.Projects.Save(_session.Current);
                _session.Dirty = false;
                await _bridge.EmitAsync("project.autosaved", new { at = DateTimeOffset.Now });
            }
            catch (Exception ex)
            {
                _session.Log.Error("Autosave failed", ex);
            }
        };
        _autosave.Start();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;

        await _bridge.EmitAsync("shell.filesDropped", new { paths = files });
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _autosave.Stop();

        if (_session.Current is null || !_session.Dirty) return;

        var answer = MessageBox.Show(
            $"Save changes to \"{_session.Current.Document.Name}\" before closing?",
            "Bitirim Clothing Creator",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

        switch (answer)
        {
            case MessageBoxResult.Cancel:
                e.Cancel = true;
                _autosave.Start();
                break;
            case MessageBoxResult.Yes:
                try { _session.Projects.Save(_session.Current); }
                catch (Exception ex)
                {
                    _session.Log.Error("Save on exit failed", ex);
                    MessageBox.Show("The project could not be saved.", "Bitirim Clothing Creator",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    e.Cancel = true;
                }
                break;
        }
    }

    // ------------------------------------------------------------------
    // splash / fatal
    // ------------------------------------------------------------------

    private void SetSplash(string status, double progress)
    {
        SplashStatus.Text = status;
        SplashBar.BeginAnimation(WidthProperty,
            new DoubleAnimation(320 * Math.Clamp(progress, 0, 1), TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
    }

    private void ShowFatal(string message)
    {
        Splash.Visibility = Visibility.Collapsed;
        WebView.Visibility = Visibility.Collapsed;
        FatalMessage.Text = message;
        FatalPanel.Visibility = Visibility.Visible;
    }

    private void OnFatalButtonClick(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(
            "https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true });
}
