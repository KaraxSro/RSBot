using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Objects;

namespace RSBot.Training.Bundle.Protection;

internal class ProtectionBundle : IBundle
{
    public void Invoke()
    {
        if (Game.Player.InAction || SkillManager.CastPending)
            return;

        if (HealthRecovery.Active && HealthRecovery.Value > HealthRecovery.Current && !IsActive(HealthRecovery.SkillId))
            if ((Game.Player.BadEffect & BadEffect.Zombie) != BadEffect.Zombie) // is it need?
            {
                SkillManager.CastBuff(
                    Game.Player.Skills.GetSkillInfoById(HealthRecovery.SkillId),
                    awaitBuffResponse: false
                );
                return;
            }

        if (ManaRecovery.Active && ManaRecovery.Value > ManaRecovery.Current && !IsActive(ManaRecovery.SkillId))
        {
            SkillManager.CastBuff(Game.Player.Skills.GetSkillInfoById(ManaRecovery.SkillId), awaitBuffResponse: false);
            return;
        }

        if (BadStateRecovery.Active)
        {
            if (BadStateRecovery.IsUniversall && !IsActive(BadStateRecovery.SkillIdForUniversall))
            {
                SkillManager.CastBuff(
                    Game.Player.Skills.GetSkillInfoById(BadStateRecovery.SkillIdForUniversall),
                    awaitBuffResponse: false
                );
                return;
            }

            if (BadStateRecovery.IsPurification && !IsActive(BadStateRecovery.SkillIdForPurification))
                SkillManager.CastBuff(
                    Game.Player.Skills.GetSkillInfoById(BadStateRecovery.SkillIdForPurification),
                    awaitBuffResponse: false
                );
        }
    }

    private bool IsActive(uint skillId)
    {
        var skill = Game.Player.Skills.GetSkillInfoById(skillId);
        return Game.Player.State.HasActiveBuff(skill, out _);
    }

    public void Refresh()
    {
        //Nothing to do
    }

    public void Stop()
    {
        //Nothing to do
    }
}
