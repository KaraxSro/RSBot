using System.Collections.Generic;
using System.Linq;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Event;
using RSBot.Core.Extensions;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Spawn;

namespace RSBot.Training.Bundle.Target;

internal class TargetBundle : IBundle
{
    private const int BLACKLIST_TIMEOUT = 5_000;

    #region Fields

    private Dictionary<uint, int> _blacklist;

    #endregion Fields

    #region Constructor

    public TargetBundle()
    {
        SubscribeEvents();
    }

    #endregion Constructor

    #region Events

    private void OnTargetBehindObstacle()
    {
        if (Game.SelectedEntity == null)
            return;

        var selectedEntityUniqueId = Game.SelectedEntity.UniqueId;
        Game.SelectedEntity?.TryDeselect();
        Game.SelectedEntity = null;

        Bundles.Movement.LastEntityWasBehindObstacle = true;

        if (_blacklist?.TryAdd(selectedEntityUniqueId, Kernel.TickCount) == true)
            Log.Debug($"Add mob [{selectedEntityUniqueId} to blacklist for {BLACKLIST_TIMEOUT}ms");
    }

    #endregion Events

    #region Methods

    private void SubscribeEvents()
    {
        EventManager.SubscribeEvent("OnTargetBehindObstacle", OnTargetBehindObstacle);
    }

    /// <summary>
    ///     Invokes this instance.
    /// </summary>
    public void Invoke()
    {
        _blacklist?.RemoveAll(
            (uniqueId, tick) =>
            {
                var flag = Kernel.TickCount - tick > BLACKLIST_TIMEOUT;
                if (flag)
                    Log.Debug($"Removed mob [{uniqueId} from blacklist!");

                return flag;
            }
        );

        var petAttacker = GetPetAttacker();
        if (petAttacker != null && Game.SelectedEntity?.UniqueId != petAttacker.UniqueId)
        {
            Log.Debug("[TargetBundle] Pet is under attack, switching target to defend it!");

            if (petAttacker.TrySelect())
                Bundles.Movement.LastEntityWasBehindObstacle = false;

            return;
        }

        var attacker = GetFromCurrentAttackers();
        if (attacker != null && Game.SelectedEntity == null)
        {
            Log.Debug("[TargetBundle] Selecting the weakest attacking mob first!");

            if (attacker.TrySelect())
                Bundles.Movement.LastEntityWasBehindObstacle = false;

            return;
        }

        if (
            attacker != null
            && SpawnManager.TryGetEntity<SpawnedMonster>(Game.SelectedEntity.UniqueId, out var selectedMonster)
            && GetMonsterStrengthPriority(attacker.Rarity) < GetMonsterStrengthPriority(selectedMonster.Rarity)
        )
        {
            Log.Debug("[TargetBundle] Found a weaker attacking mob, switching target!");

            if (attacker.TrySelect())
                Bundles.Movement.LastEntityWasBehindObstacle = false;

            return;
        }

        var warlockModeEnabled = PlayerConfig.Get("RSBot.Skills.checkWarlockMode", false);
        if (warlockModeEnabled && Game.SelectedEntity?.State.HasTwoDots() == true)
            return;

        if (Game.SelectedEntity != null && Game.SelectedEntity is not SpawnedMonster)
            Game.SelectedEntity = null;

        if (Game.SelectedEntity?.State.LifeState == LifeState.Alive)
            return;

        var monster = GetNearestEnemy();
        if (monster == null)
            return;

        if (!Container.Bot.Area.IsInSight(monster))
            return;

        if (monster.TrySelect())
            Bundles.Movement.LastEntityWasBehindObstacle = false;
    }

    private SpawnedMonster GetFromCurrentAttackers()
    {
        var attackWeakerFirst = PlayerConfig.Get<bool>("RSBot.Training.checkAttackWeakerFirst");
        if (!attackWeakerFirst)
            return null;

        if (
            !SpawnManager.TryGetEntities<SpawnedMonster>(
                e => e.AttackingPlayer && e.State.LifeState == LifeState.Alive,
                out var entities
            )
        )
            return null;

        return entities
            .OrderBy(e => GetMonsterStrengthPriority(e.Rarity))
            .ThenBy(e => e.Record.Level)
            .ThenBy(e => e.Position.DistanceToPlayer())
            .FirstOrDefault();
    }

