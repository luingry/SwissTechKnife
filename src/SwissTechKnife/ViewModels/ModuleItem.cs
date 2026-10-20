using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Media;
using SwissTechKnife.Core;
using SwissTechKnife.Infrastructure;

namespace SwissTechKnife.ViewModels
{
    public enum ModuleStatus
    {
        /// <summary>Announced in the catalog without a package yet.</summary>
        ComingSoon,
        Available,
        Enabled,
        Disabled,
    }

    /// <summary>One module as the UI sees it: catalog entry merged with local install state.</summary>
    public sealed class ModuleItem : ObservableObject
    {
        private static readonly Dictionary<string, Geometry> GeometryCache = new Dictionary<string, Geometry>(StringComparer.Ordinal);
        private static readonly Dictionary<string, (Brush Strong, Brush Soft)> BrushCache = new Dictionary<string, (Brush, Brush)>(StringComparer.OrdinalIgnoreCase);
        private static readonly Geometry FallbackIcon = Freeze(Geometry.Parse("M4,7 L12,3 L20,7 L20,17 L12,21 L4,17 Z M4,7 L12,11 L20,7 M12,11 L12,21"));

        private CatalogEntry _catalogEntry;
        private InstalledModule _installed;
        private bool _isBusy;
        private double _progress;
        private string _busyText;
        private string _error;
        private bool _restartRequired;
        private string _restartReason;
        private string _pageError;
        private bool _confirmingUninstall;
        private ImageSource _iconImage;
        private bool? _pendingActive;

        public ModuleItem(string id)
        {
            Id = id;
        }

        public string Id { get; }

        /// <summary>Entry from the live (or cached) catalog; null if the module left the catalog.</summary>
        public CatalogEntry CatalogEntry
        {
            get => _catalogEntry;
            set
            {
                _catalogEntry = value;
                OnAllPropertiesChanged();
            }
        }

        public InstalledModule Installed
        {
            get => _installed;
            set
            {
                _installed = value;
                OnAllPropertiesChanged();
            }
        }

        /// <summary>What to display: the catalog entry, else the snapshot taken at install time.</summary>
        private CatalogEntry Info => _catalogEntry ?? _installed?.Entry ?? new CatalogEntry { Id = Id, Name = Id };

        public string Name => Info.Name;

        public string Tagline => Info.Tagline;

        public string Description => string.IsNullOrWhiteSpace(Info.Description) ? Info.Tagline : Info.Description;

        public string Homepage => Info.Homepage;

        public string Version => IsInstalled ? _installed.Version : Info.Version;

        public string VersionText => string.IsNullOrWhiteSpace(Version) ? "—" : "v" + Version;

        public string LatestVersion => _catalogEntry?.Version;

        public string SizeText => FormatSize(_catalogEntry?.Size ?? _installed?.Entry?.Size ?? 0);

        public Geometry IconGeometry => ParseIcon(Info.Icon);

        public Brush AccentBrush => Brushes(Info.Accent).Strong;

        public Brush AccentSoftBrush => Brushes(Info.Accent).Soft;

        /// <summary>Raster icon location from the catalog (null = vector icon only).</summary>
        public string IconImageUrl => Info.IconImage;

        /// <summary>The module's own icon, once loaded; replaces the vector icon.</summary>
        public ImageSource IconImage
        {
            get => _iconImage;
            set
            {
                if (Set(ref _iconImage, value))
                {
                    OnPropertyChanged(nameof(HasIconImage));
                    OnPropertyChanged(nameof(TileBrush));
                }
            }
        }

        public bool HasIconImage => _iconImage != null;

        /// <summary>Icon tile color: catalog iconBackground for raster icons, else the accent tint.</summary>
        public Brush TileBrush => HasIconImage && !string.IsNullOrWhiteSpace(Info.IconBackground) ? Brushes(Info.IconBackground).Strong : AccentSoftBrush;

        public bool IsInstalled => _installed != null;

        public bool IsEnabled => _installed?.Enabled == true;

