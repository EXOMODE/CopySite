using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace CopySite
{
    public class Source
    {
        public Uri Uri { get; set; }

        public byte[] Content { get; set; }

        public string PathToFile
        {
            get
            {
                string dir = Path.GetDirectoryName(Uri.LocalPath) ?? string.Empty;
                string f = Path.GetFileName(Uri.LocalPath);
                
                return Path.Combine(dir, f);
            }
        }

        private static readonly object rootSync = new object();

        public Source(Uri uri, byte[] content)
        {
            Uri = uri;
            Content = content;
        }

        public Source(Uri uri): this(uri, null) { }

        public Source(byte[] content): this(null, content) { }

        public Source(): this((byte[])null) { }

        public async Task<bool> LoadAsync(Uri uri)
        {
            if (uri == null) return false;
            
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    Content = await client.GetByteArrayAsync(uri);
                    Uri = uri;
                }
            }
            catch
            {
                return false;
            }
            
            return true;
        }

        public Task<bool> LoadAsync() => LoadAsync(Uri);

        public bool Load(Uri uri) => LoadAsync(uri).Result;

        public bool Load() => Load(Uri);

        public async Task<bool> SaveAsync(string path, Func<byte[], byte[]> handler = null, bool isSaveContent = false)
        {
            if (string.IsNullOrWhiteSpace(path)) return await SaveAsync();

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (handler != null) Content = handler(Content);

            lock (rootSync)
            {
                File.WriteAllBytes(path, Content);
                Console.WriteLine("Ресурс сохранён: \"{0}\" ({1} байт)", path, Content.Length);

                if (!isSaveContent) Content = null;
            }
            
            return true;
        }

        public async Task<bool> SaveAsync(Func<byte[], byte[]> handler = null, bool isSaveContent = false)
        {
            if (Uri == null) return false;

            return await SaveAsync(PathToFile, handler, isSaveContent);
        }

        public bool Save(string path, Func<byte[], byte[]> handler = null, bool isSaveContent = false) => SaveAsync(path, handler, isSaveContent).Result;

        public bool Save(Func<byte[], byte[]> handler = null, bool isSaveContent = false) => SaveAsync(handler, isSaveContent).Result;
    }
}