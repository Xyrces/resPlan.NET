using System;
using System.IO;
using System.Net.Http;
using System.IO.Compression;
using System.Threading.Tasks;

namespace ResPlan.Library.Data
{
    public static class DataManager
    {
        private const string ResPlanUrl = "https://github.com/m-agour/ResPlan/raw/main/ResPlan.zip";
        private const string DataDir = "ResPlan";
        private const string PklFile = "ResPlan.pkl";

        public static async Task EnsureDataAsync()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            // Find root where ResPlan folder should be
            // Similar logic to finding venv, or we just put it relative to execution
            // But currently code expects ResPlan/ResPlan.pkl relative to CWD usually.

            // Let's assume current directory or search up
            var workingDir = Directory.GetCurrentDirectory();
            var dataPath = Path.Combine(workingDir, DataDir, PklFile);

            if (File.Exists(dataPath))
            {
                Console.WriteLine($"Data found at {dataPath}");
                return;
            }

            // Search up just in case
            var current = new DirectoryInfo(workingDir);
            while (current != null)
            {
                 var checkPath = Path.Combine(current.FullName, DataDir, PklFile);
                 if (File.Exists(checkPath))
                 {
                     Console.WriteLine($"Data found at {checkPath}");
                     return;
                 }
                 if (File.Exists(Path.Combine(current.FullName, "ResPlan.slnx")))
                 {
                     // Found root, data not found, so we should download to root/ResPlan
                     workingDir = current.FullName;
                     break;
                 }
                 current = current.Parent;
            }

            Console.WriteLine("ResPlan data not found. Downloading...");
            var targetDir = Path.Combine(workingDir, DataDir);
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            var zipPath = Path.Combine(targetDir, "ResPlan.zip");

            using (var client = new HttpClient())
            {
                using (var s = await client.GetStreamAsync(ResPlanUrl))
                {
                    using (var fs = new FileStream(zipPath, FileMode.Create))
                    {
                        await s.CopyToAsync(fs);
                    }
                }
            }

            Console.WriteLine("Extracting...");
            ZipFile.ExtractToDirectory(zipPath, targetDir, overwriteFiles: true);

            // Verify extraction
            if (!File.Exists(Path.Combine(targetDir, PklFile)))
            {
                 // Maybe it extracts to a subdirectory?
                 // The zip structure usually contains ResPlan/ResPlan.pkl?
                 // If the zip contains "ResPlan.pkl" at root, then it is in targetDir/ResPlan.pkl
                 // If the zip contains "ResPlan/ResPlan.pkl", then it is in targetDir/ResPlan/ResPlan.pkl

                 // Let's check files
                 var files = Directory.GetFiles(targetDir, "*.pkl", SearchOption.AllDirectories);
                 if (files.Length > 0)
                 {
                     Console.WriteLine($"Data extracted to {files[0]}");
                     // If it is in a subdir, we might want to move it or return the path
                 }
                 else
                 {
                     throw new FileNotFoundException("ResPlan.pkl not found after extraction.");
                 }
            }
            else
            {
                Console.WriteLine("Extraction complete.");
            }
        }

        public static string GetDataPath()
        {
             // Logic to find the pkl file
            var workingDir = Directory.GetCurrentDirectory();

            // First check local relative
            if (File.Exists(Path.Combine(workingDir, DataDir, PklFile)))
                return Path.Combine(workingDir, DataDir, PklFile);

             // Search up
            var current = new DirectoryInfo(workingDir);
            while (current != null)
            {
                 var checkPath = Path.Combine(current.FullName, DataDir, PklFile);
                 if (File.Exists(checkPath)) return checkPath;
                 // check deeper nesting if zip extracted with folder
                 var checkPath2 = Path.Combine(current.FullName, DataDir, "ResPlan", PklFile);
                 if (File.Exists(checkPath2)) return checkPath2;

                 if (File.Exists(Path.Combine(current.FullName, "ResPlan.slnx"))) break;
                 current = current.Parent;
            }

            // Default fall back
            return Path.Combine(workingDir, DataDir, PklFile);
        }
    }
}
