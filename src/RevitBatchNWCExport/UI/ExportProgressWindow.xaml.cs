using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace RevitBatchNWCExport.UI
{
    public partial class ExportProgressWindow : Window
    {
        private bool _allowClose;

        internal ExportProgressWindow(int total)
        {
            InitializeComponent();
            Bar.Maximum = Math.Max(1, total);
            CounterText.Text = $"Экспортировано 0 из {total}";
            Closing += (s, e) =>
            {
                if (_allowClose) return;
                e.Cancel = true; // крестик — то же, что «Остановить»
                RequestStop();
            };
        }

        internal event EventHandler StopRequested;

        internal void SetProgress(int done, int total, string current)
        {
            Bar.Value = done;
            CounterText.Text = $"Экспортировано {done} из {total}";
            CurrentText.Text = current;
        }

        internal void ForceClose()
        {
            _allowClose = true;
            Close();
        }

        private void Stop_Click(object sender, RoutedEventArgs e) => RequestStop();

        private void RequestStop()
        {
            StopButton.IsEnabled = false;
            StopButton.Content = "Остановка…";
            CurrentText.Text = "Экспорт остановится после текущей модели";
            StopRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Окно прогресса в отдельном потоке: основной поток Revit занят экспортом,
    /// а окно продолжает перерисовываться и принимать «Остановить».
    /// </summary>
    public sealed class ExportProgress : IDisposable
    {
        private readonly int _total;
        private ExportProgressWindow _window;
        private volatile bool _stopRequested;

        private ExportProgress(int total)
        {
            _total = total;
        }

        public bool StopRequested => _stopRequested;

        public static ExportProgress Start(int total)
        {
            var progress = new ExportProgress(total);
            using (var ready = new ManualResetEventSlim(false))
            {
                var thread = new Thread(() =>
                {
                    try
                    {
                        var window = new ExportProgressWindow(total);
                        window.StopRequested += (s, e) => progress._stopRequested = true;
                        window.Closed += (s, e) => Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                        progress._window = window;
                        window.Show();
                    }
                    finally
                    {
                        ready.Set();
                    }
                    Dispatcher.Run();
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Start();
                ready.Wait(TimeSpan.FromSeconds(10));
            }
            return progress;
        }

        public void Report(int done, string current)
        {
            var window = _window;
            window?.Dispatcher.BeginInvoke(new Action(() => window.SetProgress(done, _total, current)));
        }

        public void Dispose()
        {
            var window = _window;
            _window = null;
            window?.Dispatcher.BeginInvoke(new Action(window.ForceClose));
        }
    }
}
