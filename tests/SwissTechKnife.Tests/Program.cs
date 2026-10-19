using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using SwissTechKnife.Core;

namespace SwissTechKnife.Tests
{
    internal static class Program
    {
        private static int _failed;
        private static int _passed;

        private static int Main()
        {
            Run("Module ids reject path tricks", ModuleIds);
            Run("Versions compare numerically", Versions);
            Run("Catalog drops invalid and duplicate entries", CatalogSanitize);
            Run("Catalog package URLs resolve relative to the catalog", PackageUris);
            Run("Install extracts a valid package", InstallValid);
            Run("Install refuses a hash mismatch and leaves nothing behind", InstallHashMismatch);
            Run("Install refuses zip-slip entries", InstallZipSlip);
            Run("Install refuses a package for another module", InstallWrongId);
            Run("Install refuses a newer SDK contract", InstallNewerSdk);
            Run("State survives a corrupt state.json via .bak", StateBackup);
            Run("Garbage collection removes orphan folders", CollectGarbage);

            Console.WriteLine();
            Console.WriteLine($"{_passed} passed, {_failed} failed");
            return _failed == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine("  PASS  " + name);
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine("  FAIL  " + name + ": " + ex.Message);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception(message);
            }
        }

        private static void Throws<T>(Action action, string message) where T : Exception
        {
            try
            {
                action();
            }
            catch (T)
            {
                return;
            }
            catch (AggregateException ex) when (ex.InnerException is T)
            {
                return;
            }
            throw new Exception(message);
        }

        // ── Tests ─────────────────────────────────────────────

        private static void ModuleIds()
        {
            foreach (string ok in new[] { "smooth-mice", "winput-lan", "aq-tracker", "a1" })
            {
                Assert(ModuleId.IsValid(ok), ok + " should be valid");
            }
            foreach (string bad in new[] { null, "", "a", "../x", "..", "A-b", "-x", "x/y", "x\\y", "x.y", "c:", new string('a', 65) })
            {
                Assert(!ModuleId.IsValid(bad), (bad ?? "null") + " should be invalid");
            }
        }

        private static void Versions()
        {
            Assert(VersionText.IsNewer("1.10.0", "1.9.9"), "1.10.0 > 1.9.9");
            Assert(!VersionText.IsNewer("1.0.0", "1.0.0"), "equal is not newer");
            Assert(VersionText.IsNewer("2.0", "1.9.9"), "2.0 > 1.9.9");
            Assert(!VersionText.IsNewer("1.2.3-beta", "1.2.3"), "pre-release suffix ignored");
            Assert(VersionText.ParseOrZero("garbage") == new Version(0, 0, 0), "garbage parses to zero");
        }

        private static void CatalogSanitize()
        {
            var catalog = new Catalog
            {
                Modules = new List<CatalogEntry>
                {
                    new CatalogEntry { Id = "good", Name = "Good" },
                    new CatalogEntry { Id = "good", Name = "Duplicate" },
                    new CatalogEntry { Id = "../evil", Name = "Evil" },
                    new CatalogEntry { Id = "noname" },
                    null,
                },
            };
            Catalog clean = CatalogClient.Sanitize(catalog);
            Assert(clean.Modules.Count == 1 && clean.Modules[0].Name == "Good", "only the first valid entry survives");
        }

        private static void PackageUris()
        {
            var http = new Uri("https://example.com/feed/catalog.json");
            Assert(CatalogClient.ResolvePackageUri(http, "a-1.0.0.zip").ToString() == "https://example.com/feed/a-1.0.0.zip", "relative http");
            Assert(CatalogClient.ResolvePackageUri(http, "https://cdn.example.com/a.zip").Host == "cdn.example.com", "absolute wins");
            Uri local = CatalogClient.ResolveCatalogUri(null, @"C:\App");
            Assert(local.IsFile && local.LocalPath == @"C:\App\feed\catalog.json", "default catalog is next to the exe: " + local.LocalPath);
        }

        private static void InstallValid()
        {
            using (var sandbox = new Sandbox())
            {
                string zip = sandbox.Package("demo", Manifest("demo"), ("Demo.dll", "x"));
                InstalledModule installed = sandbox.Install(Entry("demo", zip), zip);
                string folder = Path.Combine(sandbox.Paths.ModuleRoot("demo"), installed.Folder);
                Assert(File.Exists(Path.Combine(folder, "Demo.dll")), "entry assembly extracted");
                Assert(installed.Version == "1.0.0" && installed.Enabled, "state reflects the manifest");
                Assert(!Directory.EnumerateDirectories(sandbox.Paths.ModuleRoot("demo"), ".staging-*").Any(), "staging cleaned");
                Assert(!Directory.EnumerateFiles(sandbox.Paths.Downloads).Any(), "download cleaned");
            }
        }

        private static void InstallHashMismatch()
        {
            using (var sandbox = new Sandbox())
            {
                string zip = sandbox.Package("demo", Manifest("demo"), ("Demo.dll", "x"));
                CatalogEntry entry = Entry("demo", zip);
                entry.Sha256 = new string('0', 64);
                Throws<ModuleInstallException>(() => sandbox.Install(entry, zip), "mismatch must throw");
                Assert(!Directory.Exists(sandbox.Paths.ModuleRoot("demo")) || !Directory.EnumerateFileSystemEntries(sandbox.Paths.ModuleRoot("demo")).Any(), "nothing installed");
            }
        }

