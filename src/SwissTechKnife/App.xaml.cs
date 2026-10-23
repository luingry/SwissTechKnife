using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using SwissTechKnife.Core;
using SwissTechKnife.Infrastructure;
using SwissTechKnife.Modules;
using SwissTechKnife.Sdk;
using SwissTechKnife.ViewModels;
using SwissTechKnife.Views;

namespace SwissTechKnife
{
    public partial class App : Application
    {
        private const string Source = "App";
        private SingleInstance _instance;
        private SettingsStore _settings;
        private ModuleRuntime _runtime;
        private MainWindow _window;
        private TrayIcon _tray;
        private ModuleTrayIcons _moduleTray;
        private ModuleManager _manager;
        private ShellViewModel _shell;

        public static Version HostVersion { get; } = typeof(App).Assembly.GetName().Version;

        public static string HostVersionText => $"{HostVersion.Major}.{HostVersion.Minor}.{HostVersion.Build}";

        protected override void OnStartup(StartupEventArgs e)
        {
            // --updated: started by the installer after a self-update (or after it failed); the old process may still be exiting.
            bool relaunch = e.Args.Any(a => string.Equals(a, "--restart", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "--updated", StringComparison.OrdinalIgnoreCase));
            _instance = SingleInstance.Acquire(relaunch ? TimeSpan.FromSeconds(10) : TimeSpan.Zero);
            if (!_instance.IsOwner)
            {
                _instance.SignalOwner();
                _instance.Dispose();
                Shutdown();
                return;
            }

            HostPaths paths = HostPaths.Default;
            paths.EnsureCreated();
            // Multicore JIT: records startup methods and compiles them ahead on spare cores next launch.
            ProfileOptimization.SetProfileRoot(paths.Jit);
            ProfileOptimization.StartProfile("startup.jitprofile");
            Log.Start(paths.Logs);
            Log.Info(Source, "Starting " + HostVersionText);

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (s, args) => Log.Error(Source, "Unhandled exception", args.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                Log.Error(Source, "Unobserved task exception", args.Exception);
                args.SetObserved();
            };

            base.OnStartup(e);

            FrameRate.MatchDisplay();
            FrameDiagnostics.StartIfEnabled();
            if (e.Args.Contains("--diag-resources"))
            {
                DumpResources(Resources, "app");
            }

            _settings = new SettingsStore(paths);
            _settings.Load();
            Strings.SetLanguage(_settings.Current.Language);
            var state = new StateStore(paths);
            ShellViewModel shell = null;
            _runtime = new ModuleRuntime(paths, module => new ModuleContext(
                module,
                Path.Combine(paths.ModuleRoot(module.Id), module.Folder),
                paths.ModuleData(module.Id),
                HostVersion,
                Dispatcher,
                (message, kind) => shell?.Notify(message, kind),
                id =>
                {
                    _window?.BringToFront();
                    shell?.ShowModule(id);
                }));
            var catalogClient = new CatalogClient(paths) { FallbackUri = CatalogClient.BundledCatalogUri(AppDomain.CurrentDomain.BaseDirectory) };
            var manager = new ModuleManager(paths, state, catalogClient, new PackageInstaller(paths, SdkInfo.ContractVersion), _runtime);
            Func<Uri> catalogUri = () => CatalogClient.ResolveCatalogUri(
                Environment.GetEnvironmentVariable("STK_CATALOG") ?? _settings.Current.Catalog,
                AppDomain.CurrentDomain.BaseDirectory);
            string exePath = System.Reflection.Assembly.GetEntryAssembly().Location;
            shell = new ShellViewModel(manager, catalogUri, Restart, HostVersionText, _settings, exePath, ApplyTray,
                new HostUpdater(paths), ExitForUpdate);
            _manager = manager;
            _shell = shell;
            bool atLogin = e.Args.Any(a => string.Equals(a, StartupRegistration.StartupArgument, StringComparison.OrdinalIgnoreCase));

            // First paint before any disk or network work.
            try
            {
                _window = new MainWindow(shell, _settings);
                MainWindow = _window;
                _window.HideOnClose = HideOnClose;
                if (atLogin && _settings.Current.HideToTrayOnClose)
                {
                    // Started with Windows and kept in the notification area: no window until the user asks.
                }
                else
                {
                    if (atLogin)
                    {
                        _window.WindowState = WindowState.Minimized;
                    }
                    _window.Show();
                }
            }
            catch (Exception ex)
            {
                // Without a window the process would linger invisibly: report and exit.
                Log.Error(Source, "Main window failed to load", ex);
                MessageBox.Show(Strings.Format("StartupFailed", ex.GetBaseException().Message), "SwissTechKnife", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }
            // The notification-area icon is always there; WinForms loads only after first paint.
            Dispatcher.BeginInvoke(new Action(EnsureTray), DispatcherPriority.ApplicationIdle);
            _instance.ListenForActivation(() => Dispatcher.BeginInvoke(new Action(() => _window.BringToFront())));

            _ = InitializeModulesAsync(state, manager, shell);
        }

        private static async Task InitializeModulesAsync(StateStore state, ModuleManager manager, ShellViewModel shell)
        {
            try
            {
                await Task.Run(() =>
                {
                    state.Load();
                    state.CollectGarbage();
                });
                manager.LoadInstalled();
                manager.ActivateStartupModules();
            }
            catch (Exception ex)
            {
                Log.Error(Source, "Could not load installed modules", ex);
                shell.Notify(Strings.Get("LoadModulesFailed"), NotificationKind.Error);
            }
            try
            {
                await shell.Settings.HostUpdate.ReportPreviousAttemptAsync();
            }
            catch (Exception ex)
            {
                Log.Error(Source, "Could not read the last update attempt", ex);
            }
            await shell.ReloadCatalogAsync();
        }

        /// <summary>
        /// Closing the window keeps the process when the user asked for it, or when a background module (a global
        /// shortcut, a hook) would otherwise die with it. Either way the tray icon is there to reopen or exit.
        /// </summary>
        private bool HideOnClose()
        {
            // A module icon in the notification area must keep working too.
            bool background = _runtime?.HasBackgroundModules == true || _moduleTray?.Any == true;
            if (!_settings.Current.HideToTrayOnClose && !background)
            {
                return false;
            }
            EnsureTray();
            if (!_settings.Current.HideToTrayOnClose && !_backgroundHintShown)
            {
                _backgroundHintShown = true;
                _tray.ShowHint(Strings.Get("TrayHint"));
            }
            return true;
        }

        private bool _backgroundHintShown;

        /// <summary>The icon stays in the notification area whether or not the window is open; the setting only changes what closing does.</summary>
        private void ApplyTray(bool hideToTrayOnClose)
        {
            EnsureTray();
        }

        private void EnsureTray()
        {
            if (_tray == null)
            {
                CreateTray();
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void CreateTray()
        {
            _tray = new TrayIcon(() => _window?.BringToFront(), () => _window?.ExitApplication());
            _moduleTray = new ModuleTrayIcons(_manager, id =>
            {
                _window?.BringToFront();
                _shell?.ShowModule(id);
            });
        }

        /// <summary>Diagnostics: logs every resource that fails to resolve (merged-dictionary ordering bugs).</summary>
        private static void DumpResources(ResourceDictionary dictionary, string path)
        {
            foreach (object key in dictionary.Keys)
            {
                try
                {
                    object value = dictionary[key];
                    if (value == null || value == DependencyProperty.UnsetValue)
                    {
                        Log.Warn(Source, $"Resource {path}/{key} is unset");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn(Source, $"Resource {path}/{key} failed: {ex.GetBaseException().Message}");
                }
            }
            int index = 0;
            foreach (ResourceDictionary merged in dictionary.MergedDictionaries)
            {
                DumpResources(merged, path + "/" + (merged.Source?.OriginalString ?? index.ToString()));
                index++;
            }
        }

        private void Restart()
        {
            try
            {
                Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, "--restart") { UseShellExecute = false });
                Shutdown();
            }
            catch (Exception ex)
            {
                Log.Error(Source, "Restart failed", ex);
            }
        }

        private System.Threading.Timer _exitWatchdog;

        /// <summary>
        /// The installer is waiting for this process to release its mutex. A module that hangs while stopping must not
        /// hold the update back, so the process is ended outright if the normal exit takes too long.
        /// </summary>
        private void ExitForUpdate()
        {
            _exitWatchdog = new System.Threading.Timer(_ =>
            {
                Log.Warn(Source, "Exit for the update is taking too long; ending the process");
                Log.Flush();
                // The mutex goes with the process (it is owned by the UI thread, so it cannot be released here).
                Environment.Exit(0);
            }, null, TimeSpan.FromSeconds(15), System.Threading.Timeout.InfiniteTimeSpan);
            _window?.ExitApplication();
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // A faulty module must not take the whole host down: log, tell the user, keep running.
            Log.Error(Source, "UI thread exception", e.Exception);
            (_window?.DataContext as ShellViewModel)?.Notify(Strings.Get("UnexpectedError"), NotificationKind.Error);
            e.Handled = true;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _moduleTray?.Dispose();
                _tray?.Dispose();
                _runtime?.DeactivateAll();
                _settings?.Save();
                Log.Info(Source, "Exit");
                Log.Flush();
            }
            finally
            {
                _instance?.Dispose();
                base.OnExit(e);
            }
        }
    }
}
