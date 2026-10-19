using System;
using System.Globalization;
using System.IO;

namespace SwissTechKnife.Samples.SystemMonitor
{
    /// <summary>Module settings, stored in the module's data folder so they survive updates.</summary>
    public sealed class MonitorSettings
    {
        private readonly string _path;
        private int _intervalSeconds = 1;
        private bool _showChart = true;

        public MonitorSettings(string dataDirectory)
        {
            _path = Path.Combine(dataDirectory, "settings.txt");
            Load();
        }

        public event EventHandler Changed;

        public int IntervalSeconds
        {
            get => _intervalSeconds;
            set
            {
                int clamped = Math.Max(1, Math.Min(10, value));
                if (clamped != _intervalSeconds)
                {
                    _intervalSeconds = clamped;
                    Save();
                }
            }
        }

        public bool ShowChart
        {
            get => _showChart;
            set
            {
                if (value != _showChart)
                {
                    _showChart = value;
                    Save();
                }
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return;
                }
                foreach (string line in File.ReadAllLines(_path))
                {
                    string[] parts = line.Split(new[] { '=' }, 2);
                    if (parts.Length != 2)
                    {
                        continue;
                    }
                    if (parts[0] == "interval" && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int interval))
                    {
                        _intervalSeconds = Math.Max(1, Math.Min(10, interval));
                    }
                    else if (parts[0] == "chart" && bool.TryParse(parts[1], out bool chart))
                    {
                        _showChart = chart;
                    }
                }
            }
            catch (IOException)
            {
            }
        }

        private void Save()
        {
            try
            {
                File.WriteAllLines(_path, new[]
                {
                    "interval=" + _intervalSeconds.ToString(CultureInfo.InvariantCulture),
                    "chart=" + _showChart,
                });
            }
            catch (IOException)
            {
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
