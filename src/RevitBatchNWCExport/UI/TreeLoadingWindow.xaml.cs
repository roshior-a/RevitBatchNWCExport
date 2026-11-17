using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using RevitBatchNWCExport.RevitServer;

namespace RevitBatchNWCExport.UI
{
    /// <summary>Модальное окно прогресса загрузки дерева Revit Server.</summary>
    public partial class TreeLoadingWindow : Window
    {
        private static readonly TimeSpan SlowThreshold = TimeSpan.FromSeconds(20);

        private readonly IReadOnlyList<string> _hosts;
        private readonly string _revitVersion;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private bool _finished;

        public TreeLoadingWindow(IReadOnlyList<string> hosts, string revitVersion)
        {
            InitializeComponent();
            _hosts = hosts;
            _revitVersion = revitVersion;
            ServersText.Text = "Серверы: " + string.Join(", ", hosts);
            Loaded += OnLoaded;
            // Окно закрывается только после завершения загрузки, чтобы Nodes и Status были готовы
            Closing += (s, e) =>
            {
                if (_finished) return;
                e.Cancel = true;
                _cts.Cancel();
            };
        }

        public List<TreeNodeViewModel> Nodes { get; private set; } = new List<TreeNodeViewModel>();

        /// <summary>Сообщение для вкладки Revit Server, если загрузка прервана.</summary>
        public string Status { get; private set; }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            var progress = new Progress<TreeLoadProgress>(p =>
            {
                if (!_cts.IsCancellationRequested) CountersText.Text = $"Папок: {p.Folders} · моделей: {p.Models}";
            });
            var client = new RevitServerClient(_revitVersion);
            var loader = new RevitServerTreeLoader(client);

            // Раз в секунду: серверы, которые не отвечают на запрос дольше 20 с
            var slowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            slowTimer.Tick += (s, args) =>
            {
                var slow = loader.GetSlowServers(SlowThreshold);
                SlowText.Text = string.Join("\n", slow.Select(h => $"Нужно больше времени: сервер {h} отвечает дольше обычного"));
                SlowText.Visibility = slow.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            };
            slowTimer.Start();

            try
            {
                Nodes = await loader.LoadAsync(_hosts, progress, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                Status = "Загрузка дерева Revit Server отменена. Откройте окно заново, чтобы загрузить его.";
            }
            catch (Exception ex)
            {
                Status = "Не удалось загрузить дерево Revit Server: " + ex.Message;
            }
            finally
            {
                slowTimer.Stop();
                client.Dispose();
            }

            _finished = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            CountersText.Text = "Отмена…";
            _cts.Cancel();
        }
    }
}
