using System;
using System.Threading;

namespace SwissTechKnife.Infrastructure
{
    /// <summary>
    /// One host per user session: a second launch signals the first one to come forward and exits.
    /// The mutex is acquired before settings/state load so two hosts can never overwrite each other's files.
    /// </summary>
    public sealed class SingleInstance : IDisposable
    {
        private const string MutexName = @"Local\SwissTechKnife.Host";
        private const string ActivateName = @"Local\SwissTechKnife.Activate";
        private readonly Mutex _mutex;
        private readonly EventWaitHandle _activate;
        private RegisteredWaitHandle _registration;
        private bool _owned;

        private SingleInstance(Mutex mutex, EventWaitHandle activate, bool owned)
        {
            _mutex = mutex;
            _activate = activate;
            _owned = owned;
        }

        public bool IsOwner => _owned;

        /// <param name="wait">How long to wait for a previous instance to exit (restart / update relaunch).</param>
        public static SingleInstance Acquire(TimeSpan wait)
        {
            var mutex = new Mutex(false, MutexName);
            var activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateName);
            bool owned;
            try
            {
                owned = mutex.WaitOne(wait);
            }
            catch (AbandonedMutexException)
            {
                owned = true;
            }
            return new SingleInstance(mutex, activate, owned);
        }

        public void SignalOwner() => _activate.Set();

        /// <summary>Invokes <paramref name="onActivate"/> (on a pool thread) whenever another launch signals.</summary>
        public void ListenForActivation(Action onActivate)
        {
            _registration = ThreadPool.RegisterWaitForSingleObject(_activate, (state, timedOut) => onActivate(), null, Timeout.Infinite, executeOnlyOnce: false);
        }

        public void Dispose()
        {
            _registration?.Unregister(null);
            if (_owned)
            {
                _owned = false;
                _mutex.ReleaseMutex();
            }
            _mutex.Dispose();
            _activate.Dispose();
        }
    }
}
