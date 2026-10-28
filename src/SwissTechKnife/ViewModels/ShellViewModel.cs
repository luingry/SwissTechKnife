using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using SwissTechKnife.Core;
using SwissTechKnife.Infrastructure;
using SwissTechKnife.Modules;
using SwissTechKnife.Sdk;

namespace SwissTechKnife.ViewModels
{
    public enum StoreTab
    {
        All,
        Available,
        Installed,
    }

    public enum DialogTab
    {
        Description,
        Notes,
        Settings,
    }

    public enum CatalogState
    {
        Loading,
        Ready,
        Offline,
        Failed,
    }

    public sealed class ShellViewModel : ObservableObject
    {
        private const string Source = "Shell";
        private readonly ModuleManager _manager;
        private readonly Func<Uri> _catalogUri;
        private readonly Action _restart;
        private readonly ListCollectionView _storeView;
        private readonly ListCollectionView _sidebarView;
        private readonly DispatcherTimer _searchDebounce;
        private string _searchText = string.Empty;
        private string _appliedSearch = string.Empty;
        private StoreTab _tab = StoreTab.All;
        private bool _isStoreActive = true;
        private ModuleItem _currentModule;
        private FrameworkElement _currentPage;
        private ModuleItem _selectedModule;
        private CatalogState _catalogState = CatalogState.Loading;
        private DialogTab _dialogTab;
        private ModuleItem _pendingUpdate;
        private FrameworkElement _settingsPage;
        private string _settingsError;
        private readonly SettingsStore _settings;
        private readonly DispatcherTimer _updateTimer;
        private bool _isSettingsActive;
        private string _storeSignature;
        private string _sidebarSignature;

