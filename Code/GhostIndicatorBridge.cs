using Sandbox;
using System.Linq;

public static class GhostIndicatorBridge
{
    public static bool IsGhost { get; set; } = false;

    public static void Update( Scene scene )
    {
        var localPlayer = scene.GetAllComponents<PlayerController>()
            .FirstOrDefault( p => !p.IsProxy && p.GameObject.Network.Owner != null );

        IsGhost = localPlayer != null && localPlayer.IsInGame && (!localPlayer.IsAlive || localPlayer.IsSpectating);
    }
}