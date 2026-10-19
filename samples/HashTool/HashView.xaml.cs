using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SwissTechKnife.Sdk;

namespace SwissTechKnife.Samples.HashTool
{
    public partial class HashView : UserControl
    {
        private static readonly string[] Algorithms = { "MD5", "SHA1", "SHA256", "SHA512" };
        private readonly IModuleContext _context;
        private CancellationTokenSource _cts;

        public HashView(IModuleContext context)
        {
            _context = context;
            InitializeComponent();
            Drop += OnDrop;
            ShowResults(Compute(Encoding.UTF8.GetBytes(string.Empty)));
        }

        public void Cancel() => _cts?.Cancel();

        private void OnModeChanged(object sender, RoutedEventArgs e)
        {
            if (Input == null)
            {
                return;
            }
            bool file = FileTab.IsChecked == true;
            Input.Visibility = file ? Visibility.Collapsed : Visibility.Visible;
            DropZone.Visibility = file ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnInputChanged(object sender, TextChangedEventArgs e) => ShowResults(Compute(Encoding.UTF8.GetBytes(Input.Text)));

        private void OnPickFile(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Escolher arquivo" };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            {
                _ = HashFileAsync(dialog.FileName);
            }
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0 && File.Exists(files[0]))
            {
                FileTab.IsChecked = true;
                _ = HashFileAsync(files[0]);
            }
        }

        private async Task HashFileAsync(string path)
        {
            _cts?.Cancel();
            var cts = _cts = new CancellationTokenSource();
            FileLabel.Text = Path.GetFileName(path);
            Progress.Visibility = Visibility.Visible;
            Progress.Value = 0;
            var progress = new Progress<double>(v => Progress.Value = v);
            try
            {
                List<HashResult> results = await Task.Run(() => ComputeFile(path, progress, cts.Token), cts.Token);
                ShowResults(results);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _context.Log.Error("Hash failed", ex);
                _context.Notify("Não foi possível ler o arquivo.", NotificationKind.Error);
            }
            finally
            {
                if (cts == _cts)
                {
                    Progress.Visibility = Visibility.Collapsed;
                }
            }
        }

        private static List<HashResult> Compute(byte[] data)
        {
            var results = new List<HashResult>();
            foreach (string name in Algorithms)
            {
                using (HashAlgorithm algorithm = HashAlgorithm.Create(name))
                {
                    results.Add(new HashResult(name.Replace("SHA", "SHA-"), ToHex(algorithm.ComputeHash(data))));
                }
            }
            return results;
        }

        /// <summary>One pass over the file feeds every algorithm.</summary>
        private static List<HashResult> ComputeFile(string path, IProgress<double> progress, CancellationToken token)
        {
            var algorithms = new List<HashAlgorithm>();
            try
            {
                foreach (string name in Algorithms)
                {
                    algorithms.Add(HashAlgorithm.Create(name));
                }
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
                {
                    var buffer = new byte[1 << 20];
                    long done = 0;
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        foreach (HashAlgorithm algorithm in algorithms)
                        {
                            algorithm.TransformBlock(buffer, 0, read, null, 0);
                        }
                        done += read;
                        progress.Report(stream.Length == 0 ? 1 : (double)done / stream.Length);
                    }
                }
                var results = new List<HashResult>();
                for (int i = 0; i < algorithms.Count; i++)
                {
                    algorithms[i].TransformFinalBlock(new byte[0], 0, 0);
                    results.Add(new HashResult(Algorithms[i].Replace("SHA", "SHA-"), ToHex(algorithms[i].Hash)));
                }
                return results;
            }
            finally
            {
                algorithms.ForEach(a => a.Dispose());
            }
        }

        private void ShowResults(List<HashResult> results) => Results.ItemsSource = results;

        private void OnCopy(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is string value)
            {
                try
                {
                    Clipboard.SetText(value);
                    _context.Notify("Hash copiado.", NotificationKind.Success);
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    _context.Notify("A área de transferência está ocupada. Tente de novo.", NotificationKind.Warning);
                }
            }
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }

        internal sealed class HashResult
        {
            public HashResult(string name, string value)
            {
                Name = name;
                Value = value;
            }

            public string Name { get; }

            public string Value { get; }
        }
    }
}
