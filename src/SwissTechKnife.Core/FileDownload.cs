using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace SwissTechKnife.Core
{
    /// <summary>Fewer bytes arrived than the server (or the catalog) announced.</summary>
    public sealed class IncompleteDownloadException : IOException
    {
        public IncompleteDownloadException(long read, long expected)
            : base($"Download incomplete: {read} of {expected} bytes.")
        {
        }
    }

    /// <summary>Streams http/https/file URIs to disk while hashing, so nothing is read twice.</summary>
    public static class FileDownload
    {
        /// <summary>
        /// Copies <paramref name="uri"/> to a new file at <paramref name="destination"/> and returns its SHA-256
        /// (lowercase hex). Progress goes from 0 to <paramref name="progressScale"/>.
        /// </summary>
        public static async Task<string> CopyAsync(Uri uri, string destination, long expectedSize, IProgress<double> progress, double progressScale, CancellationToken cancellationToken)
        {
            using (SHA256 sha = SHA256.Create())
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                HttpResponseMessage response = null;
                Stream input;
                long total = expectedSize;
                if (uri.IsFile)
                {
                    input = new FileStream(uri.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                    total = input.Length;
                }
                else
                {
                    response = await CatalogClient.Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    total = response.Content.Headers.ContentLength ?? expectedSize;
                    input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                }

                using (response)
                using (input)
                {
                    var buffer = new byte[81920];
                    long read = 0;
                    int count;
                    while ((count = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        sha.TransformBlock(buffer, 0, count, null, 0);
                        await output.WriteAsync(buffer, 0, count, cancellationToken).ConfigureAwait(false);
                        read += count;
                        if (total > 0)
                        {
                            progress?.Report(Math.Min(progressScale, progressScale * read / total));
                        }
                    }
                    sha.TransformFinalBlock(buffer, 0, 0);
                    if (total > 0 && read < total)
                    {
                        throw new IncompleteDownloadException(read, total);
                    }
                }
                return ToHex(sha.Hash);
            }
        }

        /// <summary>SHA-256 of a file on disk (lowercase hex).</summary>
        public static string HashFile(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920))
            {
                return ToHex(sha.ComputeHash(stream));
            }
        }

        public static bool HashEquals(string actual, string expected) =>
            !string.IsNullOrWhiteSpace(expected) && string.Equals(actual, expected.Trim(), StringComparison.OrdinalIgnoreCase);

        public static string ToHex(byte[] bytes)
        {
            var chars = new char[bytes.Length * 2];
            const string digits = "0123456789abcdef";
            for (int i = 0; i < bytes.Length; i++)
            {
                chars[i * 2] = digits[bytes[i] >> 4];
                chars[i * 2 + 1] = digits[bytes[i] & 0xF];
            }
            return new string(chars);
        }
    }
}
