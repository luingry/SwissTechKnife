using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace SwissTechKnife.Core
{
    /// <summary>Dependency-free JSON (DataContractJsonSerializer) with crash-safe file writes.</summary>
    public static class Json
    {
        private static readonly DataContractJsonSerializerSettings Settings = new DataContractJsonSerializerSettings
        {
            UseSimpleDictionaryFormat = true,
        };

        public static T Deserialize<T>(Stream stream)
        {
            var serializer = new DataContractJsonSerializer(typeof(T), Settings);
            return (T)serializer.ReadObject(stream);
        }

        public static T Deserialize<T>(byte[] utf8)
        {
            using (var stream = new MemoryStream(utf8, writable: false))
            {
                return Deserialize<T>(stream);
            }
        }

        public static byte[] Serialize<T>(T value)
        {
            var serializer = new DataContractJsonSerializer(typeof(T), Settings);
            using (var stream = new MemoryStream())
            {
                using (XmlDictionaryWriter writer = JsonReaderWriterFactory.CreateJsonWriter(stream, new UTF8Encoding(false), ownsStream: false, indent: true, indentChars: "  "))
                {
                    serializer.WriteObject(writer, value);
                }
                return stream.ToArray();
            }
        }

        public static T ReadFile<T>(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return Deserialize<T>(stream);
            }
        }

        /// <summary>
        /// Writes through a temp file and swaps it in, keeping the previous content as <c>.bak</c>,
        /// so a crash or power loss mid-write never leaves a truncated file.
        /// </summary>
        public static void WriteFileAtomic<T>(string path, T value)
        {
            byte[] bytes = Serialize(value);
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            string temp = path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
            }
            if (File.Exists(path))
            {
                File.Replace(temp, path, path + ".bak", ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temp, path);
            }
        }

        /// <summary>Reads <paramref name="path"/>, falling back to its <c>.bak</c>; returns null when neither is usable.</summary>
        public static T ReadFileOrBackup<T>(string path, Action<string, Exception> onError) where T : class
        {
            foreach (string candidate in new[] { path, path + ".bak" })
            {
                if (!File.Exists(candidate))
                {
                    continue;
                }
                try
                {
                    return ReadFile<T>(candidate);
                }
                catch (Exception ex)
                {
                    onError?.Invoke(candidate, ex);
                }
            }
            return null;
        }
    }
}
