using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Input;
using SwissTechKnife.Core;
using SwissTechKnife.Infrastructure;
using SwissTechKnife.Sdk;

namespace SwissTechKnife.ViewModels
{
    public sealed class FrequencyOption
    {
        public FrequencyOption(string value, string label)
        {
            Value = value;
            Label = label;
        }

        public string Value { get; }

        public string Label { get; }
    }

    /// <summary>Host settings page ("Configurações" at the bottom of the sidebar).</summary>
    public sealed class SettingsViewModel : ObservableObject
    {
        private readonly SettingsStore _settings;
        private readonly string _exePath;
        private readonly Action<bool> _applyTray;
        private readonly Action<string, NotificationKind> _notify;
        private bool _isChecking;

        public SettingsViewModel(SettingsStore settings, string exePath, Action<bool> applyTray, Func<System.Threading.Tasks.Task> checkNow, Action<string, NotificationKind> notify)
        {
            _settings = settings;
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
            }, _ => !_isChecking, ex => notify("Não foi possível verificar: " + ex.Message, NotificationKind.Error));
        }

        public IReadOnlyList<FrequencyOption> FrequencyOptions { get; } = new[]
        {
            new FrequencyOption(UpdateSchedule.Daily, "Diário"),
            new FrequencyOption(UpdateSchedule.Weekly, "Semanal"),
            new FrequencyOption(UpdateSchedule.Monthly, "Mensal"),
            new FrequencyOption(UpdateSchedule.Never, "Nunca"),
        };

        public ICommand CheckNowCommand { get; }

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
            get => StartupRegistration.IsEnabled(_exePath);
            set
            {
                if (value != StartWithWindows && !StartupRegistration.TrySet(_exePath, value))
                {
                    _notify("Não foi possível alterar a inicialização com o Windows.", NotificationKind.Error);
                }
                OnPropertyChanged();
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
                    _settings.Save();
                    _applyTray(value);
                    OnPropertyChanged();
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
                    return "Verificando…";
                }
                DateTime? last = UpdateSchedule.ParseUtc(_settings.Current.LastUpdateCheckUtc);
                if (last == null)
                {
                    return "Ainda não verificado";
                }
                DateTime local = last.Value.ToLocalTime();
                CultureInfo culture = CultureInfo.GetCultureInfo("pt-BR");
                string day = local.Date == DateTime.Today ? "hoje"
                    : local.Date == DateTime.Today.AddDays(-1) ? "ontem"
                    : local.ToString("dd 'de' MMM", culture);
                return "Última verificação: " + day + " às " + local.ToString("HH:mm", culture);
            }
        }

        public void Refresh()
        {
            OnPropertyChanged(nameof(LastCheckText));
            OnPropertyChanged(nameof(StartWithWindows));
        }
    }
}
