using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RevitBatchNWCExport.UI;

namespace RevitBatchNWCExport.RevitServer
{
    public sealed class TreeLoadProgress
    {
        public int Folders;
        public int Models;
    }

    /// <summary>
    /// Загрузка дерева всех серверов по HTTP. Узлы собираются в фоне и до конца загрузки
    /// не привязаны к окну, поэтому их можно наполнять не из потока интерфейса.
    /// </summary>
    public sealed class RevitServerTreeLoader
    {
        private readonly RevitServerClient _client;
        // У каждого сервера своя очередь: медленный сервер не задерживает быстрый
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _throttles =
            new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
        // Запросы, ожидающие ответа: id → (сервер, время отправки)
        private readonly ConcurrentDictionary<long, (string Host, DateTime Started)> _pending =
            new ConcurrentDictionary<long, (string Host, DateTime Started)>();
        private long _requestId;
        private int _folders;
        private int _models;

        public RevitServerTreeLoader(RevitServerClient client)
        {
            _client = client;
        }

        /// <summary>Серверы, у которых есть запрос без ответа дольше threshold.</summary>
        public List<string> GetSlowServers(TimeSpan threshold)
        {
            var now = DateTime.UtcNow;
            return _pending.Values
                .Where(p => now - p.Started > threshold)
                .Select(p => p.Host)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(h => h, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public async Task<List<TreeNodeViewModel>> LoadAsync(IEnumerable<string> hosts,
            IProgress<TreeLoadProgress> progress, CancellationToken ct)
        {
            var roots = hosts.Select(TreeNodeViewModel.CreateServer).ToList();
            await Task.WhenAll(roots.Select(async root =>
            {
                try
                {
                    await LoadFolderAsync(root, root.Name, new List<string>(), progress, ct).ConfigureAwait(false);
                    if (root.Children.Count == 0) root.Error = "моделей нет";
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    root.Children.Clear();
                    root.Error = "ошибка сервера: " + Unwrap(ex).Message;
                }
            })).ConfigureAwait(false);
            return roots;
        }

        private async Task LoadFolderAsync(TreeNodeViewModel node, string host, List<string> path,
            IProgress<TreeLoadProgress> progress, CancellationToken ct)
        {
            var contents = await Throttled(host, () => _client.GetContentsAsync(host, path, ct), ct).ConfigureAwait(false);
            Interlocked.Increment(ref _folders);
            Report(progress);

            var tasks = new List<Task>();

            foreach (var folder in (contents.Folders ?? new List<RsFolder>()).OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var child = TreeNodeViewModel.CreateFolder(folder.Name, node);
                node.Children.Add(child);
                if (folder.HasContents)
                {
                    var childPath = new List<string>(path) { folder.Name };
                    tasks.Add(LoadFolderGuardedAsync(child, host, childPath, progress, ct));
                }
            }

            foreach (var model in (contents.Models ?? new List<RsModel>()).OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var modelPath = new List<string>(path) { model.Name };
                string userVisible = "RSN://" + host + "/" + string.Join("/", modelPath);
                var child = TreeNodeViewModel.CreateModel(model.Name, userVisible, node);
                node.Children.Add(child);
                Interlocked.Increment(ref _models);
                tasks.Add(LoadModelDateAsync(child, host, modelPath, ct));
            }

            Report(progress);
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        /// <summary>Ошибка одной папки не прерывает загрузку остального дерева.</summary>
        private async Task LoadFolderGuardedAsync(TreeNodeViewModel node, string host, List<string> path,
            IProgress<TreeLoadProgress> progress, CancellationToken ct)
        {
            try
            {
                await LoadFolderAsync(node, host, path, progress, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                node.Error = "ошибка загрузки: " + Unwrap(ex).Message;
            }
        }

        private async Task LoadModelDateAsync(TreeNodeViewModel node, string host, List<string> path, CancellationToken ct)
        {
            try
            {
                var info = await Throttled(host, () => _client.GetDirectoryInfoAsync(host, path, ct), ct).ConfigureAwait(false);
                if (info != null && info.DateModified > DateTime.MinValue) node.ModifiedAt = info.DateModified.ToLocalTime();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // без даты модель всё равно можно выбрать
            }
        }

        private async Task<T> Throttled<T>(string host, Func<Task<T>> request, CancellationToken ct)
        {
            var throttle = _throttles.GetOrAdd(host, _ => new SemaphoreSlim(RevitServerClient.MaxConnectionsPerServer));
            await throttle.WaitAsync(ct).ConfigureAwait(false);
            long id = Interlocked.Increment(ref _requestId);
            _pending[id] = (host, DateTime.UtcNow);
            try
            {
                return await request().ConfigureAwait(false);
            }
            finally
            {
                _pending.TryRemove(id, out _);
                throttle.Release();
            }
        }

        private void Report(IProgress<TreeLoadProgress> progress) =>
            progress?.Report(new TreeLoadProgress { Folders = Volatile.Read(ref _folders), Models = Volatile.Read(ref _models) });

        private static Exception Unwrap(Exception ex)
        {
            while ((ex is AggregateException || ex is System.Net.Http.HttpRequestException) && ex.InnerException != null)
                ex = ex.InnerException;
            return ex;
        }
    }
}
