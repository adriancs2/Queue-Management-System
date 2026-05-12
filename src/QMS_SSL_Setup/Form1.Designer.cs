namespace QMS_SSL_Setup
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private System.Windows.Forms.GroupBox grpInputs;
        private System.Windows.Forms.Label lblDomain;
        private System.Windows.Forms.TextBox txtDomain;
        private System.Windows.Forms.Label lblEmail;
        private System.Windows.Forms.TextBox txtEmail;
        private System.Windows.Forms.Label lblAppExe;
        private System.Windows.Forms.TextBox txtAppExe;
        private System.Windows.Forms.Button btnBrowseExe;
        private System.Windows.Forms.Label lblProcName;
        private System.Windows.Forms.TextBox txtProcName;
        private System.Windows.Forms.Label lblWacs;
        private System.Windows.Forms.TextBox txtWacs;
        private System.Windows.Forms.Button btnBrowseWacs;
        private System.Windows.Forms.CheckBox chkStaging;
        private System.Windows.Forms.CheckBox chkAddFirewall;
        private System.Windows.Forms.Label lblPublicIp;
        private System.Windows.Forms.Button btnRefreshIp;

        private System.Windows.Forms.Button btnRunSetup;
        private System.Windows.Forms.Button btnRefreshStatus;
        private System.Windows.Forms.Button btnRenew;
        private System.Windows.Forms.Button btnRemove;

        private System.Windows.Forms.ListView lvPhases;
        private System.Windows.Forms.ColumnHeader colPhase;
        private System.Windows.Forms.ColumnHeader colStatus;
        private System.Windows.Forms.ColumnHeader colDetail;

        private System.Windows.Forms.GroupBox grpStatus;
        private System.Windows.Forms.TextBox txtStatus;

        private System.Windows.Forms.GroupBox grpLog;
        private System.Windows.Forms.TextBox txtLog;

        private System.Windows.Forms.SplitContainer split;

        private void InitializeComponent()
        {
            this.grpInputs = new System.Windows.Forms.GroupBox();
            this.lblDomain = new System.Windows.Forms.Label();
            this.txtDomain = new System.Windows.Forms.TextBox();
            this.lblEmail = new System.Windows.Forms.Label();
            this.txtEmail = new System.Windows.Forms.TextBox();
            this.lblAppExe = new System.Windows.Forms.Label();
            this.txtAppExe = new System.Windows.Forms.TextBox();
            this.btnBrowseExe = new System.Windows.Forms.Button();
            this.lblProcName = new System.Windows.Forms.Label();
            this.txtProcName = new System.Windows.Forms.TextBox();
            this.lblWacs = new System.Windows.Forms.Label();
            this.txtWacs = new System.Windows.Forms.TextBox();
            this.btnBrowseWacs = new System.Windows.Forms.Button();
            this.chkStaging = new System.Windows.Forms.CheckBox();
            this.chkAddFirewall = new System.Windows.Forms.CheckBox();
            this.lblPublicIp = new System.Windows.Forms.Label();
            this.btnRefreshIp = new System.Windows.Forms.Button();
            this.btnRunSetup = new System.Windows.Forms.Button();
            this.btnRefreshStatus = new System.Windows.Forms.Button();
            this.btnRenew = new System.Windows.Forms.Button();
            this.btnRemove = new System.Windows.Forms.Button();
            this.lvPhases = new System.Windows.Forms.ListView();
            this.colPhase = new System.Windows.Forms.ColumnHeader();
            this.colStatus = new System.Windows.Forms.ColumnHeader();
            this.colDetail = new System.Windows.Forms.ColumnHeader();
            this.grpStatus = new System.Windows.Forms.GroupBox();
            this.txtStatus = new System.Windows.Forms.TextBox();
            this.grpLog = new System.Windows.Forms.GroupBox();
            this.txtLog = new System.Windows.Forms.TextBox();
            this.split = new System.Windows.Forms.SplitContainer();

            // grpInputs
            this.grpInputs.Text = "Configuration";
            this.grpInputs.Location = new System.Drawing.Point(12, 12);
            this.grpInputs.Size = new System.Drawing.Size(960, 200);
            this.grpInputs.Controls.Add(this.lblDomain);
            this.grpInputs.Controls.Add(this.txtDomain);
            this.grpInputs.Controls.Add(this.lblEmail);
            this.grpInputs.Controls.Add(this.txtEmail);
            this.grpInputs.Controls.Add(this.lblAppExe);
            this.grpInputs.Controls.Add(this.txtAppExe);
            this.grpInputs.Controls.Add(this.btnBrowseExe);
            this.grpInputs.Controls.Add(this.lblProcName);
            this.grpInputs.Controls.Add(this.txtProcName);
            this.grpInputs.Controls.Add(this.lblWacs);
            this.grpInputs.Controls.Add(this.txtWacs);
            this.grpInputs.Controls.Add(this.btnBrowseWacs);
            this.grpInputs.Controls.Add(this.chkStaging);
            this.grpInputs.Controls.Add(this.chkAddFirewall);
            this.grpInputs.Controls.Add(this.lblPublicIp);
            this.grpInputs.Controls.Add(this.btnRefreshIp);

            // Row 1: Domain
            this.lblDomain.Text = "Domain:";
            this.lblDomain.Location = new System.Drawing.Point(12, 28);
            this.lblDomain.Size = new System.Drawing.Size(110, 20);
            this.txtDomain.Location = new System.Drawing.Point(125, 25);
            this.txtDomain.Size = new System.Drawing.Size(280, 23);

            this.lblEmail.Text = "Email (LE account):";
            this.lblEmail.Location = new System.Drawing.Point(425, 28);
            this.lblEmail.Size = new System.Drawing.Size(140, 20);
            this.txtEmail.Location = new System.Drawing.Point(570, 25);
            this.txtEmail.Size = new System.Drawing.Size(280, 23);

            // Row 2: App exe
            this.lblAppExe.Text = "Console App EXE:";
            this.lblAppExe.Location = new System.Drawing.Point(12, 60);
            this.lblAppExe.Size = new System.Drawing.Size(110, 20);
            this.txtAppExe.Location = new System.Drawing.Point(125, 57);
            this.txtAppExe.Size = new System.Drawing.Size(725, 23);
            this.btnBrowseExe.Text = "Browse...";
            this.btnBrowseExe.Location = new System.Drawing.Point(860, 56);
            this.btnBrowseExe.Size = new System.Drawing.Size(85, 25);
            this.btnBrowseExe.Click += new System.EventHandler(this.btnBrowseExe_Click);

            // Row 3: Process name
            this.lblProcName.Text = "Process name:";
            this.lblProcName.Location = new System.Drawing.Point(12, 92);
            this.lblProcName.Size = new System.Drawing.Size(110, 20);
            this.txtProcName.Location = new System.Drawing.Point(125, 89);
            this.txtProcName.Size = new System.Drawing.Size(280, 23);

            // Row 3 right: Public IP
            this.lblPublicIp.Text = "Public IP: (click refresh)";
            this.lblPublicIp.Location = new System.Drawing.Point(425, 92);
            this.lblPublicIp.Size = new System.Drawing.Size(340, 20);
            this.btnRefreshIp.Text = "Refresh IP";
            this.btnRefreshIp.Location = new System.Drawing.Point(770, 88);
            this.btnRefreshIp.Size = new System.Drawing.Size(80, 25);
            this.btnRefreshIp.Click += new System.EventHandler(this.btnRefreshIp_Click);

            // Row 4: wacs.exe
            this.lblWacs.Text = "wacs.exe path:";
            this.lblWacs.Location = new System.Drawing.Point(12, 124);
            this.lblWacs.Size = new System.Drawing.Size(110, 20);
            this.txtWacs.Location = new System.Drawing.Point(125, 121);
            this.txtWacs.Size = new System.Drawing.Size(725, 23);
            this.btnBrowseWacs.Text = "Browse...";
            this.btnBrowseWacs.Location = new System.Drawing.Point(860, 120);
            this.btnBrowseWacs.Size = new System.Drawing.Size(85, 25);
            this.btnBrowseWacs.Click += new System.EventHandler(this.btnBrowseWacs_Click);

            // Row 5: checkboxes
            this.chkStaging.Text = "Use Let's Encrypt staging (test certs, no rate limit)";
            this.chkStaging.Location = new System.Drawing.Point(125, 155);
            this.chkStaging.Size = new System.Drawing.Size(360, 24);
            this.chkAddFirewall.Text = "Add Windows firewall rules (80/443)";
            this.chkAddFirewall.Location = new System.Drawing.Point(500, 155);
            this.chkAddFirewall.Size = new System.Drawing.Size(280, 24);
            this.chkAddFirewall.Checked = true;

            // Action buttons
            this.btnRunSetup.Text = "Run SSL Setup";
            this.btnRunSetup.Location = new System.Drawing.Point(12, 222);
            this.btnRunSetup.Size = new System.Drawing.Size(160, 32);
            this.btnRunSetup.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold);
            this.btnRunSetup.Click += new System.EventHandler(this.btnRunSetup_Click);

            this.btnRefreshStatus.Text = "Refresh Status";
            this.btnRefreshStatus.Location = new System.Drawing.Point(180, 222);
            this.btnRefreshStatus.Size = new System.Drawing.Size(140, 32);
            this.btnRefreshStatus.Click += new System.EventHandler(this.btnRefreshStatus_Click);

            this.btnRenew.Text = "Renew Now";
            this.btnRenew.Location = new System.Drawing.Point(328, 222);
            this.btnRenew.Size = new System.Drawing.Size(140, 32);
            this.btnRenew.Click += new System.EventHandler(this.btnRenew_Click);

            this.btnRemove.Text = "Remove SSL Binding";
            this.btnRemove.Location = new System.Drawing.Point(476, 222);
            this.btnRemove.Size = new System.Drawing.Size(160, 32);
            this.btnRemove.Click += new System.EventHandler(this.btnRemove_Click);

            // SplitContainer for phases | (status + log)
            this.split.Location = new System.Drawing.Point(12, 264);
            this.split.Size = new System.Drawing.Size(960, 380);
            this.split.Orientation = System.Windows.Forms.Orientation.Horizontal;
            this.split.SplitterDistance = 170;

            // Phases listview (top panel)
            this.colPhase.Text = "Phase";
            this.colPhase.Width = 280;
            this.colStatus.Text = "Status";
            this.colStatus.Width = 90;
            this.colDetail.Text = "Detail";
            this.colDetail.Width = 580;
            this.lvPhases.Columns.Add(this.colPhase);
            this.lvPhases.Columns.Add(this.colStatus);
            this.lvPhases.Columns.Add(this.colDetail);
            this.lvPhases.View = System.Windows.Forms.View.Details;
            this.lvPhases.FullRowSelect = true;
            this.lvPhases.GridLines = true;
            this.lvPhases.Dock = System.Windows.Forms.DockStyle.Fill;
            this.split.Panel1.Controls.Add(this.lvPhases);

            // Bottom panel: status group (left) + log group (right)
            var splitBottom = new System.Windows.Forms.SplitContainer();
            splitBottom.Dock = System.Windows.Forms.DockStyle.Fill;
            splitBottom.SplitterDistance = 360;

            this.grpStatus.Text = "Current SSL Status";
            this.grpStatus.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtStatus.Multiline = true;
            this.txtStatus.ReadOnly = true;
            this.txtStatus.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.txtStatus.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtStatus.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtStatus.WordWrap = false;
            this.grpStatus.Controls.Add(this.txtStatus);
            splitBottom.Panel1.Controls.Add(this.grpStatus);

            this.grpLog.Text = "Log";
            this.grpLog.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtLog.Multiline = true;
            this.txtLog.ReadOnly = true;
            this.txtLog.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.txtLog.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtLog.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtLog.WordWrap = false;
            this.grpLog.Controls.Add(this.txtLog);
            splitBottom.Panel2.Controls.Add(this.grpLog);

            this.split.Panel2.Controls.Add(splitBottom);

            // Form
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(984, 656);
            this.Controls.Add(this.grpInputs);
            this.Controls.Add(this.btnRunSetup);
            this.Controls.Add(this.btnRefreshStatus);
            this.Controls.Add(this.btnRenew);
            this.Controls.Add(this.btnRemove);
            this.Controls.Add(this.split);
            this.Text = "QMS SSL Setup (Let's Encrypt)";
            this.MinimumSize = new System.Drawing.Size(1000, 695);
            this.Load += new System.EventHandler(this.Form1_Load);
        }
    }
}
