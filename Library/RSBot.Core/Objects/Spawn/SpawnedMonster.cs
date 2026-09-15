using RSBot.Core.Network;

namespace RSBot.Core.Objects.Spawn;

public sealed class SpawnedMonster : SpawnedNpc
{
    /// <summary>
    ///     <inheritdoc />
    /// </summary>
    /// <param name="objId">The ref obj id</param>
    public SpawnedMonster(uint objId)
        : base(objId) { }

    /// <summary>
    ///     Gets or sets the rarity.
    /// </summary>
    /// <value>
    ///     The rarity.
    /// </value>
    public MonsterRarity Rarity { get; set; }

    /// <summary>
    ///     Gets the maximum health.
    /// </summary>
    /// <value>
    ///     The maximum health.
    /// </value>
    public int MaxHealth
    {
        get
        {
            var baseHealth = Record.MaxHealth;
            switch (Rarity)
            {
                case MonsterRarity.Champion:
                    return baseHealth * 2;

                case MonsterRarity.ChampionParty:
                    return baseHealth * 20;

                case MonsterRarity.GeneralParty:
                    return baseHealth * 10;

                case MonsterRarity.Elite:
                    return baseHealth * 30;

                case MonsterRarity.EliteParty:
                    return baseHealth * 300;

                case MonsterRarity.Giant:
                    return baseHealth * 20;

                case MonsterRarity.GiantParty:
                    return baseHealth * 200;

                default:
                    return baseHealth;
            }
        }
    }

    /// <summary>
    ///     Deserialize from packet
    /// </summary>
    /// <param name="packet">The packet</param>
    internal override void Deserialize(Packet packet)
    {
        ParseBionicDetails(packet);

        base.Deserialize(packet);

        var protocolRarity = packet.ReadByte();

        if (Game.ClientType == GameClientType.Global)
        {
            // The current iSRO spawn layout retains the legacy 0x20 header for
            // regular monsters. The actual rarity follows the Global-only field:
            // uint unknown, three bytes unknown, rarity byte, uint unknown.
            packet.ReadUInt();
            packet.ReadByte();
            packet.ReadByte();
            packet.ReadByte();
            Rarity = ReadGlobalRarity(packet.ReadByte());
            packet.ReadUInt();
        }
        else
        {
            Rarity = (MonsterRarity)protocolRarity;

            if (Game.ClientType > GameClientType.Chinese && Game.ClientType != GameClientType.Japanese)
                packet.ReadUInt();
        }

        if (Record.IsEventMob)
            Rarity = MonsterRarity.Event;

        if (Record.TypeID4 == 2 || Record.TypeID4 == 3) //NPC_MOB_TIEF, NPC_MOB_HUNTER
            packet.ReadByte(); //Appeareance
    }

    private static MonsterRarity ReadGlobalRarity(byte protocolRarity)
    {
        // iSRO reports the original vSRO values in the Global-only trailing
        // rarity field. Restrict the accepted values to known monster rarities
        // so an unknown future field value cannot leak into skill dictionaries.
        return protocolRarity switch
        {
            (byte)MonsterRarity.General => MonsterRarity.General,
            (byte)MonsterRarity.Champion => MonsterRarity.Champion,
            (byte)MonsterRarity.Unique => MonsterRarity.Unique,
            (byte)MonsterRarity.Giant => MonsterRarity.Giant,
            (byte)MonsterRarity.Titan => MonsterRarity.Titan,
            (byte)MonsterRarity.Elite => MonsterRarity.Elite,
            (byte)MonsterRarity.EliteStrong => MonsterRarity.EliteStrong,
            (byte)MonsterRarity.Unique2 => MonsterRarity.Unique2,
            (byte)MonsterRarity.GeneralParty => MonsterRarity.GeneralParty,
            (byte)MonsterRarity.ChampionParty => MonsterRarity.ChampionParty,
            (byte)MonsterRarity.UniqueParty => MonsterRarity.UniqueParty,
            (byte)MonsterRarity.GiantParty => MonsterRarity.GiantParty,
            (byte)MonsterRarity.TitanParty => MonsterRarity.TitanParty,
            (byte)MonsterRarity.EliteParty => MonsterRarity.EliteParty,
            (byte)MonsterRarity.Unique2Party => MonsterRarity.Unique2Party,
            _ => MonsterRarity.General,
        };
    }
}
