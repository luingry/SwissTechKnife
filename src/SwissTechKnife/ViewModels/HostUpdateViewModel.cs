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
    /// SwissTechKnife's own update: the notice in Configurações, the confirmation popup and the
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
            RequestCommand = new RelayCommand(_ => IsConfirmOpen = true, _ => IsAvailable && !_isBusy);
            CancelCommand = new RelayCommand(_ => IsConfirmOpen = false);
            ConfirmCommand = new AsyncCommand(_ => UpdateAsync(), _ => IsAvailable && !_isBusy, ReportUnexpected);
        }

        public ICommand RequestCommand { get; }

        public ICommand CancelCommand { get; }

        public ICommand ConfirmCommand { get; }

        public bool IsAvailable => HostUpdate.IsOffered(_release, _runningVersion);

        public string CurrentVersionText => "v" + _runningVersion;

        public string LatestVersionText => IsAvailable ? "v" + VersionText.ParseOrZero(_release.Version) : null;

        public string SizeText => IsAvailable && _release.Size > 0 ? ModuleItem.FormatSize(_release.Size) : null;

        public IReadOnlyList<ReleaseNoteView> Notes =>
            HostUpdate.NotesFor(_release, _runningVersion).Select(n => new ReleaseNoteView(n, false)).ToList();

        public bool HasNotes => Notes.Count > 0;

        /// <summary>Confirmation popup (release notes + Cancelar/Atualizar).</summary>
        public bool IsConfirmOpen
        {
            get => _isConfirmOpen;
            set => Set(ref _isConfirmOpen, value && IsAvailable);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (Set(ref _isBusy, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public double Progress
        {
            get => _progress;
            private set => Set(ref _progress, value);
        }

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

        /// <summary>The notice in Configurações: an update is offered, or the last attempt needs explaining.</summary>
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
            if (!IsAvailable)
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
                    _notify("SwissTechKnife atualizado para v" + pending.ToVersion + ".", NotificationKind.Success);
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
                    Error = "A atualização para v" + pending.ToVersion + " não foi concluída. Nada foi alterado; tente de novo. Registro do instalador: " + pending.Log;
                    _notify("A atualização do SwissTechKnife não foi concluída.", NotificationKind.Error);
                    break;
            }
        }

        private async Task UpdateAsync()
        {
            HostRelease release = _release;
            IsConfirmOpen = false;
            if (!HostUpdate.IsOffered(release, _runningVersion))
            {
                return;
            }
            Error = null;
            Progress = 0;
            BusyText = "Baixando a atualização…";
            IsBusy = true;
            bool exiting = false;
            try
            {
                Uri installerUri = CatalogClient.ResolvePackageUri(_catalogSource, release.Installer);
                var progress = new Progress<double>(value => Progress = value);
                string installer = await _updater.DownloadAsync(release, installerUri, progress, CancellationToken.None);

                Progress = 1;
                BusyText = "Aguardando a permissão do Windows para instalar…";
                Process process = await _updater.StartInstallerAsync(installer, release, _runningVersion, SingleInstance.MutexName, Process.GetCurrentProcess().MainModule.FileName);
                using (process)
                {
                    var (result, exitCode) = await _updater.WaitForStartAsync(process, CancellationToken.None);
                    if (result == InstallerStartResult.Exited)
                    {
                        Error = "O instalador foi cancelado ou não pôde ser executado (código " + exitCode + "). Nada foi alterado.";
                        _notify("Atualização do SwissTechKnife cancelada.", NotificationKind.Warning);
                        return;
                    }
                }

                BusyText = "Instalando. O SwissTechKnife vai fechar e abrir de novo sozinho.";
                exiting = true;
                Log.Info(Source, "Installer running; exiting for the update to " + release.Version);
                _exitForUpdate();
            }
            catch (HostUpdateException ex)
            {
                Error = ex.Message;
                _notify("Falha ao atualizar o SwissTechKnife.", NotificationKind.Error);
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
            Error = "A atualização falhou: " + ex.Message;
            _notify("Falha ao atualizar o SwissTechKnife.", NotificationKind.Error);
        }
    }
}
