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
        Available,
        Installed,
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
        private StoreTab _tab = StoreTab.Available;
        private bool _isStoreActive = true;
        private ModuleItem _currentModule;
        private FrameworkElement _currentPage;
        private ModuleItem _selectedModule;
        private CatalogState _catalogState = CatalogState.Loading;
        private string _catalogMessage;
        private bool _isSettingsTab;
        private FrameworkElement _settingsPage;
        private string _settingsError;

        public ShellViewModel(ModuleManager manager, Func<Uri> catalogUri, Action restart, string hostVersion)
        {
            _manager = manager;
            _catalogUri = catalogUri;
            _restart = restart;
            HostVersion = "v" + hostVersion;

            _storeView = new ListCollectionView(manager.Items) { Filter = StoreFilter };
            _sidebarView = new ListCollectionView(manager.Items) { Filter = o => ((ModuleItem)o).IsEnabled };
            _searchDebounce = new DispatcherTimer(TimeSpan.FromMilliseconds(140), DispatcherPriority.Input, (s, e) => ApplySearch(), Dispatcher.CurrentDispatcher);
            _searchDebounce.Stop();
            manager.Changed += (s, e) => RefreshLists();
            manager.ToggleHandler = SetEnabled;

            ShowStoreCommand = new RelayCommand(_ => ShowStore());
            ShowTabCommand = new RelayCommand(p => { Tab = (StoreTab)p; ShowStore(); });
            OpenDetailsCommand = new RelayCommand(p => SelectedModule = p as ModuleItem, p => p is ModuleItem);
            CloseDetailsCommand = new RelayCommand(_ => SelectedModule = null);
            InstallCommand = new AsyncCommand(p => InstallAsync((ModuleItem)p), p => p is ModuleItem m && !m.IsBusy && (m.CanInstall || m.HasUpdate), ex => ReportError(ex));
            UninstallCommand = new RelayCommand(p => Uninstall((ModuleItem)p), p => p is ModuleItem m && m.IsInstalled && !m.IsBusy);
            EnableCommand = new RelayCommand(p => SetEnabled((ModuleItem)p, true), p => p is ModuleItem m && m.IsInstalled && !m.IsEnabled && !m.IsBusy);
            DisableCommand = new RelayCommand(p => SetEnabled((ModuleItem)p, false), p => p is ModuleItem m && m.IsEnabled && !m.IsBusy);
            OpenModuleCommand = new RelayCommand(p => OpenModule((ModuleItem)p), p => p is ModuleItem m && m.IsEnabled);
            ReloadCatalogCommand = new AsyncCommand(_ => ReloadCatalogAsync(), _ => CatalogState != CatalogState.Loading, ex => ReportError(ex));
            RestartCommand = new RelayCommand(_ => _restart());
            DismissToastCommand = new RelayCommand(p => DismissToast(p as Toast));
            RetryPageCommand = new RelayCommand(_ => RetryPage(), _ => _currentModule != null);
        }

        public string HostVersion { get; }

        public ICollectionView StoreItems => _storeView;

        public ICollectionView SidebarItems => _sidebarView;

        public ObservableCollection<Toast> Toasts { get; } = new ObservableCollection<Toast>();

        public ICommand ShowStoreCommand { get; }
        public ICommand ShowTabCommand { get; }
        public ICommand OpenDetailsCommand { get; }
        public ICommand CloseDetailsCommand { get; }
        public ICommand InstallCommand { get; }
        public ICommand UninstallCommand { get; }
        public ICommand EnableCommand { get; }
        public ICommand DisableCommand { get; }
        public ICommand OpenModuleCommand { get; }
        public ICommand ReloadCatalogCommand { get; }
        public ICommand RestartCommand { get; }
        public ICommand DismissToastCommand { get; }
        public ICommand RetryPageCommand { get; }

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
                    OnPropertyChanged(nameof(IsAvailableTab));
                    OnPropertyChanged(nameof(IsInstalledTab));
                    RefreshLists();
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

        public int AvailableCount => _manager.Items.Count(i => !i.IsInstalled);

        public int InstalledCount => _manager.Items.Count(i => i.IsInstalled);

        public int UpdateCount => _manager.Items.Count(i => i.HasUpdate);

        public bool HasUpdates => UpdateCount > 0;

        public bool HasSidebarModules => _manager.Items.Any(i => i.IsEnabled);

        public bool IsStoreActive
        {
            get => _isStoreActive;
            private set => Set(ref _isStoreActive, value);
        }

        /// <summary>Module whose page is shown (sidebar selection).</summary>
        public ModuleItem CurrentModule
        {
            get => _currentModule;
            set
            {
                if (value == null)
                {
                    if (_currentModule != null && !IsStoreActive)
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
                    ShowDescriptionTab();
                    OnPropertyChanged(nameof(IsDialogOpen));
                }
            }
        }

        public bool IsDialogOpen => _selectedModule != null;

        /// <summary>Dialog tab: "Descrição" (default) or "Configurações" (modules with settings).</summary>
        public bool IsDescriptionTab
        {
            get => !_isSettingsTab;
            set
            {
                if (value)
                {
                    ShowDescriptionTab();
                }
            }
        }

        public bool IsSettingsTab
        {
            get => _isSettingsTab;
            set
            {
                if (value && !_isSettingsTab && _selectedModule?.HasSettings == true)
                {
                    LoadSettingsPage(_selectedModule);
                    _isSettingsTab = true;
                    OnPropertyChanged(nameof(IsSettingsTab));
                    OnPropertyChanged(nameof(IsDescriptionTab));
                }
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
                    RefreshEmptyState();
                }
            }
        }

        public bool IsCatalogLoading => _catalogState == CatalogState.Loading;

        public bool HasCatalogNotice => _catalogState == CatalogState.Offline || _catalogState == CatalogState.Failed;

        public string CatalogMessage
        {
            get => _catalogMessage;
            private set => Set(ref _catalogMessage, value);
        }

        public bool IsListEmpty => !IsCatalogLoading && _storeView.IsEmpty;

        public bool ShowSkeletons => IsCatalogLoading && _storeView.IsEmpty;

        public string EmptyIcon { get; private set; }

        public string EmptyTitle { get; private set; }

        public string EmptyText { get; private set; }

        public bool EmptyOffersAvailable { get; private set; }

        public async Task ReloadCatalogAsync()
        {
            CatalogState = CatalogState.Loading;
            await _manager.RefreshCatalogAsync(_catalogUri(), CancellationToken.None);
            CatalogResult result = _manager.LastCatalog;
            if (result.Error == null)
            {
                CatalogState = CatalogState.Ready;
                CatalogMessage = null;
            }
            else if (result.FromCache)
            {
                CatalogState = CatalogState.Offline;
                CatalogMessage = "Sem conexão com o catálogo. Mostrando a última lista salva.";
            }
            else
            {
                CatalogState = CatalogState.Failed;
                CatalogMessage = "Não foi possível carregar o catálogo de módulos.";
            }
            RefreshLists();
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
            IsStoreActive = true;
        }

        private void OpenModule(ModuleItem item)
        {
            if (item == null || !item.IsEnabled)
            {
                return;
            }
            SelectedModule = null;
            if (ReferenceEquals(item, _currentModule) && !IsStoreActive)
            {
                return;
            }
            NotifyPageHidden();
            _currentModule = item;
            OnPropertyChanged(nameof(CurrentModule));
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
                    item.PageError = "A página do módulo falhou ao abrir: " + ex.Message;
                }
            }
            CurrentPage = page;
            NotifyPageShown();
        }

        private void ShowDescriptionTab()
        {
            if (_isSettingsTab)
            {
                _isSettingsTab = false;
                OnPropertyChanged(nameof(IsSettingsTab));
                OnPropertyChanged(nameof(IsDescriptionTab));
            }
        }

        /// <summary>Activates the module if needed and asks it for its settings view.</summary>
        private void LoadSettingsPage(ModuleItem item)
        {
            SettingsError = null;
            FrameworkElement page = null;
            if (_manager.TryActivate(item))
            {
                try
                {
                    page = _manager.Runtime.GetSettingsView(item.Id);
                    if (page == null)
                    {
                        SettingsError = "Este módulo não oferece configurações nesta versão.";
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(Source, "CreateSettingsView failed for " + item.Id, ex);
                    SettingsError = "As configurações não puderam ser abertas: " + ex.Message;
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
                    Notify(item.Name + " atualizado. Reinicie para aplicar.", NotificationKind.Info);
                }
                else
                {
                    Notify(item.Name + (update ? " atualizado." : " instalado."), NotificationKind.Success);
                }
            }
            catch (ModuleInstallException ex)
            {
                item.Error = ex.Message;
                Notify("Falha ao instalar " + item.Name + ".", NotificationKind.Error);
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
            Notify(item.Name + " desinstalado.", NotificationKind.Success);
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
            if (!IsStoreActive && PageLifecycle is IModuleViewLifecycle lifecycle)
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
            bool inTab = _tab == StoreTab.Installed ? item.IsInstalled : !item.IsInstalled;
            return inTab && item.Matches(_appliedSearch);
        }

        private void RefreshLists()
        {
            _storeView.Refresh();
            _sidebarView.Refresh();
            OnPropertyChanged(nameof(AvailableCount));
            OnPropertyChanged(nameof(InstalledCount));
            OnPropertyChanged(nameof(UpdateCount));
            OnPropertyChanged(nameof(HasUpdates));
            OnPropertyChanged(nameof(HasSidebarModules));
            RefreshEmptyState();
        }

        private void RefreshEmptyState()
        {
            EmptyOffersAvailable = false;
            if (!string.IsNullOrEmpty(_appliedSearch))
            {
                EmptyIcon = "";
                EmptyTitle = "Nada encontrado";
                EmptyText = "Nenhum módulo corresponde a “" + _appliedSearch + "”.";
            }
            else if (_tab == StoreTab.Installed)
            {
                EmptyIcon = "";
                EmptyTitle = "Nenhum módulo instalado";
                EmptyText = "Escolha o que você quer usar na aba Disponíveis.";
                EmptyOffersAvailable = AvailableCount > 0;
            }
            else if (_catalogState == CatalogState.Failed)
            {
                EmptyIcon = "";
                EmptyTitle = "Catálogo indisponível";
                EmptyText = "Verifique a conexão e tente de novo.";
            }
            else
            {
                EmptyIcon = "";
                EmptyTitle = "Tudo instalado";
                EmptyText = "Você já tem todos os módulos do catálogo.";
            }
            OnPropertyChanged(nameof(EmptyIcon));
            OnPropertyChanged(nameof(EmptyTitle));
            OnPropertyChanged(nameof(EmptyText));
            OnPropertyChanged(nameof(EmptyOffersAvailable));
            OnPropertyChanged(nameof(IsListEmpty));
            OnPropertyChanged(nameof(ShowSkeletons));
        }

        private void ReportError(Exception ex)
        {
            Log.Error(Source, "Command failed", ex);
            Notify("Algo deu errado: " + ex.Message, NotificationKind.Error);
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
