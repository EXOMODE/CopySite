using CefSharp;
using CefSharp.OffScreen;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace CopySite
{
    public class Site
    {
        public Uri Uri { get; protected set; }

        public List<Page> Pages { get; protected set; }

        public int Timeout { get; set; }

        public string OutpupPath { get; set; }

        public bool IsOriginalStructure { get; set; }

        //private static readonly object rootSync = new object();

        public static ConcurrentDictionary<Uri, Tuple<string, bool>> Cache { get; protected set; }

        public Site(Uri uri, string proxyHost = "192.168.0.1", int proxyPort = 8080, string userAgent = null, string lang = "en-US", int timeout = 0, string outputPath = "", bool isOriginalStructure = false)
        {
            if (!Cef.IsInitialized)
            {
                CefSettings settings = new CefSettings
                {
                    UserAgent = userAgent ?? "Mozilla / 5.0(Windows NT 10.0; Win64; x64) AppleWebKit / 537.36(KHTML, like Gecko) Chrome / 70.0.3538.77 Safari / 537.36",
                    IgnoreCertificateErrors = true,
                    Locale = lang,
                };

                if (!string.IsNullOrWhiteSpace(proxyHost) && proxyPort > 0) settings.CefCommandLineArgs.Add("proxy-server", $"{proxyHost}:{proxyPort}");

                Cef.Initialize(settings);
            }

            Uri = uri;
            Timeout = timeout;
            OutpupPath = outputPath;
            Pages = new List<Page>();
            IsOriginalStructure = isOriginalStructure;

            Page.Cache.Clear();
        }

        static Site() => Cache = new ConcurrentDictionary<Uri, Tuple<string, bool>>();

        protected bool TryParseSitemap(Uri uri, out IEnumerable<Uri> links)
        {
            links = new List<Uri>();
            XmlDocument rssXmlDoc = new XmlDocument();

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    HttpResponseMessage response = client.GetAsync(string.IsNullOrWhiteSpace(uri.LocalPath?.Trim('/')) ? $"{uri.Scheme}://{uri.Host}/sitemap.xml" : uri.OriginalString).Result;
                    rssXmlDoc.LoadXml(response.Content.ReadAsStringAsync().Result);

                    foreach (XmlNode topNode in rssXmlDoc.ChildNodes)
                    {
                        XmlNamespaceManager nsmgr = new XmlNamespaceManager(rssXmlDoc.NameTable);
                        nsmgr.AddNamespace("ns", topNode.NamespaceURI);

                        switch (topNode.Name.ToLower())
                        {
                            case "sitemapindex":
                                foreach (XmlNode urlNode in topNode.ChildNodes)
                                {
                                    XmlNode locNode = urlNode.SelectSingleNode("ns:loc", nsmgr);

                                    if (locNode == null || string.IsNullOrWhiteSpace(locNode.InnerText)) continue;
                                    if (TryParseSitemap(new Uri(locNode.InnerText), out IEnumerable<Uri> lnks)) ((List<Uri>)links).AddRange(lnks);
                                }
                                break;

                            case "urlset":
                                foreach (XmlNode urlNode in topNode.ChildNodes)
                                {
                                    XmlNode locNode = urlNode.SelectSingleNode("ns:loc", nsmgr);

                                    if (locNode == null || string.IsNullOrWhiteSpace(locNode.InnerText)) continue;

                                    ((List<Uri>)links).Add(new Uri(locNode.InnerText));
                                }
                                break;
                        }
                    }
                }
            }
            catch
            {
                return false;
            }

            return true;
        }

        public async Task<bool> DownloadAsync(Uri uri, bool isFull)
        {
#if DEBUG
            List<Exception> errors = new List<Exception>();
        loop:
#endif
            try
            {

                if (isFull)
                {
                    Console.WriteLine("Анализ карты сайта...");

                    if (!TryParseSitemap(new Uri($"{uri.Scheme}://{uri.Host}"), out IEnumerable<Uri> links))
                    {
                        Page mainPage = new Page(uri, OutpupPath);

                        if (!await mainPage.LoadAsync(Timeout)) return false;

                        links = mainPage.Links;
                    }

                    Console.WriteLine("Добавлено {0} ссылок.\r\n", links.Count());
                    Console.WriteLine("Запуск анализа ссылок...");

                    int i = 0;

                    foreach (Uri a in links)
                    {
#if DEBUG
                    loop2:
#endif
                        try
                        {
                            i++;

                            if (Cache.ContainsKey(a)) continue;

                            Page p = new Page(a, OutpupPath);

                            if (await p.LoadAsync(Timeout, true))
                            {
                                string html = p.Document.DocumentNode.InnerHtml;

                                //await Task.Factory.StartNew(() => Parallel.ForEach(links, l =>
                                foreach (Uri l in links)
                                {
                                    string f = p.PathToFile.Replace('/', '-').Replace('\\', '-').Trim('-') ?? "#";

                                    if (!string.IsNullOrWhiteSpace(l.OriginalString)) html = html.Replace($"href=\"{l.OriginalString}\"", $"href=\"{f}\"");
                                    if (!string.IsNullOrWhiteSpace(l.LocalPath)) html = html.Replace($"href=\"{l.LocalPath}\"", $"href=\"{f}\"");
                                    if (!string.IsNullOrWhiteSpace(l.LocalPath?.TrimStart('/'))) html = html.Replace($"href=\"{l.LocalPath?.TrimStart('/')}\"", $"href=\"{f}\"");
                                    //}));
                                }

                                p.Document.DocumentNode.InnerHtml = html;
#if DEBUG
                            loop3:
#endif
                                try
                                {
                                    p.Save(IsOriginalStructure);
                                }
                                catch (Exception e)
                                {
#if DEBUG
                                    errors.Add(e);
#endif
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine(e);
                                    Console.ResetColor();
#if DEBUG
                                    goto loop3;
#endif
                                }

                                Cache.AddOrUpdate(a, new Tuple<string, bool>(p.PathToFile, false), (_, __) => __);
                                Console.WriteLine("Добавлена страница \"{0}\" ({1} из {2}).", p.Title, i, links.Count());

                                p = null;
                            }
                        }
                        catch (Exception e)
                        {
#if DEBUG
                            errors.Add(e);
#endif
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine(e);
                            Console.ResetColor();
#if DEBUG
                            goto loop2;
#endif
                        }
                    }
                }
                else
                {
                    Page p = new Page(uri, OutpupPath);

                    if (await p.LoadAsync(Timeout, true)) p.Save(IsOriginalStructure);

                    p = null;
                }
            }
            catch (Exception e)
            {
#if DEBUG
                errors.Add(e);
#endif
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(e);
                Console.ResetColor();
#if DEBUG
                goto loop;
#else
                return false;
#endif
            }

            return true;
        }

        public Task<bool> DownloadAsync(Uri uri) => DownloadAsync(uri, false);

        public Task<bool> DownloadAsync(bool isFull) => DownloadAsync(Uri, isFull);

        public Task<bool> DownloadAsync() => DownloadAsync(false);

        public bool Download(Uri uri, bool isFull) => DownloadAsync(uri, isFull).Result;

        public bool Download(Uri uri) => Download(uri, false);

        public bool Download(bool isFull) => Download(Uri, isFull);

        public bool Download() => Download(false);
    }
}