using Sandbox;
using System.Linq;
using System.Collections.Generic;

public partial class ReadyTerminal : Component, Component.ITriggerListener
{
	[Property] public int RequiredPlayers { get; set; } = 1;
	[Property] public float CountdownTime { get; set; } = 10f;

	[Sync] private bool CountdownActive { get; set; } = false;
	[Sync] private float CountdownTimer { get; set; } = 0f;
	[Sync] private NetList<string> ReadyPlayers { get; set; } = new();
	[Sync] private NetList<string> SyncedReadyPlayers { get; set; } = new();
	private int ReadyCount { get; set; } = 0;

	private int displayReadyCount = 0;

	protected override void OnUpdate()
	{
		// Display countdown if active
		if ( CountdownActive )
		{
			CountdownTimer -= Time.Delta;

			// Update bridge so CountdownUI can read the timer
			CountdownBridge.TimeRemaining = CountdownTimer;
			CountdownBridge.IsActive = true;
			
			// Only host manages the countdown ending
			if ( Networking.IsHost && CountdownTimer <= 0f )
			{
				CountdownActive = false;
				StartGame();
			}
			
			// Show countdown with ready count
			Gizmo.Draw.Color = Color.Green;
			Gizmo.Draw.Text( $"GAME STARTING IN: {CountdownTimer:F1}s", new Transform( WorldPosition + Vector3.Up * 150 ) );
			Gizmo.Draw.Color = Color.Cyan;
			Gizmo.Draw.Text( $"Ready: {displayReadyCount}/{RequiredPlayers}\nPress E to Ready Up", new Transform( WorldPosition + Vector3.Up * 120 ) );
		}
		else
		{
			var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
			bool gameActive = gameManager != null && gameManager.CurrentState != GameManager.GameState.WaitingInLobby;
			
			if ( gameActive )
			{
				Gizmo.Draw.Color = Color.Red;
				Gizmo.Draw.Text( "GAME IN PROGRESS\nPress E to Spectate", new Transform( WorldPosition + Vector3.Up * 100 ) );
			}
			else
			{
				var text = $"READY: {displayReadyCount}/{RequiredPlayers}\nPress E to Ready Up";
				Color color = displayReadyCount >= RequiredPlayers ? Color.Green : Color.Yellow;
				
				Gizmo.Draw.Color = color;
				Gizmo.Draw.Text( text, new Transform( WorldPosition + Vector3.Up * 100 ) );
			}
		}
	}

	void ITriggerListener.OnTriggerEnter( Collider other )
	{
		// Player entered the terminal area
		var player = other.GameObject.Components.Get<PlayerController>();
		if ( player != null && !player.IsProxy )
		{
			// Show prompt
			Gizmo.Draw.Color = Color.Cyan;
			Gizmo.Draw.Text( "Press E to Ready Up!", new Transform( WorldPosition + Vector3.Up * 80 ) );
		}
	}

	void ITriggerListener.OnTriggerExit( Collider other )
	{
		// Player left the terminal area
	}

	public void PlayerReadyUp( string playerName )
	{
		// Don't allow ready up if a game is already active
		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if ( gameManager != null && gameManager.CurrentState != GameManager.GameState.WaitingInLobby )
		{
			Log.Info( $"{playerName} tried to ready up but a game is already in progress" );
			return;
		}

		// Broadcast the ready/unready action
		BroadcastReadyToggle( playerName );
	}

	public bool IsPlayerReadied( string playerId )
	{
		return SyncedReadyPlayers.Contains( playerId );
	}

	[Rpc.Broadcast]
	private void UpdateReadyCountRpc( int count )
	{
		displayReadyCount = count;
	}

