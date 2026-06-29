using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LogisticsZero {
    static class Program {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        [STAThread]
        static void Main() {
            try {
                ExtractEmbeddedResources();
            } catch (Exception ex) {
                MessageBox.Show($"Failed to extract embedded assets to Temp directory: {ex.Message}", "Resource Initialization Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ExecutiveDashboard());
        }

        private static void ExtractEmbeddedResources() {
            var assembly = Assembly.GetExecutingAssembly();
            string tempDir = Path.GetTempPath();

            string? dllResourceName = null;
            string? htmlResourceName = null;
            foreach (var name in assembly.GetManifestResourceNames()) {
                if (name.EndsWith("sovereign_engine.dll", StringComparison.OrdinalIgnoreCase)) dllResourceName = name;
                if (name.EndsWith("univer_executive.html", StringComparison.OrdinalIgnoreCase)) htmlResourceName = name;
            }

            // Extract native DLL to %TEMP%
            if (!string.IsNullOrEmpty(dllResourceName)) {
                string dllDest = Path.Combine(tempDir, "sovereign_engine.dll");
                try {
                    using (Stream? resourceStream = assembly.GetManifestResourceStream(dllResourceName)) {
                        if (resourceStream != null) {
                            using (FileStream fileStream = new FileStream(dllDest, FileMode.Create, FileAccess.Write, FileShare.None)) {
                                resourceStream.CopyTo(fileStream);
                            }
                        }
                    }
                } catch (IOException) {
                    // File might be locked because the app is already running, which is fine
                }
            }

            // Extract HTML file to %TEMP%
            if (!string.IsNullOrEmpty(htmlResourceName)) {
                string htmlDest = Path.Combine(tempDir, "univer_executive.html");
                try {
                    using (Stream? resourceStream = assembly.GetManifestResourceStream(htmlResourceName)) {
                        if (resourceStream != null) {
                            using (FileStream fileStream = new FileStream(htmlDest, FileMode.Create, FileAccess.Write, FileShare.None)) {
                                resourceStream.CopyTo(fileStream);
                            }
                        }
                    }
                } catch (IOException) {
                    // File locked is fine
                }
            }

            // Set DLL search path so P/Invoke resolves sovereign_engine.dll in %TEMP%
            SetDllDirectory(tempDir);
        }
    }
}