        public ShellViewModel(ModuleManager manager, Func<Uri> catalogUri, Action restart, string hostVersion, SettingsStore settings, string exePath, Action<bool> applyTray, HostUpdater hostUpdater, Action exitForUpdate)
        {
            _settings = settings;
            var hostUpdate = new HostUpdateViewModel(hostUpdater, hostVersion, Notify, exitForUpdate);
            Settings = new SettingsViewModel(settings, exePath, applyTray, () => ReloadCatalogAsync(force: true, announce: true), Notify, hostUpdate);
            // Hourly look at the schedule; the check itself only runs when the chosen frequency is due.
            _updateTimer = new DispatcherTimer(TimeSpan.FromHours(1), DispatcherPriority.Background, async (s, e) =>
            {
                if (!IsCatalogLoading && UpdateSchedule.IsDue(_settings.Current.UpdateFrequency, _settings.Current.LastUpdateCheckUtc, DateTime.UtcNow))
                {
                    await ReloadCatalogAsync(force: false, announce: true);
                }
            }, Dispatcher.CurrentDispatcher);
            _manager = manager;
            _catalogUri = catalogUri;
            _restart = restart;
            HostVersion = "v" + hostVersion;

            // Installed modules come first; within each group the catalog order is kept.
            _storeView = new ListCollectionView(manager.Items) { Filter = StoreFilter, CustomSort = new InstalledFirstComparer(manager.Items) };
            ApplyStoreSections();
            _sidebarView = new ListCollectionView(manager.Items) { Filter = o => ((ModuleItem)o).IsEnabled };
            _searchDebounce = new DispatcherTimer(TimeSpan.FromMilliseconds(140), DispatcherPriority.Input, (s, e) => ApplySearch(), Dispatcher.CurrentDispatcher);
            _searchDebounce.Stop();
            manager.Changed += (s, e) => RefreshLists();
            manager.ToggleHandler = SetEnabled;

            ShowStoreCommand = new RelayCommand(_ => ShowStore());
            ShowSettingsCommand = new RelayCommand(_ => ShowSettings());
            ShowTabCommand = new RelayCommand(p => { Tab = (StoreTab)p; ShowStore(); });
            OpenDetailsCommand = new RelayCommand(p => SelectedModule = p as ModuleItem, p => p is ModuleItem);
            OpenNotesCommand = new RelayCommand(p => { SelectedModule = (ModuleItem)p; IsNotesTab = true; }, p => p is ModuleItem);
            OpenModuleSettingsCommand = new RelayCommand(p => { SelectedModule = (ModuleItem)p; IsSettingsTab = true; }, p => p is ModuleItem m && m.HasSettings);
            CloseDetailsCommand = new RelayCommand(_ => SelectedModule = null);
            InstallCommand = new AsyncCommand(p => InstallAsync((ModuleItem)p), p => p is ModuleItem m && !m.IsBusy && (m.CanInstall || m.HasUpdate), ex => ReportError(ex));
            UninstallCommand = new RelayCommand(p => Uninstall((ModuleItem)p), p => p is ModuleItem m && m.IsInstalled && !m.IsBusy);
            EnableCommand = new RelayCommand(p => SetEnabled((ModuleItem)p, true), p => p is ModuleItem m && m.IsInstalled && !m.IsEnabled && !m.IsBusy);
            DisableCommand = new RelayCommand(p => SetEnabled((ModuleItem)p, false), p => p is ModuleItem m && m.IsEnabled && !m.IsBusy);
            OpenModuleCommand = new RelayCommand(p => OpenModule((ModuleItem)p), p => p is ModuleItem m && m.IsEnabled);
            Strings.LanguageChanged += (s, e) => OnLanguageChanged();
            ReloadCatalogCommand = new AsyncCommand(_ => ReloadCatalogAsync(), _ => CatalogState != CatalogState.Loading, ex => ReportError(ex));
            CheckHostUpdateCommand = new AsyncCommand(_ => ReloadCatalogAsync(force: true, announce: true, openHostUpdate: true),
                _ => CatalogState != CatalogState.Loading && !Settings.HostUpdate.IsBusy, ex => ReportError(ex));
            RestartCommand = new RelayCommand(_ => _restart());
            DismissToastCommand = new RelayCommand(p => DismissToast(p as Toast));
            RetryPageCommand = new RelayCommand(_ => RetryPage(), _ => _currentModule != null);
            CheckModuleUpdateCommand = new AsyncCommand(p => CheckModuleUpdateAsync((ModuleItem)p),
                p => p is ModuleItem m && m.IsInstalled && !m.IsBusy && CatalogState != CatalogState.Loading, ex => ReportError(ex));
            RequestUpdateCommand = new RelayCommand(p => PendingUpdate = (ModuleItem)p, p => p is ModuleItem m && m.HasUpdate && !m.IsBusy);
            CancelUpdateCommand = new RelayCommand(_ => PendingUpdate = null);
            ConfirmUpdateCommand = new AsyncCommand(async _ =>
            {
                ModuleItem item = PendingUpdate;
                PendingUpdate = null;
                if (item != null)
                {
                    await InstallAsync(item);
                }
            }, _ => PendingUpdate != null, ex => ReportError(ex));
        }

        public string HostVersion { get; }

        public ICollectionView StoreItems => _storeView;

        public ICollectionView SidebarItems => _sidebarView;

        public ObservableCollection<Toast> Toasts { get; } = new ObservableCollection<Toast>();

        public ICommand ShowStoreCommand { get; }
        public ICommand ShowSettingsCommand { get; }

        public SettingsViewModel Settings { get; }
        public ICommand ShowTabCommand { get; }
        public ICommand OpenDetailsCommand { get; }

        /// <summary>Version link: the details dialog on its Release notes tab (Description when there are none).</summary>
        public ICommand OpenNotesCommand { get; }

        /// <summary>Module page gear: the details dialog on its Settings tab.</summary>
        public ICommand OpenModuleSettingsCommand { get; }
        public ICommand CloseDetailsCommand { get; }
        public ICommand InstallCommand { get; }
        public ICommand UninstallCommand { get; }
        public ICommand EnableCommand { get; }
        public ICommand DisableCommand { get; }
        public ICommand OpenModuleCommand { get; }
        public ICommand ReloadCatalogCommand { get; }

