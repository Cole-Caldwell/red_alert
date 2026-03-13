using Sandbox;
using System.Collections.Generic;
using System.Linq;

public static class AnomalyTeamBridge
{
    public static bool IsVisible { get; set; } = false;
    public static List<AnomalyInfo> Anomalies { get; set; } = new();

    public class AnomalyInfo
    {
        public string Name { get; set; } = "";
        public bool IsAlive { get; set; } = true;
        public bool IsLocal { get; set; } = false;
    }

    public static void Update( Scene scene )
    {
        var localPlayer = scene.GetAllComponents<PlayerController>()
            .FirstOrDefault( p => !p.IsProxy && p.GameObject.Network.Owner != null );

        if ( localPlayer == null || localPlayer.Role != PlayerController.PlayerRole.Anomaly || !localPlayer.IsInGame )
        {
            IsVisible = false;
            Anomalies.Clear();
            return;
        }

        IsVisible = true;
        Anomalies.Clear();

        var allAnomalies = scene.GetAllComponents<PlayerController>()
            .Where( p => p.Role == PlayerController.PlayerRole.Anomaly && p.IsInGame )
            .ToList();

        foreach ( var a in allAnomalies )
        {
            string name = a.GameObject.Root.Name.Replace( "Player - ", "" );
            if ( string.IsNullOrEmpty( name ) || name == a.GameObject.Root.Name )
                name = a.PlayerName;

            Anomalies.Add( new AnomalyInfo
            {
                Name = name,
                IsAlive = a.IsAlive,
                IsLocal = !a.IsProxy
            } );
        }
    }
}