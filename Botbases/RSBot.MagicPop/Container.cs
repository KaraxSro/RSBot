using RSBot.MagicPop.Views;
using RSBot.MagicPop.References;
using RSBot.MagicPop.Protocol;
using RSBot.MagicPop.Bot;

namespace RSBot.MagicPop;

internal static class Container
{
    private static Main _view;

    public static GachaReferenceCatalog References { get; } = new();
    public static SilkBalanceTracker SilkBalance { get; } = new();
    public static MagicPopBot Bot { get; } = new();

    public static Main View
    {
        get
        {
            if (_view == null || _view.IsDisposed || _view.Disposing)
            {
                _view = new Main();
                _view.BindReferenceCatalog(References);
            }

            return _view;
        }
    }

    public static void UpdateReferenceStatus()
    {
        if (_view != null && !_view.IsDisposed && !_view.Disposing)
            _view.BindReferenceCatalog(References);
    }

    public static void AppendDiagnostic(string message)
    {
        if (_view != null && !_view.IsDisposed && !_view.Disposing)
            _view.AppendDiagnostic(message);
    }
}
