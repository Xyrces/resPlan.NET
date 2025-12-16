using System;
using System.IO;
using System.Linq;
using System.Diagnostics;

namespace ResPlan.Library.PythonInfrastructure
{
    public static class PythonEnvManager
    {
        private const string VenvDir = ".venv";

        public static string GetVenvPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            // Traverse up to find the root directory where .venv should be
            // This is a simple heuristic, might need adjustment based on deployment
            var current = new DirectoryInfo(baseDir);
            while (current != null)
            {
                var venvPath = Path.Combine(current.FullName, VenvDir);
                if (Directory.Exists(venvPath))
                {
                    return venvPath;
                }
                if (File.Exists(Path.Combine(current.FullName, "ResPlan.slnx"))) // Root marker
                {
                    return Path.Combine(current.FullName, VenvDir);
                }
                current = current.Parent;
            }
            return Path.Combine(Directory.GetCurrentDirectory(), VenvDir);
        }

        public static void EnsureEnvironment()
        {
            EnsureDependencies();
        }

        public static void EnsureDependencies()
        {
            var venvPath = GetVenvPath();
            var pythonPath = GetPythonPath(venvPath);

            if (!Directory.Exists(venvPath) || !File.Exists(pythonPath))
            {
                Console.WriteLine("Creating Python virtual environment...");
                CreateVenv(venvPath);
            }

            Console.WriteLine("Installing dependencies...");
            InstallDependencies(venvPath);
        }

        public static string GetPythonPath(string venvPath)
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                return Path.Combine(venvPath, "Scripts", "python.exe");
            }
            return Path.Combine(venvPath, "bin", "python3");
        }

        private static void CreateVenv(string venvPath)
        {
            // Determine python command (python on Windows, python3 on Linux/Mac)
            string pythonCmd = "python3";
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                pythonCmd = "python";
            }

            var psi = new ProcessStartInfo
            {
                FileName = pythonCmd,
                Arguments = $"-m venv \"{venvPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            try
            {
                using var p = Process.Start(psi);
                p.WaitForExit();
                if (p.ExitCode != 0)
                {
                    throw new Exception($"Failed to create venv: {p.StandardError.ReadToEnd()}");
                }
            }
            catch(Exception ex)
            {
                throw new Exception($"Could not find {pythonCmd} to create venv.", ex);
            }
        }

        private static void InstallDependencies(string venvPath)
        {
            var pythonPath = GetPythonPath(venvPath);
            // opencv-python-headless required by resplan_utils
            // geopandas required by resplan_utils (which we discovered during debugging)
            var packages = "shapely networkx matplotlib numpy scipy opencv-python-headless geopandas";

            var psi = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = $"-m pip install {packages}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var p = Process.Start(psi);
            p.WaitForExit();
             if (p.ExitCode != 0)
            {
                // check if packages are already installed
                // pip install will exit 0 even if already satisfied usually
                Console.WriteLine($"Pip install warning/error: {p.StandardError.ReadToEnd()}");
            }
        }
    }
}
