using Sandbox;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public partial class GameManager : Component
{
	// Game States
	public enum GameState
	{
		WaitingInLobby,  // NEW - waiting for players to ready up
		Lobby,      // Waiting for players
		InGame,     // Game is active
		Voting,     // Emergency meeting / voting
		GameOver    // Someone won
	}

	[Property, Sync] public GameState CurrentState { get; set; } = GameState.WaitingInLobby;
	private GameState lastVoiceState = GameState.WaitingInLobby;
	
	// Player Management
	[Property] public int MinPlayers { get; set; } = 1;  // Minimum players to start
	[Property] public float LobbyTimer { get; set; } = 10f;  // Seconds before game starts
	[Property] public int AnomalyCount { get; set; } = 1;  // Number of Anomalys
	[Property] public bool ForceLocalPlayerAsAnomaly { get; set; } = false;
	[Property] public float DiscussionTime { get; set; } = 15f;
	[Property] public float VotingTime { get; set; } = 20f;
	[Property] public Vector3 LobbySpawnArea { get; set; } = Vector3.Zero;
	[Property] public Vector3 GameSpawnArea { get; set; } = Vector3.Zero;
	[Property] public SoundEvent CitizenRoleSound { get; set; }
	[Property] public SoundEvent AnomalyRoleSound { get; set; }
	[Property] public SoundEvent EmergencyMeetingSound { get; set; }
	[Property] public SoundEvent ReadyCountdownSound { get; set; }
	[Property] public SoundEvent MeetingResultSound { get; set; }
	[Property] public SoundEvent CitizensWinSound { get; set; }
	[Property] public SoundEvent AnomalyWinSound { get; set; }
	
	private float votingTimer = 0f;
	private Dictionary<string, string> playerVotes = new Dictionary<string, string>();
	private VotingUI votingUI;
	private bool votingUIActive = false;
	private float gameStartTime = 0f;
	
	// Internal tracking
	private List<PlayerData> AllPlayers = new List<PlayerData>();
	private List<GameObject> spawnedBodies = new List<GameObject>();
	private float currentLobbyTime = 0f;
	private bool voiceCurrentlyEnabled = false;

	protected override void OnStart()
	{
		Log.Info( $"GameManager Started! (IsHost: {Networking.IsHost})" );
	}

	private bool IsVoiceEnabled()
	{
		return voiceCurrentlyEnabled;
	}

	private void EnableVoiceForAll()
	{
		if ( !Networking.IsHost )
			return;
		
		var players = Scene.GetAllComponents<PlayerController>();
		foreach ( var player in players )
		{
			// During voting, only alive in-game players get voice
			if ( CurrentState == GameState.Voting )
			{
				player.SetVoiceChatEnabled( player.IsAlive && player.IsInGame );
			}
			else if ( CurrentState == GameState.InGame )
			{
				player.SetVoiceChatEnabled( false );
			}
			else
			{
				player.SetVoiceChatEnabled( true );
			}
		}
		voiceCurrentlyEnabled = true;
		Log.Info( $"Voice chat enabled (State: {CurrentState})" );
	}

	private void EnableChatForAlive()
	{
		if ( ChatSystem.Instance == null ) return;

		var localPlayer = Scene.GetAllComponents<PlayerController>()
			.FirstOrDefault( p => !p.IsProxy && p.GameObject.Network.Owner != null );

		if ( localPlayer != null && localPlayer.IsAlive && localPlayer.IsInGame )
		{
			ChatSystem.Instance.ChatEnabled = true;
		}
		else
		{
			ChatSystem.Instance.ChatEnabled = false;
		}
	}

	private void DisableVoiceForAll()
	{
		// Only host should manage voice state
		if ( !Networking.IsHost )
			return;
		
		var players = Scene.GetAllComponents<PlayerController>();
		foreach ( var player in players )
		{
			player.SetVoiceChatEnabled( false );
		}
		voiceCurrentlyEnabled = false;
		Log.Info( "Voice chat disabled for all players" );
	}

	protected override void OnUpdate()
	{
		// Update player board data every frame
		UpdatePlayerBoard();

		if ( CurrentState != lastVoiceState )
		{
			voiceCurrentlyEnabled = false;
			lastVoiceState = CurrentState;
		}

		// Control voice based on game state
		switch ( CurrentState )
		{
			case GameState.WaitingInLobby:
				// Enable voice in lobby
				if ( !IsVoiceEnabled() )
				{
					EnableVoiceForAll();
				}
				break;
				
			case GameState.Lobby:
				UpdateLobby();
				
				// Keep voice enabled during lobby countdown
				if ( !IsVoiceEnabled() )
				{
					EnableVoiceForAll();
				}
				break;
				
			case GameState.InGame:
				UpdateGame();
				
				// Disable voice during gameplay
				if ( !IsVoiceEnabled() )
				{
					EnableVoiceForAll();
				}
				break;
				
			case GameState.Voting:
				UpdateVoting();
				
				// Enable voice during voting
				if ( !IsVoiceEnabled() )
				{
					EnableVoiceForAll();
				}
				break;
				
			case GameState.GameOver:
				UpdateGameOver();
				
				// Keep voice enabled during game over
				if ( !IsVoiceEnabled() )
				{
					EnableVoiceForAll();
				}
				break;
		}
	}

	private void UpdateLobby()
	{
		// Only host should manage lobby timer
		if ( !Networking.IsHost )
			return;
		
		// Count connected players
		int playerCount = Scene.GetAllComponents<PlayerController>().Count();
		
		// If enough players, count down
		if ( playerCount >= MinPlayers )
		{
			currentLobbyTime += Time.Delta;
			
			// Show countdown (we'll add UI later)
			if ( currentLobbyTime >= LobbyTimer )
			{
				// Broadcast game start to all clients
				StartGame();
			}
		}
		else
		{
			currentLobbyTime = 0f;  // Reset timer if not enough players
		}
	}

	private void UpdateGame()
	{
		// Periodically check win conditions during gameplay
		if ( Time.Now % 2f < Time.Delta )  // Check every 2 seconds
		{
			CheckWinConditions();
		}
	}

	[Rpc.Broadcast]
	public void SetAnomalyCountRpc( int count )
	{
		AnomalyCount = count;
		GameSettingsBridge.AnomalyCount = count;
		Log.Info( $"[GameSettings] Anomaly count set to {count}" );
	}

	private void UpdateVoting()
	{
		if ( !votingUIActive ) return;

		// Update timer
		votingTimer += Time.Delta;
		
		if ( votingUI != null )
		{
			// Discussion phase
			if ( votingTimer < DiscussionTime )
			{
				votingUI.SetTimer( DiscussionTime - votingTimer );
				votingUI.IsDiscussionPhase = true;
			}
			// Voting phase
			else if ( votingTimer < DiscussionTime + VotingTime )
			{
				// Enable voting at start of voting phase
				if ( votingUI.CanVote == false && votingUI.IsDiscussionPhase )
				{
					votingUI.EnableVoting();
					Log.Info( "Voting phase started!" );
				}
				
				votingUI.SetTimer( (DiscussionTime + VotingTime) - votingTimer );
				
				// Update vote counts
				UpdateVoteDisplay();
			}
			// Time's up - tally votes
			else
			{
				TallyVotes();
			}
		}
	}

	private void UpdatePlayerBoard()
	{
		var playerDataList = new List<PlayerBoardBridge.PlayerData>();
		
		foreach ( var conn in Connection.All )
		{
			var allPlayers = Scene.GetAllComponents<PlayerController>();
			var player = allPlayers.FirstOrDefault( p => p.GameObject.Network.Owner == conn );
			
			if ( player != null )
			{
				var playerData = PlayerBoardBridge.CreatePlayerData( conn, player );
				playerDataList.Add( playerData );
			}
		}
		
		// Store immediately with whatever cached values exist
		PlayerBoardBridge.UpdatePlayerData( playerDataList );
		
		// Then fetch real stats and update in-place
		PlayerBoardBridge.FetchAllPlayerStats( playerDataList );
	}

	private void UpdateGameOver()
	{
		// End game logic will go here
	}

	[Rpc.Broadcast]
	public void StartGameFromLobby( List<string> readyPlayerIds )
	{
		if ( CurrentState != GameState.WaitingInLobby )
		{
			Log.Warning( "StartGameFromLobby called but not in WaitingInLobby state - ignoring" );
			return;
		}
		
		Log.Info( $"StartGameFromLobby (IsHost: {Networking.IsHost}), Ready IDs: {readyPlayerIds.Count}" );

		// Mark players as in-game LOCALLY on all clients immediately
		var allPlayers = Scene.GetAllComponents<PlayerController>().ToList();
		
		foreach ( var player in allPlayers )
		{
			if ( player.GameObject.Network.Owner == null )
				continue;

			if ( player.IsSpectating )
        		continue;

			var steamId = player.GameObject.Network.Owner.SteamId.ToString();
			if ( readyPlayerIds.Contains( steamId ) )
			{
				player.IsInGame = true;
				Log.Info( $"{player.PlayerName} (SteamId: {steamId}) marked as in-game" );
			}
			else
			{
				player.IsInGame = false;
				Log.Info( $"{player.PlayerName} (SteamId: {steamId}) NOT readied - staying in lobby" );
			}
		}
		
		TeleportPlayersToGame();
		CurrentState = GameState.Lobby;

		if ( ChatSystem.Instance != null )
    		ChatSystem.Instance.ChatEnabled = false;

		currentLobbyTime = 0f;
	}

	private void TeleportPlayersToGame()
	{
		var allObjects = Scene.GetAllObjects( true );
		var gameSpawns = new List<GameObject>();
		
		foreach ( var obj in allObjects )
		{
			if ( obj.Tags != null && obj.Tags.Has( "GameSpawn" ) )
			{
				gameSpawns.Add( obj );
			}
		}
		
		if ( gameSpawns.Count == 0 )
		{
			Log.Warning( "No GameSpawn points found!" );
			return;
		}

		var allPlayerObjects = Scene.GetAllObjects( true )
			.Where( obj => obj.Components.Get<PlayerController>() != null )
			.Where( obj => obj.Components.Get<PlayerController>().IsInGame )
			.ToList();
		
		Log.Info( $"Teleporting {allPlayerObjects.Count} in-game players to {gameSpawns.Count} game spawns" );
		
		var shuffledSpawns = gameSpawns.OrderBy( _ => Game.Random.Int( 0, 1000 ) ).ToList();
		
		for ( int i = 0; i < allPlayerObjects.Count; i++ )
		{
			var playerObj = allPlayerObjects[i];
			var player = playerObj.Components.Get<PlayerController>();
			if ( player == null ) continue;
			
			var spawnIndex = i % shuffledSpawns.Count;
			var spawnPoint = shuffledSpawns[spawnIndex];
			
			playerObj.WorldPosition = spawnPoint.WorldPosition;
			
			Log.Info( $"Teleported {player.PlayerName} to game spawn {spawnIndex + 1}/{shuffledSpawns.Count}" );
		}
	}

	private void ApplyStartCooldowns()
	{
		if ( !Networking.IsHost ) return;

		var anomalies = Scene.GetAllComponents<PlayerController>()
			.Where( p => p.Role == PlayerController.PlayerRole.Anomaly && p.IsInGame && p.GameObject.Network.Owner != null )
			.ToList();

		foreach ( var anomaly in anomalies )
		{
			anomaly.ForceAbilityCooldownRpc( 15f );
		}
	}

	private void TeleportPlayersToLobby()
	{
		// Only host should teleport players
		if ( !Networking.IsHost )
			return;
			
		// Find all lobby spawn points
		var allObjects = Scene.GetAllObjects( true );
		var lobbySpawns = new List<GameObject>();
		
		foreach ( var obj in allObjects )
		{
			if ( obj.Tags != null && obj.Tags.Has( "LobbySpawn" ) )
			{
				lobbySpawns.Add( obj );
			}
		}
		
		if ( lobbySpawns.Count == 0 )
		{
			Log.Warning( "No LobbySpawn points found!" );
			return;
		}

		// Get ALL players (including test dummies)
		var allPlayerObjects = Scene.GetAllObjects( true )
			.Where( obj => obj.Components.Get<PlayerController>() != null )
			.ToList();
		
		Log.Info( $"Found {allPlayerObjects.Count} player objects to teleport to {lobbySpawns.Count} lobby spawns" );
		
		// Shuffle spawn points to randomize distribution
		var shuffledSpawns = lobbySpawns.OrderBy( _ => Game.Random.Int( 0, 1000 ) ).ToList();
		
		// ONLY teleport - don't restore anything else (RestoreToLobbyRpc handles that)
		for ( int i = 0; i < allPlayerObjects.Count; i++ )
		{
			var playerObj = allPlayerObjects[i];
			var player = playerObj.Components.Get<PlayerController>();
			if ( player == null ) continue;
			
			// Use modulo to wrap around if more players than spawns
			var spawnIndex = i % shuffledSpawns.Count;
			var spawnPoint = shuffledSpawns[spawnIndex];
			
			// ONLY teleport to spawn point
			playerObj.WorldPosition = spawnPoint.WorldPosition;
			
			Log.Info( $"Teleported {player.PlayerName} to lobby spawn {spawnIndex + 1}/{shuffledSpawns.Count}" );
		}
	}

	[Rpc.Broadcast]
	public void StartGame()
	{
		Log.Info( $"Game Starting! (IsHost: {Networking.IsHost})" );
		CurrentState = GameState.InGame;
		gameStartTime = Time.Now;
		
		// Only the host should assign roles
		if ( Networking.IsHost )
		{
			AssignRoles();
		}
	}

	private void AssignRoles()
	{
		// CRITICAL: Only the host should assign roles
		if ( !Networking.IsHost )
		{
			Log.Info( "Not host - skipping role assignment" );
			return;
		}
		
		Log.Info( "[HOST] Assigning roles..." );

		// Get all players
		var allPlayers = Scene.GetAllComponents<PlayerController>().ToList();
		
		if ( allPlayers.Count == 0 )
		{
			Log.Warning( "No players found to assign roles!" );
			return;
		}
		
		// CRITICAL FIX: Get players that have network owners (includes proxies!)
		// Proxies represent remote players on the host
		var realPlayers = allPlayers
			.Where( p => p.GameObject.Network.Owner != null && p.IsInGame )
			.ToList();
		
		//Log.Info( $"Real players found: {realPlayers.Count}" );
		foreach ( var p in realPlayers )
		{
			//Log.Info( $"  - {p.PlayerName}, IsInGame: {p.IsInGame}, IsProxy: {p.IsProxy}, Owner: {p.GameObject.Network.Owner?.DisplayName}" );
		}
		
		var playersForRoles = new List<PlayerController>( realPlayers );
		Log.Info( $"Players for role assignment: {playersForRoles.Count}" );
		
		if ( playersForRoles.Count == 0 )
		{
			Log.Error( "No players to assign roles to!" );
			return;
		}
		
		// DEBUG: Force local player as Anomaly for testing
		if ( ForceLocalPlayerAsAnomaly )
		{
			// Find the host's local player (not a proxy)
			var localPlayer = realPlayers.FirstOrDefault( p => !p.IsProxy );
			if ( localPlayer != null )
			{
				localPlayer.Role = PlayerController.PlayerRole.Anomaly;
				localPlayer.IsAlive = true;
				
				//Log.Info( $"[FORCED] {localPlayer.PlayerName} assigned as Anomaly" );
				localPlayer.ShowRoleRevealRpc( localPlayer.Role );
				
				// Make other real players Citizens
				foreach ( var player in realPlayers.Where( p => p != localPlayer ) )
				{
					player.Role = PlayerController.PlayerRole.Citizen;
					player.IsAlive = true;
					
					//Log.Info( $"Sending role reveal to {player.PlayerName} (Owner: {player.GameObject.Network.Owner.DisplayName})" );
					player.ShowRoleRevealRpc( player.Role );
				}
			}
		}
		
		// CRITICAL: Ensure at least 1 anomaly is assigned
		int anomalyCount = System.Math.Min( AnomalyCount, playersForRoles.Count );
		if ( anomalyCount < 1 )
		{
			//Log.Warning( "AnomalyCount was 0, forcing to 1!" );
			anomalyCount = 1;
		}
		
		// Shuffle players for random assignment
		var shuffled = playersForRoles.OrderBy( x => Game.Random.Int( 0, 10000 ) ).ToList();
		
		//Log.Info( $"=== ROLE ASSIGNMENT (Anomalies: {anomalyCount} out of {shuffled.Count} players) ===" );
		
		// Assign Anomalies
		for ( int i = 0; i < anomalyCount; i++ )
		{
			//Log.Info( $"[ASSIGNING ANOMALY] Index {i}, Player: {shuffled[i].PlayerName}, IsProxy: {shuffled[i].IsProxy}" );
			
			// Use RPC instead of direct assignment
			shuffled[i].AssignRoleRpc( PlayerController.PlayerRole.Anomaly );
			
			// Show role reveal to all real players (including proxies)
			if ( shuffled[i].GameObject.Network.Owner != null )
			{
				//Log.Info( $"[ANOMALY] {shuffled[i].PlayerName} (Owner: {shuffled[i].GameObject.Network.Owner.DisplayName}, IsProxy: {shuffled[i].IsProxy})" );
				shuffled[i].ShowRoleRevealRpc( PlayerController.PlayerRole.Anomaly );
				shuffled[i].ShowAnomalyAbilitiesRpc(); // NEW: Show abilities UI
			}
		}

		// Rest are Citizens
		for ( int i = anomalyCount; i < shuffled.Count; i++ )
		{
			//Log.Info( $"[ASSIGNING CITIZEN] Index {i}, Player: {shuffled[i].PlayerName}, IsProxy: {shuffled[i].IsProxy}" );
			
			// Use RPC instead of direct assignment
			shuffled[i].AssignRoleRpc( PlayerController.PlayerRole.Citizen );
			
			// Show role reveal to all real players (including proxies)
			if ( shuffled[i].GameObject.Network.Owner != null )
			{
				//Log.Info( $"[CITIZEN] {shuffled[i].PlayerName} (Owner: {shuffled[i].GameObject.Network.Owner.DisplayName}, IsProxy: {shuffled[i].IsProxy})" );
				shuffled[i].ShowRoleRevealRpc( PlayerController.PlayerRole.Citizen );
			}
		}
		
		Log.Info( "=== ROLE ASSIGNMENT COMPLETE ===" );

		// Wait a moment for roles to sync before assigning tasks
		AssignTasksAfterDelay();
	}

	private async void AssignTasksAfterDelay()
	{
		// Wait for roles to sync
		await GameTask.DelaySeconds( 0.2f );
		
		// Assign tasks to Citizens
		var taskManager = Scene.GetAllComponents<TaskManager>().FirstOrDefault();
		if ( taskManager != null )
		{
			taskManager.AssignTasksToPlayers();
		}
		else
		{
			Log.Warning( "No TaskManager found in scene!" );
		}

		ApplyStartCooldowns();
	}

	[Rpc.Broadcast]
	public void TriggerEmergencyMeeting( PlayerController reporter, DeadBody body )
	{
		// Show splash screen and play sound on all clients
		var splashObj = Scene.CreateObject();
		splashObj.Name = "Emergency Meeting Splash";
		var screenPanel = splashObj.Components.Create<ScreenPanel>();
		screenPanel.ZIndex = 999;
		splashObj.Components.Create<EmergencyMeetingSplash>();

		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if ( gameManager?.EmergencyMeetingSound != null )
		{
			Sound.Play( gameManager.EmergencyMeetingSound );
		}

		// End all blind effects
		EndAllBlindEffects();

		// Clean up all bodies when meeting is called
		CleanupDeadBodies();

		// Close any open task minigames for all players
		CloseAllTaskMinigames();

		Log.Info( $"EMERGENCY MEETING called by {reporter?.PlayerName ?? "Unknown"}!" );
		
		if ( body != null && body.IsValid() )
		{
			Log.Info( $"Body found: {body.VictimName} ({body.VictimRole})" );
		}
		else
		{
			Log.Warning( "Body was destroyed before meeting started!" );
		}
		
		CurrentState = GameState.Voting;
		votingTimer = 0f;
		playerVotes.Clear();

		// Teleport alive players to meeting room
		TeleportAlivePlayersToMeeting();
		
		ShowVotingUI( body );
		EnableChatForAlive();
	}

	[Rpc.Broadcast]
	private void CloseAllTaskMinigames()
	{
		// Only close for the local player
		var localPlayer = Scene.GetAllComponents<PlayerController>()
			.FirstOrDefault( p => !p.IsProxy && p.GameObject.Network.Owner != null );

		if ( localPlayer == null ) return;

		TaskProgressBridge.ClearTask();

		var taskMinigameNames = new[]
		{
			"Task Button Sequence UI",
			"Task Slider Match UI",
			"Task Collect Samples UI",
			"Task Memory Match UI",
			"Task Wire Connect UI",
			"Task Progress UI"
		};

		var taskUIs = Scene.GetAllObjects( true )
			.Where( obj => taskMinigameNames.Contains( obj.Name ) )
			.ToList();

		foreach ( var ui in taskUIs )
		{
			if ( ui != null && ui.IsValid() )
				ui.Destroy();
		}
	}

	private void RestoreDeadPlayersToLobby()
	{
		if ( !Networking.IsHost )
			return;
		
		var deadPlayers = Scene.GetAllComponents<PlayerController>()
			.Where( p => !p.IsAlive && p.IsInGame && p.GameObject.Network.Owner != null )
			.ToList();
		
		if ( deadPlayers.Count == 0 ) return;
		
		Log.Info( $"Restoring {deadPlayers.Count} dead players to lobby..." );
		
		var lobbySpawns = Scene.GetAllObjects( true )
			.Where( obj => obj.Tags != null && obj.Tags.Has( "LobbySpawn" ) )
			.ToList();
		
		foreach ( var player in deadPlayers )
		{
			var spawnPos = lobbySpawns.Count > 0
				? lobbySpawns[Game.Random.Int( 0, lobbySpawns.Count - 1 )].WorldPosition
				: Vector3.Zero;
			
			// HOST sets synced properties directly (this is authoritative)
			player.IsAlive = true;
			player.Role = PlayerController.PlayerRole.Citizen;
			player.IsInGame = false;
			
			// Broadcast re-enables components on all clients
			player.RestorePlayerVisuals( spawnPos );
			
			// Owner-only: clean up UI
			player.CleanupAllUIRpc();
			
			Log.Info( $"Restored {player.PlayerName}" );
		}
	}

	private void TeleportAlivePlayersToMeeting()
	{
		var meetingSpawns = Scene.GetAllObjects( true )
			.Where( obj => obj.Tags != null && obj.Tags.Has( "meetingspawn" ) )
			.ToList();

		if ( meetingSpawns.Count == 0 )
		{
			Log.Warning( "No MeetingSpawn points found!" );
			return;
		}

		var inGamePlayers = Scene.GetAllComponents<PlayerController>()
			.Where( p => p.IsInGame )
			.ToList();

		var shuffledSpawns = meetingSpawns.OrderBy( _ => Game.Random.Int( 0, 1000 ) ).ToList();

		for ( int i = 0; i < inGamePlayers.Count; i++ )
		{
			var spawnIndex = i % shuffledSpawns.Count;
			inGamePlayers[i].GameObject.WorldPosition = shuffledSpawns[spawnIndex].WorldPosition;
			Log.Info( $"Teleported {inGamePlayers[i].PlayerName} to meeting spawn {spawnIndex + 1}/{shuffledSpawns.Count} (Alive: {inGamePlayers[i].IsAlive})" );
		}

		Log.Info( $"Teleported {inGamePlayers.Count} in-game players to meeting room" );
	}

	private void TeleportAlivePlayersToGame()
	{
		var gameSpawns = Scene.GetAllObjects( true )
			.Where( obj => obj.Tags != null && obj.Tags.Has( "GameSpawn" ) )
			.ToList();

		if ( gameSpawns.Count == 0 )
		{
			Log.Warning( "No GameSpawn points found!" );
			return;
		}

		var inGamePlayers = Scene.GetAllComponents<PlayerController>()
			.Where( p => p.IsInGame )
			.ToList();

		var shuffledSpawns = gameSpawns.OrderBy( _ => Game.Random.Int( 0, 1000 ) ).ToList();

		for ( int i = 0; i < inGamePlayers.Count; i++ )
		{
			var spawnIndex = i % shuffledSpawns.Count;
			inGamePlayers[i].GameObject.WorldPosition = shuffledSpawns[spawnIndex].WorldPosition;
			Log.Info( $"Teleported {inGamePlayers[i].PlayerName} to game spawn {spawnIndex + 1}/{shuffledSpawns.Count} (Alive: {inGamePlayers[i].IsAlive})" );
		}

		Log.Info( $"Teleported {inGamePlayers.Count} in-game players back to game spawns" );
	}

	public void RegisterDeadBody( GameObject body )
	{
		spawnedBodies.Add( body );
	}

	private void ShowVotingUI( DeadBody body )
	{
		// ALWAYS activate voting timer on host, even if host player is dead
		if ( Networking.IsHost )
		{
			votingTimer = 0f;
			votingUIActive = true;
		}

		// Only show voting UI to ALIVE, IN-GAME players
		var localPlayer = Scene.GetAllComponents<PlayerController>()
			.FirstOrDefault( p => !p.IsProxy && p.GameObject.Network.Owner != null );
		
		if ( localPlayer == null )
		{
			Log.Info( "No local player found" );
			return;
		}

		if ( !localPlayer.IsAlive || !localPlayer.IsInGame )
		{
			Log.Info( $"Local player not eligible for voting - IsAlive:{localPlayer.IsAlive}, IsInGame:{localPlayer.IsInGame}, Name:{localPlayer.PlayerName}" );
			return;
		}
		
		// Create voting UI with ScreenPanel
		if ( votingUI == null )
		{
			var uiObject = Scene.CreateObject();
			uiObject.Name = "Voting UI";
			
			var screenPanel = uiObject.Components.Create<ScreenPanel>();
			screenPanel.ZIndex = 200;
			
			votingUI = uiObject.Components.Create<VotingUI>();
		}

		votingUIActive = true;

		// Set body information
		string bodyInfo;
		if ( body != null && body.IsValid() )
		{
			bodyInfo = $"{body.VictimName} was found dead! Role: {body.VictimRole}";
		}
		else
		{
			bodyInfo = "Emergency meeting called!";
		}
		
		votingUI.SetBodyInfo( bodyInfo );

		// Build player data — only show in-game players
		var players = Scene.GetAllComponents<PlayerController>()
			.Where( p => p.IsInGame && !p.IsSpectating )
			.ToList();
		var playerData = players.Select( p => new VotingUI.PlayerVoteData
		{
			Name = GetPlayerDisplayName( p ),
			IsAlive = p.IsAlive,
			VoteCount = 0,
			SteamId = p.GameObject.Network.Owner?.SteamId ?? 0
		} ).ToList();

		votingUI.UpdatePlayers( playerData );
		votingUI.SetTimer( DiscussionTime );
		votingUI.IsVisible = true;
		
		Log.Info( $"Voting UI shown! {playerData.Count} total players" );
	}

	private string GetPlayerDisplayName( PlayerController player )
	{
		// Use the same name resolution as nametag
		string name = player.GameObject.Root.Name.Replace( "Player - ", "" );
		
		// Fallback for test dummies or if name is empty
		if ( string.IsNullOrEmpty( name ) || name == player.GameObject.Root.Name )
			name = player.PlayerName;
		
		return name;
	}

	public void CastVote( string targetName )
	{
		var localPlayer = Scene.GetAllComponents<PlayerController>()
			.FirstOrDefault( p => !p.IsProxy && p.GameObject.Network.Owner != null );

		if ( localPlayer != null && localPlayer.IsAlive )
		{
			string voterName = GetPlayerDisplayName( localPlayer );
			playerVotes[voterName] = targetName;
			BroadcastVote( voterName, targetName );
		}
		else
		{
			Log.Info( "Dead players cannot vote!" );
		}
	}

	[Rpc.Broadcast]
	private void BroadcastVote( string voterName, string targetName )
	{
		//Log.Info( $"{voterName} voted for {targetName}" );
		
		// Update votes on ALL clients
		playerVotes[voterName] = targetName;
		
		// Update vote display on ALL clients
		UpdateVoteDisplay();
	}

	private void UpdateVoteDisplay()
	{
		if ( votingUI == null ) return;

		// Count votes
		var voteCounts = new Dictionary<string, int>();
		
		foreach ( var vote in playerVotes.Values )
		{
			if ( vote != "SKIP" )
			{
				if ( !voteCounts.ContainsKey( vote ) )
					voteCounts[vote] = 0;
				voteCounts[vote]++;
			}
		}

		votingUI.UpdateVoteCounts( voteCounts );
	}

	[Rpc.Broadcast]
	private void TallyVotes()
	{
		votingUIActive = false;

		// Count how many in-game alive players there are (for defaulting non-voters to skip)
		var inGameAlivePlayers = Scene.GetAllComponents<PlayerController>()
			.Where( p => p.IsInGame && p.IsAlive )
			.ToList();

		int totalVoters = inGameAlivePlayers.Count;

		// Count explicit skip votes
		int skipVotes = playerVotes.Values.Count( v => v == "SKIP" );

		// Non-voters default to skip
		int nonVoters = totalVoters - playerVotes.Count;
		skipVotes += nonVoters;

		// Count player votes (excluding skips)
		var voteCounts = playerVotes.Values
			.Where( v => v != "SKIP" )
			.GroupBy( v => v )
			.Select( g => new { Name = g.Key, Count = g.Count() } )
			.OrderByDescending( x => x.Count )
			.ToList();

		// Determine result
		string resultType = "no-eject";
		string ejectedName = "";
		ulong ejectedSteamId = 0;
		PlayerController ejectedPlayer = null;

		if ( voteCounts.Any() && voteCounts[0].Count > 0 )
		{
			int topVoteCount = voteCounts[0].Count;

			// Check for tie between top voted players
			bool isTied = voteCounts.Count > 1 && voteCounts[1].Count == topVoteCount;

			// Top voted player must have strictly more votes than skip votes AND no tie
			if ( !isTied && topVoteCount > skipVotes )
			{
				string ejected = voteCounts[0].Name;

				Log.Info( $"============================" );
				Log.Info( $"{ejected} was ejected! ({topVoteCount} votes vs {skipVotes} skips)" );

				ejectedPlayer = Scene.GetAllComponents<PlayerController>()
					.FirstOrDefault( p => GetPlayerDisplayName( p ) == ejected );

				if ( ejectedPlayer != null )
				{
					ejectedName = ejected;
					ejectedSteamId = ejectedPlayer.GameObject.Network.Owner?.SteamId ?? 0;

					if ( ejectedPlayer.Role == PlayerController.PlayerRole.Anomaly )
					{
						resultType = "was-anomaly";
					}
					else
					{
						resultType = "not-anomaly";
					}

					Log.Info( $"{ejected} was a {ejectedPlayer.Role}!" );
				}

				Log.Info( $"============================" );
			}
			else
			{
				if ( isTied )
					Log.Info( $"Vote tied at {topVoteCount} votes each - no one ejected" );
				else
					Log.Info( $"Top vote ({topVoteCount}) did not exceed skips ({skipVotes}) - no one ejected" );

				resultType = "no-eject";
			}
		}
		else
		{
			Log.Info( "No one was ejected. (All skipped or no votes cast)" );
			resultType = "no-eject";
		}

		// Destroy voting UI
		if ( votingUI != null )
		{
			votingUI.GameObject.Destroy();
			votingUI = null;
		}

		// Handle based on result type
		if ( resultType == "not-anomaly" && ejectedPlayer != null )
		{
			// Citizen voted out — kill them first, then show result after delay
			HandleCitizenEjection( ejectedPlayer, ejectedName, ejectedSteamId );
		}
		else if ( resultType == "was-anomaly" )
		{
			// Anomaly voted out — show result immediately, then end game
			HandleAnomalyEjection( ejectedPlayer, ejectedName, ejectedSteamId );
		}
		else
		{
			// No one ejected — show result, then resume
			HandleNoEjection();
		}
	}

	private async void HandleCitizenEjection( PlayerController ejectedPlayer, string ejectedName, ulong ejectedSteamId )
	{
		// Kill the citizen (ragdoll in meeting room, become ghost)
		KillPlayerFromVote( ejectedPlayer );

		// Wait 5 seconds for death to process and ragdoll to display
		await GameTask.DelaySeconds( 5f );

		// Show result splash
		ShowMeetingResultSplash( "not-anomaly", ejectedName, ejectedSteamId );

		// Wait for splash to finish
		await GameTask.DelaySeconds( 2.5f );

		// Clean up the vote-kill ragdoll
		CleanupDeadBodies();

		// Disable chat when resuming game
		if ( ChatSystem.Instance != null )
			ChatSystem.Instance.ChatEnabled = false;

		// Teleport all in-game players back to game spawns
		TeleportAlivePlayersToGame();
		CurrentState = GameState.InGame;
		ApplyStartCooldowns();
	}

	private async void HandleAnomalyEjection( PlayerController ejectedPlayer, string ejectedName, ulong ejectedSteamId )
	{
		// Kill the anomaly (ragdoll in meeting room, become ghost)
		KillPlayerFromVote( ejectedPlayer );

		// Wait for ragdoll to display
		await GameTask.DelaySeconds( 5f );

		// Show result splash
		ShowMeetingResultSplash( "was-anomaly", ejectedName, ejectedSteamId );

		// Wait for splash to finish
		await GameTask.DelaySeconds( 2.5f );

		// Clean up ragdoll
		CleanupDeadBodies();

		if ( ChatSystem.Instance != null )
    		ChatSystem.Instance.ChatEnabled = false;

		// Check win conditions — this will trigger EndGame -> ReturnToLobby
		CheckWinConditions();

		// If somehow no winner, resume game
		if ( CurrentState != GameState.GameOver )
		{
			TeleportAlivePlayersToGame();
			CurrentState = GameState.InGame;
		}
	}

	private async void HandleNoEjection()
	{
		// Show result splash
		ShowMeetingResultSplash( "no-eject", "", 0 );

		// Wait for splash to finish
		await GameTask.DelaySeconds( 2.5f );

		if ( ChatSystem.Instance != null )
    		ChatSystem.Instance.ChatEnabled = false;

		// Teleport all in-game players back to game spawns
		TeleportAlivePlayersToGame();
		CurrentState = GameState.InGame;
		ApplyStartCooldowns();
	}

	[Rpc.Broadcast]
	private void KillPlayerFromVote( PlayerController target )
	{
		if ( target == null || !target.IsValid() ) return;

		target.IsAlive = false;
		Log.Info( $"[VoteKill] {target.PlayerName} has been ejected and killed!" );

		// Spawn ragdoll at their current position (meeting room)
		var deathPosition = target.WorldPosition;
		var deathRotation = target.WorldRotation;
		var targetRenderer = target.GameObject.Components.GetInDescendants<SkinnedModelRenderer>();

		// Find any player with a RagdollPrefab to use
		var playerWithPrefab = Scene.GetAllComponents<PlayerController>()
			.FirstOrDefault( p => p.RagdollPrefab != null );

		if ( playerWithPrefab?.RagdollPrefab != null )
		{
			var ragdoll = playerWithPrefab.RagdollPrefab.Clone();
			ragdoll.NetworkMode = NetworkMode.Never;
			ragdoll.WorldPosition = deathPosition;
			ragdoll.WorldRotation = deathRotation;

			var ragdollRenderer = ragdoll.Components.Get<SkinnedModelRenderer>();
			if ( ragdollRenderer != null )
			{
				ragdollRenderer.Enabled = true;
				ragdollRenderer.UseAnimGraph = false;

				if ( targetRenderer != null )
				{
					ragdollRenderer.Model = targetRenderer.Model;
					ragdollRenderer.MaterialGroup = targetRenderer.MaterialGroup;
					ragdollRenderer.Tint = targetRenderer.Tint;
				}

				// Clone clothing
				if ( targetRenderer != null )
				{
					foreach ( var child in targetRenderer.GameObject.Children )
					{
						if ( !child.IsValid() || !child.Name.StartsWith( "Clothing" ) ) continue;
						var childRenderer = child.Components.Get<SkinnedModelRenderer>();
						if ( childRenderer == null ) continue;

						var clothingObj = new GameObject( true, child.Name );
						clothingObj.Parent = ragdollRenderer.GameObject;

						var clothingRenderer = clothingObj.Components.Create<SkinnedModelRenderer>();
						clothingRenderer.Model = childRenderer.Model;
						clothingRenderer.BoneMergeTarget = ragdollRenderer;
						clothingRenderer.MaterialGroup = childRenderer.MaterialGroup;
						clothingRenderer.Tint = childRenderer.Tint;
						clothingRenderer.UseAnimGraph = false;
					}
				}
			}

			// Physics
			var modelPhysics = ragdoll.Components.Get<ModelPhysics>();
			if ( modelPhysics != null && ragdollRenderer != null )
			{
				modelPhysics.Model = ragdollRenderer.Model;
				modelPhysics.Renderer = ragdollRenderer;

				if ( targetRenderer != null )
				{
					modelPhysics.CopyBonesFrom( targetRenderer, true );
				}
			}

			// Remove DeadBody component so it can't be reported
			var deadBody = ragdoll.Components.Get<DeadBody>();
			if ( deadBody != null )
			{
				deadBody.Destroy();
			}

			// Register for cleanup
			if ( Networking.IsHost )
			{
				var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
				gameManager?.RegisterDeadBody( ragdoll );
			}
		}

		// Clear tasks for the killed player
		var taskManager = Scene.GetAllComponents<TaskManager>().FirstOrDefault();
		if ( taskManager != null )
		{
			taskManager.ClearPlayerTasks( target );
		}

		// Ghost the player and show death UI
		if ( target.GameObject.Network.Owner != null )
		{
			target.PlayDeathSoundRpc();
			target.ShowDeathUIRpc();
		}
		target.BecomeGhostRpc();
	}

	[Rpc.Broadcast]
	private void ShowMeetingResultSplash( string resultType, string ejectedName, ulong ejectedSteamId )
	{
		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if ( gameManager?.MeetingResultSound != null )
		{
			Sound.Play( gameManager.MeetingResultSound );
		}
		var splashObj = Scene.CreateObject();
		splashObj.Name = "Meeting Result Splash";
		var screenPanel = splashObj.Components.Create<ScreenPanel>();
		screenPanel.ZIndex = 998;
		var splash = splashObj.Components.Create<MeetingResultSplash>();
		splash.ResultType = resultType;
		splash.EjectedName = ejectedName;
		splash.EjectedSteamId = ejectedSteamId;

		Log.Info( $"[MeetingResult] Showing: {resultType}, Ejected: {ejectedName}" );
	}

	[Rpc.Broadcast]
	private void CleanupDeadBodies()
	{
		// Clean up host-tracked bodies
		if ( Networking.IsHost )
		{
			var bodiesToDestroy = new List<GameObject>( spawnedBodies );
			spawnedBodies.Clear();
			
			foreach ( var body in bodiesToDestroy )
			{
				if ( body != null && body.IsValid() )
					body.Destroy();
			}
			Log.Info( $"[HOST] Cleaned up {bodiesToDestroy.Count} tracked bodies" );
		}
		
		// ALL clients: find and destroy local ragdolls by tag
		var ragdolls = Scene.GetAllObjects( true )
			.Where( obj => obj.Tags.Has( "ragdoll" ) )
			.ToList();
		
		foreach ( var ragdoll in ragdolls )
		{
			if ( ragdoll != null && ragdoll.IsValid() )
				ragdoll.Destroy();
		}
		Log.Info( $"Cleaned up {ragdolls.Count} ragdolls on {(Networking.IsHost ? "HOST" : "CLIENT")}" );
	}

	private void EndAllBlindEffects()
	{
		var players = Scene.GetAllComponents<PlayerController>()
    		.Where( p => p.GameObject.Network.Owner != null && p.IsInGame );
		
		foreach ( var player in players )
		{
			player.EndBlindEffectRpc();
		}

		// Also clear any mimic effects
		var allPlayersForMimic = Scene.GetAllComponents<PlayerController>().ToList();
		foreach ( var p in allPlayersForMimic )
		{
			if ( p.IsInGame && p.IsAlive )
			{
				p.RemoveMimicRpc();
			}
		}
		
		Log.Info( "Ended all blind effects" );
	}

	private void CheckWinConditions()
	{
		// Only host should check win conditions
		if ( !Networking.IsHost )
			return;
		
		// Grace period - don't check win conditions for the first 10 seconds
    	if ( Time.Now - gameStartTime < 7.5f )
        	return;

		var players = Scene.GetAllComponents<PlayerController>().Where( p => p.IsInGame && !p.IsSpectating ).ToList();
		var alivePlayers = players.Where( p => p.IsAlive ).ToList();
		
		// Include test dummies in anomaly count
		var aliveAnomalies = alivePlayers.Where( p => p.Role == PlayerController.PlayerRole.Anomaly ).ToList();
		var aliveCitizens = alivePlayers.Where( p => p.Role == PlayerController.PlayerRole.Citizen ).ToList();

		// Don't check win conditions if game hasn't started properly
		if ( CurrentState != GameState.InGame && CurrentState != GameState.Voting )
			return;

		Log.Info( $"[WinCheck] Anomalies: {aliveAnomalies.Count}, Citizens: {aliveCitizens.Count}" );

		// Anomaly wins if Citizens are equal to or fewer than Anomalies (1v1 or worse)
		if ( aliveCitizens.Count > 0 && aliveAnomalies.Count >= aliveCitizens.Count )
		{
			EndGame( "ANOMALY" );
			return;
		}

		// Citizens win if all Anomalies are dead
		if ( aliveAnomalies.Count == 0 && aliveCitizens.Count > 0 )
		{
			EndGame( "CITIZENS" );
			return;
		}

		// Citizens also win if all alive citizens have completed all their tasks
		var taskManager = Scene.GetAllComponents<TaskManager>().FirstOrDefault();
		if ( taskManager != null && aliveCitizens.Count > 0 )
		{
			bool allTasksComplete = true;
			
			foreach ( var citizen in aliveCitizens )
			{
				if ( !taskManager.HasPlayerCompletedAllTasks( citizen ) )
				{
					allTasksComplete = false;
					break;
				}
			}
			
			if ( allTasksComplete )
			{
				Log.Info( "All alive citizens completed their tasks!" );
				EndGame( "CITIZENS" );
				return;
			}
		}
	}

	[Rpc.Broadcast]
	private void EndGame( string winner )
	{
		EndAllBlindEffects();
		CurrentState = GameState.GameOver;

		Log.Info( "================================" );
		Log.Info( $"   {winner} WIN!" );
		Log.Info( "================================" );

		// Show all players and their roles
		var players = Scene.GetAllComponents<PlayerController>().ToList();
		foreach ( var player in players )
		{
			string status = player.IsAlive ? "ALIVE" : "DEAD";
			Log.Info( $"{player.PlayerName} - {player.Role} - {status}" );
		}

		// Destroy voting UI if still around
		if ( votingUI != null )
		{
			votingUI.GameObject.Destroy();
			votingUI = null;
		}

		// Wait then return to lobby and show win screen
		EndGameAfterDelay( winner );
	}

	private async void EndGameAfterDelay( string winner )
	{
		await GameTask.DelaySeconds( 2f );

		// Show win UI BEFORE returning to lobby (roles still intact)
		ShowGameOverUI( winner );

		PlayerBoardBridge.InvalidateCache();

		// Wait for players to see the result
		await GameTask.DelaySeconds( 2f );

		ReturnToLobby();
	}

	private void ShowGameOverUI( string winner )
	{
		var uiObject = Scene.CreateObject();
		uiObject.Name = "Game Over UI";
		var gameOverUI = uiObject.Components.Create<GameOverUI>();

		if ( winner == "CITIZENS" )
		{
			gameOverUI.ShowCitizensWin();

			// Each citizen increments their own win stat locally
			var localPlayer = Scene.GetAllComponents<PlayerController>()
				.FirstOrDefault( p => !p.IsProxy && p.GameObject.Network.Owner != null );
			if ( localPlayer != null && localPlayer.Role == PlayerController.PlayerRole.Citizen && localPlayer.IsInGame )
			{
				Sandbox.Services.Stats.Increment( "citizen_wins", 1 );
				Log.Info( "[Stats] Incremented citizen_wins for local player" );
			}
		}
		else
		{
			string anomalyName = "Unknown";
			var anomaly = Scene.GetAllComponents<PlayerController>()
				.FirstOrDefault( p => p.Role == PlayerController.PlayerRole.Anomaly );
			
			if ( anomaly != null )
			{
				anomalyName = anomaly.GameObject.Root.Name.Replace( "Player - ", "" );
				if ( string.IsNullOrEmpty( anomalyName ) || anomalyName == anomaly.GameObject.Root.Name )
					anomalyName = anomaly.PlayerName;
			}

			gameOverUI.ShowAnomalyWins( anomalyName );

			// The anomaly increments their own win stat locally
			var localPlayer = Scene.GetAllComponents<PlayerController>()
				.FirstOrDefault( p => !p.IsProxy && p.GameObject.Network.Owner != null );
			if ( localPlayer != null && localPlayer.Role == PlayerController.PlayerRole.Anomaly && localPlayer.IsInGame )
			{
				Sandbox.Services.Stats.Increment( "anomaly_wins", 1 );
				Log.Info( "[Stats] Incremented anomaly_wins for local player" );
			}
		}
	}

	[Rpc.Broadcast]
	private void ReturnToLobby()
	{
		Log.Info( "Returning to lobby..." );

		var players = Scene.GetAllComponents<PlayerController>()
			.Where( p => p.GameObject.Network.Owner != null && p.IsInGame )
			.ToList();

		// Find lobby spawns
		var lobbySpawns = Scene.GetAllObjects( true )
			.Where( obj => obj.Tags != null && obj.Tags.Has( "LobbySpawn" ) )
			.ToList();

		// Restore ALL players to lobby state
		for ( int i = 0; i < players.Count; i++ )
		{
			var spawnPoint = lobbySpawns.Count > 0 
				? lobbySpawns[i % lobbySpawns.Count].WorldPosition 
				: Vector3.Zero;
			
			// Host sets synced properties directly
			players[i].IsAlive = true;
			players[i].Role = PlayerController.PlayerRole.Citizen;
			players[i].IsSpectating = false;

			// Broadcast visual restore to all clients
			players[i].RestorePlayerVisuals( spawnPoint );

			// Owner-only UI cleanup
			players[i].CleanupAllUIRpc();
		}

		// Mark all players as no longer in-game
		if ( Networking.IsHost )
		{
			foreach ( var p in Scene.GetAllComponents<PlayerController>() )
			{
				if ( p.GameObject.Network.Owner != null )
					p.SetInGameRpc( false );
				else
					p.IsInGame = false; // Test dummies - direct set is fine
			}
		}
		
		// Schedule cleanup
		CleanupDeadBodies();
		
		// Destroy victory UI
		if ( votingUI != null )
		{
			var uiObject = votingUI.GameObject;
			votingUI = null;
			if ( uiObject != null && uiObject.IsValid() )
				spawnedBodies.Add( uiObject );
		}

		// Clear all task assignments
		var taskManager = Scene.GetAllComponents<TaskManager>().FirstOrDefault();
		if ( taskManager != null )
		{
			var allPlayers = Scene.GetAllComponents<PlayerController>().ToList();
			foreach ( var player in allPlayers )
			{
				taskManager.ClearPlayerTasks( player );
			}
		}

		// Reset emergency button cooldown
		var emergencyButtons = Scene.GetAllComponents<EmergencyButton>().ToList();
		foreach ( var button in emergencyButtons )
		{
			button.ResetCooldown();
		}

		// Reset voting state
		playerVotes.Clear();
		votingUIActive = false;
		votingTimer = 0f;
		
		CurrentState = GameState.WaitingInLobby;
		EnableVoiceForAll();

		if ( ChatSystem.Instance != null )
    		ChatSystem.Instance.ChatEnabled = true;
	}
}

// Helper class to track player data
public class PlayerData
{
	public string PlayerName { get; set; }
	public bool IsAlive { get; set; } = true;
	public bool IsAnomaly { get; set; } = false;
}