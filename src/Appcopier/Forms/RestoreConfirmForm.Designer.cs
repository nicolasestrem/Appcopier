namespace Views
{
    partial class RestoreConfirmForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.txtPlan = new System.Windows.Forms.TextBox();
            this.pnlConsent = new System.Windows.Forms.FlowLayoutPanel();
            this.lblCaveat = new System.Windows.Forms.Label();
            this.btnRestore = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.SuspendLayout();
            //
            // txtPlan
            //
            this.txtPlan.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtPlan.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.txtPlan.Font = new System.Drawing.Font("Segoe UI Variable Display", 9.75F);
            this.txtPlan.Location = new System.Drawing.Point(24, 24);
            this.txtPlan.Multiline = true;
            this.txtPlan.Name = "txtPlan";
            this.txtPlan.ReadOnly = true;
            this.txtPlan.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtPlan.Size = new System.Drawing.Size(512, 268);
            this.txtPlan.TabIndex = 1;
            //
            // pnlConsent
            //
            this.pnlConsent.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.pnlConsent.AutoScroll = true;
            this.pnlConsent.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.pnlConsent.Location = new System.Drawing.Point(24, 302);
            this.pnlConsent.Name = "pnlConsent";
            this.pnlConsent.Size = new System.Drawing.Size(512, 116);
            this.pnlConsent.TabIndex = 2;
            this.pnlConsent.WrapContents = false;
            //
            // lblCaveat
            //
            this.lblCaveat.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lblCaveat.Font = new System.Drawing.Font("Segoe UI Variable Display", 9.75F, System.Drawing.FontStyle.Bold);
            this.lblCaveat.ForeColor = System.Drawing.Color.Magenta;
            this.lblCaveat.Location = new System.Drawing.Point(24, 426);
            this.lblCaveat.Name = "lblCaveat";
            this.lblCaveat.Size = new System.Drawing.Size(512, 72);
            this.lblCaveat.TabIndex = 3;
            //
            // btnRestore
            //
            this.btnRestore.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnRestore.AutoEllipsis = true;
            this.btnRestore.BackColor = System.Drawing.SystemColors.Control;
            this.btnRestore.FlatAppearance.BorderSize = 0;
            this.btnRestore.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRestore.Font = new System.Drawing.Font("Segoe UI Variable Display", 12F, System.Drawing.FontStyle.Bold);
            this.btnRestore.ForeColor = System.Drawing.Color.Magenta;
            this.btnRestore.Location = new System.Drawing.Point(24, 508);
            this.btnRestore.Name = "btnRestore";
            this.btnRestore.Padding = new System.Windows.Forms.Padding(0, 0, 5, 0);
            this.btnRestore.Size = new System.Drawing.Size(248, 34);
            this.btnRestore.TabIndex = 5;
            this.btnRestore.TabStop = false;
            this.btnRestore.Text = "Restore";
            this.btnRestore.UseVisualStyleBackColor = false;
            this.btnRestore.Click += new System.EventHandler(this.btnRestore_Click);
            //
            // btnCancel
            //
            this.btnCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnCancel.AutoEllipsis = true;
            this.btnCancel.BackColor = System.Drawing.SystemColors.Control;
            this.btnCancel.FlatAppearance.BorderSize = 0;
            this.btnCancel.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCancel.Font = new System.Drawing.Font("Segoe UI Variable Display", 12F, System.Drawing.FontStyle.Bold);
            this.btnCancel.ForeColor = System.Drawing.Color.Black;
            this.btnCancel.Location = new System.Drawing.Point(288, 508);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Padding = new System.Windows.Forms.Padding(0, 0, 5, 0);
            this.btnCancel.Size = new System.Drawing.Size(248, 34);
            this.btnCancel.TabIndex = 4;
            this.btnCancel.Text = "Cancel";
            this.btnCancel.UseVisualStyleBackColor = false;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            //
            // RestoreConfirmForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            // Cancel is BOTH the accept and the cancel button: Enter and Escape must land on the
            // harmless outcome, because this dialog is the last point at which a restore that
            // overwrites the machine can still be stopped.
            this.AcceptButton = this.btnCancel;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(560, 566);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnRestore);
            this.Controls.Add(this.lblCaveat);
            this.Controls.Add(this.pnlConsent);
            this.Controls.Add(this.txtPlan);
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.MinimumSize = new System.Drawing.Size(480, 480);
            this.Name = "RestoreConfirmForm";
            this.Opacity = 0.95D;
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Confirm restore";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.TextBox txtPlan;
        private System.Windows.Forms.FlowLayoutPanel pnlConsent;
        private System.Windows.Forms.Label lblCaveat;
        private System.Windows.Forms.Button btnRestore;
        private System.Windows.Forms.Button btnCancel;
    }
}
