using System.Windows.Forms;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Event;
using RSBot.Core.Objects;
using RSBot.Core.Plugins;
using RSBot.MagicPop.Validation;

namespace RSBot.MagicPop;

public sealed class Bootstrap : IBotbase
{
    private readonly Area _area = new();
    private bool _initialized;

    public static bool IsActive => Kernel.Bot.Running && Kernel.Bot.Botbase?.Name == "RSBot.MagicPop";

    public string Author => "RSBot Team";

    public string Description => "Automates Magic POP card purchasing, rolling and losing-coupon cleanup.";

    public string Name => "RSBot.MagicPop";

    public string Title => "Magic POP";

    public string Version => "1.0.0";

    public bool Enabled { get; set; }

    public Area Area => _area;

    public Control View => Container.View;

    public void Initialize()
    {
        if (_initialized)
            return;

        EventManager.SubscribeEvent("OnLoadGameData", OnLoadGameData);
        EventManager.SubscribeEvent("OnLoadCharacter", OnLoadCharacter);
        EventManager.SubscribeEvent("OnAgentServerDisconnected", OnAgentServerDisconnected);
        EventManager.SubscribeEvent("OnTeleportStart", OnTeleportStart);
        EventManager.SubscribeEvent("OnTeleportComplete", OnTeleportComplete);
        _initialized = true;
        Container.View.AppendDiagnostic("Magic POP botbase initialized.");
        LoadReferencesIfAvailable();
        Log.Debug("[Magic POP] Botbase registered to the kernel.");
    }

    public void Start()
    {
        if (!Container.References.IsValid)
        {
            var reason = Container.References.IsLoaded
                ? Container.References.Error
                : "Magic POP reference data has not been loaded yet.";
            Log.Error($"[Magic POP] Start blocked. {reason}");
            Container.View.AppendDiagnostic($"Start blocked: {reason}");
            Container.View.BindReferenceCatalog(Container.References);
            Kernel.Bot.Stop();
            return;
        }

        var targets = Container.View.GetSelectedTargetsSnapshot();
        if (!string.IsNullOrWhiteSpace(Container.View.TargetConfigurationError))
        {
            Log.Error($"[Magic POP] Start blocked. {Container.View.TargetConfigurationError}");
            Container.View.AppendDiagnostic($"Start blocked: {Container.View.TargetConfigurationError}");
            Container.View.SetStatus("target configuration is stale");
            Kernel.Bot.Stop();
            return;
        }

        var validation = MagicPopPrerequisiteValidator.Validate(Container.References, targets);
        foreach (var information in validation.Information)
        {
            Log.Notify($"[Magic POP] {information}");
            Container.View.AppendDiagnostic(information);
        }

        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors)
            {
                Log.Error($"[Magic POP] Start blocked. {error}");
                Container.View.AppendDiagnostic($"Start blocked: {error}");
            }

            Container.View.SetStatus("validation failed");
            Kernel.Bot.Stop();
            return;
        }

        Container.Bot.Start(targets);
    }

    public void Stop()
    {
        Container.Bot.Stop();
        Container.View.SetRunning(false);
        Log.Notify("[Magic POP] Botbase stopped.");
    }

    public void Tick()
    {
        Container.Bot.Tick();
    }

    public void Translate()
    {
        LanguageManager.Translate(View, Kernel.Language);
    }

    public void Enable()
    {
        if (View != null)
            View.Enabled = true;
    }

    public void Disable()
    {
        if (View != null)
            View.Enabled = false;
    }

    private static void OnLoadGameData()
    {
        Container.AppendDiagnostic("Game-data load event received; loading Magic POP references.");
        Container.References.Load();
        AppendReferenceResult();
        Container.UpdateReferenceStatus();
    }

    private static void OnLoadCharacter()
    {
        if (IsActive && !Container.Bot.IsReturningToHotan)
        {
            Log.Warn("[Magic POP] Character state changed while running; stopping to discard in-flight operations safely.");
            Kernel.Bot.Stop();
        }

        LoadReferencesIfAvailable();
    }

    private static void OnAgentServerDisconnected()
    {
        Container.SilkBalance.Reset();
        if (IsActive)
            Kernel.Bot.Stop();
    }

    private static void OnTeleportStart()
    {
        if (!IsActive)
            return;

        if (Container.Bot.HandleTeleportStart())
            return;

        Log.Warn("[Magic POP] Teleport started while running; stopping to discard in-flight operations safely.");
        Kernel.Bot.Stop();
    }

    private static void OnTeleportComplete()
    {
        if (IsActive)
            Container.Bot.HandleTeleportComplete();
    }

    private static void LoadReferencesIfAvailable()
    {
        var itemCount = Game.ReferenceManager?.ItemData?.Count ?? 0;
        var shopGoodCount = Game.ReferenceManager?.ShopGoods?.Count ?? 0;
        if ((!Container.References.IsLoaded || !Container.References.IsValid)
            && Game.MediaPk2 != null
            && itemCount > 0
            && shopGoodCount > 0)
        {
            Container.AppendDiagnostic($"Loading references (items={itemCount}, shop goods={shopGoodCount}).");
            Container.References.Load();
            AppendReferenceResult();
        }
        else if (!Container.References.IsLoaded)
            Container.AppendDiagnostic($"Waiting for game data (PK2={(Game.MediaPk2 == null ? "missing" : "ready")}, items={itemCount}, shop goods={shopGoodCount}).");

        Container.UpdateReferenceStatus();
    }

    private static void AppendReferenceResult()
    {
        if (!string.IsNullOrWhiteSpace(Container.References.LoadDetails))
            Container.AppendDiagnostic(Container.References.LoadDetails);

        if (Container.References.IsValid)
            Container.AppendDiagnostic($"Reference catalog ready: {Container.References.Rewards.Count} reward(s).");
        else
            Container.AppendDiagnostic($"Reference load failed: {Container.References.Error ?? "unknown error"}");
    }
}
