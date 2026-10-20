using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SwissTechKnife.Core
{
    public sealed class CatalogResult
    {
        public CatalogResult(Catalog catalog, Uri source, bool fromCache, Exception error)
        {
            Catalog = catalog;
            Source = source;
            FromCache = fromCache;
            Error = error;
        }

        public Catalog Catalog { get; }

        public Uri Source { get; }

        /// <summary>True when the live catalog failed and the last good copy was used instead.</summary>
        public bool FromCache { get; }

        public Exception Error { get; }
    }

    /// <summary>Fetches catalog.json (http/https/file) and keeps the last good copy for offline starts.</summary>
    public sealed class CatalogClient
    {
        private const string Source = "Catalog";
        private static readonly Lazy<HttpClient> SharedHttp = new Lazy<HttpClient>(CreateHttp);
        private readonly HostPaths _paths;

        public CatalogClient(HostPaths paths)
        {
            _paths = paths;
        }

        internal static HttpClient Http => SharedHttp.Value;

        public static Uri ResolveCatalogUri(string setting, string appDirectory)
        {
            string value = string.IsNullOrWhiteSpace(setting) ? Path.Combine("feed", "catalog.json") : setting.Trim();
            if (Uri.TryCreate(value, UriKind.Absolute, out Uri absolute) && (absolute.Scheme == Uri.UriSchemeHttps || absolute.Scheme == Uri.UriSchemeHttp || absolute.IsFile))
            {
                return absolute;
            }
            string path = Path.IsPathRooted(value) ? value : Path.Combine(appDirectory, value);
            return new Uri(Path.GetFullPath(path));
        }

        public static Uri ResolvePackageUri(Uri catalogUri, string package) => new Uri(catalogUri, package);

        public async Task<CatalogResult> LoadAsync(Uri uri, CancellationToken cancellationToken)
        {
            try
            {
                byte[] bytes = await ReadAllAsync(uri, cancellationToken).ConfigureAwait(false);
                Catalog catalog = Sanitize(Json.Deserialize<Catalog>(bytes));
                TryWriteCache(bytes);
                return new CatalogResult(catalog, uri, fromCache: false, error: null);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Log.Warn(Source, "Catalog unavailable at " + uri + ": " + ex.Message);
                if (File.Exists(_paths.CatalogCacheFile))
                {
                    try
                    {
                        Catalog cached = Sanitize(Json.ReadFile<Catalog>(_paths.CatalogCacheFile));
                        return new CatalogResult(cached, uri, fromCache: true, error: ex);
                    }
                    catch (Exception cacheError)
                    {
                        Log.Warn(Source, "Catalog cache unreadable: " + cacheError.Message);
                    }
                }
                return new CatalogResult(new Catalog(), uri, fromCache: false, error: ex);
            }
        }

        /// <summary>
        /// Uses the last saved catalog without touching the network (update checks not due).
        /// Falls back to a live load when nothing is cached yet.
        /// </summary>
        public async Task<CatalogResult> LoadCachedAsync(Uri uri, CancellationToken cancellationToken)
        {
            if (File.Exists(_paths.CatalogCacheFile))
            {
                try
                {
                    Catalog cached = await Task.Run(() => Sanitize(Json.ReadFile<Catalog>(_paths.CatalogCacheFile)), cancellationToken).ConfigureAwait(false);
                    return new CatalogResult(cached, uri, fromCache: true, error: null);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    Log.Warn(Source, "Catalog cache unreadable: " + ex.Message);
                }
            }
            return await LoadAsync(uri, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Drops malformed and duplicate entries so one bad entry never hides the rest.</summary>
        internal static Catalog Sanitize(Catalog catalog)
        {
            var result = new Catalog { Schema = catalog?.Schema ?? 1 };
            if (catalog?.Host != null)
            {
                if (HostUpdate.IsValid(catalog.Host))
                {
                    result.Host = catalog.Host;
                }
                else
                {
                    Log.Warn(Source, "Skipped invalid host entry: " + (catalog.Host.Version ?? "<null>"));
                }
            }
            if (catalog?.Modules == null)
            {
                return result;
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (CatalogEntry entry in catalog.Modules)
            {
                if (entry == null || !ModuleId.IsValid(entry.Id) || string.IsNullOrWhiteSpace(entry.Name))
                {
                    Log.Warn(Source, "Skipped invalid catalog entry: " + (entry?.Id ?? "<null>"));
                    continue;
                }
                if (!seen.Add(entry.Id))
                {
                    Log.Warn(Source, "Skipped duplicate catalog entry: " + entry.Id);
                    continue;
                }
                result.Modules.Add(entry);
            }
            return result;
        }

        public static async Task<byte[]> ReadAllAsync(Uri uri, CancellationToken cancellationToken)
        {
            if (uri.IsFile)
            {
                return await Task.Run(() => File.ReadAllBytes(uri.LocalPath), cancellationToken).ConfigureAwait(false);
            }
            using (HttpResponseMessage response = await Http.GetAsync(uri, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
        }

        private void TryWriteCache(byte[] bytes)
        {
            try
            {
                Directory.CreateDirectory(_paths.Cache);
                string temp = _paths.CatalogCacheFile + ".tmp";
                File.WriteAllBytes(temp, bytes);
                if (File.Exists(_paths.CatalogCacheFile))
                {
                    File.Delete(_paths.CatalogCacheFile);
                }
                File.Move(temp, _paths.CatalogCacheFile);
            }
            catch (Exception ex)
            {
                Log.Warn(Source, "Could not cache catalog: " + ex.Message);
            }
        }

        private static HttpClient CreateHttp()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SwissTechKnife/" + typeof(CatalogClient).Assembly.GetName().Version);
            return client;
        }
    }
}