        /// <summary>Set by the shell: performs enable/disable when the switch is flipped.</summary>
        public Action<ModuleItem, bool> ToggleHandler { get; set; }

        /// <summary>Two-way target for the enable switch on the card and in the dialog.</summary>
        public bool IsActive
        {
            get => _pendingActive ?? IsEnabled;
            set
            {
                if (value == IsActive)
                {
                    return;
                }
                // The switch (and its label) flips now; the real enable/disable runs once the knob animation ends.
                _pendingActive = value;
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(ActiveText));
                Infrastructure.AfterAnimation.Run(() =>
                {
                    bool target = _pendingActive ?? IsEnabled;
                    _pendingActive = null;
                    if (target != IsEnabled)
                    {
                        ToggleHandler?.Invoke(this, target);
                    }
                    // Re-sync if the change was refused or failed.
                    OnPropertyChanged(nameof(IsActive));
                    OnPropertyChanged(nameof(ActiveText));
                });
            }
        }

        public string ActiveText => IsActive ? "Ativado" : "Desativado";

        public ModuleStatus Status =>
            _installed != null ? (_installed.Enabled ? ModuleStatus.Enabled : ModuleStatus.Disabled)
            : (_catalogEntry?.IsAvailable == true ? ModuleStatus.Available : ModuleStatus.ComingSoon);

        public bool IsComingSoon => Status == ModuleStatus.ComingSoon;

        public bool CanInstall => Status == ModuleStatus.Available;

        public bool HasUpdate => IsInstalled && _catalogEntry?.IsAvailable == true && Core.VersionText.IsNewer(_catalogEntry.Version, _installed.Version);

        public bool StartsWithHost => _installed?.Manifest?.StartsWithHost == true;

        /// <summary>Enabled module that declares a settings view.</summary>
        public bool HasSettings => IsEnabled && _installed.Manifest?.HasSettings == true;

        /// <summary>Notes for every version up to the current one, newest first; the current is flagged.</summary>
        public IReadOnlyList<ReleaseNoteView> ReleaseNotes
        {
            get
            {
                Version current = Core.VersionText.ParseOrZero(Version);
                return Notes()
                    .Where(n => Core.VersionText.ParseOrZero(n.Version) <= current)
                    .Select(n => new ReleaseNoteView(n, Core.VersionText.ParseOrZero(n.Version) == current))
                    .ToList();
            }
        }

        public bool HasReleaseNotes => ReleaseNotes.Count > 0;

        /// <summary>What an update brings: versions after the installed one, up to the catalog version.</summary>
        public IReadOnlyList<ReleaseNoteView> UpdateNotes
        {
            get
            {
                if (!HasUpdate)
                {
                    return new ReleaseNoteView[0];
                }
                Version installed = Core.VersionText.ParseOrZero(_installed.Version);
                Version target = Core.VersionText.ParseOrZero(_catalogEntry.Version);
                return Notes()
                    .Where(n => Core.VersionText.ParseOrZero(n.Version) > installed && Core.VersionText.ParseOrZero(n.Version) <= target)
                    .Select(n => new ReleaseNoteView(n, false))
                    .ToList();
            }
        }

        public bool HasUpdateNotes => UpdateNotes.Count > 0;

        public bool ShowTabs => HasSettings || HasReleaseNotes;

        private IEnumerable<ReleaseNote> Notes() =>
            ((_catalogEntry ?? _installed?.Entry)?.ReleaseNotes ?? new List<ReleaseNote>())
                .Where(n => n != null && Core.VersionText.TryParse(n.Version, out _))
                .OrderByDescending(n => Core.VersionText.ParseOrZero(n.Version));

        public string UpdateText => HasUpdate ? "v" + _catalogEntry.Version + " disponível" : null;

        // Dialog action layout: exactly one primary action per state.
        public bool ShowInstall => Status == ModuleStatus.Available;

        public bool ShowComingSoon => Status == ModuleStatus.ComingSoon;

        public bool ShowUpdate => HasUpdate;

        public bool ShowOpenPrimary => Status == ModuleStatus.Enabled && !HasUpdate;

