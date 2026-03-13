using Sandbox;
using System.Collections.Generic;
using System.Linq;

public static class ReadyStatusBridge
{
    public class PlayerReadyInfo
    {
        public string Name { get; set; } = "";
        public ulong SteamId { get; set; } = 0;
        public PlayerStatus Status { get; set; } = PlayerStatus.Unready;
    }

    public enum PlayerStatus
    {
        Unready,
        Ready,
        InGame
    }

    private static List<PlayerReadyInfo> cachedPlayers = new();
    private static bool shouldShow = false;

    public static void Update( Scene scene )
    {
        var localPlayer = scene.GetAllComponents<PlayerController>()
            .FirstOrDefault( p => !p.IsProxy && p.GameObject.Network.Owner != null );

        // Only show in lobby (not in game)
        if ( localPlayer == null || localPlayer.IsInGame )
        {
            shouldShow = false;
            return;
        }

        var gameManager = scene.GetAllComponents<GameManager>().FirstOrDefault();
        if ( gameManager == null )
        {
            shouldShow = false;
            return;
        }

        // Only show during WaitingInLobby or when a game is in progress but player is in lobby
        bool gameInProgress = gameManager.CurrentState == GameManager.GameState.InGame
            || gameManager.CurrentState == GameManager.GameState.Voting
            || gameManager.CurrentState == GameManager.GameState.Lobby;

        bool inLobbyWaiting = gameManager.CurrentState == GameManager.GameState.WaitingInLobby;

        if ( !inLobbyWaiting && !gameInProgress )
        {
            shouldShow = false;
            return;
        }

        shouldShow = true;

        // Get the ready terminal to check who's readied
        var terminal = scene.GetAllComponents<ReadyTerminal>().FirstOrDefault();

        // Build player list
        var players = new List<PlayerReadyInfo>();

        foreach ( var conn in Connection.All )
        {
            var allPlayers = scene.GetAllComponents<PlayerController>();
            var player = allPlayers.FirstOrDefault( p => p.GameObject.Network.Owner == conn );

            if ( player == null ) continue;

            var info = new PlayerReadyInfo
            {
                Name = conn.DisplayName,
                SteamId = conn.SteamId
            };

            if ( player.IsInGame && gameInProgress )
            {
                info.Status = PlayerStatus.InGame;
            }
            else if ( terminal != null && IsPlayerReady( terminal, conn.SteamId.ToString() ) )
            {
                info.Status = PlayerStatus.Ready;
            }
            else
            {
                info.Status = PlayerStatus.Unready;
            }

            players.Add( info );
        }

        // Sort: Ready first, then InGame, then Unready
        players.Sort( ( a, b ) =>
        {
            int PriorityOf( PlayerStatus s ) => s == PlayerStatus.Ready ? 0 : s == PlayerStatus.InGame ? 1 : 2;
            return PriorityOf( a.Status ).CompareTo( PriorityOf( b.Status ) );
        } );

        cachedPlayers = players;
    }

    private static bool IsPlayerReady( ReadyTerminal terminal, string playerId )
    {
        return terminal.IsPlayerReadied( playerId );
    }

    public static List<PlayerReadyInfo> GetPlayers() => new List<PlayerReadyInfo>( cachedPlayers );
    public static bool ShouldShow() => shouldShow;
}
