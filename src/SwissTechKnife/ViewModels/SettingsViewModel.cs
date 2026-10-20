using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using SwissTechKnife.Core;
using SwissTechKnife.Infrastructure;
using SwissTechKnife.Sdk;

namespace SwissTechKnife.ViewModels
{
    /// <summary>Combo box option whose label follows the UI language (the selected value never changes).</summary>
    public sealed class FrequencyOption : ObservableObject
    {
        private readonly string _labelKey;

        public FrequencyOption(string value, string labelKey)
        {
            Value = value;
            _labelKey = labelKey;
        }

        public string Value { get; }

        public string Label => Strings.Get(_labelKey);

        public void RefreshLabel() => OnPropertyChanged(nameof(Label));

        /// <summary>Also the item's accessible name.</summary>
        public override string ToString() => Label;
    }

    /// <summary>A supported UI language, shown by its native name.</summary>
    public sealed class LanguageOption
    {
        public LanguageOption(string code, string name)
        {
            Code = code;
            Name = name;
        }

        public string Code { get; }

        public string Name { get; }

        /// <summary>Also the item's accessible name.</summary>
        public override string ToString() => Name;
    }

    /// <summary>Host settings page ("Settings" at the bottom of the sidebar).</summary>
    public sealed class SettingsViewModel : ObservableObject
    {
        private readonly SettingsStore _settings;
        private readonly string _exePath;
        private readonly Action<bool> _applyTray;
        private readonly Action<string, NotificationKind> _notify;
        private bool _isChecking;
        private bool? _pendingStartup;
        private string _pendingLanguage;

        public SettingsViewModel(SettingsStore settings, string exePath, Action<bool> applyTray, Func<System.Threading.Tasks.Task> checkNow, Action<string, NotificationKind> notify, HostUpdateViewModel hostUpdate)
        {
            _settings = settings;
            HostUpdate = hostUpdate;
            _exePath = exePath;
            _applyTray = applyTray;
            _notify = notify;
            CheckNowCommand = new AsyncCommand(async _ =>
            {
                IsChecking = true;
                try
                {
                    await checkNow();
                }
                finally
                {
                    IsChecking = false;
                    CommandManager.InvalidateRequerySuggested();
                }
            }, _ => !_isChecking, ex => notify(Strings.Format("CheckFailed", ex.Message), NotificationKind.Error));
            Strings.LanguageChanged += (s, e) => OnLanguageChanged();
        }

        public IReadOnlyList<FrequencyOption> FrequencyOptions { get; } = new[]
        {
            new FrequencyOption(UpdateSchedule.Daily, "Daily"),
            new FrequencyOption(UpdateSchedule.Weekly, "Weekly"),
            new FrequencyOption(UpdateSchedule.Monthly, "Monthly"),
            new FrequencyOption(UpdateSchedule.Never, "Never"),
        };

        /// <summary>Supported UI languages as (code, native name); names are not translated.</summary>
        public IReadOnlyList<LanguageOption> LanguageOptions { get; } = Strings.Languages.Select(l => new LanguageOption(l.Key, l.Value)).ToList();

        public string Language
        {
            get => _pendingLanguage ?? Strings.Language;
            set
            {
                string normalized = Strings.Normalize(value);
                if (normalized == Language)
                {
                    return;
                }
                _pendingLanguage = normalized;
                _settings.Current.Language = normalized;
                OnPropertyChanged();
                // Switching re-renders every localized text: let the combo box close first.
                AfterAnimation.Run(() =>
                {
                    if (_pendingLanguage == null)
                    {
                        return;
                    }
                    string target = _pendingLanguage;
                    _pendingLanguage = null;
                    _settings.Save();
                    Strings.SetLanguage(target);
                });
            }
        }

        public ICommand CheckNowCommand { get; }

        /// <summary>SwissTechKnife's own update (notice at the top of "Updates").</summary>
        public HostUpdateViewModel HostUpdate { get; }

        public string UpdateFrequency
        {
            get => UpdateSchedule.Normalize(_settings.Current.UpdateFrequency);
            set
            {
                string normalized = UpdateSchedule.Normalize(value);
                if (normalized != UpdateFrequency)
                {
                    _settings.Current.UpdateFrequency = normalized;
                    _settings.Save();
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>Reads the Run value each time, so changes made outside the app show correctly.</summary>
        public bool StartWithWindows
        {
            get => _pendingStartup ?? StartupRegistration.IsEnabled(_exePath);
            set
            {
                if (value == StartWithWindows)
                {
                    return;
                }
                _pendingStartup = value;
                OnPropertyChanged();
                AfterAnimation.Run(() =>
                {
                    bool target = _pendingStartup ?? value;
                    _pendingStartup = null;
                    if (target != StartupRegistration.IsEnabled(_exePath) && !StartupRegistration.TrySet(_exePath, target))
                    {
                        _notify(Strings.Get("StartupChangeFailed"), NotificationKind.Error);
                    }
                    OnPropertyChanged(nameof(StartWithWindows));
                });
            }
        }

        public bool HideToTrayOnClose
        {
            get => _settings.Current.HideToTrayOnClose;
            set
            {
                if (value != _settings.Current.HideToTrayOnClose)
                {
                    _settings.Current.HideToTrayOnClose = value;
                    OnPropertyChanged();
                    // Creating the tray icon loads WinForms: do it after the switch animation.
                    AfterAnimation.Run(() =>
                    {
                        _settings.Save();
                        _applyTray(_settings.Current.HideToTrayOnClose);
                    });
                }
            }
        }

        public bool IsChecking
        {
            get => _isChecking;
            private set
            {
                if (Set(ref _isChecking, value))
                {
                    OnPropertyChanged(nameof(LastCheckText));
                }
            }
        }

        public string LastCheckText
        {
            get
            {
                if (_isChecking)
                {
                    return Strings.Get("Checking");
                }
                DateTime? last = UpdateSchedule.ParseUtc(_settings.Current.LastUpdateCheckUtc);
                if (last == null)
                {
                    return Strings.Get("NeverChecked");
                }
                DateTime local = last.Value.ToLocalTime();
                CultureInfo culture = Strings.Culture;
                string day = local.Date == DateTime.Today ? Strings.Get("Today")
                    : local.Date == DateTime.Today.AddDays(-1) ? Strings.Get("Yesterday")
                    : local.ToString(Strings.Get("ShortDateFormat"), culture);
                return Strings.Format("LastCheck", day, local.ToString("t", culture));
            }
        }

        private void OnLanguageChanged()
        {
            foreach (FrequencyOption option in FrequencyOptions)
            {
                option.RefreshLabel();
            }
            OnPropertyChanged(nameof(Language));
            OnPropertyChanged(nameof(LastCheckText));
        }

        public void Refresh()
        {
            OnPropertyChanged(nameof(LastCheckText));
            OnPropertyChanged(nameof(StartWithWindows));
        }
    }
}
