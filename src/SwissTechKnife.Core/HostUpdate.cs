using System;
using System.Collections.Generic;
using System.Linq;

namespace SwissTechKnife.Core
{
    public enum HostUpdateOutcome
    {
        /// <summary>No update was attempted (or its record is unusable).</summary>
        None,

        /// <summary>The running version is the one the installer was meant to install.</summary>
        Succeeded,

        /// <summary>The installer ran (or was meant to) and the old version is still running.</summary>
        Failed,
    }

    /// <summary>Pure rules for the host self-update: what the catalog offers and how an attempt ended.</summary>
    public static class HostUpdate
    {
        /// <summary>Lowercase or uppercase hex, exactly 64 characters.</summary>
        public static bool IsSha256(string text)
        {
            if (text == null)
            {
                return false;
            }
            string value = text.Trim();
            if (value.Length != 64)
            {
                return false;
            }
            foreach (char c in value)
            {
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>A host entry the app can act on: parseable version, an installer and a well-formed hash.</summary>
        public static bool IsValid(HostRelease release) =>
            release != null
            && VersionText.TryParse(release.Version, out _)
            && !string.IsNullOrWhiteSpace(release.Installer)
            && IsSha256(release.Sha256)
            && release.Size >= 0;

        /// <summary>True when the catalog offers a host strictly newer than <paramref name="runningVersion"/>.</summary>
        public static bool IsOffered(HostRelease release, string runningVersion) =>
            IsValid(release) && VersionText.IsNewer(release.Version, runningVersion);

        /// <summary>What the update brings: notes after the running version up to the offered one, newest first.</summary>
        public static IReadOnlyList<ReleaseNote> NotesFor(HostRelease release, string runningVersion)
        {
            if (!IsOffered(release, runningVersion) || release.ReleaseNotes == null)
            {
                return new ReleaseNote[0];
            }
            Version running = VersionText.ParseOrZero(runningVersion);
            Version target = VersionText.ParseOrZero(release.Version);
            return release.ReleaseNotes
                .Where(n => n != null && VersionText.TryParse(n.Version, out _))
                .Where(n => VersionText.ParseOrZero(n.Version) > running && VersionText.ParseOrZero(n.Version) <= target)
                .OrderByDescending(n => VersionText.ParseOrZero(n.Version))
                .ToList();
        }

        /// <summary>The notes of exactly <paramref name="version"/>, from any host entry (what the running build brought).</summary>
        public static IReadOnlyList<ReleaseNote> NotesOf(HostRelease release, string version)
        {
            if (release?.ReleaseNotes == null || !VersionText.TryParse(version, out Version wanted))
            {
                return new ReleaseNote[0];
            }
            return release.ReleaseNotes
                .Where(n => n != null && VersionText.TryParse(n.Version, out Version v) && v == wanted)
                .Take(1)
                .ToList();
        }

        /// <summary>Downloaded installer name; the version is normalized so the catalog cannot inject a path.</summary>
        public static string InstallerFileName(string version) =>
            "SwissTechKnife-" + VersionText.ParseOrZero(version) + "-setup.exe";

        /// <summary>
        /// Inno Setup command line for an unattended update. The /STK* switches are read by the installer's
        /// [Code]: wait for the host mutex, write the signal file, relaunch <paramref name="appPath"/> when done.
        /// </summary>
        public static string InstallerArguments(string logPath, string signalPath, string mutexName, string appPath) =>
            "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /STKUPDATE"
            + " " + Quote("/LOG=" + logPath)
            + " " + Quote("/STKSIGNAL=" + signalPath)
            + " " + Quote("/STKMUTEX=" + mutexName)
            + " " + Quote("/STKAPP=" + appPath);

        /// <summary>How the last attempt ended, judged from the version that is running now.</summary>
        public static HostUpdateOutcome Evaluate(PendingHostUpdate pending, string runningVersion)
        {
            if (pending == null || !VersionText.TryParse(pending.ToVersion, out Version target))
            {
                return HostUpdateOutcome.None;
            }
            return VersionText.ParseOrZero(runningVersion) >= target ? HostUpdateOutcome.Succeeded : HostUpdateOutcome.Failed;
        }

        private static string Quote(string argument) => "\"" + argument.Replace("\"", string.Empty) + "\"";
    }
}
