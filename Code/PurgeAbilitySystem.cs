using Sandbox;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Defines a purge ability that can be unlocked and equipped
/// </summary>
public class PurgeAbilityData
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "";
    public int WinsRequired { get; set; } = 0;
    public bool IsDefault { get; set; } = false;
    public float Duration { get; set; } = 10f;
}

/// <summary>
/// Static registry of all purge abilities
/// Add new abilities here as they are developed
/// </summary>
public static class PurgeAbilityRegistry
{
    private static List<PurgeAbilityData> abilities = new()
    {
        new PurgeAbilityData
        {
            Id = "blind",
            Name = "BLACKOUT",
            Description = "Blind all living citizens for a short duration. Citizens lose all vision and must navigate in darkness.",
            Icon = "ui/red-alert-blind_2.png",
            WinsRequired = 0,
            IsDefault = true,
            Duration = 10f
        },
        new PurgeAbilityData
        {
            Id = "doublekill",
            Name = "DOUBLE KILL",
            Description = "Reset your kill cooldown instantly. Use after a kill to strike again immediately.",
            Icon = "ui/red-alert-doublekill.png",
            WinsRequired = 5,
            Duration = 0f
        },
        new PurgeAbilityData
        {
            Id = "xray",
            Name = "X-RAY",
            Description = "See all living citizens through walls for a short duration. Coordinate your killings with ease.",
            Icon = "ui/red-alert-xray.png",
            WinsRequired = 10,
            Duration = 20f
        },
        new PurgeAbilityData
        {
            Id = "mimic",
            Name = "MIMIC",
            Description = "Transform into a random alive citizen for a short duration. Copy their exact appearance and nametag.",
            Icon = "ui/red-alert-clone.png",
            WinsRequired = 15,
            Duration = 20f
        }
    };

    public static List<PurgeAbilityData> GetAll() => new List<PurgeAbilityData>( abilities );

    public static PurgeAbilityData GetById( string id )
    {
        return abilities.FirstOrDefault( a => a.Id == id );
    }

    public static PurgeAbilityData GetDefault()
    {
        return abilities.FirstOrDefault( a => a.IsDefault );
    }

    public static List<PurgeAbilityData> GetUnlocked( int anomalyWins )
    {
        return abilities.Where( a => a.WinsRequired <= anomalyWins ).ToList();
    }
}

/// <summary>
/// Bridge for the progression UI to read player data
/// </summary>
public static class PurgeProgressionBridge
{
    public static bool IsOpen { get; set; } = false;
    public static int AnomalyWins { get; set; } = 0;
    public static string EquippedAbilityId { get; set; } = "blind";
    public static bool PlayEquipSound { get; set; } = false;

    public static void Open( int wins, string equippedId )
    {
        AnomalyWins = wins;
        EquippedAbilityId = equippedId;
        IsOpen = true;
    }

    public static void Close()
    {
        IsOpen = false;
    }

    public static void SetEquipped( string abilityId )
    {
        EquippedAbilityId = abilityId;
    }
}