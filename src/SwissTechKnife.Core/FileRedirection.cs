using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SwissTechKnife.Core
{
    /// <summary>
    /// Detects file-system redirection. Started from inside another app's MSIX container (the Claude desktop app's
    /// shells, for one), %LOCALAPPDATA% is virtualized even without a package identity: reads merge the container's
    /// private copy with the real profile and writes land in the private copy only.
    /// </summary>
    public static class FileRedirection
    {
        /// <summary>True when opening <paramref name="file"/> resolves somewhere else; false when it doesn't or can't be told.</summary>
        public static bool IsRedirected(string file)
        {
            try
            {
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var buffer = new StringBuilder(1024);
                    uint length = GetFinalPathNameByHandle(stream.SafeFileHandle, buffer, (uint)buffer.Capacity, 0);
                    if (length == 0 || length >= buffer.Capacity)
                    {
                        return false;
                    }
                    string final = buffer.ToString();
                    if (final.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
                    {
                        final = @"\\" + final.Substring(8);
                    }
                    else if (final.StartsWith(@"\\?\", StringComparison.Ordinal))
                    {
                        final = final.Substring(4);
                    }
                    return !string.Equals(final, Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is EntryPointNotFoundException)
            {
                return false;
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint length, uint flags);
    }
}
