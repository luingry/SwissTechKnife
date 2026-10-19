using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace SwissTechKnife.Samples.SystemMonitor
{
    public partial class SettingsView : UserControl
    {
        private readonly MonitorSettings _settings;
        private readonly bool _ready;

        public SettingsView(MonitorSettings settings)
        {
            _settings = settings;
            InitializeComponent();
            foreach (ComboBoxItem item in Interval.Items)
            {
                if ((string)item.Tag == settings.IntervalSeconds.ToString(CultureInfo.InvariantCulture))
                {
                    Interval.SelectedItem = item;
                }
            }
            if (Interval.SelectedItem == null)
            {
                Interval.SelectedIndex = 0;
            }
            ShowChart.IsChecked = settings.ShowChart;
            _ready = true;
        }

        private void OnIntervalChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_ready && Interval.SelectedItem is ComboBoxItem item)
            {
                _settings.IntervalSeconds = int.Parse((string)item.Tag, CultureInfo.InvariantCulture);
            }
        }

        private void OnChartChanged(object sender, RoutedEventArgs e)
        {
            if (_ready)
            {
                _settings.ShowChart = ShowChart.IsChecked == true;
            }
        }
    }
}
