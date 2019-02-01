using System;
using System.IO;

namespace CopySite
{
    internal static class Program
    {
        private static string outputPath;
        private static string url;

        private static void Main(string[] args)
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
                }
            }
        }
    }
}