namespace RSBot.Views
{
    partial class ExitDialog
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
            labelInfo = new SDUI.Controls.Label();
            panel1 = new SDUI.Controls.Panel();
            btnNo = new SDUI.Controls.Button();
            btnYes = new SDUI.Controls.Button();
            radioGraceful = new System.Windows.Forms.RadioButton();
            radioForced = new System.Windows.Forms.RadioButton();
            pictureBox1 = new System.Windows.Forms.PictureBox();
            panel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).BeginInit();
            SuspendLayout();
            // 
            // labelInfo
            // 
            labelInfo.ApplyGradient = false;
            labelInfo.AutoSize = true;
            labelInfo.ForeColor = System.Drawing.Color.FromArgb(0, 0, 0);
            labelInfo.Gradient = new System.Drawing.Color[]
    {
    System.Drawing.Color.Gray,
    System.Drawing.Color.Black
    };
            labelInfo.GradientAnimation = false;
            labelInfo.Location = new System.Drawing.Point(145, 22);
            labelInfo.Name = "labelInfo";
            labelInfo.Size = new System.Drawing.Size(302, 30);
            labelInfo.TabIndex = 0;
            labelInfo.Text = "Are you sure you want to close RSBot and the game client?\r\nChoose how the client should be closed:";
            // 
            // panel1
            // 
            panel1.Border = new System.Windows.Forms.Padding(0, 1, 0, 0);
            panel1.BorderColor = System.Drawing.Color.Transparent;
            panel1.Controls.Add(btnNo);
            panel1.Controls.Add(btnYes);
            panel1.Dock = System.Windows.Forms.DockStyle.Bottom;
            panel1.Location = new System.Drawing.Point(0, 161);
            panel1.Name = "panel1";
            panel1.Radius = 0;
            panel1.ShadowDepth = 0F;
            panel1.Size = new System.Drawing.Size(480, 49);
            panel1.TabIndex = 1;
            // 
            // btnNo
            // 
            btnNo.Color = System.Drawing.Color.Transparent;
            btnNo.DialogResult = System.Windows.Forms.DialogResult.No;
            btnNo.Location = new System.Drawing.Point(389, 14);
            btnNo.Name = "btnNo";
            btnNo.Radius = 2;
            btnNo.ShadowDepth = 4F;
            btnNo.Size = new System.Drawing.Size(75, 23);
            btnNo.TabIndex = 1;
            btnNo.Text = "No";
            btnNo.UseVisualStyleBackColor = true;
            // 
            // btnYes
            // 
            btnYes.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            btnYes.Color = System.Drawing.Color.Transparent;
            btnYes.DialogResult = System.Windows.Forms.DialogResult.Yes;
            btnYes.Location = new System.Drawing.Point(308, 14);
            btnYes.Name = "btnYes";
            btnYes.Radius = 2;
            btnYes.ShadowDepth = 4F;
            btnYes.Size = new System.Drawing.Size(75, 23);
            btnYes.TabIndex = 0;
            btnYes.Text = "Yes";
            btnYes.UseVisualStyleBackColor = true;
            //
            // radioGraceful
            //
            radioGraceful.AutoSize = true;
            radioGraceful.Checked = true;
            radioGraceful.Location = new System.Drawing.Point(145, 72);
            radioGraceful.Name = "radioGraceful";
            radioGraceful.Size = new System.Drawing.Size(279, 19);
            radioGraceful.TabIndex = 1;
            radioGraceful.TabStop = true;
            radioGraceful.Text = "Graceful (logout first, force close after 7 seconds)";
            radioGraceful.UseVisualStyleBackColor = true;
            //
            // radioForced
            //
            radioForced.AutoSize = true;
            radioForced.Location = new System.Drawing.Point(145, 104);
            radioForced.Name = "radioForced";
            radioForced.Size = new System.Drawing.Size(201, 19);
            radioForced.TabIndex = 2;
            radioForced.Text = "Forced (close client immediately)";
            radioForced.UseVisualStyleBackColor = true;
            // 
            // pictureBox1
            // 
            pictureBox1.Image = Properties.Resources.app;
            pictureBox1.Location = new System.Drawing.Point(15, 5);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.Size = new System.Drawing.Size(111, 110);
            pictureBox1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            pictureBox1.TabIndex = 2;
            pictureBox1.TabStop = false;
            // 
            // ExitDialog
            // 
            AcceptButton = btnYes;
            AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            CancelButton = btnNo;
            ClientSize = new System.Drawing.Size(480, 210);
            Controls.Add(labelInfo);
            Controls.Add(radioForced);
            Controls.Add(radioGraceful);
            Controls.Add(pictureBox1);
            Controls.Add(panel1);
            ForeColor = System.Drawing.Color.Black;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "ExitDialog";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            TopMost = true;
            Load += ExitDialog_Load;
            panel1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)pictureBox1).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private SDUI.Controls.Label labelInfo;
        private SDUI.Controls.Panel panel1;
        private System.Windows.Forms.PictureBox pictureBox1;
        private SDUI.Controls.Button btnNo;
        private SDUI.Controls.Button btnYes;
        private System.Windows.Forms.RadioButton radioGraceful;
        private System.Windows.Forms.RadioButton radioForced;
    }
}
