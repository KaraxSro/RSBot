using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using SDUI.Controls;

namespace RSBot.Views.Controls;

[ToolboxItem(false)]
public partial class MiniCosControl : DoubleBufferedControl
{
    private static readonly Size RegularSize = new(78, 104);
    private static readonly Size CompactSize = new(58, 78);
    private bool _selected;

    public MiniCosControl()
    {
        InitializeComponent();
    }

    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            panel.BorderColor = Color.Transparent;
        }
    }

    public void SetCompactMode(bool compact)
    {
        var targetSize = compact ? CompactSize : RegularSize;
        var iconHeight = compact ? 54 : 78;
        var barHeight = compact ? 5 : 6;

        SuspendLayout();
        panel.SuspendLayout();

        // Remove the designer constraints while resizing so WinForms cannot clip
        // the new dimensions, then lock the thumbnail to its selected layout.
        MinimumSize = Size.Empty;
        MaximumSize = Size.Empty;
        Size = targetSize;
        Icon.Height = iconHeight;
        Hp.Height = barHeight;
        Hgp.Height = barHeight;
        Satiety.Height = barHeight;
        Level.Font = new Font("Arial", compact ? 7F : 8.25F, FontStyle.Bold, GraphicsUnit.Point);
        Level.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        Level.Location = new Point(2, Math.Max(0, iconHeight - Level.Height - 2));
        MinimumSize = targetSize;
        MaximumSize = targetSize;

        panel.ResumeLayout(true);
        ResumeLayout(true);
    }

    private void OnClick_Redirector(object sender, EventArgs e)
    {
        OnClick(e);
    }
}