	[Rpc.Broadcast]
	private void BroadcastReadyToggle( string playerId )
	{
		if ( !Networking.IsHost )
			return;

		if ( ReadyPlayers.Contains( playerId ) )
		{
			ReadyPlayers.Remove( playerId );
			Log.Info( $"{playerId} is no longer ready ({ReadyPlayers.Count}/{RequiredPlayers})" );

			// Show feedback to the player
			var feedbackPlayer = Scene.GetAllComponents<PlayerController>()
				.FirstOrDefault( p => p.GameObject.Network.Owner != null 
					&& p.GameObject.Network.Owner.SteamId.ToString() == playerId );
			if ( feedbackPlayer != null )
			{
				feedbackPlayer.ShowReadyFeedbackRpc( false );
			}

			// If countdown is active and we drop below threshold, cancel it
			if ( CountdownActive && ReadyPlayers.Count < RequiredPlayers )
			{
				CancelCountdown();
			}
		}
		else
		{
			ReadyPlayers.Add( playerId );
			Log.Info( $"{playerId} is ready! ({ReadyPlayers.Count}/{RequiredPlayers})" );

			// Show feedback to the player
			var feedbackPlayer = Scene.GetAllComponents<PlayerController>()
				.FirstOrDefault( p => p.GameObject.Network.Owner != null 
					&& p.GameObject.Network.Owner.SteamId.ToString() == playerId );
			if ( feedbackPlayer != null )
			{
				feedbackPlayer.ShowReadyFeedbackRpc( true );
			}

			// Only start countdown if not already active and threshold met
			if ( !CountdownActive && ReadyPlayers.Count >= RequiredPlayers )
			{
				StartCountdown();
			}
		}

		ReadyCount = ReadyPlayers.Count;
		UpdateReadyCountRpc( ReadyPlayers.Count );

		// Sync the ready player list to all clients
		SyncReadyListRpc( ReadyPlayers.ToList() );
	}

	[Rpc.Broadcast]
	private void SyncReadyListRpc( List<string> readyIds )
	{
		SyncedReadyPlayers.Clear();
		foreach ( var id in readyIds )
		{
			SyncedReadyPlayers.Add( id );
		}
	}

	[Rpc.Broadcast]
	private void CancelCountdown()
	{
		CountdownActive = false;
		CountdownTimer = 0f;
		CountdownBridge.TimeRemaining = 0f;
		CountdownBridge.IsActive = false;
		Log.Info( "Countdown cancelled - not enough players ready" );
	}

	private void StartCountdown()
	{
		// Only host should trigger countdown
		if ( !Networking.IsHost )
			return;
		
		// Broadcast the countdown to all clients
		BroadcastCountdown();
	}

	[Rpc.Broadcast]
	private void BroadcastCountdown()
	{
		CountdownActive = true;
		CountdownTimer = CountdownTime;

		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if ( gameManager?.ReadyCountdownSound != null )
		{
			Sound.Play( gameManager.ReadyCountdownSound );
		}

		// Spawn countdown UI
		var uiObject = Scene.CreateObject();
		uiObject.Name = "Countdown UI";
		var countdownUI = uiObject.Components.Create<CountdownUI>();
		countdownUI.ShowInitialMessage();

		Log.Info( $"Countdown started! Game starting in {CountdownTime} seconds..." );
	}

	private void StartGame()
	{
		if ( !Networking.IsHost )
			return;
		
		Log.Info( "Enough players ready! Starting game..." );
		
		// Pass the list of ready player IDs to GameManager
		var readyList = ReadyPlayers.ToList();
		
		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if ( gameManager != null )
		{
			gameManager.StartGameFromLobby( readyList );
		}
		
		ResetTerminalRpc();
	}

	[Rpc.Broadcast]
	private void ResetTerminalRpc()
	{
		ReadyPlayers.Clear();
		SyncedReadyPlayers.Clear();
		CountdownActive = false;
		CountdownTimer = 0f;
		ReadyCount = 0;
		displayReadyCount = 0;
		CountdownBridge.TimeRemaining = 0f;
		CountdownBridge.IsActive = false;
		Log.Info( "Ready terminal reset" );
	}
}