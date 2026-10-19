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

        public static Version HostVersion { get; } = typeof(App).Assembly.GetName().Version;

        public static string HostVersionText => $"{HostVersion.Major}.{HostVersion.Minor}.{HostVersion.Build}";

        protected override void OnStartup(StartupEventArgs e)
        {
            bool relaunch = e.Args.Any(a => string.Equals(a, "--restart", StringComparison.OrdinalIgnoreCase));
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

            if (e.Args.Contains("--diag-resources"))
            {
                DumpResources(Resources, "app");
            }

            _settings = new SettingsStore(paths);
            _settings.Load();
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
            var manager = new ModuleManager(paths, state, new CatalogClient(paths), new PackageInstaller(paths, SdkInfo.ContractVersion), _runtime);
            Func<Uri> catalogUri = () => CatalogClient.ResolveCatalogUri(
                Environment.GetEnvironmentVariable("STK_CATALOG") ?? _settings.Current.Catalog,
                AppDomain.CurrentDomain.BaseDirectory);
            shell = new ShellViewModel(manager, catalogUri, Restart, HostVersionText);

            // First paint before any disk or network work.
            try
            {
                _window = new MainWindow(shell, _settings);
                MainWindow = _window;
                _window.Show();
            }
            catch (Exception ex)
            {
                // Without a window the process would linger invisibly: report and exit.
                Log.Error(Source, "Main window failed to load", ex);
                MessageBox.Show("O SwissTechKnife não conseguiu abrir.\n\n" + ex.GetBaseException().Message, "SwissTechKnife", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }
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
                shell.Notify("Não foi possível carregar os módulos instalados.", NotificationKind.Error);
            }
            await shell.ReloadCatalogAsync();
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

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // A faulty module must not take the whole host down: log, tell the user, keep running.
            Log.Error(Source, "UI thread exception", e.Exception);
            (_window?.DataContext as ShellViewModel)?.Notify("Um erro inesperado ocorreu e foi registrado no log.", NotificationKind.Error);
            e.Handled = true;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
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
