using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using SwissTechKnife.Core;
using SwissTechKnife.Infrastructure;
using SwissTechKnife.Sdk;

namespace SwissTechKnife.ViewModels
{
    /// <summary>
    /// SwissTechKnife's own update: the notice in Settings, the confirmation popup and the
    /// download → verify → installer → exit flow. The installer relaunches the app when it is done.
    /// </summary>
    public sealed class HostUpdateViewModel : ObservableObject
    {
        private const string Source = "HostUpdate";
        // A record younger than this with only the "started" signal means the installer may still be running.
        private static readonly TimeSpan InstallerGrace = TimeSpan.FromMinutes(10);
        private readonly HostUpdater _updater;
        private readonly string _runningVersion;
        private readonly Action<string, NotificationKind> _notify;
        private readonly Action _exitForUpdate;
        private HostRelease _release;
        private Uri _catalogSource;
        private string _announcedVersion;
        private bool _isConfirmOpen;
        private bool _isCurrentNotes;
        private bool _isBusy;
        private double _progress;
        private string _busyText;
        private string _error;

        public HostUpdateViewModel(HostUpdater updater, string runningVersion, Action<string, NotificationKind> notify, Action exitForUpdate)
        {
            _updater = updater;
            _runningVersion = runningVersion;
            _notify = notify;
            _exitForUpdate = exitForUpdate;
            RequestCommand = new RelayCommand(_ => ShowUpdate(), _ => IsAvailable && !_isBusy);
            ShowCurrentNotesCommand = new RelayCommand(_ => ShowCurrentNotes());
            CancelCommand = new RelayCommand(_ => IsConfirmOpen = false);
            ConfirmCommand = new AsyncCommand(_ => UpdateAsync(), _ => IsAvailable && !_isBusy, ReportUnexpected);
        }

        public ICommand RequestCommand { get; }

        /// <summary>The same popup with the running version's notes only (click on the version number).</summary>
        public ICommand ShowCurrentNotesCommand { get; }

        public ICommand CancelCommand { get; }

        public ICommand ConfirmCommand { get; }

        public bool IsAvailable => HostUpdate.IsOffered(_release, _runningVersion);

        public string CurrentVersionText => "v" + _runningVersion;

        public string LatestVersionText => IsAvailable ? "v" + VersionText.ParseOrZero(_release.Version) : null;

        public string SizeText => IsAvailable && _release.Size > 0 ? ModuleItem.FormatSize(_release.Size) : null;

        public IReadOnlyList<ReleaseNoteView> Notes => _isCurrentNotes
            ? HostUpdate.NotesOf(_release, _runningVersion).Select(n => new ReleaseNoteView(n, true)).ToList()
            : HostUpdate.NotesFor(_release, _runningVersion).Select(n => new ReleaseNoteView(n, false)).ToList();

        public bool HasNotes => Notes.Count > 0;

        /// <summary>True while the popup shows the running version's notes (nothing to confirm).</summary>
        public bool IsCurrentNotes => _isCurrentNotes;

        public bool IsUpdateConfirm => !_isCurrentNotes;

        /// <summary>The popup: release notes, plus Cancel/Update when it confirms an update.</summary>
        public bool IsConfirmOpen
        {
            get => _isConfirmOpen;
            set => Set(ref _isConfirmOpen, value && (IsAvailable || _isCurrentNotes));
        }

        /// <summary>Opens the update confirmation (release notes + install) when a newer version is offered.</summary>
        public void ShowUpdate()
        {
            if (!IsAvailable || _isBusy)
            {
                return;
            }
            SetMode(currentNotes: false);
            IsConfirmOpen = true;
        }

        private void ShowCurrentNotes()
        {
            SetMode(currentNotes: true);
            IsConfirmOpen = true;
        }