        private static void InstallZipSlip()
        {
            using (var sandbox = new Sandbox())
            {
                string zip = sandbox.Package("demo", Manifest("demo"), ("Demo.dll", "x"), ("../../escaped.txt", "pwned"));
                Throws<ModuleInstallException>(() => sandbox.Install(Entry("demo", zip), zip), "zip-slip must throw");
                Assert(!File.Exists(Path.Combine(sandbox.Paths.Modules, "escaped.txt")) && !File.Exists(Path.Combine(sandbox.Paths.Root, "escaped.txt")), "nothing escaped");
            }
        }

        private static void InstallWrongId()
        {
            using (var sandbox = new Sandbox())
            {
                string zip = sandbox.Package("demo", Manifest("other"), ("Demo.dll", "x"));
                Throws<ModuleInstallException>(() => sandbox.Install(Entry("demo", zip), zip), "id mismatch must throw");
            }
        }

        private static void InstallNewerSdk()
        {
            using (var sandbox = new Sandbox())
            {
                string zip = sandbox.Package("demo", Manifest("demo", sdk: 99), ("Demo.dll", "x"));
                Throws<ModuleInstallException>(() => sandbox.Install(Entry("demo", zip), zip), "newer sdk must throw");
            }
        }

        private static void StateBackup()
        {
            using (var sandbox = new Sandbox())
            {
                var store = new StateStore(sandbox.Paths);
                store.Load();
                store.Upsert(new InstalledModule { Id = "demo", Version = "1.0.0", Folder = "1.0.0", Manifest = Manifest("demo") });
                store.SetEnabled("demo", false); // second write creates state.json.bak with the first state
                File.WriteAllText(sandbox.Paths.StateFile, "{ corrupt");
                var reloaded = new StateStore(sandbox.Paths);
                reloaded.Load();
                Assert(reloaded.Find("demo") != null, "backup state recovered");
            }
        }

        private static void CollectGarbage()
        {
            using (var sandbox = new Sandbox())
            {
                var store = new StateStore(sandbox.Paths);
                store.Load();
                store.Upsert(new InstalledModule { Id = "demo", Version = "1.0.0", Folder = "1.0.0", Manifest = Manifest("demo") });
                string kept = Path.Combine(sandbox.Paths.ModuleRoot("demo"), "1.0.0");
                string orphanVersion = Path.Combine(sandbox.Paths.ModuleRoot("demo"), "0.9.0");
                string orphanModule = Path.Combine(sandbox.Paths.Modules, "gone", "1.0.0");
                foreach (string dir in new[] { kept, orphanVersion, orphanModule })
                {
                    Directory.CreateDirectory(dir);
                }
                store.CollectGarbage();
                Assert(Directory.Exists(kept), "installed version kept");
                Assert(!Directory.Exists(orphanVersion), "old version removed");
                Assert(!Directory.Exists(Path.Combine(sandbox.Paths.Modules, "gone")), "uninstalled module removed");
            }
        }

        // ── Helpers ───────────────────────────────────────────

        private static ModuleManifest Manifest(string id, int sdk = 1) => new ModuleManifest
        {
            Id = id,
            Version = "1.0.0",
            EntryAssembly = "Demo.dll",
            EntryType = "Demo.Module",
            SdkVersion = sdk,
        };

        private static CatalogEntry Entry(string id, string zip) => new CatalogEntry
        {
            Id = id,
            Name = id,
            Version = "1.0.0",
            Package = Path.GetFileName(zip),
            Sha256 = Sha256(zip),
            Size = new FileInfo(zip).Length,
        };

        private static string Sha256(string file)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(file))
            {
                return string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2")));
            }
        }

        private sealed class Sandbox : IDisposable
        {
            private readonly string _dir = Path.Combine(Path.GetTempPath(), "stk-tests-" + Guid.NewGuid().ToString("N"));

            public Sandbox()
            {
                Paths = new HostPaths(Path.Combine(_dir, "root"));
                Paths.EnsureCreated();
                Directory.CreateDirectory(Path.Combine(_dir, "feed"));
            }

            public HostPaths Paths { get; }

            public string Package(string name, ModuleManifest manifest, params (string Path, string Content)[] files)
            {
                string zip = Path.Combine(_dir, "feed", name + ".zip");
                using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
                {
                    Write(archive, "module.json", Encoding.UTF8.GetString(Json.Serialize(manifest)));
                    foreach (var file in files)
                    {
                        Write(archive, file.Path, file.Content);
                    }
                }
                return zip;
            }

            public InstalledModule Install(CatalogEntry entry, string zip)
            {
                var installer = new PackageInstaller(Paths, maxSdkVersion: 1);
                return installer.InstallAsync(entry, new Uri(zip), null, CancellationToken.None).GetAwaiter().GetResult();
            }

            public void Dispose()
            {
                try
                {
                    Directory.Delete(_dir, recursive: true);
                }
                catch (IOException)
                {
                }
            }

            private static void Write(ZipArchive archive, string path, string content)
            {
                using (var writer = new StreamWriter(archive.CreateEntry(path).Open()))
                {
                    writer.Write(content);
                }
            }
        }
    }
}
