using System;
using System.Collections.Generic;
using System.IO;

namespace CopySite
{
    internal static class Program
    {
        private static string outputPath;
        private static string url;
        private static string proxyHost;
        private static int proxyPort;
        private static string userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/70.0.3538.77 Safari/537.36";
        private static string lang = "en-US";
        private static int timeout = 1;
        private static bool isFull;
        private static bool isOriginalStructure;

        private static void ParseArgs(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "-output":
                    case "-o":
                        outputPath = args[i + 1].Trim('"', '\'');

                        if (!Directory.Exists(outputPath)) Directory.CreateDirectory(outputPath);

                        i++;
                        break;

                    case "-url":
                    case "-u":
                        url = args[i + 1].Trim('"', '\'');
                        i++;
                        break;

                    case "-proxyhost":
                    case "-ph":
                        proxyHost = args[i + 1].Trim('"', '\'');
                        i++;
                        break;

                    case "-proxyport":
                    case "-pp":
                        proxyPort = int.Parse(args[i + 1].Trim('"', '\''));
                        i++;
                        break;

                    case "-proxy":
                    case "-p":
                        string[] p = args[i + 1].Trim('"', '\'').Split(':');
                        proxyHost = p[0];
                        proxyPort = int.Parse(p[1]);
                        i++;
                        break;

                    case "-useragent":
                    case "-ua":
                        userAgent = args[i + 1].Trim('"', '\'');
                        i++;
                        break;

                    case "-lang":
                    case "-l":
                        lang = args[i + 1].Trim('"', '\'');
                        i++;
                        break;

                    case "-timeout":
                    case "-t":
                        timeout = int.Parse(args[i + 1].Trim('"', '\''));
                        i++;
                        break;

                    case "--full":
                    case "--f":
                        isFull = true;
                        break;

                    case "--origin":
                    case "--o":
                        isOriginalStructure = true;
                        break;
                }
            }
        }

        private static void Main(string[] args)
        {
            args = "-output 'mishka-shop/' -url 'https://mishka-shop.com/' -timeout 5 -lang 'ru-RU'".Split(new[] { " " }, StringSplitOptions.RemoveEmptyEntries);

            ParseArgs(args);


#if DEBUG
            List<Exception> errors = new List<Exception>();

        loop:
#endif
            try
            {
                Site site = new Site(new Uri(url), proxyHost, proxyPort, userAgent, lang, timeout, outputPath, isOriginalStructure);
                site.Download(isFull);
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
#endif
            }
        }
    }
}