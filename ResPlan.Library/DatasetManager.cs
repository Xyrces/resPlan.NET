using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.IO.Compression;

namespace ResPlan.Library
{
    public class DatasetManager
    {
        private const string DatasetUrl = "https://github.com/m-agour/ResPlan/archive/refs/heads/main.zip";
        private readonly string _cacheDir;

        public DatasetManager(string cacheDir = "ResPlanData")
        {
            _cacheDir = cacheDir;
        }

        public async Task<string> EnsureDatasetAvailable()
        {
            string pklPath = Path.Combine(_cacheDir, "ResPlan.pkl");
            if (File.Exists(pklPath)) return pklPath;

            if (!Directory.Exists(_cacheDir)) Directory.CreateDirectory(_cacheDir);

            Console.WriteLine("Downloading ResPlan dataset from GitHub...");
            string repoZipPath = Path.Combine(_cacheDir, "repo.zip");

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("ResPlan.NET");
                var bytes = await client.GetByteArrayAsync(DatasetUrl);
                await File.WriteAllBytesAsync(repoZipPath, bytes);
            }

            Console.WriteLine("Extracting repository...");
            string extractPath = Path.Combine(_cacheDir, "repo_extract");
            if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
            ZipFile.ExtractToDirectory(repoZipPath, extractPath);

            string[] zips = Directory.GetFiles(extractPath, "ResPlan.zip", SearchOption.AllDirectories);
            if (zips.Length == 0)
            {
                throw new FileNotFoundException("Could not find ResPlan.zip in the repository archive.");
            }
            string innerZipPath = zips[0];

            Console.WriteLine("Extracting ResPlan.pkl...");
            ZipFile.ExtractToDirectory(innerZipPath, _cacheDir);

            if (!File.Exists(pklPath))
            {
                string[] pkls = Directory.GetFiles(_cacheDir, "ResPlan.pkl", SearchOption.AllDirectories);
                if (pkls.Length > 0)
                {
                    File.Move(pkls[0], pklPath, true);
                }
                else
                {
                    throw new FileNotFoundException("Failed to extract ResPlan.pkl");
                }
            }

            File.Delete(repoZipPath);
            Directory.Delete(extractPath, true);

            return pklPath;
        }
    }
}
