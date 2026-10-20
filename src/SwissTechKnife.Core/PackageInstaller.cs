using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SwissTechKnife.Core
{
    /// <summary>An install failure with a message fit for the user.</summary>
    public sealed class ModuleInstallException : Exception
    {
        public ModuleInstallException(string message, Exception inner = null) : base(message, inner)
        {
        }
    }

    /// <summary>
    /// Download → verify SHA-256 → extract to a staging folder → validate module.json → move into place.
    /// Nothing under modules/&lt;id&gt;/ changes unless every step succeeds.
    /// </summary>
    public sealed class PackageInstaller
    {
        private const string Source = "Installer";
        private const long MaxExtractedBytes = 512L * 1024 * 1024;
        private readonly HostPaths _paths;
        private readonly int _maxSdkVersion;

        public PackageInstaller(HostPaths paths, int maxSdkVersion)
        {
            _paths = paths;
            _maxSdkVersion = maxSdkVersion;
        }

        public async Task<InstalledModule> InstallAsync(CatalogEntry entry, Uri packageUri, IProgress<double> progress, CancellationToken cancellationToken)
        {
            ModuleId.Validate(entry.Id);
            if (!entry.IsAvailable)
            {
                throw new ModuleInstallException(Strings.Get("PackageNotPublished"));
            }

            Directory.CreateDirectory(_paths.Downloads);
            string download = Path.Combine(_paths.Downloads, entry.Id + "-" + Guid.NewGuid().ToString("N") + ".zip");
            string moduleRoot = _paths.ModuleRoot(entry.Id);
            string staging = Path.Combine(moduleRoot, ".staging-" + Guid.NewGuid().ToString("N"));
            try
            {
                string hash = await FileDownload.CopyAsync(packageUri, download, entry.Size, progress, 0.95, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(hash, entry.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    Log.Warn(Source, $"Hash mismatch for {entry.Id}: expected {entry.Sha256}, got {hash}");
                    throw new ModuleInstallException(Strings.Get("PackageHashMismatch"));
                }

                cancellationToken.ThrowIfCancellationRequested();
                ModuleManifest manifest = await Task.Run(() => ExtractAndValidate(download, staging, entry.Id), cancellationToken).ConfigureAwait(false);

                string folder = VersionText.ParseOrZero(manifest.Version).ToString();
                string target = Path.Combine(moduleRoot, folder);
                if (Directory.Exists(target))
                {
                    folder += "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                    target = Path.Combine(moduleRoot, folder);
                }
                Directory.Move(staging, target);
                progress?.Report(1);
                Log.Info(Source, $"Installed {entry.Id} {manifest.Version} into {target}");

                return new InstalledModule
                {
                    Id = entry.Id,
                    Version = manifest.Version,
                    Enabled = true,
                    InstalledUtc = DateTime.UtcNow.ToString("o"),
                    Folder = folder,
                    Manifest = manifest,
                    Entry = entry.Clone(),
                };
            }
            catch (ModuleInstallException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                throw new ModuleInstallException(Strings.Get("PackageDownloadFailed"), ex);
            }
            catch (IncompleteDownloadException ex)
            {
                throw new ModuleInstallException(Strings.Get("DownloadIncomplete"), ex);
            }
            catch (InvalidDataException ex)
            {
                throw new ModuleInstallException(Strings.Get("PackageCorrupt"), ex);
            }
            catch (Exception ex)
            {
                Log.Error(Source, "Install failed for " + entry.Id, ex);
                throw new ModuleInstallException(Strings.Format("PackageInstallFailed", ex.Message), ex);
            }
            finally
            {
                TryDeleteFile(download);
                StateStore.TryDelete(staging);
            }
        }

        private ModuleManifest ExtractAndValidate(string zipPath, string staging, string expectedId)
        {
            Directory.CreateDirectory(staging);
            string stagingRoot = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
            long extracted = 0;
            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                foreach (ZipArchiveEntry item in archive.Entries)
                {
                    string target = Path.GetFullPath(Path.Combine(staging, item.FullName));
                    if (!target.StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new ModuleInstallException(Strings.Get("PackageBadPaths"));
                    }
                    if (item.FullName.EndsWith("/", StringComparison.Ordinal) || item.FullName.EndsWith("\\", StringComparison.Ordinal))
                    {
                        Directory.CreateDirectory(target);
                        continue;
                    }
                    extracted += item.Length;
                    if (extracted > MaxExtractedBytes)
                    {
                        throw new ModuleInstallException(Strings.Get("PackageTooLarge"));
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    item.ExtractToFile(target, overwrite: false);
                }
            }

            string manifestPath = Path.Combine(staging, "module.json");
            if (!File.Exists(manifestPath))
            {
                throw new ModuleInstallException(Strings.Get("PackageNoManifest"));
            }
            ModuleManifest manifest = Json.ReadFile<ModuleManifest>(manifestPath);
            Validate(manifest, expectedId, staging);
            return manifest;
        }

        internal void Validate(ModuleManifest manifest, string expectedId, string folder)
        {
            if (manifest == null || manifest.Id != expectedId)
            {
                throw new ModuleInstallException(Strings.Get("PackageWrongModule"));
            }
            if (!VersionText.TryParse(manifest.Version, out _))
            {
                throw new ModuleInstallException(Strings.Get("PackageBadVersion"));
            }
            if (manifest.SdkVersion > _maxSdkVersion)
            {
                throw new ModuleInstallException(Strings.Get("PackageNeedsNewerHost"));
            }
            if (string.IsNullOrWhiteSpace(manifest.EntryType)
                || string.IsNullOrWhiteSpace(manifest.EntryAssembly)
                || manifest.EntryAssembly.IndexOfAny(new[] { '/', '\\', ':' }) >= 0
                || !File.Exists(Path.Combine(folder, manifest.EntryAssembly)))
            {
                throw new ModuleInstallException(Strings.Get("PackageBadEntry"));
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
