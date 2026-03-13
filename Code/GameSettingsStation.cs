using Sandbox;
using System.Linq;

public sealed class GameSettingsStation : Component, Component.ITriggerListener
{
    [Property] public SoundEvent OpenSound { get; set; }
    [Property] public SoundEvent ChangeSound { get; set; }
    
    public static bool PlayChangeSound { get; set; } = false;
    private bool playerNearby = false;
    private PlayerController nearbyPlayer = null;

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

            if ( GameSettingsBridge.IsOpen )
            {
                GameSettingsBridge.Close();
            }
        }
    }

    protected override void OnUpdate()
    {
        if ( playerNearby && nearbyPlayer != null && !nearbyPlayer.IsProxy )
        {
            // Update player count for the UI
            GameSettingsBridge.PlayerCount = Scene.GetAllComponents<PlayerController>()
                .Count( p => p.GameObject.Network.Owner != null );

            // Auto-reset anomaly count if not enough players
            if ( GameSettingsBridge.PlayerCount < 8 && GameSettingsBridge.AnomalyCount == 2 )
            {
                var gm = Scene.GetAllComponents<GameManager>().FirstOrDefault();
                if ( gm != null )
                {
                    gm.SetAnomalyCountRpc( 1 );
                }
            }
            
            if ( GameSettingsStation.PlayChangeSound )
            {
                GameSettingsStation.PlayChangeSound = false;
                if ( ChangeSound != null )
                {
                    var handle = Sound.Play( ChangeSound );
                    if ( handle != null )
                    {
                        handle.ListenLocal = true;
                        handle.Volume = 1.0f;
                    }
                }
            }

            Gizmo.Draw.Color = Color.Cyan;
            Gizmo.Draw.Text( "Press E - Game Settings", new Transform( WorldPosition + Vector3.Up * 50 ) );

            if ( Input.Pressed( "Use" ) )
            {
                if ( GameSettingsBridge.IsOpen )
                {
                    GameSettingsBridge.Close();
                }
                else
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

                    // Sync current anomaly count from GameManager
                    var gm = Scene.GetAllComponents<GameManager>().FirstOrDefault();
                    if ( gm != null )
                    {
                        GameSettingsBridge.AnomalyCount = gm.AnomalyCount;
                    }

                    GameSettingsBridge.Open();
                }
            }
        }
    }
}