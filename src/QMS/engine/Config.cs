using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace System.Engine
{
    public class AppConfig
    {
        public int HttpPort { get; set; } = 8080;
        public string PrinterName { get; set; } = "";
        public List<string> Departments { get; set; } = new List<string>();

        private const string DefaultContent =
@"# Queue Ticket System — config.txt
#
# http_port      The TCP port the queue server listens on.
#
# printer_name   The Windows printer name used for ticket printing.
#                Leave blank to use the Windows default printer.
#
#                For printing to a printer attached to ANOTHER PC:
#                  1. On the PC with the physical printer: share the printer
#                     in Windows ('Printer properties' -> 'Sharing' tab).
#                  2. On THIS PC: 'Add Printer' -> select the shared network
#                     printer. Windows installs the driver and gives the
#                     mapped printer a local name (e.g. 'EPSON TM-T82 on PC1').
#                  3. Put that local name here. The OS handles the network
#                     transport — this app just calls the standard print API.
#                  4. The same approach works with Linux/macOS print servers
#                     exposed via SMB/IPP, as long as Windows can map them.
#
#                If the configured printer is offline, printing fails but the
#                queue still advances — operators can retry from the UI.
#
# departments    Add the departments here, separated by comma. White space in between allowed.
#                Department IDs are auto-assigned in order (1, 2, 3, ...).

http_port=8080
printer_name=
departments=General Inquiries, Payments, License Services, Account & Register
";

        public static AppConfig Load(string path)
        {
            if (!File.Exists(path))
            {
                File.WriteAllText(path, DefaultContent, Encoding.UTF8);
                Console.WriteLine($"[config] Created default {path}");
            }

            var cfg = new AppConfig();
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string val = line.Substring(eq + 1).Trim();

                switch (key)
                {
                    case "http_port":
                        int p; if (int.TryParse(val, out p)) cfg.HttpPort = p;
                        break;
                    case "printer_name":
                        cfg.PrinterName = val;
                        break;
                    case "departments":
                        foreach (string part in val.Split(','))
                        {
                            string name = part.Trim();
                            if (name.Length > 0) cfg.Departments.Add(name);
                        }
                        break;
                }
            }

            if (cfg.Departments.Count == 0)
            {
                cfg.Departments.Add("General Inquiries");
                cfg.Departments.Add("Payments");
                cfg.Departments.Add("License Services");
                cfg.Departments.Add("Account & Register");
            }
            return cfg;
        }
    }
}
