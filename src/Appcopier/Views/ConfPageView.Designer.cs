namespace Views
{
    partial class ConfPageView
    {
        /// <summary>
        /// Erforderliche Designervariable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Verwendete Ressourcen bereinigen.
        /// </summary>
        /// <param name="disposing">True, wenn verwaltete Ressourcen gelöscht werden sollen; andernfalls False.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Vom Komponenten-Designer generierter Code

        /// <summary>
        /// Erforderliche Methode für die Designerunterstützung.
        /// Der Inhalt der Methode darf nicht mit dem Code-Editor geändert werden.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.headerLabel = new System.Windows.Forms.Label();
            this.linkSubHeader = new System.Windows.Forms.LinkLabel();
            this.treeConfigurations = new System.Windows.Forms.TreeView();
            this.txtInfo = new System.Windows.Forms.TextBox();
            this.btnBackup = new System.Windows.Forms.Button();
            this.btnMenuRestore = new System.Windows.Forms.Button();
            this.btnMenuMore = new System.Windows.Forms.Button();
            this.resultsPanel = new Views.RunResultsPanel();
            this.logToggle = new System.Windows.Forms.LinkLabel();
            this.rtbLog = new System.Windows.Forms.RichTextBox();
            this.tt = new System.Windows.Forms.ToolTip(this.components);
            this.contextMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.menuSelectInstalled = new System.Windows.Forms.ToolStripMenuItem();
            this.menuSelectAll = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.textHeaderApp = new System.Windows.Forms.ToolStripTextBox();
            this.menuOpenBackupFolder = new System.Windows.Forms.ToolStripMenuItem();
            this.menuOpenAppBackups = new System.Windows.Forms.ToolStripMenuItem();
            this.root = new System.Windows.Forms.TableLayoutPanel();
            this.content = new System.Windows.Forms.TableLayoutPanel();
            this.actions = new System.Windows.Forms.TableLayoutPanel();
            this.contextMenu.SuspendLayout();
            this.root.SuspendLayout();
            this.content.SuspendLayout();
            this.actions.SuspendLayout();
            this.SuspendLayout();
            //
            // headerLabel
            //
            this.headerLabel.AutoSize = true;
            this.headerLabel.Dock = System.Windows.Forms.DockStyle.Top;
            this.headerLabel.Margin = new System.Windows.Forms.Padding(0, 0, 0, Appcopier.Ui.SpaceXs);
            this.headerLabel.Name = "headerLabel";
            this.headerLabel.Text = "Choose what to back up";
            //
            // linkSubHeader
            //
            this.linkSubHeader.AutoSize = true;
            this.linkSubHeader.Dock = System.Windows.Forms.DockStyle.Top;
            this.linkSubHeader.LinkBehavior = System.Windows.Forms.LinkBehavior.HoverUnderline;
            this.linkSubHeader.LinkColor = System.Drawing.Color.Black;
            this.linkSubHeader.Margin = new System.Windows.Forms.Padding(0, 0, 0, Appcopier.Ui.SpaceS);
            this.linkSubHeader.Name = "linkSubHeader";
            this.linkSubHeader.TabStop = true;
            this.linkSubHeader.Text = "Choose settings";
            //
            // treeConfigurations
            //
            this.treeConfigurations.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.treeConfigurations.CheckBoxes = true;
            this.treeConfigurations.Dock = System.Windows.Forms.DockStyle.Fill;
            this.treeConfigurations.FullRowSelect = true;
            this.treeConfigurations.ItemHeight = 30;
            this.treeConfigurations.LineColor = System.Drawing.Color.Orchid;
            this.treeConfigurations.Name = "treeConfigurations";
            this.treeConfigurations.AfterCheck += new System.Windows.Forms.TreeViewEventHandler(this.treeConfigurations_AfterCheck);
            this.treeConfigurations.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.treeConfigurations_AfterSelect);
            //
            // txtInfo
            //
            this.txtInfo.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.txtInfo.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtInfo.Multiline = true;
            this.txtInfo.Name = "txtInfo";
            this.txtInfo.ReadOnly = true;
            this.txtInfo.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            //
            // content (tree | info)
            //
            this.content.AutoScroll = true;
            this.content.ColumnCount = 2;
            this.content.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 55F));
            this.content.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 45F));
            this.content.Controls.Add(this.treeConfigurations, 0, 0);
            this.content.Controls.Add(this.txtInfo, 1, 0);
            this.content.Dock = System.Windows.Forms.DockStyle.Fill;
            this.content.Location = new System.Drawing.Point(0, 0);
            this.content.Name = "content";
            this.content.RowCount = 1;
            this.content.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            //
            // btnBackup
            //
            this.btnBackup.AutoSize = true;
            this.btnBackup.BackColor = System.Drawing.Color.Black;
            this.btnBackup.FlatAppearance.BorderSize = 0;
            this.btnBackup.ForeColor = System.Drawing.Color.White;
            this.btnBackup.Name = "btnBackup";
            this.btnBackup.Padding = new System.Windows.Forms.Padding(Appcopier.Ui.SpaceM, Appcopier.Ui.SpaceXs, Appcopier.Ui.SpaceM, Appcopier.Ui.SpaceXs);
            this.btnBackup.Text = "Back up now";
            this.btnBackup.UseVisualStyleBackColor = false;
            this.btnBackup.Click += new System.EventHandler(this.btnBackup_Click);
            //
            // btnMenuRestore
            //
            this.btnMenuRestore.AutoSize = true;
            this.btnMenuRestore.BackColor = System.Drawing.Color.Transparent;
            this.btnMenuRestore.FlatAppearance.BorderSize = 0;
            this.btnMenuRestore.FlatAppearance.MouseOverBackColor = System.Drawing.Color.Thistle;
            this.btnMenuRestore.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnMenuRestore.Font = new System.Drawing.Font("Segoe MDL2 Assets", 12F);
            this.btnMenuRestore.ForeColor = System.Drawing.Color.DimGray;
            this.btnMenuRestore.Name = "btnMenuRestore";
            this.btnMenuRestore.Padding = new System.Windows.Forms.Padding(Appcopier.Ui.SpaceXs, 0, Appcopier.Ui.SpaceXs, 0);
            this.btnMenuRestore.Text = "Restore";
            this.tt.SetToolTip(this.btnMenuRestore, "Restore your back ups");
            this.btnMenuRestore.UseVisualStyleBackColor = false;
            this.btnMenuRestore.Click += new System.EventHandler(this.btnRestore_Click);
            //
            // btnMenuMore
            //
            this.btnMenuMore.AutoSize = true;
            this.btnMenuMore.BackColor = System.Drawing.Color.Transparent;
            this.btnMenuMore.FlatAppearance.BorderSize = 0;
            this.btnMenuMore.FlatAppearance.MouseOverBackColor = System.Drawing.Color.Thistle;
            this.btnMenuMore.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnMenuMore.Font = new System.Drawing.Font("Segoe MDL2 Assets", 10.25F);
            this.btnMenuMore.ForeColor = System.Drawing.Color.Black;
            this.btnMenuMore.Name = "btnMenuMore";
            this.btnMenuMore.Padding = new System.Windows.Forms.Padding(Appcopier.Ui.SpaceXs, 0, Appcopier.Ui.SpaceXs, 0);
            this.btnMenuMore.Text = "...";
            this.btnMenuMore.UseVisualStyleBackColor = false;
            this.btnMenuMore.Click += new System.EventHandler(this.btnMenuMore_Click);
            //
            // resultsPanel
            //
            this.resultsPanel.AutoScroll = true;
            this.resultsPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.resultsPanel.Name = "resultsPanel";
            //
            // logToggle
            //
            this.logToggle.AutoSize = true;
            this.logToggle.Dock = System.Windows.Forms.DockStyle.Top;
            this.logToggle.LinkBehavior = System.Windows.Forms.LinkBehavior.HoverUnderline;
            this.logToggle.LinkColor = System.Drawing.Color.DimGray;
            this.logToggle.Margin = new System.Windows.Forms.Padding(0, Appcopier.Ui.SpaceS, 0, Appcopier.Ui.SpaceXs);
            this.logToggle.Name = "logToggle";
            this.logToggle.TabStop = true;
            this.logToggle.Text = "Activity log";
            this.logToggle.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.logToggle_LinkClicked);
            //
            // rtbLog
            //
            this.rtbLog.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.rtbLog.Dock = System.Windows.Forms.DockStyle.Top;
            this.rtbLog.Font = new System.Drawing.Font("Segoe UI Variable Text", 9F);
            this.rtbLog.ForeColor = System.Drawing.Color.DimGray;
            this.rtbLog.HideSelection = false;
            this.rtbLog.Name = "rtbLog";
            this.rtbLog.ReadOnly = true;
            this.rtbLog.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.Vertical;
            this.rtbLog.Size = new System.Drawing.Size(0, 120);
            this.rtbLog.Text = "This app supports you in backing up, sharing, and restoring your key settings.";
            //
            // actions
            //
            this.actions.AutoSize = true;
            this.actions.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.actions.ColumnCount = 4;
            this.actions.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, Appcopier.Ui.SpaceM));
            this.actions.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            this.actions.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            this.actions.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.actions.Controls.Add(new System.Windows.Forms.Label(), 0, 0);
            this.actions.Controls.Add(this.btnBackup, 1, 0);
            this.actions.Controls.Add(this.btnMenuRestore, 2, 0);
            this.actions.Controls.Add(this.btnMenuMore, 3, 0);
            this.actions.Dock = System.Windows.Forms.DockStyle.Top;
            this.actions.Name = "actions";
            this.actions.RowCount = 1;
            this.actions.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            //
            // root
            //
            this.root.AutoScroll = true;
            this.root.ColumnCount = 1;
            this.root.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.root.Controls.Add(this.rtbLog, 0, 6);
            this.root.Controls.Add(this.logToggle, 0, 5);
            this.root.Controls.Add(this.resultsPanel, 0, 4);
            this.root.Controls.Add(this.actions, 0, 3);
            this.root.Controls.Add(this.content, 0, 2);
            this.root.Controls.Add(this.linkSubHeader, 0, 1);
            this.root.Controls.Add(this.headerLabel, 0, 0);
            this.root.Dock = System.Windows.Forms.DockStyle.Fill;
            this.root.Location = new System.Drawing.Point(Appcopier.Ui.SpaceM, Appcopier.Ui.SpaceM);
            this.root.Name = "root";
            this.root.RowCount = 7;
            this.root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 0F));
            //
            // contextMenu
            //
            this.contextMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.menuSelectInstalled,
            this.menuSelectAll,
            this.toolStripSeparator1,
            this.textHeaderApp,
            this.menuOpenBackupFolder,
            this.menuOpenAppBackups});
            this.contextMenu.Name = "menuMain";
            this.contextMenu.Size = new System.Drawing.Size(204, 148);
            //
            // menuSelectInstalled
            //
            this.menuSelectInstalled.Name = "menuSelectInstalled";
            this.menuSelectInstalled.Size = new System.Drawing.Size(203, 24);
            this.menuSelectInstalled.Text = "Select available";
            this.menuSelectInstalled.Click += new System.EventHandler(this.menuSelectInstalled_Click);
            //
            // menuSelectAll
            //
            this.menuSelectAll.Name = "menuSelectAll";
            this.menuSelectAll.Size = new System.Drawing.Size(203, 24);
            this.menuSelectAll.Text = "Select all";
            this.menuSelectAll.Click += new System.EventHandler(this.menuSelectAll_Click);
            //
            // toolStripSeparator1
            //
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(200, 6);
            //
            // textHeaderApp
            //
            this.textHeaderApp.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.textHeaderApp.Name = "textHeaderApp";
            this.textHeaderApp.Size = new System.Drawing.Size(100, 16);
            this.textHeaderApp.Text = "Settings";
            //
            // menuOpenBackupFolder
            //
            this.menuOpenBackupFolder.Name = "menuOpenBackupFolder";
            this.menuOpenBackupFolder.Size = new System.Drawing.Size(203, 24);
            this.menuOpenBackupFolder.Text = "Open backups folder";
            this.menuOpenBackupFolder.Click += new System.EventHandler(this.menuOpenBackupFolder_Click);
            //
            // menuOpenAppBackups
            //
            this.menuOpenAppBackups.Name = "menuOpenAppBackups";
            this.menuOpenAppBackups.Size = new System.Drawing.Size(203, 24);
            this.menuOpenAppBackups.Text = "Open app backups";
            this.menuOpenAppBackups.Click += new System.EventHandler(this.menuOpenAppBackups_Click);
            //
            // ConfPageView
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.AutoScroll = true;
            this.BackColor = Appcopier.Ui.Surface;
            this.Controls.Add(this.root);
            this.Name = "ConfPageView";
            this.Size = new System.Drawing.Size(824, 759);
            this.contextMenu.ResumeLayout(false);
            this.contextMenu.PerformLayout();
            this.content.ResumeLayout(false);
            this.content.PerformLayout();
            this.actions.ResumeLayout(false);
            this.actions.PerformLayout();
            this.root.ResumeLayout(false);
            this.root.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.Label headerLabel;
        private System.Windows.Forms.LinkLabel linkSubHeader;
        private System.Windows.Forms.TreeView treeConfigurations;
        private System.Windows.Forms.TextBox txtInfo;
        private System.Windows.Forms.Button btnBackup;
        private System.Windows.Forms.Button btnMenuRestore;
        private System.Windows.Forms.Button btnMenuMore;
        private Views.RunResultsPanel resultsPanel;
        private System.Windows.Forms.LinkLabel logToggle;
        private System.Windows.Forms.RichTextBox rtbLog;
        private System.Windows.Forms.ToolTip tt;
        private System.Windows.Forms.ContextMenuStrip contextMenu;
        private System.Windows.Forms.ToolStripMenuItem menuSelectInstalled;
        private System.Windows.Forms.ToolStripMenuItem menuSelectAll;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripTextBox textHeaderApp;
        private System.Windows.Forms.ToolStripMenuItem menuOpenBackupFolder;
        private System.Windows.Forms.ToolStripMenuItem menuOpenAppBackups;
        private System.Windows.Forms.TableLayoutPanel root;
        private System.Windows.Forms.TableLayoutPanel content;
        private System.Windows.Forms.TableLayoutPanel actions;
    }
}
