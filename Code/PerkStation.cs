using Sandbox;
using System.Linq;

public class PerkStation : Component, Component.ITriggerListener
{
    [Property] public SoundEvent OpenSound { get; set; }
    [Property] public SoundEvent CloseSound { get; set; }
    [Property] public SoundEvent EquipSound { get; set; }

    private bool playerInRange = false;

    protected override void OnUpdate()
    {
        // Close perk shop if local player disconnected or no longer exists
        if ( PerkBridge.IsOpen )
        {
            var localCheck = Scene.GetAllComponents<PlayerController>()
                .FirstOrDefault( p => !p.IsProxy && p.GameObject.Network.Owner != null );
            if ( localCheck == null )
            {
                PerkBridge.Close();
                playerInRange = false;
                return;
            }
        }

        if ( !playerInRange ) return;

        var localPlayer = Scene.GetAllComponents<PlayerController>()
            .FirstOrDefault( p => !p.IsProxy && p.GameObject.Network.Owner != null );

        if ( localPlayer == null ) return;

        // Don't allow during game
        var gm = Scene.GetAllComponents<GameManager>().FirstOrDefault();
        if ( gm != null && gm.CurrentState != GameManager.GameState.WaitingInLobby )
            return;

        if ( Input.Pressed( "Use" ) )
        {
            if ( PerkBridge.IsOpen )
            {
                PerkBridge.Close();
                if ( CloseSound != null )
                {
                    var handle = Sound.Play( CloseSound );
                    if ( handle != null ) handle.ListenLocal = true;
                }
            }
            else
            {
                // Fetch balance before opening
                FetchBalance();
                PerkBridge.Open();
                if ( OpenSound != null )
                {
                    var handle = Sound.Play( OpenSound );
                    if ( handle != null ) 
                    {
                        handle.ListenLocal = true;
                        handle.Volume = 0.5f;
                    }
                }
            }
        }

        // Handle equip sound
        if ( PerkBridge.PlayEquipSound && EquipSound != null )
        {
            var handle = Sound.Play( EquipSound );
            if ( handle != null ) handle.ListenLocal = true;
            PerkBridge.PlayEquipSound = false;
        }
    }

    private async void FetchBalance()
    {
        try
        {
            var creditsBoard = Sandbox.Services.Leaderboards.GetFromStat( Game.Ident, "credits" );
            creditsBoard.MaxEntries = 50;
            await creditsBoard.Refresh();

            var spentBoard = Sandbox.Services.Leaderboards.GetFromStat( Game.Ident, "credits_spent" );
            spentBoard.MaxEntries = 50;
            await spentBoard.Refresh();

            long localSteamId = (long)(Connection.Local?.SteamId ?? 0);
            int earned = 0;
            int spent = 0;

            foreach ( var entry in creditsBoard.Entries )
            {
                if ( entry.SteamId == localSteamId )
                {
                    earned = (int)entry.Value;
                    break;
                }
            }

            foreach ( var entry in spentBoard.Entries )
            {
                if ( entry.SteamId == localSteamId )
                {
                    spent = (int)entry.Value;
                    break;
                }
            }

            PerkBridge.CachedBalance = earned - spent;
            Log.Info( $"[PerkStation] Balance: {PerkBridge.CachedBalance} (earned: {earned}, spent: {spent})" );
        }
        catch ( System.Exception e )
        {
            Log.Warning( $"[PerkStation] Failed to fetch balance: {e.Message}" );
        }
    }

    void ITriggerListener.OnTriggerEnter( Collider other )
    {
        var player = other.GameObject.Components.Get<PlayerController>();
        if ( player != null && !player.IsProxy )
        {
            playerInRange = true;
        }
    }

    void ITriggerListener.OnTriggerExit( Collider other )
    {
        var player = other.GameObject.Components.Get<PlayerController>();
        if ( player != null && !player.IsProxy )
        {
            playerInRange = false;
            if ( PerkBridge.IsOpen )
            {
                PerkBridge.Close();
                if ( CloseSound != null )
                {
                    var handle = Sound.Play( CloseSound );
                    if ( handle != null ) handle.ListenLocal = true;
                }
            }
        }
    }
}