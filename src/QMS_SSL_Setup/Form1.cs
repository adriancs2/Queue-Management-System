using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QMS_SSL_Setup
{
    public partial class Form1 : Form
    {
        // Fixed app id used when calling `netsh http add sslcert`. Any GUID works as long
        // as it stays consistent so we can find/replace our own binding later.
        private const string AppId = "{a1b2c3d4-5e6f-7890-abcd-ef0123456789}";

        public Form1()
        {
            InitializeComponent();
        }

        // ---- lifecycle ----------------------------------------------------

        private void Form1_Load(object sender, EventArgs e)
        {
            // Auto-locate wacs.exe in ./win-acme/wacs.exe next to this installer.
            var here = AppDomain.CurrentDomain.BaseDirectory;
            var guessWacs = Path.Combine(here, "win-acme", "wacs.exe");
            if (File.Exists(guessWacs)) txtWacs.Text = guessWacs;

            // Guess the console app exe — sibling SSE\bin\Debug\SSE.exe etc.
            // User can override.
            var guessApp = FindSiblingExe();
            if (guessApp != null)
            {
                txtAppExe.Text = guessApp;
                txtProcName.Text = Path.GetFileNameWithoutExtension(guessApp);
            }

            if (!IsAdministrator())
            {
                MessageBox.Show(this,
                    "This tool must be run as Administrator.\n\n" +
                    "Most operations (netsh http, firewall, certificate store) require elevation.",
                    "Administrator required",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            RefreshStatusAsync();
        }

        private string FindSiblingExe()
        {
            try
            {
                var here = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                // Walk up looking for SSE.exe in known relative spots.
                var candidates = new[]
                {
                    @"..\..\..\SSE\bin\Debug\SSE.exe",
                    @"..\..\..\SSE\bin\Release\SSE.exe",
                    @"..\..\SSE\bin\Debug\SSE.exe",
                    @"..\SSE\bin\Debug\SSE.exe",
                };
                foreach (var rel in candidates)
                {
                    var p = Path.GetFullPath(Path.Combine(here.FullName, rel));
                    if (File.Exists(p)) return p;
                }
            }
            catch { }
            return null;
        }

        // ---- input handlers -----------------------------------------------

        private void btnBrowseExe_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog { Filter = "Executable (*.exe)|*.exe" })
            {
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    txtAppExe.Text = ofd.FileName;
                    if (string.IsNullOrWhiteSpace(txtProcName.Text))
                        txtProcName.Text = Path.GetFileNameWithoutExtension(ofd.FileName);
                }
            }
        }

        private void btnBrowseWacs_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog { Filter = "wacs.exe|wacs.exe" })
            {
                if (ofd.ShowDialog(this) == DialogResult.OK)
                    txtWacs.Text = ofd.FileName;
            }
        }

        private async void btnRefreshIp_Click(object sender, EventArgs e)
        {
            lblPublicIp.Text = "Public IP: resolving...";
            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) })
                {
                    var ip = (await http.GetStringAsync("https://api.ipify.org")).Trim();
                    lblPublicIp.Text = "Public IP: " + ip;
                }
            }
            catch (Exception ex)
            {
                lblPublicIp.Text = "Public IP: (failed) " + ex.Message;
            }
        }

        // ---- main actions -------------------------------------------------

        private async void btnRunSetup_Click(object sender, EventArgs e)
        {
            if (!ValidateInputs()) return;

            SetBusy(true);
            try
            {
                ResetPhases(new[]
                {
                    "1. Administrator check",
                    "2. DNS resolves to this machine",
                    "3. Firewall rules (80/443)",
                    "4. URL ACL reservations",
                    "5. Stop console app",
                    "6. Issue certificate (win-acme)",
                    "7. Bind certificate to port 443",
                    "8. Write post-renewal script",
                    "9. Start console app",
                    "10. TLS self-test"
                });

                var domain = txtDomain.Text.Trim();
                var email = txtEmail.Text.Trim();
                var appExe = txtAppExe.Text.Trim();
                var procName = txtProcName.Text.Trim();
                var wacs = txtWacs.Text.Trim();
                var staging = chkStaging.Checked;

                // 1
                if (!await Phase(0, () => Task.FromResult(IsAdministrator()
                    ? Ok("Running elevated.")
                    : Fail("Not elevated. Restart as Administrator.")))) return;

                // 2
                if (!await Phase(1, () => CheckDnsAsync(domain))) return;

                // 3
                if (chkAddFirewall.Checked)
                {
                    if (!await Phase(2, () => AddFirewallRulesAsync())) return;
                }
                else
                {
                    SetPhase(2, "SKIP", "Firewall rule creation disabled.");
                }

                // 4
                if (!await Phase(3, () => AddUrlAclsAsync())) return;

                // 5
                if (!await Phase(4, () => StopAppAsync(procName))) return;

                // 6
                string thumbprint = null;
                if (!await Phase(5, async () =>
                {
                    var r = await RunWacsAsync(wacs, domain, email, staging);
                    if (r.Ok) thumbprint = r.Detail; // we stash thumbprint in Detail
                    return r;
                })) { TryStartApp(appExe); return; }

                // 7
                if (!await Phase(6, () => BindCertAsync(thumbprint)))
                { TryStartApp(appExe); return; }

                // 8
                if (!await Phase(7, () => WritePostRenewalScriptAsync(wacs, appExe, procName)))
                { TryStartApp(appExe); return; }

                // 9
                if (!await Phase(8, () => StartAppAsync(appExe))) return;

                // 10
                await Phase(9, () => TlsSelfTestAsync(domain, thumbprint));

                Log("=== Setup finished ===");
                RefreshStatusAsync();
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void btnRefreshStatus_Click(object sender, EventArgs e)
            => await RefreshStatusAsync();

        private async void btnRenew_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtWacs.Text) || !File.Exists(txtWacs.Text))
            {
                MessageBox.Show(this, "wacs.exe path not set.");
                return;
            }
            SetBusy(true);
            try
            {
                Log("Triggering wacs --renew ...");
                var (code, stdout, stderr) = await RunProcessAsync(txtWacs.Text,
                    "--renew --baseuri " + (chkStaging.Checked
                        ? "https://acme-staging-v02.api.letsencrypt.org/"
                        : "https://acme-v02.api.letsencrypt.org/"));
                Log(stdout);
                if (!string.IsNullOrWhiteSpace(stderr)) Log("[stderr] " + stderr);
                Log("wacs exit=" + code);
                await RefreshStatusAsync();
            }
            finally { SetBusy(false); }
        }

        private async void btnRemove_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(this,
                "Remove the SSL binding from port 443? (Cert remains in Windows store, scheduled task is unaffected.)",
                "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            SetBusy(true);
            try
            {
                var (code, stdout, stderr) = await RunProcessAsync("netsh",
                    "http delete sslcert ipport=0.0.0.0:443");
                Log(stdout);
                if (!string.IsNullOrWhiteSpace(stderr)) Log("[stderr] " + stderr);
                await RefreshStatusAsync();
            }
            finally { SetBusy(false); }
        }

        // ---- phases -------------------------------------------------------

        private async Task<PhaseResult> CheckDnsAsync(string domain)
        {
            try
            {
                IPAddress[] addrs;
                try { addrs = await Dns.GetHostAddressesAsync(domain); }
                catch (Exception ex) { return Fail("DNS lookup failed: " + ex.Message); }

                string publicIp = null;
                try
                {
                    using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) })
                        publicIp = (await http.GetStringAsync("https://api.ipify.org")).Trim();
                }
                catch { }

                var resolved = string.Join(",", addrs.Select(a => a.ToString()));
                if (publicIp == null) return Ok($"Resolved {domain} -> {resolved} (could not check public IP).");

                var match = addrs.Any(a => a.ToString() == publicIp);
                return match
                    ? Ok($"{domain} -> {resolved}, matches public IP {publicIp}.")
                    : Fail($"{domain} resolves to {resolved} but public IP is {publicIp}. Fix DNS A record before continuing.");
            }
            catch (Exception ex) { return Fail(ex.Message); }
        }

        private async Task<PhaseResult> AddFirewallRulesAsync()
        {
            var sb = new StringBuilder();
            foreach (var (name, port) in new[] { ("QMS HTTP 80", 80), ("QMS HTTPS 443", 443) })
            {
                var (code, so, se) = await RunProcessAsync("netsh",
                    $"advfirewall firewall add rule name=\"{name}\" dir=in action=allow protocol=TCP localport={port}");
                sb.AppendLine($"[{port}] exit={code} {so.Trim()} {se.Trim()}");
            }
            return Ok(sb.ToString().Trim());
        }

        private async Task<PhaseResult> AddUrlAclsAsync()
        {
            var user = WindowsIdentity.GetCurrent().Name;
            var sb = new StringBuilder();
            foreach (var url in new[] { "http://+:80/", "https://+:443/" })
            {
                var (code, so, se) = await RunProcessAsync("netsh",
                    $"http add urlacl url={url} user=\"{user}\"");
                // exit 183 == already exists; treat as success
                sb.AppendLine($"[{url}] exit={code}");
                if (!string.IsNullOrWhiteSpace(so)) sb.AppendLine("  " + so.Trim());
            }
            return Ok(sb.ToString().Trim());
        }

        private async Task<PhaseResult> StopAppAsync(string procName)
        {
            if (string.IsNullOrWhiteSpace(procName)) return Ok("No process name set; skipping.");
            try
            {
                var procs = Process.GetProcessesByName(procName);
                if (procs.Length == 0) return Ok($"No running process named '{procName}'.");
                foreach (var p in procs)
                {
                    try { p.CloseMainWindow(); }
                    catch { }
                    if (!p.WaitForExit(2000))
                    {
                        try { p.Kill(); } catch { }
                    }
                }
                await Task.Delay(500);
                return Ok($"Stopped {procs.Length} process(es).");
            }
            catch (Exception ex) { return Fail(ex.Message); }
        }

        private async Task<PhaseResult> RunWacsAsync(string wacs, string domain, string email, bool staging)
        {
            if (!File.Exists(wacs)) return Fail("wacs.exe not found at " + wacs);

            var args = new StringBuilder();
            args.Append("--target manual ");
            args.Append("--host \"").Append(domain).Append("\" ");
            args.Append("--emailaddress \"").Append(email).Append("\" ");
            args.Append("--accepttos ");
            args.Append("--validation selfhosting ");
            args.Append("--store certificatestore ");
            args.Append("--certificatestore My ");
            // Skip wacs's own installation step — we bind via netsh ourselves below.
            args.Append("--installation none ");
            if (staging) args.Append("--test ");

            Log("Running: wacs.exe " + args);
            var (code, stdout, stderr) = await RunProcessAsync(wacs, args.ToString(),
                workingDir: Path.GetDirectoryName(wacs));
            Log(stdout);
            if (!string.IsNullOrWhiteSpace(stderr)) Log("[stderr] " + stderr);

            if (code != 0)
                return Fail($"wacs exited {code}. See log above. Common causes: port 80 blocked, DNS mismatch, rate limit.");

            // Find the freshest cert for this domain in My store.
            var thumb = FindFreshestThumbprint(domain);
            if (thumb == null) return Fail("wacs reported success but cert for " + domain + " not found in store.");

            return Ok(thumb); // detail = thumbprint
        }

        private string FindFreshestThumbprint(string domain)
        {
            using (var store = new X509Store(StoreName.My, StoreLocation.LocalMachine))
            {
                store.Open(OpenFlags.ReadOnly);
                var match = store.Certificates.Cast<X509Certificate2>()
                    .Where(c => c.Subject.IndexOf("CN=" + domain, StringComparison.OrdinalIgnoreCase) >= 0
                             || c.GetNameInfo(X509NameType.DnsName, false)?.Equals(domain, StringComparison.OrdinalIgnoreCase) == true)
                    .OrderByDescending(c => c.NotBefore)
                    .FirstOrDefault();
                return match?.Thumbprint;
            }
        }

        private async Task<PhaseResult> BindCertAsync(string thumbprint)
        {
            if (string.IsNullOrWhiteSpace(thumbprint)) return Fail("No thumbprint.");

            // Remove any existing binding (ignore errors).
            await RunProcessAsync("netsh", "http delete sslcert ipport=0.0.0.0:443");

            var (code, so, se) = await RunProcessAsync("netsh",
                $"http add sslcert ipport=0.0.0.0:443 certhash={thumbprint} appid={AppId} certstorename=My");
            Log(so);
            if (!string.IsNullOrWhiteSpace(se)) Log("[stderr] " + se);
            if (code != 0) return Fail("netsh add sslcert failed: " + so + " " + se);
            return Ok("Bound " + thumbprint + " to 0.0.0.0:443");
        }

        private async Task<PhaseResult> WritePostRenewalScriptAsync(string wacs, string appExe, string procName)
        {
            try
            {
                var dir = Path.GetDirectoryName(wacs);
                var scriptPath = Path.Combine(dir, "Scripts", "qms-post-renewal.ps1");
                var scriptDir = Path.GetDirectoryName(scriptPath);
                if (!Directory.Exists(scriptDir)) Directory.CreateDirectory(scriptDir);

                var script =
@"# Auto-generated by QMS_SSL_Setup. Called by win-acme after every successful renewal.
param(
    [Parameter(Mandatory=$true)][string]$CertThumbprint
)
$ErrorActionPreference = 'Continue'
$AppId = '" + AppId + @"'
$AppExe = '" + appExe.Replace("'", "''") + @"'
$ProcName = '" + procName.Replace("'", "''") + @"'

Write-Host ""Post-renewal: rebinding $CertThumbprint""

# Stop app so HTTP.sys releases the socket
if ($ProcName) {
    Get-Process -Name $ProcName -ErrorAction SilentlyContinue | ForEach-Object {
        $_.CloseMainWindow() | Out-Null
        Start-Sleep -Seconds 2
        if (-not $_.HasExited) { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
    }
}

netsh http delete sslcert ipport=0.0.0.0:443 | Out-Null
netsh http add sslcert ipport=0.0.0.0:443 certhash=$CertThumbprint appid=$AppId certstorename=My

if ($AppExe -and (Test-Path $AppExe)) {
    Start-Process -FilePath $AppExe -WorkingDirectory (Split-Path $AppExe)
}
";
                File.WriteAllText(scriptPath, script, Encoding.UTF8);

                // Tell the user how to wire it (wacs's renewal task already exists — this script is invoked
                // via the renewal's installation step the next time you re-create the cert). Easiest path:
                // re-run setup with --installation script next time. For now we just write the file.
                return Ok("Wrote " + scriptPath + ". Re-run wacs with --installation script --script <path> --scriptparameters \"{CertThumbprint}\" to wire it.");
            }
            catch (Exception ex) { return Fail(ex.Message); }
        }

        private async Task<PhaseResult> StartAppAsync(string appExe)
        {
            if (string.IsNullOrWhiteSpace(appExe) || !File.Exists(appExe))
                return Ok("No app exe to start.");
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = appExe,
                    WorkingDirectory = Path.GetDirectoryName(appExe),
                    UseShellExecute = true
                });
                await Task.Delay(1500);
                return Ok("Started " + appExe);
            }
            catch (Exception ex) { return Fail(ex.Message); }
        }

        private void TryStartApp(string appExe)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(appExe) && File.Exists(appExe))
                    Process.Start(new ProcessStartInfo { FileName = appExe, UseShellExecute = true });
            }
            catch { }
        }

        private async Task<PhaseResult> TlsSelfTestAsync(string domain, string expectedThumbprint)
        {
            await Task.Delay(1500);
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                string seenThumb = null;
                ServicePointManager.ServerCertificateValidationCallback = (s, c, ch, p) =>
                {
                    if (c is X509Certificate2 c2) seenThumb = c2.Thumbprint;
                    return true; // we're inspecting, not validating chain (could be staging)
                };
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create("https://" + domain + "/");
                    req.Timeout = 8000;
                    req.AllowAutoRedirect = false;
                    using (var resp = (HttpWebResponse)req.GetResponse()) { /* drain */ }
                }
                catch (WebException) { /* HTTP errors are fine — we just want the TLS handshake */ }
                finally { ServicePointManager.ServerCertificateValidationCallback = null; }

                if (seenThumb == null) return Fail("No TLS handshake observed.");
                return string.Equals(seenThumb, expectedThumbprint, StringComparison.OrdinalIgnoreCase)
                    ? Ok("TLS OK. Cert thumbprint matches: " + seenThumb)
                    : Fail("TLS responded but thumbprint differs. Got " + seenThumb + ", expected " + expectedThumbprint);
            }
            catch (Exception ex) { return Fail(ex.Message); }
        }

        // ---- status -------------------------------------------------------

        private async Task RefreshStatusAsync()
        {
            var sb = new StringBuilder();

            sb.AppendLine("== netsh http show sslcert ipport=0.0.0.0:443 ==");
            var (_, certOut, _) = await RunProcessAsync("netsh", "http show sslcert ipport=0.0.0.0:443");
            sb.AppendLine(string.IsNullOrWhiteSpace(certOut) ? "(no binding)" : certOut.Trim());

            // Try to enrich with cert dates from the store.
            var hashMatch = Regex.Match(certOut ?? "", @"[A-Fa-f0-9]{40}");
            if (hashMatch.Success)
            {
                using (var store = new X509Store(StoreName.My, StoreLocation.LocalMachine))
                {
                    store.Open(OpenFlags.ReadOnly);
                    var cert = store.Certificates.Cast<X509Certificate2>()
                        .FirstOrDefault(c => string.Equals(c.Thumbprint, hashMatch.Value, StringComparison.OrdinalIgnoreCase));
                    if (cert != null)
                    {
                        sb.AppendLine();
                        sb.AppendLine("== Certificate detail ==");
                        sb.AppendLine("Subject:    " + cert.Subject);
                        sb.AppendLine("Issuer:     " + cert.Issuer);
                        sb.AppendLine("NotBefore:  " + cert.NotBefore);
                        sb.AppendLine("NotAfter:   " + cert.NotAfter +
                                      "  (" + (int)(cert.NotAfter - DateTime.Now).TotalDays + " days remaining)");
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine("== Scheduled renewal task ==");
            var (_, schOut, _) = await RunProcessAsync("schtasks",
                "/query /fo LIST /v");
            // Filter for win-acme tasks
            var lines = (schOut ?? "").Split('\n');
            bool inAcme = false;
            int kept = 0;
            foreach (var line in lines)
            {
                if (line.StartsWith("HostName:", StringComparison.OrdinalIgnoreCase)) inAcme = false;
                if (line.IndexOf("win-acme", StringComparison.OrdinalIgnoreCase) >= 0) inAcme = true;
                if (inAcme) { sb.AppendLine(line.TrimEnd()); kept++; }
            }
            if (kept == 0) sb.AppendLine("(no win-acme scheduled task found)");

            txtStatus.Text = sb.ToString();
        }

        // ---- helpers ------------------------------------------------------

        private bool ValidateInputs()
        {
            if (string.IsNullOrWhiteSpace(txtDomain.Text)) { Warn("Domain is required."); return false; }
            if (string.IsNullOrWhiteSpace(txtEmail.Text)) { Warn("Email is required."); return false; }
            if (string.IsNullOrWhiteSpace(txtWacs.Text) || !File.Exists(txtWacs.Text))
            { Warn("wacs.exe path is invalid."); return false; }
            return true;
        }

        private void Warn(string msg) =>
            MessageBox.Show(this, msg, "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);

        private static bool IsAdministrator()
        {
            using (var id = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }

        private async Task<(int Code, string Stdout, string Stderr)> RunProcessAsync(
            string fileName, string args, string workingDir = null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            if (!string.IsNullOrEmpty(workingDir)) psi.WorkingDirectory = workingDir;

            using (var p = new Process { StartInfo = psi, EnableRaisingEvents = true })
            {
                var so = new StringBuilder();
                var se = new StringBuilder();
                p.OutputDataReceived += (s, e) => { if (e.Data != null) so.AppendLine(e.Data); };
                p.ErrorDataReceived  += (s, e) => { if (e.Data != null) se.AppendLine(e.Data); };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                await Task.Run(() => p.WaitForExit());
                return (p.ExitCode, so.ToString(), se.ToString());
            }
        }

        // ---- phase UI -----------------------------------------------------

        private struct PhaseResult { public bool Ok; public string Detail; }
        private static PhaseResult Ok(string d) => new PhaseResult { Ok = true, Detail = d ?? "" };
        private static PhaseResult Fail(string d) => new PhaseResult { Ok = false, Detail = d ?? "" };

        private void ResetPhases(string[] names)
        {
            lvPhases.Items.Clear();
            foreach (var n in names)
                lvPhases.Items.Add(new ListViewItem(new[] { n, "PENDING", "" }));
            txtLog.Clear();
        }

        private void SetPhase(int idx, string status, string detail)
        {
            if (idx < 0 || idx >= lvPhases.Items.Count) return;
            var it = lvPhases.Items[idx];
            it.SubItems[1].Text = status;
            it.SubItems[2].Text = (detail ?? "").Replace("\r", " ").Replace("\n", " ");
            switch (status)
            {
                case "OK":   it.BackColor = System.Drawing.Color.Honeydew; break;
                case "FAIL": it.BackColor = System.Drawing.Color.MistyRose; break;
                case "SKIP": it.BackColor = System.Drawing.Color.LightYellow; break;
                case "RUN":  it.BackColor = System.Drawing.Color.LightCyan; break;
            }
            it.EnsureVisible();
            Application.DoEvents();
        }

        private async Task<bool> Phase(int idx, Func<Task<PhaseResult>> work)
        {
            SetPhase(idx, "RUN", "");
            Log($"--- {lvPhases.Items[idx].SubItems[0].Text} ---");
            try
            {
                var r = await work();
                SetPhase(idx, r.Ok ? "OK" : "FAIL", r.Detail);
                Log((r.Ok ? "[OK] " : "[FAIL] ") + r.Detail);
                return r.Ok;
            }
            catch (Exception ex)
            {
                SetPhase(idx, "FAIL", ex.Message);
                Log("[EXCEPTION] " + ex);
                return false;
            }
        }

        private void Log(string s)
        {
            if (s == null) return;
            txtLog.AppendText(s + Environment.NewLine);
        }

        private void SetBusy(bool busy)
        {
            btnRunSetup.Enabled = !busy;
            btnRefreshStatus.Enabled = !busy;
            btnRenew.Enabled = !busy;
            btnRemove.Enabled = !busy;
            grpInputs.Enabled = !busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }
    }
}
