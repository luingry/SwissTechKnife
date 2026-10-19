using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SwissTechKnife.Core;

namespace SwissTechKnife.Modules
{
    /// <summary>
    /// Module icon images: downloaded once into cache/icons, decoded off the UI thread at tile size and frozen,
    /// so they show offline and never block rendering.
    /// </summary>
    public sealed class IconCache
    {
        private const string Source = "Icons";
        private const int DecodeSize = 128;
        private readonly string _directory;

        public IconCache(HostPaths paths)
        {
            _directory = Path.Combine(paths.Cache, "icons");
        }

        /// <summary>Returns a frozen image, or null if the icon is unavailable (the vector icon is used instead).</summary>
        public async Task<ImageSource> GetAsync(Uri uri, CancellationToken cancellationToken)
        {
            string file = Path.Combine(_directory, Key(uri) + ".png");
            try
            {
                byte[] bytes;
                if (File.Exists(file))
                {
                    bytes = await Task.Run(() => File.ReadAllBytes(file), cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    bytes = await CatalogClient.ReadAllAsync(uri, cancellationToken).ConfigureAwait(false);
                    await Task.Run(() =>
                    {
                        Directory.CreateDirectory(_directory);
                        File.WriteAllBytes(file, bytes);
                    }, cancellationToken).ConfigureAwait(false);
                }
                return await Task.Run(() => Decode(bytes), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Log.Warn(Source, "Icon unavailable " + uri + ": " + ex.Message);
                return null;
            }
        }

        private static ImageSource Decode(byte[] bytes)
        {
            var image = new BitmapImage();
            using (var stream = new MemoryStream(bytes, writable: false))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = DecodeSize;
                image.StreamSource = stream;
                image.EndInit();
            }
            image.Freeze();
            return image;
        }

        private static string Key(Uri uri)
        {
            using (SHA1 sha = SHA1.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(uri.AbsoluteUri));
                var sb = new StringBuilder(16);
                for (int i = 0; i < 8; i++)
                {
                    sb.Append(hash[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }
}
