using System;
using RSBot.Core;
using RSBot.Core.Components;
using SDUI.Controls;

namespace RSBot.Views;

public partial class ExitDialog : UIWindowBase
{
    public enum ClientExitMode
    {
        Graceful,
        Forced,
    }

    public ExitDialog()
    {
        InitializeComponent();
    }

    public ClientExitMode ExitMode => radioForced.Checked ? ClientExitMode.Forced : ClientExitMode.Graceful;

    private void ExitDialog_Load(object sender, EventArgs e)
    {
        LanguageManager.Translate(this, Kernel.Language);
    }
}
