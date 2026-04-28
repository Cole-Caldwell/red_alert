using System;
using Sandbox;
using System.Collections.Generic;
using System.Linq;

public sealed class BlackjackTable : Component, Component.ITriggerListener
{
	[Property] public SoundEvent JoinSound { get; set; }
	[Property] public SoundEvent LeaveSound { get; set; }
	[Property] public SoundEvent CardDealSound { get; set; }
	[Property] public SoundEvent ChipSound { get; set; }
	[Property] public SoundEvent WinSound { get; set; }
	[Property] public SoundEvent LoseSound { get; set; }

	public const int MaxSeats = 7;

	// Synced seat data
	[Sync] public string Seat0 { get; set; } = "";
	[Sync] public string Seat1 { get; set; } = "";
	[Sync] public string Seat2 { get; set; } = "";
	[Sync] public string Seat3 { get; set; } = "";
	[Sync] public string Seat4 { get; set; } = "";
	[Sync] public string Seat5 { get; set; } = "";
	[Sync] public string Seat6 { get; set; } = "";

	[Sync] public string SeatName0 { get; set; } = "";
	[Sync] public string SeatName1 { get; set; } = "";
	[Sync] public string SeatName2 { get; set; } = "";
	[Sync] public string SeatName3 { get; set; } = "";
	[Sync] public string SeatName4 { get; set; } = "";
	[Sync] public string SeatName5 { get; set; } = "";
	[Sync] public string SeatName6 { get; set; } = "";

	private bool playerInRange = false;
	private BlackjackManager manager;
	private static GameObject blackjackUIObject = null;

	public string GetSeatSteamId( int index )
	{
		return index switch
		{
			0 => Seat0, 1 => Seat1, 2 => Seat2, 3 => Seat3,
			4 => Seat4, 5 => Seat5, 6 => Seat6, _ => ""
		};
	}

	public string GetSeatName( int index )
	{
		return index switch
		{
			0 => SeatName0, 1 => SeatName1, 2 => SeatName2, 3 => SeatName3,
			4 => SeatName4, 5 => SeatName5, 6 => SeatName6, _ => ""
		};
	}

	private void SetSeat( int index, string steamId, string name )
	{
		switch ( index )
		{
			case 0: Seat0 = steamId; SeatName0 = name; break;
			case 1: Seat1 = steamId; SeatName1 = name; break;
			case 2: Seat2 = steamId; SeatName2 = name; break;
			case 3: Seat3 = steamId; SeatName3 = name; break;
			case 4: Seat4 = steamId; SeatName4 = name; break;
			case 5: Seat5 = steamId; SeatName5 = name; break;
			case 6: Seat6 = steamId; SeatName6 = name; break;
		}
	}

	public int GetOccupiedCount()
	{
		int count = 0;
		for ( int i = 0; i < MaxSeats; i++ )
		{
			if ( !string.IsNullOrEmpty( GetSeatSteamId( i ) ) )
				count++;
		}
		return count;
	}

	public int FindSeatForPlayer( string steamId )
	{
		for ( int i = 0; i < MaxSeats; i++ )
		{
			if ( GetSeatSteamId( i ) == steamId )
				return i;
		}
		return -1;
	}

	private int FindEmptySeat()
	{
		for ( int i = 0; i < MaxSeats; i++ )
		{
			if ( string.IsNullOrEmpty( GetSeatSteamId( i ) ) )
				return i;
		}
		return -1;
	}

	protected override void OnStart()
	{
		manager = GameObject.Components.Get<BlackjackManager>();
		if ( manager == null )
		{
			manager = GameObject.Components.Create<BlackjackManager>();
			manager.Table = this;
		}
	}

