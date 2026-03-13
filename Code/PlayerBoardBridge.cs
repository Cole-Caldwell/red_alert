using Sandbox;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public static class PlayerBoardBridge
{
    public class PlayerData
    {
        public string Name { get; set; } = "";
        public int CitizenWins { get; set; } = 0;
        public int AnomalyWins { get; set; } = 0;
        public int TotalWins => CitizenWins + AnomalyWins;
        public bool IsLocal { get; set; } = false;
        public Connection Connection { get; set; }
        public ulong SteamId { get; set; } = 0;
    }
    
    private static List<PlayerData> cachedPlayers = new List<PlayerData>();
    
    // Cache for fetched stats so we don't spam the API
    private static Dictionary<ulong, (int CitizenWins, int AnomalyWins, float FetchTime)> statsCache = new();
    private static float cacheDuration = 30f; // Refresh stats every 30 seconds
    
    public static void UpdatePlayerData( List<PlayerData> players )
    {
        cachedPlayers = new List<PlayerData>( players );
    }
    
    public static List<PlayerData> GetPlayerData()
    {
        return new List<PlayerData>( cachedPlayers );
    }
    
    public static PlayerData CreatePlayerData( Connection conn, PlayerController controller )
    {
        var data = new PlayerData
        {
            Name = conn.DisplayName,
            CitizenWins = 0,
            AnomalyWins = 0,
            IsLocal = conn == Connection.Local,
            Connection = conn,
            SteamId = conn.SteamId
        };
        
        // Check cache first
        if ( statsCache.TryGetValue( conn.SteamId, out var cached ) && (Time.Now - cached.FetchTime) < cacheDuration )
        {
            data.CitizenWins = cached.CitizenWins;
            data.AnomalyWins = cached.AnomalyWins;
        }
        
        return data;
    }
    
    public static async void FetchAllPlayerStats( List<PlayerData> players )
    {
        try
        {
            var citizenBoard = Sandbox.Services.Leaderboards.GetFromStat( Game.Ident, "citizen_wins" );
            citizenBoard.MaxEntries = 50;
            await citizenBoard.Refresh();

            var anomalyBoard = Sandbox.Services.Leaderboards.GetFromStat( Game.Ident, "anomaly_wins" );
            anomalyBoard.MaxEntries = 50;
            await anomalyBoard.Refresh();

            foreach ( var player in players )
            {
                int citizenWins = 0;
                int anomalyWins = 0;

                foreach ( var entry in citizenBoard.Entries )
                {
                    if ( entry.SteamId == (long)player.SteamId )
                    {
                        citizenWins = (int)entry.Value;
                        break;
                    }
                }

                foreach ( var entry in anomalyBoard.Entries )
                {
                    if ( entry.SteamId == (long)player.SteamId )
                    {
                        anomalyWins = (int)entry.Value;
                        break;
                    }
                }

                player.CitizenWins = citizenWins;
                player.AnomalyWins = anomalyWins;
            }

            // Update the cached data with the fetched stats
            cachedPlayers = new List<PlayerData>( players );
            //Log.Info( $"[PlayerBoard] Fetched stats for {players.Count} players" );
        }
        catch ( System.Exception e )
        {
            //Log.Warning( $"[PlayerBoard] Failed to fetch stats: {e.Message}" );
        }
    }
    
    // Force refresh all cached stats (call after a game ends)
    public static void InvalidateCache()
    {
        statsCache.Clear();
    }
}