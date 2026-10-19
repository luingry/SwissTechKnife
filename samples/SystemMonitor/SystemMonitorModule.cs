using System.Windows;
using SwissTechKnife.Sdk;

namespace SwissTechKnife.Samples.SystemMonitor
{
    public sealed class SystemMonitorModule : IModule, IModuleSettings
    {
        private IModuleContext _context;
        private MonitorSettings _settings;
        private MonitorView _view;

        public void Initialize(IModuleContext context)
        {
            _context = context;
            _settings = new MonitorSettings(context.DataDirectory);
            _context.Log.Info("System monitor ready");
        }

        public FrameworkElement CreateView() => _view = new MonitorView(_settings);

        public FrameworkElement CreateSettingsView() => new SettingsView(_settings);

        public void Dispose()
        {
            _view?.Stop();
            _view = null;
        }
    }
}
