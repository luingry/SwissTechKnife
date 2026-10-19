using System;
using System.Windows.Threading;

namespace SwissTechKnife.Sdk
{
    /// <summary>Services the host gives each module.</summary>
    public interface IModuleContext
    {
        /// <summary>Stable module id from the catalog (e.g. <c>smooth-mice</c>).</summary>
        string ModuleId { get; }

        Version ModuleVersion { get; }

        Version HostVersion { get; }

        /// <summary>Folder the package was extracted into. Read-only: it is replaced on update.</summary>
        string InstallDirectory { get; }

        /// <summary>Per-module writable folder that survives updates (settings, caches, logs).</summary>
        string DataDirectory { get; }

        /// <summary>UI dispatcher shared by the host and every module.</summary>
        Dispatcher Dispatcher { get; }

        IModuleLog Log { get; }

        /// <summary>Shows a transient host notification (toast).</summary>
        void Notify(string message, NotificationKind kind = NotificationKind.Info);

        /// <summary>Brings the host window forward and navigates to this module's page.</summary>
        void ShowModulePage();
    }

    public interface IModuleLog
    {
        void Info(string message);

        void Warn(string message);

        void Error(string message, Exception exception = null);
    }

    public enum NotificationKind
    {
        Info,
        Success,
        Warning,
        Error,
    }
}
