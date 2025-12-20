using System;
using System.IO;
using System.Linq;
using System.Diagnostics;

namespace ResPlan.Library.PythonInfrastructure
{
    public static class PythonEnvManager
    {
        private const string VenvDir = ".venv";
        private static readonly object _lock = new object();

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

        public static void EnsureEnvironment(Action<string> logger = null)
        {
            EnsureDependencies(logger);
        }

        public static void EnsureDependencies(Action<string> logger = null)
        {
            lock (_lock)
            {
                var venvPath = GetVenvPath();
                var pythonPath = GetPythonPath(venvPath);

                if (!Directory.Exists(venvPath) || !File.Exists(pythonPath))
                {
                    logger?.Invoke("Creating Python virtual environment...");
                    CreateVenv(venvPath, logger);
                }

                logger?.Invoke("Installing dependencies...");
                InstallDependencies(venvPath, logger);
            }
        }

        public static string GetPythonPath(string venvPath)
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                return Path.Combine(venvPath, "Scripts", "python.exe");
            }
            return Path.Combine(venvPath, "bin", "python3");
        }

        private static void CreateVenv(string venvPath, Action<string> logger = null)
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
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using var p = new Process { StartInfo = psi };

                if (logger != null)
                {
                    p.OutputDataReceived += (sender, e) => { if (e.Data != null) logger(e.Data); };
                    p.ErrorDataReceived += (sender, e) => { if (e.Data != null) logger(e.Data); };
                }

                p.Start();

                if (logger != null)
                {
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                }

                p.WaitForExit();

                if (p.ExitCode != 0)
                {
                     // If we are not logging, we might want to capture stderr to throw.
                     // But we can't easily capture it if we didn't hook up events or redirected to logger.
                     // If logger is null, we can't read from StandardError because we didn't call BeginErrorReadLine (which consumes it).
                     // But wait, if logger is null, we are not calling BeginErrorReadLine. Can we read p.StandardError.ReadToEnd() after WaitForExit?
                     // Yes, if we didn't start async read.

                     if (logger == null)
                     {
                         throw new Exception($"Failed to create venv: {p.StandardError.ReadToEnd()}");
                     }
                     else
                     {
                         throw new Exception($"Failed to create venv. Check logs for details. Exit code: {p.ExitCode}");
                     }
                }
            }
            catch(Exception ex)
            {
                throw new Exception($"Could not find {pythonCmd} to create venv.", ex);
            }
        }

        private static void InstallDependencies(string venvPath, Action<string> logger = null)
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
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = new Process { StartInfo = psi };

            if (logger != null)
            {
                p.OutputDataReceived += (sender, e) => { if (e.Data != null) logger(e.Data); };
                p.ErrorDataReceived += (sender, e) => { if (e.Data != null) logger(e.Data); };
            }

            p.Start();

            if (logger != null)
            {
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }

            p.WaitForExit();

            if (p.ExitCode != 0)
            {
                if (logger == null)
                {
                    Console.WriteLine($"Pip install warning/error: {p.StandardError.ReadToEnd()}");
                }
                else
                {
                    logger($"Pip install exited with code {p.ExitCode}.");
                }
            }
        }
    }
}