        /// <summary>Title-bar "Check for updates": a live catalog check; a newer SwissTechKnife opens its confirmation.</summary>
        public ICommand CheckHostUpdateCommand { get; }

        /// <summary>Per-module "Check for updates": a live catalog check; a newer version opens its confirmation.</summary>
        public ICommand CheckModuleUpdateCommand { get; }
        public ICommand RestartCommand { get; }
        public ICommand DismissToastCommand { get; }
        public ICommand RetryPageCommand { get; }
        public ICommand RequestUpdateCommand { get; }
        public ICommand CancelUpdateCommand { get; }
        public ICommand ConfirmUpdateCommand { get; }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (Set(ref _searchText, value ?? string.Empty))
                {
                    _searchDebounce.Stop();
                    _searchDebounce.Start();
                }
            }
        }

        public StoreTab Tab
        {
            get => _tab;
            set
            {
                if (Set(ref _tab, value))
                {
                    OnPropertyChanged(nameof(IsAllTab));
                    OnPropertyChanged(nameof(IsAvailableTab));
                    OnPropertyChanged(nameof(IsInstalledTab));
                    // One deferred refresh covers both the section change and the new membership.
                    using (_storeView.DeferRefresh())
                    {
                        ApplyStoreSections();
                        RefreshLists();
                    }
                    // IsEmpty is stale until the deferred refresh lands; re-read it now, or the previous
                    // tab's empty state stays on top of the new list.
                    RefreshEmptyState();
                }
            }
        }

        public bool IsAllTab
        {
            get => _tab == StoreTab.All;
            set
            {
                if (value)
                {
                    Tab = StoreTab.All;
                }
            }
        }

        public bool IsAvailableTab
        {
            get => _tab == StoreTab.Available;
            set
            {
                if (value)
                {
                    Tab = StoreTab.Available;
                }
            }
        }

        public bool IsInstalledTab
        {
            get => _tab == StoreTab.Installed;
            set
            {
                if (value)
                {
                    Tab = StoreTab.Installed;
                }
            }
        }

        public int AllCount => _manager.Items.Count;

        public int AvailableCount => _manager.Items.Count(i => !i.IsInstalled);

        public int InstalledCount => _manager.Items.Count(i => i.IsInstalled);

        public int UpdateCount => _manager.Items.Count(i => i.HasUpdate);

        public bool HasUpdates => UpdateCount > 0;

        public bool HasSidebarModules => _manager.Items.Any(i => i.IsEnabled);

        public bool IsStoreActive
        {
            get => _isStoreActive;
            private set
            {
                if (Set(ref _isStoreActive, value))
                {
                    OnPropertyChanged(nameof(IsModulePageActive));
                }
            }
        }

        public bool IsSettingsActive
        {
            get => _isSettingsActive;
            private set
            {
                if (Set(ref _isSettingsActive, value))
                {
                    OnPropertyChanged(nameof(IsModulePageActive));
                }
            }
        }

        public bool IsModulePageActive => !_isStoreActive && !_isSettingsActive;

        /// <summary>Module whose page is shown (sidebar selection).</summary>
        public ModuleItem CurrentModule
        {
            get => _currentModule;
            set
            {
                if (value == null)
                {
                    if (_currentModule != null && IsModulePageActive)
                    {
                        // The sidebar list cleared its selection (item filtered out): fall back to the store.
                        ShowStore();
                    }
                    return;
                }
                OpenModule(value);
            }
        }

        public FrameworkElement CurrentPage
        {
            get => _currentPage;
            private set => Set(ref _currentPage, value);
        }

        /// <summary>Module shown in the details dialog (null = closed).</summary>
        public ModuleItem SelectedModule
        {
            get => _selectedModule;
            set
            {
                if (_selectedModule != null)
                {
                    _selectedModule.ConfirmingUninstall = false;
                    if (!_selectedModule.IsBusy)
                    {
                        _selectedModule.Error = null;
                    }
                }
                if (Set(ref _selectedModule, value))
                {
                    PendingUpdate = null;
                    ShowDescriptionTab();
                    OnPropertyChanged(nameof(IsDialogOpen));
                }
            }
        }

        public bool IsDialogOpen => _selectedModule != null;

        /// <summary>Dialog tab: Description (default), Release notes, Settings (modules with settings).</summary>
        public DialogTab DialogTab
        {
            get => _dialogTab;
            private set
            {
                if (Set(ref _dialogTab, value))
                {
                    OnPropertyChanged(nameof(IsDescriptionTab));
                    OnPropertyChanged(nameof(IsNotesTab));
                    OnPropertyChanged(nameof(IsSettingsTab));
                }
            }
        }

        public bool IsDescriptionTab
        {
            get => _dialogTab == DialogTab.Description;
            set
            {
                if (value)
                {
                    DialogTab = DialogTab.Description;
                }
            }
        }

        public bool IsNotesTab
        {
            get => _dialogTab == DialogTab.Notes;
            set
            {
                if (value && _selectedModule?.HasReleaseNotes == true)
                {
                    DialogTab = DialogTab.Notes;
                }
            }
        }

        public bool IsSettingsTab
        {
            get => _dialogTab == DialogTab.Settings;
            set
            {
                if (value && _dialogTab != DialogTab.Settings && _selectedModule?.HasSettings == true)
                {
                    LoadSettingsPage(_selectedModule);
                    DialogTab = DialogTab.Settings;
                }
            }
        }

        /// <summary>Module whose update is waiting for confirmation (popup over the details dialog).</summary>
        public ModuleItem PendingUpdate
        {
            get => _pendingUpdate;
            private set
            {
                if (Set(ref _pendingUpdate, value))
                {
                    OnPropertyChanged(nameof(IsUpdateConfirmOpen));
                }
            }
        }

        public bool IsUpdateConfirmOpen => _pendingUpdate != null;

        /// <summary>Closes the topmost overlay (Esc): the update confirmation first, then the dialog.</summary>
        public void CloseTopOverlay()
        {
            if (IsUpdateConfirmOpen)
            {
                PendingUpdate = null;
            }
            else
            {
                SelectedModule = null;
            }
        }

        public FrameworkElement SettingsPage
        {
            get => _settingsPage;
            private set => Set(ref _settingsPage, value);
        }

        public string SettingsError
        {
            get => _settingsError;
            private set => Set(ref _settingsError, value);
        }

        public CatalogState CatalogState
        {
            get => _catalogState;
            private set
            {
                if (Set(ref _catalogState, value))
                {
                    OnPropertyChanged(nameof(IsCatalogLoading));
                    OnPropertyChanged(nameof(HasCatalogNotice));
                    OnPropertyChanged(nameof(CatalogMessage));
                    RefreshEmptyState();
                }
            }
        }

        public bool IsCatalogLoading => _catalogState == CatalogState.Loading;

        public bool HasCatalogNotice => _catalogState == CatalogState.Offline || _catalogState == CatalogState.Failed;

        public string CatalogMessage =>
            _catalogState == CatalogState.Offline ? Strings.Get("CatalogOffline")
            : _catalogState == CatalogState.Failed ? Strings.Get("CatalogFailed")
            : null;

        public bool IsListEmpty => !IsCatalogLoading && _storeView.IsEmpty;

        public bool ShowSkeletons => IsCatalogLoading && _storeView.IsEmpty;

        public string EmptyIcon { get; private set; }

        public string EmptyTitle { get; private set; }

        public string EmptyText { get; private set; }

        public bool EmptyOffersAvailable { get; private set; }

        /// <summary>
        /// Loads the catalog. A remote catalog is fetched only when <paramref name="force"/> is set or the update
        /// frequency is due; otherwise the saved copy is used. A local (bundled) catalog is always read.
        /// With <paramref name="openHostUpdate"/>, a newer SwissTechKnife opens its confirmation instead of a toast.
        /// </summary>
        public async Task ReloadCatalogAsync(bool force = false, bool announce = false, bool openHostUpdate = false)
        {
            HostSettings settings = _settings.Current;
            Uri uri = _catalogUri();
            bool live = force || uri.IsFile || UpdateSchedule.IsDue(settings.UpdateFrequency, settings.LastUpdateCheckUtc, DateTime.UtcNow);
            int updatesBefore = UpdateCount;
            CatalogState = CatalogState.Loading;
            await _manager.RefreshCatalogAsync(uri, live, CancellationToken.None);
            CatalogResult result = _manager.LastCatalog;
            if (live && result.Error == null)
            {
                settings.LastUpdateCheckUtc = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
                _settings.Save();
            }
            if (!_updateTimer.IsEnabled)
            {
                _updateTimer.Start();
            }
            Settings.Refresh();
            if (result.Error == null)
            {
                CatalogState = CatalogState.Ready;
            }
            else if (result.FromCache)
            {
                CatalogState = CatalogState.Offline;
            }
            else
            {
                CatalogState = CatalogState.Failed;
            }
            RefreshLists();
            foreach (ModuleItem repaired in await _manager.RepairMissingAsync())
            {
                Notify(Strings.Format("ModuleRepaired", repaired.Name), NotificationKind.Success);
                if (ReferenceEquals(repaired, _currentModule) && IsModulePageActive && CurrentPage == null)
                {
                    RetryPage();
                }
            }
            bool hostOffered =Settings.HostUpdate.SetRelease(result.Catalog.Host, result.Source);
            if (openHostUpdate && Settings.HostUpdate.IsAvailable)
            {
                Settings.HostUpdate.ShowUpdate();
            }
            else if (hostOffered || (announce && force && Settings.HostUpdate.IsAvailable))
            {
                // The first check that finds a new SwissTechKnife says so, startup included; "Check now" repeats it.
                Notify(Strings.Format("HostUpdateToast", Settings.HostUpdate.LatestVersionText), NotificationKind.Success);
            }
            if (announce && result.Error == null)
            {
                int found = UpdateCount;
                if (found > updatesBefore)
                {
                    Notify(found == 1 ? Strings.Get("ModuleUpdatesOne") : Strings.Format("ModuleUpdatesMany", found), NotificationKind.Success);
                }
                else if (force && !Settings.HostUpdate.IsAvailable)
                {
                    Notify(Strings.Get("AllUpToDate"), NotificationKind.Info);
                }
            }
            else if (announce && force)
            {
                Notify(Strings.Get("UpdateCheckFailed"), NotificationKind.Warning);
            }
        }

        private async Task CheckModuleUpdateAsync(ModuleItem item)
        {
            item.IsCheckingUpdate = true;
            try
            {
                await ReloadCatalogAsync(force: true);
            }
            finally
            {
                item.IsCheckingUpdate = false;
            }
            if (item.HasUpdate)
            {
                // The confirmation lives over the details dialog, so the dialog opens first.
                SelectedModule = item;
                PendingUpdate = item;
            }
            else if (_manager.LastCatalog.Error != null)
            {
                Notify(Strings.Get("UpdateCheckFailed"), NotificationKind.Warning);
            }
            else
            {
                Notify(Strings.Format("ModuleUpToDate", item.Name), NotificationKind.Info);
            }
        }

        public void Notify(string message, NotificationKind kind)
        {
            var toast = new Toast(message, kind);
            Toasts.Add(toast);
            while (Toasts.Count > 3)
            {
                Toasts.RemoveAt(0);
            }
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(kind == NotificationKind.Error ? 6 : 3.5) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                DismissToast(toast);
            };
            timer.Start();
        }

        public void ShowModule(string id)
        {
            ModuleItem item = _manager.Find(id);
            if (item != null)
            {
                OpenModule(item);
            }
        }

        private void DismissToast(Toast toast)
        {
            if (toast == null || toast.IsClosing)
            {
                return;
            }
            toast.IsClosing = true;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                Toasts.Remove(toast);
            };
            timer.Start();
        }

        private void ShowStore()
        {
            NotifyPageHidden();
            _currentModule = null;
            OnPropertyChanged(nameof(CurrentModule));
            CurrentPage = null;
            IsSettingsActive = false;
            IsStoreActive = true;
        }

        private void ShowSettings()
        {
            if (IsSettingsActive)
            {
                return;
            }
            NotifyPageHidden();
            _currentModule = null;
            OnPropertyChanged(nameof(CurrentModule));
            CurrentPage = null;
            Settings.Refresh();
            IsStoreActive = false;
            IsSettingsActive = true;
        }

        private void OpenModule(ModuleItem item)
        {
            if (item == null || !item.IsEnabled)
            {
                return;
            }
            SelectedModule = null;
            if (ReferenceEquals(item, _currentModule) && IsModulePageActive)
            {
                return;
            }
            NotifyPageHidden();
            _currentModule = item;
            OnPropertyChanged(nameof(CurrentModule));
            IsSettingsActive = false;
            IsStoreActive = false;

            FrameworkElement page = null;
            if (_manager.TryActivate(item))
            {
                try
                {
                    page = _manager.Runtime.GetView(item.Id);
                    item.PageError = null;
                }
                catch (Exception ex)
                {
                    Log.Error(Source, "CreateView failed for " + item.Id, ex);
                    item.PageError = Strings.Format("PageFailed", ex.Message);
                }
            }
            CurrentPage = page;
            NotifyPageShown();
        }

        private void ShowDescriptionTab() => DialogTab = DialogTab.Description;

        /// <summary>Activates the module if needed and asks it for its settings view.</summary>
        private void LoadSettingsPage(ModuleItem item)
        {
            SettingsError = null;
            FrameworkElement page = null;
            if (!item.HasModuleSettings)
            {
                // Host options only: the module is not loaded just to open the tab.
            }
            else if (_manager.TryActivate(item))
            {
                try
                {
                    page = _manager.Runtime.GetSettingsView(item.Id);
                    if (page == null)
                    {
                        SettingsError = Strings.Get("NoSettingsInVersion");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(Source, "CreateSettingsView failed for " + item.Id, ex);
                    SettingsError = Strings.Format("SettingsOpenFailed", ex.Message);
                }
            }
            else
            {
                SettingsError = item.PageError;
            }
            SettingsPage = page;
        }

        private void RetryPage()
        {
            ModuleItem item = _currentModule;
            if (item == null)
            {
                return;
            }
            _manager.Runtime.Deactivate(item.Id);
            _currentModule = null;
            IsStoreActive = true;
            OpenModule(item);
        }

        private async Task InstallAsync(ModuleItem item)
        {
            bool update = item.IsInstalled;
            try
            {
                await _manager.InstallAsync(item);
                if (item.RestartRequired)
                {
                    Notify(Strings.Format("ModuleUpdatedRestart", item.Name), NotificationKind.Info);
                }
                else
                {
                    Notify(Strings.Format(update ? "ModuleUpdated" : "ModuleInstalled", item.Name), NotificationKind.Success);
                }
            }
            catch (ModuleInstallException ex)
            {
                item.Error = ex.Message;
                Notify(Strings.Format("InstallFailed", item.Name), NotificationKind.Error);
            }
        }

        private void Uninstall(ModuleItem item)
        {
            if (!item.ConfirmingUninstall)
            {
                item.ConfirmingUninstall = true;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    item.ConfirmingUninstall = false;
                };
                timer.Start();
                return;
            }
            item.ConfirmingUninstall = false;
            if (ReferenceEquals(_currentModule, item))
            {
                ShowStore();
            }
            if (ReferenceEquals(_selectedModule, item))
            {
                ShowDescriptionTab();
                SettingsPage = null;
            }
            _manager.Uninstall(item);
            Notify(Strings.Format("ModuleUninstalled", item.Name), NotificationKind.Success);
        }

        private void SetEnabled(ModuleItem item, bool enabled)
        {
            if (!enabled && ReferenceEquals(_currentModule, item))
            {
                ShowStore();
            }
            if (!enabled && ReferenceEquals(_selectedModule, item))
            {
                ShowDescriptionTab();
                SettingsPage = null;
            }
            _manager.SetEnabled(item, enabled);
        }

        private void NotifyPageHidden()
        {
            if (IsModulePageActive && PageLifecycle is IModuleViewLifecycle lifecycle)
            {
                SafeLifecycle(lifecycle.OnViewHidden);
            }
        }

        private void NotifyPageShown()
        {
            if (PageLifecycle is IModuleViewLifecycle lifecycle)
            {
                SafeLifecycle(lifecycle.OnViewShown);
            }
        }

        /// <summary>The page itself, or its view model, may opt into show/hide callbacks.</summary>
        private IModuleViewLifecycle PageLifecycle => _currentPage as IModuleViewLifecycle ?? _currentPage?.DataContext as IModuleViewLifecycle;

        private static void SafeLifecycle(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error(Source, "Module view lifecycle callback failed", ex);
            }
        }

        private void ApplySearch()
        {
            _searchDebounce.Stop();
            _appliedSearch = _searchText.Trim();
            RefreshLists();
        }

        private bool StoreFilter(object obj)
        {
            var item = (ModuleItem)obj;
            bool inTab = _tab == StoreTab.All || (_tab == StoreTab.Installed ? item.IsInstalled : !item.IsInstalled);
            return inTab && item.Matches(_appliedSearch);
        }

        /// <summary>The All tab splits the list into Installed and Available sections; the other tabs are one list.</summary>
        private void ApplyStoreSections()
        {
            bool sectioned = _tab == StoreTab.All;
            if (sectioned == (_storeView.GroupDescriptions.Count > 0))
            {
                return;
            }
            if (sectioned)
            {
                _storeView.GroupDescriptions.Add(new StoreSectionGroup());
            }
            else
            {
                _storeView.GroupDescriptions.Clear();
            }
        }

        private void RefreshLists()
        {
            // Refresh a view only when its membership changed: a refresh regenerates every card,
            // which is visible (entry animations) and costly when, say, a switch just toggled.
            // The store signature carries the install state too: in the All tab installing moves a card up.
            RefreshIfChanged(_storeView, StoreFilter, ref _storeSignature, i => (i.IsInstalled ? "+" : "-") + i.Id);
            RefreshIfChanged(_sidebarView, o => ((ModuleItem)o).IsEnabled, ref _sidebarSignature, i => i.Id);
            OnPropertyChanged(nameof(AllCount));
            OnPropertyChanged(nameof(AvailableCount));
            OnPropertyChanged(nameof(InstalledCount));
            OnPropertyChanged(nameof(UpdateCount));
            OnPropertyChanged(nameof(HasUpdates));
            OnPropertyChanged(nameof(HasSidebarModules));
            RefreshEmptyState();
        }

        private void RefreshIfChanged(ListCollectionView view, Predicate<object> filter, ref string signature, Func<ModuleItem, string> key)
        {
            string current = string.Join("|", _manager.Items.Where(i => filter(i)).Select(key));
            if (current != signature)
            {
                signature = current;
                view.Refresh();
            }
        }

        private void RefreshEmptyState()
        {
            EmptyOffersAvailable = false;
            if (!string.IsNullOrEmpty(_appliedSearch))
            {
                EmptyIcon = "";
                EmptyTitle = Strings.Get("EmptySearchTitle");
                EmptyText = Strings.Format("EmptySearchText", _appliedSearch);
            }
            else if (_tab == StoreTab.Installed)
            {
                EmptyIcon = "";
                EmptyTitle = Strings.Get("EmptyInstalledTitle");
                EmptyText = Strings.Get("EmptyInstalledText");
                EmptyOffersAvailable = AvailableCount > 0;
            }
            else if (_catalogState == CatalogState.Failed)
            {
                EmptyIcon = "";
                EmptyTitle = Strings.Get("EmptyFailedTitle");
                EmptyText = Strings.Get("EmptyFailedText");
            }
            else if (_tab == StoreTab.All)
            {
                EmptyIcon = "";
                EmptyTitle = Strings.Get("EmptyCatalogTitle");
                EmptyText = Strings.Get("EmptyCatalogText");
            }
            else
            {
                EmptyIcon = "";
                EmptyTitle = Strings.Get("EmptyAllTitle");
                EmptyText = Strings.Get("EmptyAllText");
            }
            OnPropertyChanged(nameof(EmptyIcon));
            OnPropertyChanged(nameof(EmptyTitle));
            OnPropertyChanged(nameof(EmptyText));
            OnPropertyChanged(nameof(EmptyOffersAvailable));
            OnPropertyChanged(nameof(IsListEmpty));
            OnPropertyChanged(nameof(ShowSkeletons));
        }

        /// <summary>Re-reads every text the view models build in code; XAML text follows on its own.</summary>
        private void OnLanguageChanged()
        {
            OnPropertyChanged(nameof(CatalogMessage));
            RefreshEmptyState();
            foreach (ModuleItem item in _manager.Items)
            {
                item.RefreshText();
            }
        }

        private void ReportError(Exception ex)
        {
            Log.Error(Source, "Command failed", ex);
            Notify(Strings.Format("SomethingWentWrong", ex.Message), NotificationKind.Error);
        }
    }

    /// <summary>Store section of a module: <see cref="StoreTab.Installed"/> or <see cref="StoreTab.Available"/>.</summary>
    internal sealed class StoreSectionGroup : GroupDescription
    {
        public override object GroupNameFromItem(object item, int level, System.Globalization.CultureInfo culture) =>
            ((ModuleItem)item).IsInstalled ? StoreTab.Installed : StoreTab.Available;
    }

    /// <summary>Installed modules before available ones; ties keep the source (catalog) order, so the sort is stable.</summary>
    internal sealed class InstalledFirstComparer : System.Collections.IComparer
    {
        private readonly System.Collections.Generic.IList<ModuleItem> _source;

        public InstalledFirstComparer(System.Collections.Generic.IList<ModuleItem> source)
        {
            _source = source;
        }

        public int Compare(object x, object y)
        {
            var a = (ModuleItem)x;
            var b = (ModuleItem)y;
            if (a.IsInstalled != b.IsInstalled)
            {
                return a.IsInstalled ? -1 : 1;
            }
            return _source.IndexOf(a).CompareTo(_source.IndexOf(b));
        }
    }

    public sealed class Toast : ObservableObject
    {
        private bool _isClosing;

        public Toast(string message, NotificationKind kind)
        {
            Message = message;
            Kind = kind;
        }

        public string Message { get; }

        public NotificationKind Kind { get; }

        public string Icon
        {
            get
            {
                switch (Kind)
                {
                    case NotificationKind.Success: return "";
                    case NotificationKind.Warning: return "";
                    case NotificationKind.Error: return "";
                    default: return "";
                }
            }
        }

        public bool IsClosing
        {
            get => _isClosing;
            set => Set(ref _isClosing, value);
        }
    }
}