	protected override void OnUpdate()
	{
		if ( !playerInRange ) return;

		var localPlayer = Scene.GetAllComponents<PlayerController>()
			.FirstOrDefault( p => !p.IsProxy && p.GameObject.Network.Owner != null );

		if ( localPlayer == null ) return;

		// Don't allow during game
		var gm = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if ( gm != null && gm.CurrentState != GameManager.GameState.WaitingInLobby )
		{
			// Auto-eject if seated
			string localSteamId = Connection.Local?.SteamId.ToString() ?? "";
			if ( FindSeatForPlayer( localSteamId ) >= 0 )
			{
				LeaveTable( localPlayer );
			}
			else if ( BlackjackBridge.IsOpen && BlackjackBridge.ActiveTable == this )
			{
				BlackjackBridge.Close();
				DestroyUI();
			}
			return;
		}

		// Show interact prompt
		string localId = Connection.Local?.SteamId.ToString() ?? "";
		bool isSeated = FindSeatForPlayer( localId ) >= 0;

		if ( isSeated )
		{
			Gizmo.Draw.Color = Color.Green;
			Gizmo.Draw.Text( "Press E — Leave Table", new Transform( WorldPosition + Vector3.Up * 50 ), "Consolas", 18 );
		}
		else if ( GetOccupiedCount() >= MaxSeats )
		{
			Gizmo.Draw.Color = Color.Red;
			Gizmo.Draw.Text( "TABLE FULL", new Transform( WorldPosition + Vector3.Up * 50 ), "Consolas", 18 );
		}
		else
		{
			Gizmo.Draw.Color = Color.Yellow;
			Gizmo.Draw.Text( "Press E — Blackjack Table", new Transform( WorldPosition + Vector3.Up * 50 ), "Consolas", 18 );
		}

		if ( Input.Pressed( "Use" ) )
		{
			if ( isSeated )
			{
				LeaveTable( localPlayer );
			}
			else if ( GetOccupiedCount() < MaxSeats )
			{
				JoinTable( localPlayer );
			}
		}
	}

	private void JoinTable( PlayerController player )
	{
		string steamId = Connection.Local?.SteamId.ToString() ?? "";
		string displayName = player.GameObject.Root.Name.Replace( "Player - ", "" );

		// Don't pick a seat locally - two clients pressing E in the same tick would both see the same
		// empty seat and claim it. Ask the host to pick authoritatively.
		RequestJoinRpc( steamId, displayName );
	}

	[Rpc.Broadcast]
	public void RequestJoinRpc( string steamId, string displayName )
	{
		if ( !Networking.IsHost ) return;
		if ( string.IsNullOrEmpty( steamId ) ) return;
		if ( FindSeatForPlayer( steamId ) >= 0 ) return; // already seated

		int seat = FindEmptySeat();
		if ( seat < 0 ) return; // table full

		BroadcastPlayerJoined( steamId, displayName, seat );
	}

	public void SpectateTable( PlayerController player = null )
	{
		string steamId = Connection.Local?.SteamId.ToString() ?? "";
		int seat = FindSeatForPlayer( steamId );

		if ( seat < 0 ) return;

		if ( player == null )
		{
			player = Scene.GetAllComponents<PlayerController>()
				.FirstOrDefault( p => !p.IsProxy && p.GameObject.Network.Owner != null );
		}

		// Commit net credit change as if leaving — we are abandoning the seat
		BlackjackBridge.LastKnownBalance = BlackjackBridge.CachedBalance;
		BlackjackBridge.LastLeaveTime = DateTime.UtcNow;

		int net = BlackjackBridge.SessionNetChange;
		if ( net > 0 )
		{
			Sandbox.Services.Stats.Increment( "credits", net );
		}
		else if ( net < 0 )
		{
			Sandbox.Services.Stats.Increment( "credits_spent", -net );
		}

		BroadcastPlayerLeft( steamId, seat );
		player?.UnmountFromStation();

		// Keep the UI open in spectator mode
		BlackjackBridge.LocalSeatIndex = -1;
		BlackjackBridge.SessionNetChange = 0;
		BlackjackBridge.CurrentBet = 0;

		if ( LeaveSound != null )
		{
			var handle = Sound.Play( LeaveSound );
			if ( handle != null ) handle.ListenLocal = true;
		}
	}

	public void LeaveTable( PlayerController player = null )
	{
		string steamId = Connection.Local?.SteamId.ToString() ?? "";
		int seat = FindSeatForPlayer( steamId );

		if ( seat < 0 ) return;

		if ( player == null )
		{
			player = Scene.GetAllComponents<PlayerController>()
				.FirstOrDefault( p => !p.IsProxy && p.GameObject.Network.Owner != null );
		}

		// Remember balance before clearing state, so quick remounts use accurate data
		BlackjackBridge.LastKnownBalance = BlackjackBridge.CachedBalance;
		BlackjackBridge.LastLeaveTime = DateTime.UtcNow;

		// Commit net credit change to stats on unmount
		int net = BlackjackBridge.SessionNetChange;
		if ( net > 0 )
		{
			Sandbox.Services.Stats.Increment( "credits", net );
		}
		else if ( net < 0 )
		{
			Sandbox.Services.Stats.Increment( "credits_spent", -net );
		}

		BroadcastPlayerLeft( steamId, seat );
		player?.UnmountFromStation();

		BlackjackBridge.Close();
		DestroyUI();

		if ( LeaveSound != null )
		{
			var handle = Sound.Play( LeaveSound );
			if ( handle != null ) handle.ListenLocal = true;
		}
	}

