using CefSharp;
using CefSharp.OffScreen;
using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CopySite
{
    public class Page
    {
        public Uri Uri { get; protected set; }

        public string OutputPath { get; set; }

        public string PathToFile
        {
            get
            {
                string dir = Path.GetDirectoryName(Uri?.LocalPath ?? string.Empty) ?? string.Empty;
                string f = Path.GetFileName(Uri?.LocalPath ?? string.Empty);

                if (string.IsNullOrWhiteSpace(f)) f = "default.html";

                if (!f.EndsWith(".html") && !f.EndsWith(".htm") && !f.EndsWith(".php")) f += ".html";

                f = Path.Combine(dir.TrimStart('/'), f);
                int i = 1;

                while (File.Exists(f))
                {
                    f = Path.Combine(Path.GetDirectoryName(f), $"{Path.GetFileNameWithoutExtension(f)}-{i}.html");
                    i++;
                }

                return f;
            }
        }

        public HtmlDocument Document { get; protected set; }

        public string Title
        {
            get => Document.DocumentNode.QuerySelector("title").InnerText;
            set => Document.DocumentNode.QuerySelector("title").InnerHtml = value;
        }

        public string Description
        {
            get => Document.DocumentNode.QuerySelector("meta[name='description']").GetAttributeValue("content", string.Empty);
            set => Document.DocumentNode.QuerySelector("meta[name='description']").SetAttributeValue("content", value);
        }

        public List<Source> Styles { get; protected set; }
        public Source LocalStyle { get; protected set; }

        public List<Source> PreloadScripts { get; protected set; }
        public List<Source> Scripts { get; protected set; }
        public Source LocalPreloadScript { get; protected set; }
        public Source LocalScript { get; protected set; }

        public IEnumerable<Uri> Links => (from a in Document.DocumentNode.QuerySelectorAll("a").AsParallel()
                                          let href = a.GetAttributeValue("href", null)?.TrimEnd('#')?.Split('#')?[0]
                                          where TryParseUri(href, out Uri link, out bool isAnotherHost) && !isAnotherHost && !Site.Cache.ContainsKey(link) && !href.ToLower().Contains("void(")
                                          select href).Select(x =>
                                          {
                                              if (TryParseUri(x, out Uri link)) return link;

                                              return null;
                                          }).Distinct();

        private static readonly object rootSync = new object();

        public static ConcurrentDictionary<Uri, Tuple<string, bool>> Cache { get; protected set; }

        public Page(Uri uri, string outputPath = "")
        {
            Uri = uri;
            OutputPath = outputPath;
            Document = new HtmlDocument();
            Styles = new List<Source>();
            PreloadScripts = new List<Source>();
            Scripts = new List<Source>();
        }

        static Page() => Cache = new ConcurrentDictionary<Uri, Tuple<string, bool>>();

        private bool TryParseUri(string url, out Uri uri, out bool isRemoteHost)
        {
            uri = null;
            isRemoteHost = false;

            if (string.IsNullOrWhiteSpace(url)) return false;

            if (url.StartsWith("//") || url.StartsWith("http://") || url.StartsWith("https://"))
            {
                uri = new Uri(url);
                isRemoteHost = !url.Contains(Uri.Host);
                return true;
            }

            if (url.StartsWith("/") || url.StartsWith("\\"))
            {
                url = url.Replace('\\', '/');
                uri = new Uri($"{Uri.Scheme}://{Uri.Host}{url}");
                return true;
            }

            uri = new Uri(Uri, url);
            return true;
        }

        private bool TryParseUri(string url, out Uri uri) => TryParseUri(url, out uri, out _);

        private async Task<byte[]> ContentHandlerAsync(byte[] content, bool isLocal = false, Uri baseUri = null, bool isSaveFiles = false)
        {
            string style = Encoding.UTF8.GetString(content ?? new byte[0]);

            Regex regex = new Regex(@"(?<type>url|src)\s*\([\s\'\""]*(?<url>[^\)]*)[\'\""]*\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            MatchCollection matches = regex.Matches(style);
            List<string> ready = new List<string>();

            await Task.Factory.StartNew(() => Parallel.ForEach(matches.Cast<Match>(), async match =>
            {
                string url = match.Groups["url"].Value?.Trim('\'', '"');

                if (string.IsNullOrWhiteSpace(url) || ready.Contains(url)) return;

                ready.Add(url);

                if (!url.StartsWith("http://") && !url.StartsWith("https://") && !url.StartsWith("/") && !url.StartsWith("\\")) url = baseUri.LocalPath + '/' + url;

                if (TryParseUri(url, out Uri src))
                {
                    Source s = new Source(src);

                    if (Cache.ContainsKey(src)) style = style.Replace(url, Cache[src].Item1);

                    if ((!Cache.ContainsKey(src) || !Cache[src].Item2) && await s.LoadAsync())
                    {
                        string type = "images";
                        string pref = "../";

                        if (isLocal) pref += "../";

                        switch (Path.GetExtension(src.LocalPath)?.Trim('.')?.Split('#')?[0])
                        {
                            case "jpg":
                            case "jpeg":
                            case "png":
                            case "gif":
                            case "svg":
                                type = "images";
                                break;

                            case "css":
                                type = "styles";
                                pref = isLocal ? "../" : string.Empty;
                                break;

                            case "woff":
                            case "woff2":
                            case "ttf":
                            case "otf":
                            case "eot":
                                type = "fonts";
                                break;
                        }

                        string f = Path.GetFileName(src.LocalPath)?.Split('#')?[0] ?? "unknown.file";

                        if (f.Length > 50) f = f.Substring(f.Length - 50);

                        string p = Path.Combine($"{pref}{type}/", f);

                        if (isSaveFiles && s.Save(Path.Combine(OutputPath, $"client/{type}/{(isLocal ? "pages/" : "")}", f))) style = style.Replace(url, p);

                        Cache.AddOrUpdate(src, new Tuple<string, bool>(p, isSaveFiles), (_, __) => __);
                    }
                }
            }));
            
            return Encoding.UTF8.GetBytes(style);
        }

        private async Task<HtmlDocument> AnalyseAsync(HtmlDocument document, bool isSaveFiles = false)
        {
            await Task.Factory.StartNew(() => Parallel.Invoke(
                async () =>
                {
                    IList<HtmlNode> links = document.DocumentNode.QuerySelectorAll("link, style, script");

                    for (int i = 0; i < links.Count; i++) await AnalyseAsync(links[i], isSaveFiles);
                },
                () =>
                {
                    IList<HtmlNode> images = document.DocumentNode.QuerySelectorAll("img");

                    Parallel.For(0, images.Count, async i => await AnalyseAsync(images[i], isSaveFiles));
                }
            ));
            
            HtmlNode e = Document.DocumentNode.QuerySelector("head");
            string f = Path.GetFileNameWithoutExtension(PathToFile);
            string p = $"client/styles/pages/{f}.css";
            
            if (LocalStyle != null && LocalStyle.Content != null)
            {
                LocalStyle.Content = await ContentHandlerAsync(LocalStyle.Content, true);

                if (LocalStyle.Save(Path.Combine(OutputPath, p))) e.InnerHtml += $"\r\n\t<link rel=\"stylesheet\" href=\"{p}\" />\r\n";
            }

            p = $"client/scripts/pages/{f}-preload.js";

            if (LocalPreloadScript != null && LocalPreloadScript.Content != null && LocalPreloadScript.Save(Path.Combine(OutputPath, p))) e.InnerHtml += $"\r\n\t<script src=\"{p}\"></script>\r\n";

            e = Document.DocumentNode.QuerySelector("body");
            p = $"client/scripts/pages/{f}.js";

            if (LocalScript != null && LocalScript.Content != null && LocalScript.Save(Path.Combine(OutputPath, p))) e.InnerHtml += $"\r\n\t<script src=\"{p}\"></script>\r\n";

            return document;
        }

        private async Task<HtmlNode> AnalyseAsync(HtmlNode node, bool isSaveFiles = false)
        {
            if (node.ChildNodes.Count > 0) for (int i = 0; i < node.ChildNodes.Count; i++) node.ChildNodes[i] = await AnalyseAsync(node.ChildNodes[i], isSaveFiles);
            
            switch (node.Name)
            {
                case "script":
                    {
                        if (TryParseUri(node.GetAttributeValue("src", null), out Uri src, out bool isAnotherHost))
                        {
                            Source s = new Source(src);

                            if (Cache.ContainsKey(src)) node.SetAttributeValue("src", Cache[src].Item1);

                            if ((!Cache.ContainsKey(src) || !Cache[src].Item2) && await s.LoadAsync())
                            {
                                if (!isAnotherHost)
                                {
                                    string p = Path.Combine("client/scripts/", Path.GetFileName(src.LocalPath));

                                    if (isSaveFiles && s.Save(Path.Combine(OutputPath, p))) node.SetAttributeValue("src", p);

                                    Cache.AddOrUpdate(src, new Tuple<string, bool>(p, isSaveFiles), (_, __) => __);
                                }

                                if (node.ParentNode.Name == "head")
                                    PreloadScripts.Add(s);
                                else
                                    Scripts.Add(s);
                            }
                        }
                        else
                        {
                            if (node.ParentNode.Name == "head")
                            {
                                if (LocalPreloadScript == null) LocalPreloadScript = new Source();
                                
                                string c = Encoding.UTF8.GetString(LocalPreloadScript.Content ?? new byte[0]);
                                c += node.InnerHtml + "\r\n\r\n";
                                LocalPreloadScript.Content = Encoding.UTF8.GetBytes(c);
                            }
                            else
                            {
                                if (LocalScript == null) LocalScript = new Source();
                                
                                string c = Encoding.UTF8.GetString(LocalScript.Content ?? new byte[0]);
                                c += node.InnerHtml + "\r\n\r\n";
                                LocalScript.Content = Encoding.UTF8.GetBytes(c);
                            }

                            node.Remove();
                        }
                    }
                    break;

                case "link":
                    {
                        switch (node.GetAttributeValue("rel", null))
                        {
                            case "stylesheet":
                                if (TryParseUri(node.GetAttributeValue("href", null), out Uri href, out bool isAnotherHost))
                                {
                                    Source s = new Source(href);

                                    if (Cache.ContainsKey(href)) node.SetAttributeValue("href", Cache[href].Item1);

                                    if ((!Cache.ContainsKey(href) || !Cache[href].Item2) && await s.LoadAsync())
                                    {
                                        if (!isAnotherHost)
                                        {
                                            s.Content = await ContentHandlerAsync(s.Content, baseUri: href);
                                            string p = Path.Combine("client/styles/", Path.GetFileName(href.LocalPath));

                                            if (isSaveFiles && s.Save(Path.Combine(OutputPath, p))) node.SetAttributeValue("href", p);

                                            Cache.AddOrUpdate(href, new Tuple<string, bool>(p, isSaveFiles), (_, __) => __);
                                        }

                                        Styles.Add(s);
                                    }
                                }
                                break;
                        }
                    }
                    break;

                case "style":
                    {
                        if (LocalStyle == null) LocalStyle = new Source();
                        
                        string c = Encoding.UTF8.GetString(LocalStyle.Content ?? new byte[0]);
                        c += node.InnerHtml + "\r\n\r\n";
                        LocalStyle.Content = Encoding.UTF8.GetBytes(c);
                        node.Remove();
                    }
                    break;

                case "img":
                    {
                        if (TryParseUri(node.GetAttributeValue("src", null), out Uri src))
                        {
                            Source s = new Source(src);

                            if (Cache.ContainsKey(src)) node.SetAttributeValue("src", Cache[src].Item1);

                            if ((!Cache.ContainsKey(src) || !Cache[src].Item2) && await s.LoadAsync())
                            {
                                string p = Path.Combine("client/images/", Path.GetFileName(src.LocalPath));

                                if (isSaveFiles && s.Save(Path.Combine(OutputPath, p))) node.SetAttributeValue("src", p);

                                Cache.AddOrUpdate(src, new Tuple<string, bool>(p, isSaveFiles), (_, __) => __);
                            }
                        }
                    }
                    break;
            }

            return node;
        }

        protected virtual void OnBrowserLoadingStateChanged(object sender, LoadingStateChangedEventArgs e)
        {
            if (!e.IsLoading) ((IWebBrowser)sender).LoadingStateChanged -= OnBrowserLoadingStateChanged;
        }
        
        public async Task<bool> LoadAsync(Uri uri, int timeout, bool isSaveFiles = false)
        {
            if (uri == null) return false;

            try
            {
                using (ChromiumWebBrowser browser = new ChromiumWebBrowser(browserSettings: new BrowserSettings
                {
                    FileAccessFromFileUrls = CefState.Enabled,
                    Javascript = CefState.Enabled,
                    ImageLoading = CefState.Disabled,
                }))
                {
                    while (!browser.IsBrowserInitialized) await Task.Delay(100);

                    browser.LoadingStateChanged += OnBrowserLoadingStateChanged;
                    browser.Load(uri.AbsoluteUri);

                    while (browser.IsLoading) await Task.Delay(100);

                    await Task.Delay(timeout * 1000);

                    string html = await browser.GetSourceAsync();
                    Document.LoadHtml(html);
                    Document = await AnalyseAsync(Document, isSaveFiles);
                    Uri = uri;
                }
            }
            catch
            {
                return false;
            }

            return true;
        }

        public Task<bool> LoadAsync(Uri uri, bool isSaveFiles = false) => LoadAsync(uri, 0, isSaveFiles);

        public Task<bool> LoadAsync(int timeout, bool isSaveFiles = false) => LoadAsync(Uri, timeout, isSaveFiles);

        public Task<bool> LoadAsync(bool isSaveFiles = false) => LoadAsync(Uri, isSaveFiles);

        public bool Load(Uri uri, int timeout, bool isSaveFiles = false) => LoadAsync(uri, timeout, isSaveFiles).Result;

        public bool Load(Uri uri, bool isSaveFiles = false) => Load(uri, 0, isSaveFiles);

        public bool Load(int timeout, bool isSaveFiles = false) => Load(Uri, timeout, isSaveFiles);

        public bool Load(bool isSaveFiles = false) => Load(Uri, isSaveFiles);

        public bool Save(string path, bool isOriginalStructure = false)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            if (isOriginalStructure)
            {
                string dir = Path.GetDirectoryName(path);

                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            else
                path = path.Replace('/', '-').Replace('\\', '-').Trim('-');

            lock (rootSync)
            {
                File.WriteAllText(Path.Combine(OutputPath, isOriginalStructure ? "pages/" : "", path), Document.DocumentNode.WriteContentTo());
                Console.WriteLine("Страница сохранена: \"{0}\"\r\n", path);
            }

            return true;
        }

        public bool Save(bool isOriginalStructure = false)
        {
            if (Uri == null) return false;

            return Save(PathToFile, isOriginalStructure);
        }
    }
}