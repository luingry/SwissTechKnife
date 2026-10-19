using System;
using System.IO;
using System.Windows.Threading;
using SwissTechKnife.Core;
using SwissTechKnife.Sdk;

namespace SwissTechKnife.Modules
{
    public sealed class ModuleContext : IModuleContext
    {
        private readonly Action<string, NotificationKind> _notify;
        private readonly Action<string> _showPage;

        public ModuleContext(InstalledModule module, string installDirectory, string dataDirectory, Version hostVersion,
            Dispatcher dispatcher, Action<string, NotificationKind> notify, Action<string> showPage)
        {
            ModuleId = module.Id;
            ModuleVersion = VersionText.ParseOrZero(module.Version);
            HostVersion = hostVersion;
            InstallDirectory = installDirectory;
            DataDirectory = dataDirectory;
            Dispatcher = dispatcher;
            Log = new ModuleLog(module.Id);
            _notify = notify;
            _showPage = showPage;
            Directory.CreateDirectory(dataDirectory);
        }

        public string ModuleId { get; }

        public Version ModuleVersion { get; }

        public Version HostVersion { get; }

        public string InstallDirectory { get; }

        public string DataDirectory { get; }

        public Dispatcher Dispatcher { get; }

        public IModuleLog Log { get; }

        public void Notify(string message, NotificationKind kind = NotificationKind.Info)
        {
            if (Dispatcher.CheckAccess())
            {
                _notify(message, kind);
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(() => _notify(message, kind)));
            }
        }

        public void ShowModulePage()
        {
            if (Dispatcher.CheckAccess())
            {
                _showPage(ModuleId);
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(() => _showPage(ModuleId)));
            }
        }

        private sealed class ModuleLog : IModuleLog
        {
            private readonly string _source;

            public ModuleLog(string id)
            {
                _source = "module:" + id;
            }

            public void Info(string message) => Core.Log.Info(_source, message);

            public void Warn(string message) => Core.Log.Warn(_source, message);

            public void Error(string message, Exception exception = null) => Core.Log.Error(_source, message, exception);
        }
    }
}
