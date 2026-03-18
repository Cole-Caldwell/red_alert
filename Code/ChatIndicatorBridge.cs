using Sandbox;
using System.Linq;

public static class ChatIndicatorBridge
{
    public static bool IsLocalPlayerTyping { get; set; } = false;

    public static void SetTyping( bool typing, Scene scene )
    {
        IsLocalPlayerTyping = typing;

        var localPlayer = scene.GetAllComponents<PlayerController>()
            .FirstOrDefault( p => !p.IsProxy && p.GameObject.Network.Owner != null );

        if ( localPlayer != null )
        {
            localPlayer.IsTyping = typing;
        }
    }
}