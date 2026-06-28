using System;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace LogisticsZero {
    // Expose the bridge class to JavaScript via COM interop
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDual)]
    public class BridgeObject {
        private readonly PivotEngine _pivotEngine;
        private readonly GiaiPrintService _printService;

        public BridgeObject() {
            _pivotEngine = new PivotEngine();
            _pivotEngine.InitializeDatabase();
            _printService = new GiaiPrintService();
        }

        // Runs DuckDB aggregate query over SQLite
        public string RunOlapQuery(string department) {
            try {
                var (totalCost, avgDepreciation, executionTimeMs) = _pivotEngine.AggregateByDepartment(department);
                return $"{totalCost:F2}|{avgDepreciation * 100:F2}|{executionTimeMs:F3}";
            } catch (Exception ex) {
                return $"Error: {ex.Message}";
            }
        }

        // Triggers the direct GIAI Printing
        public string PrintGiai(string tagNumber, string printerName) {
            try {
                _printService.PrintGiaiTag(tagNumber, string.IsNullOrEmpty(printerName) ? null : printerName);
                return "SUCCESS";
            } catch (Exception ex) {
                return $"Error: {ex.Message}";
            }
        }

        // Bridge to call C++20 reevaluation engine
        public string RecalculateSheet(int cellCount, int exprCount, int threadCount) {
            try {
                // Initialize mock structural buffers
                Cell[] cells = new Cell[cellCount];
                ExpressionNode[] exprs = new ExpressionNode[exprCount];
                
                // Invoke native recalculation engine
                EngineInterop.Recalculate(cells, cellCount, exprs, exprCount, threadCount);
                return "SUCCESS";
            } catch (Exception ex) {
                return $"Error: {ex.Message}";
            }
        }
    }

    public partial class ExecutiveDashboard : Form {
        private Microsoft.Web.WebView2.WinForms.WebView2 webView;

        public ExecutiveDashboard() {
            InitializeComponent();
            InitializeWebView();
        }

        private void InitializeComponent() {
            this.webView = new Microsoft.Web.WebView2.WinForms.WebView2();
            ((System.ComponentModel.ISupportInitialize)(this.webView)).BeginInit();
            this.SuspendLayout();
            
            // WebView2 component configuration
            this.webView.Dock = DockStyle.Fill;
            this.webView.Location = new System.Drawing.Point(0, 0);
            this.webView.Name = "webView";
            this.webView.Size = new System.Drawing.Size(1280, 800);
            this.webView.TabIndex = 0;
            
            // Main Dashboard Form settings
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1280, 800);
            this.Controls.Add(this.webView);
            this.Name = "ExecutiveDashboard";
            this.Text = "Logistics-Zero Sovereign Executive Dashboard";
            ((System.ComponentModel.ISupportInitialize)(this.webView)).EndInit();
            this.ResumeLayout(false);
        }

        private async void InitializeWebView() {
            try {
                await webView.EnsureCoreWebView2Async(null);
                
                // Allow the WebView2 scripts to access the C# COM bridge
                webView.CoreWebView2.AddHostObjectToScript("bridge", new BridgeObject());
                
                // Disable telemetry and unnecessary WebView2 features
                webView.CoreWebView2.Settings.IsWebMessageEnabled = true;
                webView.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
                webView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;

                // Load the local UniverJS UI frontend
                string htmlPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "univer_executive.html");
                if (System.IO.File.Exists(htmlPath)) {
                    webView.CoreWebView2.Navigate($"file:///{htmlPath.Replace('\\', '/')}");
                } else {
                    webView.CoreWebView2.NavigateToString("<h2>univer_executive.html not found in execution directory!</h2>");
                }
            } catch (Exception ex) {
                MessageBox.Show($"Failed to initialize WebView2: {ex.Message}", "Initialization Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