        private void SetMode(bool currentNotes)
        {
            _isCurrentNotes = currentNotes;
            OnPropertyChanged(nameof(IsCurrentNotes));
            OnPropertyChanged(nameof(IsUpdateConfirm));
            OnPropertyChanged(nameof(Notes));
            OnPropertyChanged(nameof(HasNotes));
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (Set(ref _isBusy, value))
                {
                    OnPropertyChanged(nameof(IsProgressIndeterminate));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public double Progress
        {
            get => _progress;
            private set
            {
                if (Set(ref _progress, value))
                {
                    OnPropertyChanged(nameof(IsProgressIndeterminate));
                }
            }
        }

        /// <summary>Downloaded and now waiting for UAC / the installer: no measurable progress left.</summary>
        public bool IsProgressIndeterminate => _isBusy && _progress >= 1;

        public string BusyText
        {
            get => _busyText;
            private set => Set(ref _busyText, value);
        }

        public string Error
        {
            get => _error;
            private set
            {
                if (Set(ref _error, value))
                {
                    OnPropertyChanged(nameof(HasError));
                    OnPropertyChanged(nameof(ShowNotice));
                }
            }
        }

        public bool HasError => _error != null;

        /// <summary>The notice in Settings: an update is offered, or the last attempt needs explaining.</summary>
        public bool ShowNotice => IsAvailable || HasError;

        /// <summary>Takes the host entry of a freshly loaded catalog. Returns true the first time a version is offered.</summary>
        public bool SetRelease(HostRelease release, Uri catalogSource)
        {
            if (_isBusy)
            {
                return false;
            }
            _release = release;
            _catalogSource = catalogSource;
            if (!IsAvailable && !_isCurrentNotes)
            {
                IsConfirmOpen = false;
            }
            OnAllPropertiesChanged();
            CommandManager.InvalidateRequerySuggested();
            if (IsAvailable && _announcedVersion != _release.Version)
            {
                _announcedVersion = _release.Version;
                return true;
            }
            return false;
        }

        /// <summary>Reports how the last update attempt ended (the installer relaunches the app either way).</summary>
        public async Task ReportPreviousAttemptAsync()
        {
            PendingHostUpdate pending = await Task.Run(() => _updater.ReadPending());
            if (pending == null)
            {
                return;
            }
            switch (HostUpdate.Evaluate(pending, _runningVersion))
            {
                case HostUpdateOutcome.Succeeded:
                    Log.Info(Source, $"Updated {pending.FromVersion} → {pending.ToVersion}");
                    await Task.Run(() => _updater.Finish(pending, succeeded: true));
                    _notify(Strings.Format("HostUpdated", pending.ToVersion), NotificationKind.Success);
                    break;
                case HostUpdateOutcome.Failed:
                    DateTime? started = UpdateSchedule.ParseUtc(pending.StartedUtc);
                    if (pending.Signal == "started" && started != null && DateTime.UtcNow - started.Value < InstallerGrace)
                    {
                        // Opened by hand while the installer still runs: it relaunches the app when done.
                        return;
                    }
                    Log.Warn(Source, $"Update to {pending.ToVersion} did not complete (signal: {pending.Signal ?? "none"}); log: {pending.Log}");
                    await Task.Run(() => _updater.Finish(pending, succeeded: false));
                    Error = Strings.Format("HostUpdateIncomplete", pending.ToVersion, pending.Log);
                    _notify(Strings.Get("HostUpdateIncompleteToast"), NotificationKind.Error);
                    break;
            }
        }

        private async Task UpdateAsync()
        {
            HostRelease release = _release;
            IsConfirmOpen = false;
            if (_isCurrentNotes || !HostUpdate.IsOffered(release, _runningVersion))
            {
                return;
            }
            Error = null;
            Progress = 0;
            BusyText = Strings.Get("DownloadingUpdate");
            IsBusy = true;
            bool exiting = false;
            try
            {
                Uri installerUri = CatalogClient.ResolvePackageUri(_catalogSource, release.Installer);
                var progress = new Progress<double>(value => Progress = value);
                string installer = await _updater.DownloadAsync(release, installerUri, progress, CancellationToken.None);

                Progress = 1;
                BusyText = Strings.Get("WaitingForPermission");
                var (result, exitCode) = await _updater.StartInstallerAsync(installer, release, _runningVersion, SingleInstance.MutexName,
                    Process.GetCurrentProcess().MainModule.FileName, CancellationToken.None);
                if (result == InstallerStartResult.Exited)
                {
                    Error = Strings.Format("InstallerCanceled", exitCode);
                    _notify(Strings.Get("HostUpdateCanceledToast"), NotificationKind.Warning);
                    return;
                }

                BusyText = Strings.Get("InstallingHost");
                exiting = true;
                Log.Info(Source, "Installer running; exiting for the update to " + release.Version);
                _exitForUpdate();
            }
            catch (HostUpdateException ex)
            {
                Error = ex.Message;
                _notify(Strings.Get("HostUpdateFailedToast"), NotificationKind.Error);
            }
            finally
            {
                if (!exiting)
                {
                    IsBusy = false;
                }
            }
        }

        private void ReportUnexpected(Exception ex)
        {
            Log.Error(Source, "Host update failed", ex);
            IsBusy = false;
            Error = Strings.Format("HostUpdateFailed", ex.Message);
            _notify(Strings.Get("HostUpdateFailedToast"), NotificationKind.Error);
        }
    }
}