    private SpawnedMonster GetPetAttacker()
    {
        if (!PlayerConfig.Get<bool>("RSBot.Training.checkDefendPetFirst"))
            return null;

        var growthId = Game.Player.Growth?.UniqueId ?? 0;
        var fellowId = Game.Player.Fellow?.UniqueId ?? 0;
        if (growthId == 0 && fellowId == 0)
            return null;

        if (
            !SpawnManager.TryGetEntities<SpawnedMonster>(
                monster =>
                    monster.State.LifeState == LifeState.Alive
                    && (
                        (growthId != 0 && monster.TargetId == growthId)
                        || (fellowId != 0 && monster.TargetId == fellowId)
                    ),
                out var attackers
            )
        )
            return null;

        return attackers
            .OrderBy(monster => GetMonsterStrengthPriority(monster.Rarity))
            .ThenBy(monster => monster.Record.Level)
            .ThenBy(monster => monster.DistanceToPlayer)
            .FirstOrDefault();
    }

    private static int GetMonsterStrengthPriority(MonsterRarity rarity)
    {
        return rarity switch
        {
            MonsterRarity.General or MonsterRarity.GeneralParty => 0,
            MonsterRarity.Champion or MonsterRarity.ChampionParty => 1,
            MonsterRarity.Giant or MonsterRarity.GiantParty => 2,
            MonsterRarity.Elite or MonsterRarity.EliteParty => 3,
            MonsterRarity.EliteStrong => 4,
            MonsterRarity.Unique
            or MonsterRarity.Unique2
            or MonsterRarity.UniqueParty
            or MonsterRarity.Unique2Party => 5,
            MonsterRarity.Titan or MonsterRarity.TitanParty => 6,
            MonsterRarity.Event => 7,
            _ => 8,
        };
    }

    /// <summary>
    ///     Gets the nearest enemy.
    /// </summary>
    /// <returns></returns>
    private SpawnedMonster GetNearestEnemy()
    {
        var warlockModeEnabled = PlayerConfig.Get<bool>("RSBot.Skills.checkWarlockMode");
        var ignorePillar = PlayerConfig.Get<bool>("RSBot.Training.checkBoxDimensionPillar");

        if (
            !SpawnManager.TryGetEntities<SpawnedMonster>(
                m =>
                    m.State.LifeState == LifeState.Alive
                    && //Only alive
                    !(warlockModeEnabled && m.State.HasTwoDots())
                    && //Has two Dots?
                    (_blacklist == null || !_blacklist.ContainsKey(m.UniqueId))
                    && //Is not blacklisted
                    (m.AttackingPlayer || !Bundles.Avoidance.AvoidMonster(m.Rarity))
                    && //Is attacking player or shouldn't be avoided
                    Container.Bot.Area.IsInSight(m)
                    && //Is in training area
                    !m.Record.IsPandora
                    && //Isn't pandora box
                    !(m.Record.IsDimensionPillar && ignorePillar)
                    && //Isn't dimension pillar
                    !m.Record.IsSummonFlower,
                out var entities
            )
        )
            return default;

        return entities
            .OrderByDescending(m => m.AttackingPlayer)
            .ThenByDescending(m => Bundles.Avoidance.PreferMonster(m.Rarity))
            .ThenBy(m => m.Movement.Source.DistanceTo(Game.Player.Movement.Source))
            .FirstOrDefault(m => !m.IsBehindObstacle);
    }

    /// <summary>
    ///     Refreshes this instance.
    /// </summary>
    public void Refresh()
    {
        _blacklist = new Dictionary<uint, int>(8);
    }

    public void Stop()
    {
        _blacklist = null;
    }

    #endregion Methods
}
