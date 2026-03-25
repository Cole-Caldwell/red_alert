using System.Collections.Generic;
using System.Linq;

public enum PerkRole
{
    Universal,
    CitizenOnly,
    AnomalyOnly
}

public enum PerkTier
{
    Cheap,
    Mid,
    Expensive,
    Premium
}

public enum PerkActivation
{
    Active,
    Passive
}

public class PerkData
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int Cost { get; set; } = 0;
    public PerkTier Tier { get; set; } = PerkTier.Cheap;
    public PerkRole Role { get; set; } = PerkRole.Universal;
    public PerkActivation Activation { get; set; } = PerkActivation.Active;
    public string Icon { get; set; } = "";
}

public static class PerkRegistry
{
    private static List<PerkData> perks = new()
    {
        // === CHEAP (200) ===
        new PerkData
        {
            Id = "quick_fix",
            Name = "QUICK FIX",
            Description = "Instantly complete your current task. Skip the minigame entirely.",
            Cost = 200,
            Tier = PerkTier.Cheap,
            Role = PerkRole.CitizenOnly,
            Activation = PerkActivation.Active
        },
        new PerkData
        {
            Id = "iron_will",
            Name = "IRON WILL",
            Description = "Resist the next blackout effect. You stay unaffected while other citizens are blinded.",
            Cost = 200,
            Tier = PerkTier.Cheap,
            Role = PerkRole.CitizenOnly,
            Activation = PerkActivation.Passive
        },
        new PerkData
        {
            Id = "speed_boost",
            Name = "SPEED BOOST",
            Description = "Increased movement speed for 15 seconds. Move faster to chase or reposition.",
            Cost = 200,
            Tier = PerkTier.Cheap,
            Role = PerkRole.AnomalyOnly,
            Activation = PerkActivation.Active
        },
        new PerkData
        {
            Id = "quiet_steps",
            Name = "QUIET STEPS",
            Description = "Disable your footstep sounds for 30 seconds. Move silently without anyone hearing you.",
            Cost = 200,
            Tier = PerkTier.Cheap,
            Role = PerkRole.AnomalyOnly,
            Activation = PerkActivation.Active
        },

        // === MID (500) ===
        new PerkData
        {
            Id = "last_known",
            Name = "LAST KNOWN",
            Description = "See the last position of every player as a static marker for 5 seconds.",
            Cost = 500,
            Tier = PerkTier.Mid,
            Role = PerkRole.Universal,
            Activation = PerkActivation.Active
        },
        new PerkData
        {
            Id = "emergency_recall",
            Name = "EMERGENCY RECALL",
            Description = "Instantly teleport back to the emergency button. One-time escape from danger.",
            Cost = 500,
            Tier = PerkTier.Mid,
            Role = PerkRole.Universal,
            Activation = PerkActivation.Active
        },
        new PerkData
        {
            Id = "reveal",
            Name = "REVEAL",
            Description = "After a meeting starts, one random player's role is privately revealed to you.",
            Cost = 500,
            Tier = PerkTier.Mid,
            Role = PerkRole.CitizenOnly,
            Activation = PerkActivation.Passive
        },

        // === EXPENSIVE (1000) ===
        new PerkData
        {
            Id = "shield",
            Name = "SHIELD",
            Description = "Survive one kill attempt. The anomaly's kill fails and goes on cooldown.",
            Cost = 1000,
            Tier = PerkTier.Expensive,
            Role = PerkRole.CitizenOnly,
            Activation = PerkActivation.Passive
        },
        new PerkData
        {
            Id = "paranoia_immunity",
            Name = "PARANOIA IMMUNITY",
            Description = "All purge abilities have no effect on you for the entire round.",
            Cost = 1000,
            Tier = PerkTier.Expensive,
            Role = PerkRole.CitizenOnly,
            Activation = PerkActivation.Passive
        },

        // === PREMIUM (2000) ===
        new PerkData
        {
            Id = "tracker_tag",
            Name = "TRACKER TAG",
            Description = "Secretly tag one player at round start. See their outline through walls permanently.",
            Cost = 2000,
            Tier = PerkTier.Premium,
            Role = PerkRole.CitizenOnly,
            Activation = PerkActivation.Active
        },
        new PerkData
        {
            Id = "second_chance",
            Name = "SECOND CHANCE",
            Description = "If you get voted out, your role is NOT revealed. Creates massive doubt.",
            Cost = 2000,
            Tier = PerkTier.Premium,
            Role = PerkRole.Universal,
            Activation = PerkActivation.Passive
        },
    };

    public static List<PerkData> GetAll() => new List<PerkData>( perks );

    public static PerkData GetById( string id )
    {
        return perks.FirstOrDefault( p => p.Id == id );
    }

    public static List<PerkData> GetByRole( PerkRole role )
    {
        return perks.Where( p => p.Role == role ).OrderBy( p => p.Cost ).ToList();
    }

    public static List<PerkData> GetByTier( PerkTier tier )
    {
        return perks.Where( p => p.Tier == tier ).ToList();
    }

    public static string GetTierLabel( PerkTier tier )
    {
        switch ( tier )
        {
            case PerkTier.Cheap: return "STANDARD";
            case PerkTier.Mid: return "ADVANCED";
            case PerkTier.Expensive: return "ELITE";
            case PerkTier.Premium: return "LEGENDARY";
            default: return "UNKNOWN";
        }
    }

    public static string GetRoleLabel( PerkRole role )
    {
        switch ( role )
        {
            case PerkRole.Universal: return "UNIVERSAL";
            case PerkRole.CitizenOnly: return "CITIZEN";
            case PerkRole.AnomalyOnly: return "ANOMALY";
            default: return "UNKNOWN";
        }
    }
}