	[Rpc.Broadcast]
	public void BroadcastPlayerJoined( string steamId, string name, int seatIndex )
	{
		SetSeat( seatIndex, steamId, name );

		// Update bridge seat data only for clients watching this table
		if ( this == BlackjackBridge.ActiveTable || BlackjackBridge.ActiveTable == null )
		{
			BlackjackBridge.Seats[seatIndex].IsOccupied = true;
			BlackjackBridge.Seats[seatIndex].PlayerName = name;
			BlackjackBridge.Seats[seatIndex].SteamId = ulong.TryParse( steamId, out var id ) ? id : 0;
		}

		// If this broadcast is for the local player, do the local mount/UI/sound work now that we know our seat
		string localSteamId = Connection.Local?.SteamId.ToString() ?? "";
		if ( steamId == localSteamId )
		{
			var localPlayer = Scene.GetAllComponents<PlayerController>()
				.FirstOrDefault( p => !p.IsProxy && p.GameObject.Network.Owner != null );

			if ( localPlayer != null )
			{
				localPlayer.MountToBlackjackTable( this );
				FetchBalance();
				OverrideInstanceBalance( name );
				EnsureUIExists();
				BlackjackBridge.Open( this, seatIndex );

				if ( JoinSound != null )
				{
					var handle = Sound.Play( JoinSound );
					if ( handle != null ) handle.ListenLocal = true;
				}
			}
		}

		// Host: notify manager
		if ( Networking.IsHost )
		{
			manager?.OnPlayerJoined( seatIndex, steamId, name );
		}
	}

	[Rpc.Broadcast]
	public void BroadcastPlayerLeft( string steamId, int seatIndex )
	{
		SetSeat( seatIndex, "", "" );

		// Clear bridge seat data only for clients watching this table
		if ( this == BlackjackBridge.ActiveTable || BlackjackBridge.ActiveTable == null )
		{
			BlackjackBridge.Seats[seatIndex].IsOccupied = false;
			BlackjackBridge.Seats[seatIndex].PlayerName = "";
			BlackjackBridge.Seats[seatIndex].SteamId = 0;
			BlackjackBridge.Seats[seatIndex].ClearForNewRound();
		}

		// Host: notify manager
		if ( Networking.IsHost )
		{
			manager?.OnPlayerLeft( seatIndex, steamId );
		}
	}

	[Rpc.Broadcast]
	public void EjectAllPlayers()
	{
		// Each client checks if they are seated and leaves
		string localId = Connection.Local?.SteamId.ToString() ?? "";
		int seat = FindSeatForPlayer( localId );
		if ( seat >= 0 )
		{
			var localPlayer = Scene.GetAllComponents<PlayerController>()
				.FirstOrDefault( p => !p.IsProxy && p.GameObject.Network.Owner != null );

			// Remember balance before clearing state
			BlackjackBridge.LastKnownBalance = BlackjackBridge.CachedBalance;
			BlackjackBridge.LastLeaveTime = DateTime.UtcNow;

			// Commit net credit change to stats before closing
			int net = BlackjackBridge.SessionNetChange;
			if ( net > 0 )
			{
				Sandbox.Services.Stats.Increment( "credits", net );
			}
			else if ( net < 0 )
			{
				Sandbox.Services.Stats.Increment( "credits_spent", -net );
			}

			localPlayer?.UnmountFromStation();
			BlackjackBridge.Close();
			DestroyUI();
		}

		// Host clears all seats
		if ( Networking.IsHost )
		{
			for ( int i = 0; i < MaxSeats; i++ )
			{
				SetSeat( i, "", "" );
				BlackjackBridge.Seats[i].IsOccupied = false;
				BlackjackBridge.Seats[i].PlayerName = "";
				BlackjackBridge.Seats[i].SteamId = 0;
				BlackjackBridge.Seats[i].ClearForNewRound();
			}
			manager?.OnAllPlayersEjected();
		}
	}

	private void EnsureUIExists()
	{
		if ( blackjackUIObject != null && blackjackUIObject.IsValid ) return;

		blackjackUIObject = Scene.CreateObject();
		blackjackUIObject.Name = "Blackjack UI";

		var screenPanel = blackjackUIObject.Components.Create<ScreenPanel>();
		screenPanel.ZIndex = 600;

		blackjackUIObject.Components.Create<BlackjackUI>();

		Log.Info( "[BlackjackTable] Created BlackjackUI overlay" );
	}

