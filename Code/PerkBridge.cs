using Sandbox;

public static class PerkBridge
{
    public static bool IsOpen { get; set; } = false;
    public static string EquippedPerkId { get; set; } = "";
    public static bool PerkUsedThisRound { get; set; } = false;
    public static int CachedBalance { get; set; } = 0;
    public static bool PlayEquipSound { get; set; } = false;

    public static void Open()
    {
        IsOpen = true;
    }

    public static void Close()
    {
        IsOpen = false;
    }

    public static void EquipPerk( string perkId )
    {
        EquippedPerkId = perkId;
    }

    public static void UnequipPerk()
    {
        EquippedPerkId = "";
    }

    public static void MarkPerkUsed()
    {
        PerkUsedThisRound = true;
    }

    public static void ResetForNewRound()
    {
        PerkUsedThisRound = false;
    }

    public static bool HasPerkEquipped()
    {
        return !string.IsNullOrEmpty( EquippedPerkId );
    }

    public static PerkData GetEquippedPerk()
    {
        if ( string.IsNullOrEmpty( EquippedPerkId ) ) return null;
        return PerkRegistry.GetById( EquippedPerkId );
    }

    // HUD state — updated by PlayerController each frame, read by PerkHudUI.razor
    public static bool IsPerkActive { get; set; } = false;
    public static float PerkTimeRemaining { get; set; } = 0f;
    public static string ActivePerkName { get; set; } = "";
}