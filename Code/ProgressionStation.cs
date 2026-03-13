using Sandbox;
using System.Linq;

/// <summary>
/// Physical station in the lobby where players can view and equip purge abilities.
/// Add to a GameObject with a trigger collider.
/// </summary>
public class ProgressionStation : Component, Component.ITriggerListener
{
    [Property] public float InteractRange { get; set; } = 150f;
    [Property] public SoundEvent OpenSound { get; set; }
    [Property] public SoundEvent EquipSound { get; set; }

    private bool playerNearby = false;
    private PlayerController nearbyPlayer = null;

    protected override void OnUpdate()
    {
        if ( playerNearby && nearbyPlayer != null && !nearbyPlayer.IsProxy )
        {
            // Play equip sound if triggered by UI
            if ( PurgeProgressionBridge.PlayEquipSound )
            {
                PurgeProgressionBridge.PlayEquipSound = false;
                if ( EquipSound != null )
                {
                    var handle = Sound.Play( EquipSound );
                    if ( handle != null )
                    {
                        handle.ListenLocal = true;
                        handle.Volume = 1.0f;
                    }
                }
            }
            
            // Show interact prompt
            Gizmo.Draw.Color = Color.Red;
            Gizmo.Draw.Text( "Press E — Anomaly Abilities", new Transform( WorldPosition + Vector3.Up * 50 ), "Consolas", 18 );

            // Don't allow interaction during a game
            var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
            if ( gameManager != null && gameManager.CurrentState != GameManager.GameState.WaitingInLobby )
                return;

            if ( Input.Pressed( "Use" ) )
            {
                if ( PurgeProgressionBridge.IsOpen )
                {
                    PurgeProgressionBridge.Close();
                }
                else
                {
                    OpenProgression();
                }
            }
        }
    }

    private void OpenProgression()
    {
        if ( OpenSound != null )
        {
            var handle = Sound.Play( OpenSound );
            if ( handle != null )
            {
                handle.ListenLocal = true;
                handle.Volume = 0.5f;
            }
        }
        
        // Get player's anomaly wins from the leaderboard cache
        int anomalyWins = 0;
        var bridgeData = PlayerBoardBridge.GetPlayerData();
        var localData = bridgeData.FirstOrDefault( p => p.IsLocal );
        if ( localData != null )
        {
            anomalyWins = localData.AnomalyWins;
        }

        // Get currently equipped ability (stored locally)
        string equippedId = PurgeProgressionBridge.EquippedAbilityId;

        PurgeProgressionBridge.Open( anomalyWins, equippedId );
        Log.Info( $"[ProgressionStation] Opened with {anomalyWins} anomaly wins, equipped: {equippedId}" );
    }

    void ITriggerListener.OnTriggerEnter( Collider other )
    {
        var player = other.GameObject.Components.Get<PlayerController>();
        if ( player != null && !player.IsProxy )
        {
            playerNearby = true;
            nearbyPlayer = player;
        }
    }

    void ITriggerListener.OnTriggerExit( Collider other )
    {
        var player = other.GameObject.Components.Get<PlayerController>();
        if ( player != null && player == nearbyPlayer )
        {
            playerNearby = false;
            nearbyPlayer = null;

            // Close the UI if it's open
            if ( PurgeProgressionBridge.IsOpen )
            {
                PurgeProgressionBridge.Close();
            }
        }
    }
}
