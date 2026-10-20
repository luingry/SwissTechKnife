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
        // An isolated profile (STK_HOME) is an isolated instance, so a dev or test host can run next to the installed one.
        // Declared first: static initializers run in order.
        private static readonly string Suffix = ProfileSuffix();
        private static readonly string ActivateName = @"Local\SwissTechKnife.Activate" + Suffix;
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

        /// <summary>Held while the host runs; the installer waits for it to be released before replacing files.</summary>
        public static string MutexName { get; } = @"Local\SwissTechKnife.Host" + Suffix;

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

        private static string ProfileSuffix()
        {
            string home = Environment.GetEnvironmentVariable("STK_HOME");
            if (string.IsNullOrEmpty(home))
            {
                return string.Empty;
            }
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(System.IO.Path.GetFullPath(home).ToUpperInvariant()));
                return "." + BitConverter.ToString(hash, 0, 6).Replace("-", string.Empty);
            }
        }

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
