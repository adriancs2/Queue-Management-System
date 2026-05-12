using System;
using System.Drawing;
using System.Drawing.Printing;

namespace System.Engine
{
    /// <summary>
    /// Receipt-style ticket printer using GDI PrintDocument.
    ///
    /// Local vs. remote printer:
    ///   This class always prints "locally" through the Windows print spooler.
    ///   To print on a printer attached to a different PC, share that printer
    ///   in Windows on the host PC and map it on this PC via 'Add Printer'.
    ///   Then put the mapped local name in config.txt (printer_name=...).
    ///   The OS handles all network transport — no separate listener app.
    ///   The same approach works for Linux/macOS print servers exposed via
    ///   SMB/IPP, as long as Windows can map the printer.
    ///
    /// PrintDocument.Print() is synchronous; calling it from multiple threads
    /// concurrently still works because the Windows spooler serializes jobs
    /// per printer. No additional locking is required here.
    /// </summary>
    public static class Printer
    {
        public static void PrintTicket(string printerName, int counter, int ticket, string deptName)
        {
            try
            {
                using (var doc = new PrintDocument())
                {
                    doc.PrintController = new StandardPrintController();
                    if (!string.IsNullOrEmpty(printerName))
                        doc.PrinterSettings.PrinterName = printerName;

                    // 80mm thermal receipt = ~315 (hundredths of an inch)
                    // The installed driver normally already reports the right width.
                    // If the driver defaults to A4, set an explicit PaperSize here:
                    //   doc.DefaultPageSettings.PaperSize = new PaperSize("Receipt80", 315, 1000);
                    doc.DefaultPageSettings.Margins = new Margins(10, 10, 10, 10);

                    doc.PrintPage += (s, e) => RenderTicket(e, counter, ticket, deptName);
                    doc.Print();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[printer] FAILED: {ex.Message}");
            }
        }

        private static void RenderTicket(PrintPageEventArgs e, int counter, int ticket, string deptName)
        {
            using (var fontTitle  = new Font("Arial", 12, FontStyle.Regular))
            using (var fontDept   = new Font("Arial", 14, FontStyle.Bold))
            using (var fontTicket = new Font("Arial", 60, FontStyle.Bold))
            using (var fontSmall  = new Font("Arial", 10, FontStyle.Regular))
            using (var center     = new StringFormat { Alignment = StringAlignment.Center })
            {
                float w = e.PageBounds.Width;
                float y = e.MarginBounds.Top;

                e.Graphics.DrawString("Queue Ticket", fontTitle, Brushes.Black,
                    new RectangleF(0, y, w, 20), center);
                y += 28;

                e.Graphics.DrawString(deptName ?? "", fontDept, Brushes.Black,
                    new RectangleF(0, y, w, 24), center);
                y += 36;

                e.Graphics.DrawString(ticket.ToString(), fontTicket, Brushes.Black,
                    new RectangleF(0, y, w, 90), center);
                y += 100;

                e.Graphics.DrawString("Counter " + counter, fontTitle, Brushes.Black,
                    new RectangleF(0, y, w, 20), center);
                y += 28;

                e.Graphics.DrawString(DateTime.Now.ToString("yyyy-MM-dd HH:mm"), fontSmall, Brushes.Black,
                    new RectangleF(0, y, w, 16), center);
                y += 24;

                e.Graphics.DrawString("Please wait for your number to be called.", fontSmall, Brushes.Black,
                    new RectangleF(0, y, w, 16), center);
            }
        }
    }
}
