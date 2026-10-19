using System.Windows;
using SwissTechKnife.Sdk;

namespace SwissTechKnife.Samples.SystemMonitor
{
    public sealed class SystemMonitorModule : IModule
    {
        private IModuleContext _context;
        private MonitorView _view;

        public void Initialize(IModuleContext context)
        {
            _context = context;
            _context.Log.Info("System monitor ready");
        }

        public FrameworkElement CreateView() => _view = new MonitorView();

        public void Dispose()
        {
            _view?.Stop();
            _view = null;
        }
    }
}
