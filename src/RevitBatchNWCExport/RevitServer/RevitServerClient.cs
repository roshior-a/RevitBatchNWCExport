using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RevitBatchNWCExport.RevitServer
{
    [DataContract]
    public sealed class RsContents
    {
        [DataMember] public List<RsFolder> Folders { get; set; }
        [DataMember] public List<RsModel> Models { get; set; }
    }

    [DataContract]
    public sealed class RsFolder
    {
        [DataMember] public string Name { get; set; }
        [DataMember] public bool HasContents { get; set; }
    }

    [DataContract]
    public sealed class RsModel
    {
        [DataMember] public string Name { get; set; }
    }

    [DataContract]
    public sealed class RsDirectoryInfo
    {
        [DataMember] public DateTime DateModified { get; set; }
        [DataMember] public string LastModifiedBy { get; set; }
    }

    /// <summary>
    /// REST API администрирования Revit Server:
    /// http://{host}/RevitServerAdminRESTService{год}/AdminRESTService.svc/{путь}/contents
    /// Путь — имена папок через «|», корень — «|».
    /// </summary>
    public sealed class RevitServerClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _version;

        public const int MaxConnectionsPerServer = 8;

        public RevitServerClient(string revitVersion)
        {
            _version = revitVersion;
            var handler = new HttpClientHandler
            {
                UseDefaultCredentials = true,
                // В .NET Framework по умолчанию только 2 соединения на сервер
                MaxConnectionsPerServer = MaxConnectionsPerServer
            };
            // Без таймаута: большие серверы считают размеры папок и отвечают на корень по 30+ с.
            // Прервать загрузку можно кнопкой «Отмена».
            _http = new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        }

        public Task<RsContents> GetContentsAsync(string host, IReadOnlyList<string> folderPath, CancellationToken ct) =>
            GetAsync<RsContents>(Url(host, folderPath, "contents"), ct);

        public Task<RsDirectoryInfo> GetDirectoryInfoAsync(string host, IReadOnlyList<string> itemPath, CancellationToken ct) =>
            GetAsync<RsDirectoryInfo>(Url(host, itemPath, "directoryinfo"), ct);

        private string Url(string host, IReadOnlyList<string> path, string operation)
        {
            string encoded = path.Count == 0 ? "|" : string.Join("|", path.Select(Uri.EscapeDataString));
            return $"http://{host}/RevitServerAdminRESTService{_version}/AdminRESTService.svc/{encoded}/{operation}";
        }

        /// <summary>
        /// Под нагрузкой Revit Server иногда отвечает 500 или 400 на исправный запрос,
        /// и каждый раз в другой папке, поэтому запрос повторяется с паузой.
        /// </summary>
        private static readonly TimeSpan[] RetryDelays = { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(8) };

        private async Task<T> GetAsync<T>(string url, CancellationToken ct)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    return await GetOnceAsync<T>(url, ct).ConfigureAwait(false);
                }
                catch (HttpRequestException) when (attempt < RetryDelays.Length && !ct.IsCancellationRequested)
                {
                    await Task.Delay(RetryDelays[attempt], ct).ConfigureAwait(false);
                }
            }
        }

        private async Task<T> GetOnceAsync<T>(string url, CancellationToken ct)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.Add("User-Name", Environment.UserName);
                request.Headers.Add("User-Machine-Name", Environment.MachineName);
                request.Headers.Add("Operation-GUID", Guid.NewGuid().ToString());

                using (var response = await _http.SendAsync(request, ct).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                    var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    using (var stream = new MemoryStream(bytes))
                        return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
                }
            }
        }

        public void Dispose() => _http.Dispose();
    }
}
