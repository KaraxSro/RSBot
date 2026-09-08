using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using SDUI.Controls;
using Label = SDUI.Controls.Label;
using ProgressBar = SDUI.Controls.ProgressBar;

namespace RSBot.Views.Controls;

[ToolboxItem(false)]
public class CosControlBase : DoubleBufferedControl
{
    private Label label1;
    protected Label labelLevel;
    protected Label lblPetName;
    private Panel panel1;
    protected ProgressBar progressHP;

    public CosControlBase()
    {
        BackColor = Color.Transparent;
        MiniCosControl = new MiniCosControl();
        MiniCosControl.Dock = System.Windows.Forms.DockStyle.Left;
        InitializeComponent();
    }

    public MiniCosControl MiniCosControl { get; }

    public virtual void Initialize() { }

    public virtual void Reset()
    {
        progressHP.Value = 0;
        MiniCosControl.Hp.Value = 0;

        progressHP.Maximum = 0;
        MiniCosControl.Hp.Maximum = 0;
    }

    private void InitializeComponent()
    {
        label1 = new Label();
        lblPetName = new Label();
        progressHP = new ProgressBar();
        panel1 = new Panel();
        labelLevel = new Label();
        panel1.SuspendLayout();
        SuspendLayout();
        // 
        // label1
        // 
        label1.ApplyGradient = false;
        label1.AutoSize = true;
        label1.ForeColor = Color.FromArgb(0, 0, 0);
        label1.Gradient = new Color[]
{
    Color.Gray,
    Color.Black
};
        label1.GradientAnimation = false;
        label1.Location = new Point(12, 51);
        label1.Name = "label1";
        label1.Size = new Size(31, 20);
        label1.TabIndex = 20;
        label1.Text = "HP:";
        // 
        // lblPetName
        // 
        lblPetName.ApplyGradient = false;
        lblPetName.AutoSize = true;
        lblPetName.Dock = System.Windows.Forms.DockStyle.Left;
        lblPetName.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        lblPetName.ForeColor = Color.FromArgb(0, 0, 0);
        lblPetName.Gradient = new Color[]
{
    Color.Gray,
    Color.Black
};
        lblPetName.GradientAnimation = false;
        lblPetName.Location = new Point(0, 0);
        lblPetName.Name = "lblPetName";
        lblPetName.Size = new Size(103, 20);
        lblPetName.TabIndex = 19;
        lblPetName.Text = "No pet found";
        // 
        // progressHP
        // 
        progressHP.BackColor = Color.Transparent;
        progressHP.DrawHatch = false;
        progressHP.ForeColor = Color.Firebrick;
        progressHP.Gradient = new Color[]
{
    Color.Maroon,
    Color.Red
};
        progressHP.HatchType = HatchStyle.Percent10;
        progressHP.Anchor = System.Windows.Forms.AnchorStyles.Top
            | System.Windows.Forms.AnchorStyles.Left
            | System.Windows.Forms.AnchorStyles.Right;
        progressHP.Location = new Point(60, 50);
        progressHP.Maximum = 100L;
        progressHP.MaxPercentShowValue = 100F;
        progressHP.Name = "progressHP";
        progressHP.PercentIndices = 2;
        progressHP.Radius = 1;
        progressHP.ShowAsPercent = false;
        progressHP.ShowValue = true;
        progressHP.Size = new Size(225, 22);
        progressHP.TabIndex = 18;
        progressHP.Text = "0 / 100";
        progressHP.Value = 0L;
        // 
        // panel1
        // 
        panel1.BackColor = Color.Transparent;
        panel1.Border = new System.Windows.Forms.Padding(0, 0, 0, 0);
        panel1.BorderColor = Color.Transparent;
        panel1.Controls.Add(labelLevel);
        panel1.Controls.Add(lblPetName);
        panel1.Anchor = System.Windows.Forms.AnchorStyles.Top
            | System.Windows.Forms.AnchorStyles.Left
            | System.Windows.Forms.AnchorStyles.Right;
        panel1.Location = new Point(60, 14);
        panel1.Name = "panel1";
        panel1.Radius = 10;
        panel1.ShadowDepth = 4F;
        panel1.Size = new Size(225, 26);
        panel1.TabIndex = 21;
        // 
        // labelLevel
        // 
        labelLevel.ApplyGradient = false;
        labelLevel.AutoSize = true;
        labelLevel.Dock = System.Windows.Forms.DockStyle.Left;
        labelLevel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        labelLevel.ForeColor = Color.FromArgb(0, 0, 0);
        labelLevel.Gradient = new Color[]
{
    Color.Gray,
    Color.Black
};
        labelLevel.GradientAnimation = false;
        labelLevel.Location = new Point(103, 0);
        labelLevel.Name = "labelLevel";
        labelLevel.Size = new Size(0, 20);
        labelLevel.TabIndex = 20;
        // 
        // CosControlBase
        // 
        Controls.Add(panel1);
        Controls.Add(label1);
        Controls.Add(progressHP);
        Name = "CosControlBase";
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
        Size = new Size(302, 84);
        panel1.ResumeLayout(false);
        panel1.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    /// <summary>
    ///     Arranges the pet name and status bars in DPI-safe rows.
    /// </summary>
    protected void ArrangeStats(
        params (System.Windows.Forms.Control Label, System.Windows.Forms.Control Value)[] extraRows
    )
    {
        SuspendLayout();

        var layout = new System.Windows.Forms.TableLayoutPanel
        {
            BackColor = Color.Transparent,
            ColumnCount = 2,
            Dock = System.Windows.Forms.DockStyle.Top,
            Padding = new System.Windows.Forms.Padding(8),
            RowCount = extraRows.Length + 2,
        };
        layout.ColumnStyles.Add(
            new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 52F)
        );
        layout.ColumnStyles.Add(
            new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F)
        );
        layout.RowStyles.Add(
            new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 34F)
        );
        for (var i = 1; i < layout.RowCount; i++)
            layout.RowStyles.Add(
                new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 32F)
            );

        panel1.Dock = System.Windows.Forms.DockStyle.Fill;
        panel1.Margin = new System.Windows.Forms.Padding(52, 3, 4, 5);
        layout.Controls.Add(panel1, 0, 0);
        layout.SetColumnSpan(panel1, 2);

        AddStatRow(layout, label1, progressHP, 1);
        for (var i = 0; i < extraRows.Length; i++)
            AddStatRow(layout, extraRows[i].Label, extraRows[i].Value, i + 2);

        var layoutHeight = layout.Padding.Vertical + 34 + 32 * (extraRows.Length + 1);
        layout.Height = layoutHeight;
        Controls.Add(layout);
        layout.BringToFront();

        MinimumSize = new Size(0, layoutHeight);
        MaximumSize = new Size(0, layoutHeight);
        Height = layoutHeight;

        ResumeLayout(true);
    }

    private static void AddStatRow(
        System.Windows.Forms.TableLayoutPanel layout,
        System.Windows.Forms.Control label,
        System.Windows.Forms.Control value,
        int row
    )
    {
        label.AutoSize = false;
        label.Dock = System.Windows.Forms.DockStyle.Fill;
        label.Margin = new System.Windows.Forms.Padding(0, 3, 4, 3);
        if (label is System.Windows.Forms.Label windowsLabel)
            windowsLabel.TextAlign = ContentAlignment.MiddleLeft;

        value.Dock = System.Windows.Forms.DockStyle.Fill;
        value.Margin = new System.Windows.Forms.Padding(0, 5, 4, 5);

        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(value, 1, row);
    }
}
