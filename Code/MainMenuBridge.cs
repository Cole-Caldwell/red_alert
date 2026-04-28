using Sandbox;
using Sandbox.Network;
using System.Collections.Generic;
using System.Threading.Tasks;

public static class MainMenuBridge
{
	public enum MenuScreen
	{
		Main,
		JoinLobby
	}

	public static MenuScreen CurrentScreen { get; set; } = MenuScreen.Main;
	public static bool IsConnecting { get; set; } = false;
	public static string StatusMessage { get; set; } = "";
	public static List<LobbyInformation> AvailableLobbies { get; set; } = new();
	public static bool IsRefreshingLobbies { get; set; } = false;

	public static async Task CreateLobby()
	{
		if ( IsConnecting ) return;

		IsConnecting = true;
		StatusMessage = "CREATING LOBBY...";

		try
		{
			Networking.CreateLobby( new LobbyConfig
			{
				MaxPlayers = 10,
				Privacy = LobbyPrivacy.Public,
				Name = $"{Connection.Local?.DisplayName ?? "Player"}'s Lobby"
			} );

			await Task.Delay( 500 );

			Game.ActiveScene.LoadFromFile( "scenes/prototype.scene" );
		}
		catch ( System.Exception ex )
		{
			StatusMessage = "FAILED TO CREATE LOBBY";
			Log.Error( $"[MainMenu] CreateLobby failed: {ex.Message}" );
			IsConnecting = false;
		}
	}

	public static async Task RefreshLobbies()
	{
		if ( IsRefreshingLobbies ) return;

		IsRefreshingLobbies = true;

		try
		{
			var lobbies = await Networking.QueryLobbies( Game.Ident );
			AvailableLobbies = lobbies ?? new();
		}
		catch ( System.Exception ex )
		{
			Log.Error( $"[MainMenu] QueryLobbies failed: {ex.Message}" );
			AvailableLobbies = new();
		}

		IsRefreshingLobbies = false;
	}

	public static void JoinLobby( LobbyInformation lobby )
	{
		if ( IsConnecting ) return;

		IsConnecting = true;
		StatusMessage = "JOINING LOBBY...";

		try
		{
			Networking.Connect( lobby.LobbyId );
			Game.ActiveScene.LoadFromFile( "scenes/prototype.scene" );
		}
		catch ( System.Exception ex )
		{
			StatusMessage = "FAILED TO JOIN LOBBY";
			Log.Error( $"[MainMenu] JoinLobby failed: {ex.Message}" );
			IsConnecting = false;
		}
	}

	public static void Disconnect()
	{
		Networking.Disconnect();
		Reset();
		Game.ActiveScene.LoadFromFile( "scenes/mainmenu.scene" );
	}

	public static void Reset()
	{
		CurrentScreen = MenuScreen.Main;
		IsConnecting = false;
		StatusMessage = "";
		AvailableLobbies = new();
		IsRefreshingLobbies = false;
	}
}
