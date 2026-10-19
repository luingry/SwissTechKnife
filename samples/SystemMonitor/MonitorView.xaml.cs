using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SwissTechKnife.Sdk;

namespace SwissTechKnife.Samples.SystemMonitor
{
    /// <summary>Samples once per second, and only while the page is visible.</summary>
    public partial class MonitorView : UserControl, IModuleViewLifecycle
    {
        private const int Samples = 60;
        private readonly double[] _cpu = new double[Samples];
        private readonly NativeStats _stats = new NativeStats();
        private readonly DispatcherTimer _timer;
        private int _count;

        public MonitorView()
        {
            InitializeComponent();
            _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (s, e) => Tick(), Dispatcher);
            _timer.Stop();
            ChartHost.SizeChanged += (s, e) => DrawChart();
        }

        public void OnViewShown()
        {
            _stats.SampleCpu();
            Tick();
            _timer.Start();
        }

        public void OnViewHidden() => _timer.Stop();

        public void Stop() => _timer.Stop();

        private void Tick()
        {
            double cpu = _stats.SampleCpu();
            Array.Copy(_cpu, 1, _cpu, 0, Samples - 1);
            _cpu[Samples - 1] = cpu;
            _count = Math.Min(Samples, _count + 1);
            CpuText.Text = Math.Round(cpu * 100).ToString(CultureInfo.CurrentCulture);

            var (total, available) = NativeStats.Memory();
            if (total > 0)
            {
                double used = total - available;
                MemoryText.Text = (used / Gb).ToString("0.0", CultureInfo.CurrentCulture) + " GB";
                MemoryCaption.Text = "de " + (total / Gb).ToString("0.0", CultureInfo.CurrentCulture) + " GB em uso";
                MemoryBar.Value = used / total;
            }

            TimeSpan uptime = NativeStats.Uptime();
            UptimeText.Text = uptime.Days > 0
                ? string.Format(CultureInfo.CurrentCulture, "{0}d {1}h {2}min", uptime.Days, uptime.Hours, uptime.Minutes)
                : string.Format(CultureInfo.CurrentCulture, "{0}h {1}min", uptime.Hours, uptime.Minutes);
            DrawChart();
        }

        private const double Gb = 1024.0 * 1024 * 1024;

        private void DrawChart()
        {
            double width = ChartHost.ActualWidth;
            double height = ChartHost.ActualHeight;
            if (width <= 0 || height <= 0)
            {
                return;
            }
            var line = new PointCollection(Samples);
            var area = new PointCollection(Samples + 2);
            double step = width / (Samples - 1);
            for (int i = Samples - _count; i < Samples; i++)
            {
                var point = new System.Windows.Point(i * step, height - 2 - _cpu[i] * (height - 4));
                line.Add(point);
                area.Add(point);
            }
            if (line.Count > 0)
            {
                area.Add(new System.Windows.Point(line[line.Count - 1].X, height));
                area.Add(new System.Windows.Point(line[0].X, height));
            }
            line.Freeze();
            area.Freeze();
            CpuLine.Points = line;
            CpuArea.Points = area;
        }
    }
}
