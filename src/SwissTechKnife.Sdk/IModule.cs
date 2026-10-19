using System;
using System.Windows;

namespace SwissTechKnife.Sdk
{
    /// <summary>
    /// Entry point of a module. The host creates one instance per enabled module, on the UI thread,
    /// through a public parameterless constructor named in the package's <c>module.json</c>.
    /// </summary>
    /// <remarks>
    /// Lifecycle: constructor → <see cref="Initialize"/> → <see cref="CreateView"/> (zero or more times) →
    /// <see cref="IDisposable.Dispose"/> when the module is disabled, uninstalled or the host exits.
    /// Keep the constructor and <see cref="Initialize"/> cheap: heavy work belongs on a background thread.
    /// </remarks>
    public interface IModule : IDisposable
    {
        /// <summary>Called once before anything else. Store the context; do not block.</summary>
        void Initialize(IModuleContext context);

        /// <summary>
        /// Builds the module's page. Called the first time the user opens the module; the host keeps the
        /// returned element alive (state is preserved while navigating) until the module is disposed.
        /// </summary>
        FrameworkElement CreateView();
    }

    /// <summary>
    /// Optional: modules that must run without their page open (hooks, tray work, listeners).
    /// Requires <c>"activation": "startup"</c> in <c>module.json</c>; the host then initializes the module
    /// at startup and calls <see cref="Start"/> right after <see cref="IModule.Initialize"/>.
    /// </summary>
    public interface IBackgroundModule : IModule
    {
        /// <summary>Starts background work. Must return quickly.</summary>
        void Start();

        /// <summary>Stops background work. Called before <see cref="IDisposable.Dispose"/>.</summary>
        void Stop();
    }

    /// <summary>Optional: notified when the module's page is shown or hidden (pause timers while hidden).</summary>
    public interface IModuleViewLifecycle
    {
        void OnViewShown();

        void OnViewHidden();
    }
}