        public bool ShowOpenSecondary => Status == ModuleStatus.Enabled && HasUpdate;



        public bool IsBusy
        {
            get => _isBusy;
            set => Set(ref _isBusy, value);
        }

        /// <summary>0..1 while installing/updating.</summary>
        public double Progress
        {
            get => _progress;
            set => Set(ref _progress, value);
        }

        public string BusyText
        {
            get => _busyText;
            set => Set(ref _busyText, value);
        }

        /// <summary>Last action failure, shown in the details dialog.</summary>
        public string Error
        {
            get => _error;
            set
            {
                if (Set(ref _error, value))
                {
                    OnPropertyChanged(nameof(HasError));
                }
            }
        }

        public bool HasError => !string.IsNullOrEmpty(_error);

        /// <summary>Failure loading or rendering the module page.</summary>
        public string PageError
        {
            get => _pageError;
            set => Set(ref _pageError, value);
        }

        /// <summary>First click on "Desinstalar" arms it; the second click confirms.</summary>
        public bool ConfirmingUninstall
        {
            get => _confirmingUninstall;
            set => Set(ref _confirmingUninstall, value);
        }

        public bool RestartRequired
        {
            get => _restartRequired;
            set => Set(ref _restartRequired, value);
        }

        public string RestartReason
        {
            get => _restartReason;
            set => Set(ref _restartReason, value);
        }

        public bool Matches(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }
            var culture = CultureInfo.CurrentCulture.CompareInfo;
            const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
            return culture.IndexOf(Name ?? string.Empty, query, options) >= 0
                || culture.IndexOf(Tagline ?? string.Empty, query, options) >= 0;
        }

        public void Refresh() => OnAllPropertiesChanged();

        private static Geometry ParseIcon(string data)
        {
            if (string.IsNullOrWhiteSpace(data))
            {
                return FallbackIcon;
            }
            if (GeometryCache.TryGetValue(data, out Geometry cached))
            {
                return cached;
            }
            Geometry geometry;
            try
            {
                geometry = Freeze(Geometry.Parse(data));
            }
            catch (FormatException)
            {
                geometry = FallbackIcon;
            }
            GeometryCache[data] = geometry;
            return geometry;
        }

        private static (Brush Strong, Brush Soft) Brushes(string hex)
        {
            string key = string.IsNullOrWhiteSpace(hex) ? "#E8743C" : hex.Trim();
            if (BrushCache.TryGetValue(key, out var cached))
            {
                return cached;
            }
            Color color;
            try
            {
                color = (Color)ColorConverter.ConvertFromString(key);
            }
            catch (FormatException)
            {
                color = Color.FromRgb(0xE8, 0x74, 0x3C);
            }
            var strong = new SolidColorBrush(color);
            var soft = new SolidColorBrush(Color.FromArgb(0x26, color.R, color.G, color.B));
            strong.Freeze();
            soft.Freeze();
            return BrushCache[key] = (strong, soft);
        }

        private static Geometry Freeze(Geometry geometry)
        {
            geometry.Freeze();
            return geometry;
        }

        internal static string FormatSize(long bytes)
        {
            if (bytes <= 0)
            {
                return "—";
            }
            if (bytes < 1024 * 1024)
            {
                return Math.Max(1, bytes / 1024).ToString(CultureInfo.CurrentCulture) + " KB";
            }
            return (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.CurrentCulture) + " MB";
        }
    }

    /// <summary>One version in the "Notas da versão" tab or the update confirmation.</summary>
    public sealed class ReleaseNoteView
    {
        private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

        public ReleaseNoteView(ReleaseNote note, bool isCurrent)
        {
            VersionText = "v" + note.Version;
            IsCurrent = isCurrent;
            Notes = (note.Notes ?? new List<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            DateText = DateTime.TryParseExact(note.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date)
                ? date.ToString("d MMM yyyy", PtBr)
                : null;
        }

        public string VersionText { get; }

        public string DateText { get; }

        public bool IsCurrent { get; }

        public IReadOnlyList<string> Notes { get; }
    }
}
