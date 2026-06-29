using System;
using System.Drawing;
using System.Drawing.Printing;

namespace LogisticsZero {
    public class GiaiPrintService {
        private string _formattedGiaiTag;

        public void PrintGiaiTag(string tagNumber, string printerName = null) {
            // Ensure the input tag matches the GS1 GIAI Standard format (8004)
            string cleanTag = tagNumber.Trim();
            if (cleanTag.StartsWith("(8004)")) {
                cleanTag = cleanTag.Substring(6);
            } else if (cleanTag.StartsWith("8004")) {
                cleanTag = cleanTag.Substring(4);
            }
            
            // Format strictly according to GS1 GIAI AI (8004)
            _formattedGiaiTag = $"(8004){cleanTag}";

            using (var pd = new PrintDocument()) {
                if (!string.IsNullOrEmpty(printerName)) {
                    pd.PrinterSettings.PrinterName = printerName;
                }

                pd.PrintPage += new PrintPageEventHandler(PrintPageHandler);
                
                // Use StandardPrintController to bypass standard Windows Print Progress Dialogs
                pd.PrintController = new StandardPrintController();
                
                pd.Print();
            }
        }

        private void PrintPageHandler(object sender, PrintPageEventArgs ev) {
            // Drawing the bare-metal label on the Graphics context
            using (var headerFont = new Font("Arial", 10, FontStyle.Bold))
            using (var tagFont = new Font("Consolas", 14, FontStyle.Bold))
            using (var brush = new SolidBrush(Color.Black))
            using (var pen = new Pen(Color.Black, 2)) {
                float x = 20;
                float y = 20;

                // 1. Draw outer label border
                ev.Graphics.DrawRectangle(pen, 10, 10, 300, 100);

                // 2. Draw generic headers
                ev.Graphics.DrawString("SOVEREIGN ASSET INVENTORY", headerFont, brush, x, y);
                ev.Graphics.DrawString("GIAI IDENTIFIER:", headerFont, brush, x, y + 20);

                // 3. Draw the formatted GIAI Tag (e.g. (8004)123456)
                ev.Graphics.DrawString(_formattedGiaiTag, tagFont, brush, x, y + 40);

                // 4. Draw a visual representation of a barcode (simple zebra lines)
                float lineX = x;
                for (int i = 0; i < 25; i++) {
                    float width = (i % 3 == 0) ? 3 : 1;
                    ev.Graphics.FillRectangle(brush, lineX, y + 65, width, 20);
                    lineX += width + 2;
                }
            }

            ev.HasMorePages = false; // Single-page tag layout
        }
    }
}
