using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SwissTechKnife.Core
{
    /// <summary>A host update failure with a message fit for the user.</summary>
    public sealed class HostUpdateException : Exception
    {
        public HostUpdateException(string message, Exception inner = null) : base(message, inner)
        {
        }
    }

    public enum InstallerStartResult
    {
        /// <summary>The installer is running elevated and waits for the host to exit.</summary>
        Started,

        /// <summary>The installer exited before starting (UAC declined, bad file…); see the exit code.</summary>
        Exited,
    }

    /// <summary>
    /// Host self-update: download the Inno Setup installer into downloads/, verify SHA-256, run it unattended.
    /// The installer waits for this process to exit, installs, and relaunches the app (see installer/SwissTechKnife.iss).
    /// The outcome is recorded in downloads/host-update.json and reported by the next start.
    /// </summary>
    public sealed class HostUpdater
    {
        private const string Source = "HostUpdate";
        private const int DownloadAttempts = 3;
        private readonly HostPaths _paths;

        public HostUpdater(HostPaths paths)
        {
            _paths = paths;
        }

        public string PendingFile => Path.Combine(_paths.Downloads, "host-update.json");

        /// <summary>Files in downloads/ that belong to the self-update (kept by the startup cleanup).</summary>
        public static bool OwnsDownload(string fileName) =>
            fileName.StartsWith("host-update", StringComparison.OrdinalIgnoreCase)
            || (fileName.StartsWith("SwissTechKnife-", StringComparison.OrdinalIgnoreCase) && fileName.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase));

        public string SignalFile => Path.Combine(_paths.Downloads, "host-update.signal");

        /// <summary>
        /// Returns the verified installer path. A previous download with the right hash is reused;
        /// network failures are retried a few times before giving up.
        /// </summary>
        public async Task<string> DownloadAsync(HostRelease release, Uri installerUri, IProgress<double> progress, CancellationToken cancellationToken)
        {
            if (!HostUpdate.IsValid(release))
            {
                throw new HostUpdateException(Strings.Get("UpdateInvalid"));
            }
            Directory.CreateDirectory(_paths.Downloads);
            string target = Path.Combine(_paths.Downloads, HostUpdate.InstallerFileName(release.Version));
            DeleteInstallersExcept(target);
            if (File.Exists(target) && await Task.Run(() => FileDownload.HashEquals(FileDownload.HashFile(target), release.Sha256), cancellationToken).ConfigureAwait(false))
            {
                Log.Info(Source, "Reusing verified installer " + target);
                progress?.Report(1);
                return target;
            }

            for (int attempt = 1; ; attempt++)
            {
                string partial = target + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".partial";
                try
                {
                    string hash = await FileDownload.CopyAsync(installerUri, partial, release.Size, progress, 1, cancellationToken).ConfigureAwait(false);
                    if (!FileDownload.HashEquals(hash, release.Sha256))
                    {
                        Log.Warn(Source, $"Hash mismatch for {release.Version}: expected {release.Sha256}, got {hash}");
                        throw new HostUpdateException(Strings.Get("UpdateHashMismatch"));
                    }
                    TryDelete(target);
                    File.Move(partial, target);
                    Log.Info(Source, $"Downloaded {installerUri} to {target}");
                    return target;
                }
                catch (Exception ex) when ((ex is HttpRequestException || ex is IOException) && attempt < DownloadAttempts)
                {
                    Log.Warn(Source, $"Download attempt {attempt} failed: {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    throw new HostUpdateException(Strings.Get("UpdateDownloadFailed"), ex);
                }
                catch (IncompleteDownloadException ex)
                {
                    throw new HostUpdateException(Strings.Get("DownloadIncomplete"), ex);
                }
                finally
                {
                    TryDelete(partial);
                }
            }
        }

        /// <summary>
        /// Re-checks the installer hash, records the attempt and starts the installer. The caller then waits with
        /// <see cref="WaitForStartAsync"/> and exits once it reports <see cref="InstallerStartResult.Started"/>.
        /// </summary>
        public async Task<Process> StartInstallerAsync(string installerPath, HostRelease release, string runningVersion, string mutexName, string appPath)
        {
            // The file sat on disk since the download: verify again right before running it.
            bool intact = await Task.Run(() => File.Exists(installerPath) && FileDownload.HashEquals(FileDownload.HashFile(installerPath), release.Sha256)).ConfigureAwait(false);
            if (!intact)
            {
                TryDelete(installerPath);
                throw new HostUpdateException(Strings.Get("InstallerChanged"));
            }

            string log = Path.Combine(_paths.Downloads, "host-update-" + VersionText.ParseOrZero(release.Version) + ".log");
            TryDelete(SignalFile);
            TryDelete(log);
            Json.WriteFileAtomic(PendingFile, new PendingHostUpdate
            {
                FromVersion = runningVersion,
                ToVersion = VersionText.ParseOrZero(release.Version).ToString(),
                Installer = installerPath,
                Log = log,
                StartedUtc = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
            });

            var info = new ProcessStartInfo(installerPath, HostUpdate.InstallerArguments(log, SignalFile, mutexName, appPath))
            {
                // Shell execute lets the installer raise its own UAC prompt and keep the original
                // (non-elevated) user to relaunch the app with.
                UseShellExecute = true,
                WorkingDirectory = _paths.Downloads,
            };
            try
            {
                Log.Info(Source, "Starting " + installerPath + " " + info.Arguments);
                return Process.Start(info) ?? throw new HostUpdateException(Strings.Get("InstallerNotStarted"));
            }
            catch (Win32Exception ex)
            {
                ClearPending();
                throw new HostUpdateException(Strings.Format("InstallerNotStartedDetail", ex.Message), ex);
            }
        }

        /// <summary>
        /// Waits until the installer signals that it is running (after any UAC prompt) or exits early.
        /// </summary>
        public async Task<(InstallerStartResult Result, int ExitCode)> WaitForStartAsync(Process installer, CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ReadSignal() != null)
                {
                    return (InstallerStartResult.Started, 0);
                }
                if (installer.HasExited)
                {
                    // The signal may have landed between the two checks.
                    if (ReadSignal() != null)
                    {
                        return (InstallerStartResult.Started, 0);
                    }
                    int code = installer.ExitCode;
                    Log.Warn(Source, "Installer exited before starting, code " + code);
                    ClearPending();
                    return (InstallerStartResult.Exited, code);
                }
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>The last attempt, with the installer's final signal ("ok"/"failed") when it wrote one.</summary>
        public PendingHostUpdate ReadPending()
        {
            if (!File.Exists(PendingFile))
            {
                return null;
            }
            try
            {
                PendingHostUpdate pending = Json.ReadFile<PendingHostUpdate>(PendingFile);
                if (pending != null)
                {
                    pending.Signal = ReadSignal();
                }
                return pending;
            }
            catch (Exception ex)
            {
                Log.Warn(Source, "Unreadable update record: " + ex.Message);
                ClearPending();
                return null;
            }
        }

        /// <summary>Forgets the attempt. On success the installer is deleted too; after a failure it is kept for a retry.</summary>
        public void Finish(PendingHostUpdate pending, bool succeeded)
        {
            ClearPending();
            if (succeeded)
            {
                DeleteInstallersExcept(null);
                // Only the latest installer log stays, for diagnostics.
                foreach (string log in Directory.EnumerateFiles(_paths.Downloads, "host-update-*.log"))
                {
                    if (!string.Equals(log, pending?.Log, StringComparison.OrdinalIgnoreCase))
                    {
                        TryDelete(log);
                    }
                }
            }
        }

        private void DeleteInstallersExcept(string keep)
        {
            foreach (string file in Directory.EnumerateFiles(_paths.Downloads, "SwissTechKnife-*-setup.exe"))
            {
                if (!string.Equals(file, keep, StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(file);
                }
            }
        }

        private void ClearPending()
        {
            TryDelete(PendingFile);
            TryDelete(PendingFile + ".bak");
            TryDelete(SignalFile);
        }

        private string ReadSignal()
        {
            try
            {
                return File.Exists(SignalFile) ? File.ReadAllText(SignalFile).Trim() : null;
            }
            catch (IOException)
            {
                // Being written right now: next poll.
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