	private static void DestroyUI()
	{
		if ( blackjackUIObject != null && blackjackUIObject.IsValid )
			blackjackUIObject.Destroy();
		blackjackUIObject = null;
	}

	private async void FetchBalance()
	{
		try
		{
			var creditsBoard = Sandbox.Services.Leaderboards.GetFromStat( Game.Ident, "credits" );
			creditsBoard.MaxEntries = 50;
			await creditsBoard.Refresh();

			var spentBoard = Sandbox.Services.Leaderboards.GetFromStat( Game.Ident, "credits_spent" );
			spentBoard.MaxEntries = 50;
			await spentBoard.Refresh();

			long localSteamId = (long)(Connection.Local?.SteamId ?? 0UL);
			int earned = 0;
			int spent = 0;

			foreach ( var entry in creditsBoard.Entries )
			{
				if ( entry.SteamId == localSteamId )
				{
					earned = (int)entry.Value;
					break;
				}
			}

			foreach ( var entry in spentBoard.Entries )
			{
				if ( entry.SteamId == localSteamId )
				{
					spent = (int)entry.Value;
					break;
				}
			}

			// If we left a table recently, the API data is likely stale — use our remembered balance
			if ( BlackjackBridge.LastKnownBalance.HasValue
				&& BlackjackBridge.LastLeaveTime.HasValue
				&& (DateTime.UtcNow - BlackjackBridge.LastLeaveTime.Value).TotalMinutes < 2.5 )
			{
				BlackjackBridge.CachedBalance = BlackjackBridge.LastKnownBalance.Value;
				Log.Info( $"[BlackjackTable] Using remembered balance: {BlackjackBridge.CachedBalance} (API returned: {earned - spent})" );
			}
			else
			{
				BlackjackBridge.CachedBalance = earned - spent;
				BlackjackBridge.LastKnownBalance = null;
				BlackjackBridge.LastLeaveTime = null;
				Log.Info( $"[BlackjackTable] Balance: {BlackjackBridge.CachedBalance} (earned: {earned}, spent: {spent})" );
			}
		}
		catch ( System.Exception e )
		{
			Log.Warning( $"[BlackjackTable] Failed to fetch balance: {e.Message}" );
		}
	}

	private async void OverrideInstanceBalance( string playerName )
	{
		await GameTask.DelaySeconds( 2f );
		if ( System.Text.RegularExpressions.Regex.IsMatch( playerName, @"\(\d+\)$" ) )
		{
			BlackjackBridge.CachedBalance = 10000;
			Log.Info( $"[BlackjackTable] Instance detected ({playerName}) — balance set to 10000" );
		}
	}

	// Sound helpers called by BlackjackManager
	public void PlayCardSound()
	{
		if ( CardDealSound != null )
		{
			var handle = Sound.Play( CardDealSound );
			if ( handle != null ) handle.ListenLocal = true;
		}
	}

	public void PlayChipSound()
	{
		if ( ChipSound != null )
		{
			var handle = Sound.Play( ChipSound );
			if ( handle != null ) handle.ListenLocal = true;
		}
	}

	public void PlayWinSound()
	{
		if ( WinSound != null )
		{
			var handle = Sound.Play( WinSound );
			if ( handle != null ) handle.ListenLocal = true;
		}
	}

	public void PlayLoseSound()
	{
		if ( LoseSound != null )
		{
			var handle = Sound.Play( LoseSound );
			if ( handle != null ) handle.ListenLocal = true;
		}
	}

	void ITriggerListener.OnTriggerEnter( Collider other )
	{
		var player = other.GameObject.Components.Get<PlayerController>();
		if ( player != null && !player.IsProxy )
		{
			playerInRange = true;
		}
	}

	void ITriggerListener.OnTriggerExit( Collider other )
	{
		var player = other.GameObject.Components.Get<PlayerController>();
		if ( player != null && !player.IsProxy )
		{
			playerInRange = false;

			// Auto-leave if seated and walked away
			string localId = Connection.Local?.SteamId.ToString() ?? "";
			if ( FindSeatForPlayer( localId ) >= 0 )
			{
				LeaveTable( player );
			}
			else if ( BlackjackBridge.IsOpen && BlackjackBridge.ActiveTable == this )
			{
				BlackjackBridge.Close();
				DestroyUI();
			}
		}
	}
}
