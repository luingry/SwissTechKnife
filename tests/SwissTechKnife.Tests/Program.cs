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
            Run("Garbage collection keeps the self-update record and installer", CollectGarbageKeepsHostUpdate);
            Run("SHA-256 text is 64 hex characters", Sha256Format);
            Run("Host update is offered only for a newer, valid release", HostOffered);
            Run("Host update notes cover running (exclusive) to offered (inclusive)", HostNotes);
            Run("Catalog keeps a valid host entry and drops an invalid one", HostSanitize);
            Run("Host update outcome follows the running version", HostOutcome);
            Run("Installer command line is unattended and quoted", HostArguments);
            Run("Installer download verifies SHA-256 and reuses a verified file", HostDownload);
            Run("Installer download refuses a hash mismatch and keeps nothing", HostDownloadMismatch);
            Run("Every UI string has a pt-BR translation; unknown languages fall back to English", Localization);

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

        private static void Localization()
        {
            string missing = string.Join(", ", Strings.MissingTranslations());
            Assert(missing.Length == 0, "missing pt-BR strings: " + missing);
            Assert(Strings.Normalize(null) == Strings.English && Strings.Normalize("fr") == Strings.English, "unknown language is English");
            Assert(Strings.Normalize("PT-br") == Strings.PortugueseBrazil, "language codes are case-insensitive");
            try
            {
                Strings.SetLanguage(Strings.PortugueseBrazil);
                Assert(Strings.Get("Install") == "Instalar", "pt-BR table is used");
                Assert(Strings.Get("NoSuchKey") == "NoSuchKey", "unknown key returns itself");
            }
            finally
            {
                Strings.SetLanguage(Strings.English);
            }
            Assert(Strings.Get("Install") == "Install", "English is restored");
        }

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

        private static string Sha(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return FileDownload.ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));
            }
        }

        private static HostRelease Release(string version, string sha = null) => new HostRelease
        {
            Version = version,
            Installer = "SwissTechKnife-" + version + "-setup.exe",
            Sha256 = sha ?? new string('a', 64),
            Size = 10,
            ReleaseNotes = new List<ReleaseNote>
            {
                new ReleaseNote { Version = "0.3.0", Notes = { "old" } },
                new ReleaseNote { Version = "0.5.0", Notes = { "future" } },
                new ReleaseNote { Version = "0.3.1", Notes = { "fix" } },
                new ReleaseNote { Version = "0.4.0", Notes = { "new" } },
                new ReleaseNote { Version = "bad", Notes = { "ignored" } },
                null,
            },
        };

        private static void Sha256Format()
        {
            Assert(HostUpdate.IsSha256(new string('a', 64)), "lowercase hex");
            Assert(HostUpdate.IsSha256(" " + new string('F', 64) + " "), "uppercase, trimmed");
            Assert(!HostUpdate.IsSha256(new string('a', 63)), "too short");
            Assert(!HostUpdate.IsSha256(new string('g', 64)), "not hex");
            Assert(!HostUpdate.IsSha256(null), "null");
            Assert(FileDownload.HashEquals("ABC", " abc "), "hash compare ignores case and spaces");
            Assert(!FileDownload.HashEquals("abc", null), "missing expected hash never matches");
        }

        private static void HostOffered()
        {
            Assert(HostUpdate.IsOffered(Release("0.4.0"), "0.3.0"), "newer is offered");
            Assert(!HostUpdate.IsOffered(Release("0.3.0"), "0.3.0"), "same version is not offered");
            Assert(!HostUpdate.IsOffered(Release("0.2.9"), "0.3.0"), "older is not offered");
            Assert(HostUpdate.IsOffered(Release("0.10.0"), "0.9.0"), "numeric ordering");
            Assert(!HostUpdate.IsOffered(null, "0.3.0"), "no entry");
            Assert(!HostUpdate.IsOffered(Release("x.y"), "0.3.0"), "unparseable version");
            Assert(!HostUpdate.IsOffered(Release("0.4.0", "nothex"), "0.3.0"), "bad hash");
            HostRelease noInstaller = Release("0.4.0");
            noInstaller.Installer = " ";
            Assert(!HostUpdate.IsOffered(noInstaller, "0.3.0"), "no installer");
            Assert(HostUpdate.InstallerFileName("0.4.0") == "SwissTechKnife-0.4.0-setup.exe", "installer file name");
            Assert(HostUpdate.InstallerFileName(@"..\..\evil") == "SwissTechKnife-0.0.0-setup.exe", "version cannot inject a path");
        }

        private static void HostNotes()
        {
            IReadOnlyList<ReleaseNote> notes = HostUpdate.NotesFor(Release("0.4.0"), "0.3.0");
            Assert(string.Join(",", notes.Select(n => n.Version)) == "0.4.0,0.3.1", "newest first, (0.3.0, 0.4.0]: " + string.Join(",", notes.Select(n => n.Version)));
            Assert(HostUpdate.NotesFor(Release("0.3.0"), "0.3.0").Count == 0, "no notes without an update");
            HostRelease bare = Release("0.4.0");
            bare.ReleaseNotes = null;
            Assert(HostUpdate.NotesFor(bare, "0.3.0").Count == 0, "missing notes");
        }

        private static void HostSanitize()
        {
            Catalog clean = CatalogClient.Sanitize(new Catalog { Host = Release("0.4.0") });
            Assert(clean.Host != null && clean.Host.Version == "0.4.0", "valid host kept");
            Catalog dirty = CatalogClient.Sanitize(new Catalog { Host = Release("0.4.0", "short") });
            Assert(dirty.Host == null, "invalid host dropped");
            byte[] json = Encoding.UTF8.GetBytes("{\"schema\":1,\"modules\":[],\"host\":{\"version\":\"0.4.0\",\"installer\":\"x.exe\",\"sha256\":\"" + new string('b', 64) + "\",\"size\":5,\"releaseNotes\":[{\"version\":\"0.4.0\",\"date\":\"2026-10-20\",\"notes\":[\"Automatic updates.\"]}]}}");
            Catalog parsed = CatalogClient.Sanitize(Json.Deserialize<Catalog>(json));
            Assert(parsed.Host?.ReleaseNotes?.Single().Notes.Single() == "Automatic updates.", "host entry parses from catalog.json");
            Assert(CatalogClient.Sanitize(Json.Deserialize<Catalog>(Encoding.UTF8.GetBytes("{\"modules\":[]}"))).Host == null, "catalog without host");
        }

        private static void HostOutcome()
        {
            var pending = new PendingHostUpdate { FromVersion = "0.3.0", ToVersion = "0.4.0" };
            Assert(HostUpdate.Evaluate(pending, "0.4.0") == HostUpdateOutcome.Succeeded, "target running");
            Assert(HostUpdate.Evaluate(pending, "0.4.1") == HostUpdateOutcome.Succeeded, "newer than target running");
            Assert(HostUpdate.Evaluate(pending, "0.3.0") == HostUpdateOutcome.Failed, "old version still running");
            Assert(HostUpdate.Evaluate(null, "0.3.0") == HostUpdateOutcome.None, "no attempt");
            Assert(HostUpdate.Evaluate(new PendingHostUpdate { ToVersion = "?" }, "0.3.0") == HostUpdateOutcome.None, "unusable record");
        }

        private static void HostArguments()
        {
            string args = HostUpdate.InstallerArguments(@"C:\Users\A B\log.txt", @"C:\Users\A B\s.signal", @"Local\SwissTechKnife.Host", @"C:\Program Files\SwissTechKnife\SwissTechKnife.exe");
            foreach (string flag in new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS", "/STKUPDATE" })
            {
                Assert(args.Contains(flag), "missing " + flag);
            }
            Assert(args.Contains("\"/LOG=C:\\Users\\A B\\log.txt\""), "log path quoted: " + args);
            Assert(args.Contains("\"/STKAPP=C:\\Program Files\\SwissTechKnife\\SwissTechKnife.exe\""), "app path quoted");
            Assert(!HostUpdate.InstallerArguments("a\"b", "s", "m", "x").Contains("a\"b"), "quotes cannot break out");
        }

        private static void HostDownload()
        {
            using (var sandbox = new Sandbox())
            {
                string source = sandbox.FeedFile("SwissTechKnife-0.4.0-setup.exe", "installer bytes");
                HostRelease release = Release("0.4.0", Sha("installer bytes"));
                var updater = new HostUpdater(sandbox.Paths);
                string path = updater.DownloadAsync(release, new Uri(source), null, CancellationToken.None).GetAwaiter().GetResult();
                Assert(path == Path.Combine(sandbox.Paths.Downloads, "SwissTechKnife-0.4.0-setup.exe"), "lands in downloads: " + path);
                Assert(File.ReadAllText(path) == "installer bytes", "content copied");
                Assert(Directory.GetFiles(sandbox.Paths.Downloads).Length == 1, "no partial files left");

                File.Delete(source);
                string again = updater.DownloadAsync(release, new Uri(source), null, CancellationToken.None).GetAwaiter().GetResult();
                Assert(again == path, "verified download reused without the source");
            }
        }

        private static void HostDownloadMismatch()
        {
            using (var sandbox = new Sandbox())
            {
                string source = sandbox.FeedFile("SwissTechKnife-0.4.0-setup.exe", "tampered");
                var updater = new HostUpdater(sandbox.Paths);
                Throws<HostUpdateException>(() => updater.DownloadAsync(Release("0.4.0", Sha("original")), new Uri(source), null, CancellationToken.None).GetAwaiter().GetResult(), "mismatch must throw");
                Assert(!Directory.EnumerateFiles(sandbox.Paths.Downloads).Any(), "nothing kept after a mismatch");
            }
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
            Uri remote = CatalogClient.ResolveCatalogUri(null, @"C:\App");
            Assert(remote.AbsoluteUri == CatalogClient.DefaultCatalogUrl, "default catalog is the published one: " + remote);
            Uri bundled = CatalogClient.BundledCatalogUri(@"C:\App");
            Assert(bundled.IsFile && bundled.LocalPath == @"C:\App\feed\catalog.json", "bundled catalog is next to the exe: " + bundled.LocalPath);
            Uri relative = CatalogClient.ResolveCatalogUri(@"feed\catalog.json", @"C:\App");
            Assert(relative.IsFile && relative.LocalPath == @"C:\App\feed\catalog.json", "relative setting resolves next to the exe");
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

        private static void CollectGarbageKeepsHostUpdate()
        {
            using (var sandbox = new Sandbox())
            {
                string[] kept = { "host-update.json", "host-update.signal", "host-update-0.4.0.log", "SwissTechKnife-0.4.0-setup.exe" };
                string[] removed = { "demo-1a2b.zip", "SwissTechKnife-0.4.0-setup.exe.1a2b3c4d.partial" };
                foreach (string name in kept.Concat(removed))
                {
                    File.WriteAllText(Path.Combine(sandbox.Paths.Downloads, name), "x");
                }
                var store = new StateStore(sandbox.Paths);
                store.Load();
                store.CollectGarbage();
                foreach (string name in kept)
                {
                    Assert(File.Exists(Path.Combine(sandbox.Paths.Downloads, name)), name + " must survive the restart the update causes");
                }
                foreach (string name in removed)
                {
                    Assert(!File.Exists(Path.Combine(sandbox.Paths.Downloads, name)), name + " should be cleaned");
                }
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

            public string FeedFile(string name, string content)
            {
                string path = Path.Combine(_dir, "feed", name);
                File.WriteAllText(path, content);
                return path;
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
