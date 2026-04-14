using System;
using Sandbox;
using System.Collections.Generic;
using System.Linq;

public sealed class PokerTable : Component, Component.ITriggerListener
{
	[Property] public SoundEvent JoinSound { get; set; }
	[Property] public SoundEvent LeaveSound { get; set; }
	[Property] public SoundEvent CardDealSound { get; set; }
	[Property] public SoundEvent ChipSound { get; set; }
	[Property] public SoundEvent WinSound { get; set; }
	[Property] public SoundEvent LoseSound { get; set; }

	public const int MaxSeats = 10;

	// Synced seat occupancy. Chips per seat are also synced so spectators can see stack sizes.
	[Sync] public string Seat0 { get; set; } = "";
	[Sync] public string Seat1 { get; set; } = "";
	[Sync] public string Seat2 { get; set; } = "";
	[Sync] public string Seat3 { get; set; } = "";
	[Sync] public string Seat4 { get; set; } = "";
	[Sync] public string Seat5 { get; set; } = "";
	[Sync] public string Seat6 { get; set; } = "";
	[Sync] public string Seat7 { get; set; } = "";
	[Sync] public string Seat8 { get; set; } = "";
	[Sync] public string Seat9 { get; set; } = "";

	[Sync] public string SeatName0 { get; set; } = "";
	[Sync] public string SeatName1 { get; set; } = "";
	[Sync] public string SeatName2 { get; set; } = "";
	[Sync] public string SeatName3 { get; set; } = "";
	[Sync] public string SeatName4 { get; set; } = "";
	[Sync] public string SeatName5 { get; set; } = "";
	[Sync] public string SeatName6 { get; set; } = "";
	[Sync] public string SeatName7 { get; set; } = "";
	[Sync] public string SeatName8 { get; set; } = "";
	[Sync] public string SeatName9 { get; set; } = "";

	[Sync] public long SeatChips0 { get; set; }
	[Sync] public long SeatChips1 { get; set; }
	[Sync] public long SeatChips2 { get; set; }
	[Sync] public long SeatChips3 { get; set; }
	[Sync] public long SeatChips4 { get; set; }
	[Sync] public long SeatChips5 { get; set; }
	[Sync] public long SeatChips6 { get; set; }
	[Sync] public long SeatChips7 { get; set; }
	[Sync] public long SeatChips8 { get; set; }
	[Sync] public long SeatChips9 { get; set; }

	private bool playerInRange = false;
	private PokerManager manager;
	private static GameObject pokerUIObject = null;

	public string GetSeatSteamId( int index ) => index switch
	{
		0 => Seat0, 1 => Seat1, 2 => Seat2, 3 => Seat3, 4 => Seat4,
		5 => Seat5, 6 => Seat6, 7 => Seat7, 8 => Seat8, 9 => Seat9, _ => ""
	};

	public string GetSeatName( int index ) => index switch
	{
		0 => SeatName0, 1 => SeatName1, 2 => SeatName2, 3 => SeatName3, 4 => SeatName4,
		5 => SeatName5, 6 => SeatName6, 7 => SeatName7, 8 => SeatName8, 9 => SeatName9, _ => ""
	};

	public long GetSeatChips( int index ) => index switch
	{
		0 => SeatChips0, 1 => SeatChips1, 2 => SeatChips2, 3 => SeatChips3, 4 => SeatChips4,
		5 => SeatChips5, 6 => SeatChips6, 7 => SeatChips7, 8 => SeatChips8, 9 => SeatChips9, _ => 0
	};

	public void SetSeatChips( int index, long chips )
	{
		switch ( index )
		{
			case 0: SeatChips0 = chips; break;
			case 1: SeatChips1 = chips; break;
			case 2: SeatChips2 = chips; break;
			case 3: SeatChips3 = chips; break;
			case 4: SeatChips4 = chips; break;
			case 5: SeatChips5 = chips; break;
			case 6: SeatChips6 = chips; break;
			case 7: SeatChips7 = chips; break;
			case 8: SeatChips8 = chips; break;
			case 9: SeatChips9 = chips; break;
		}
	}

	private void SetSeatRaw( int index, string steamId, string name )
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
			case 7: Seat7 = steamId; SeatName7 = name; break;
			case 8: Seat8 = steamId; SeatName8 = name; break;
			case 9: Seat9 = steamId; SeatName9 = name; break;
		}
	}

	private bool IsLocalActiveTable()
	{
		if ( PokerBridge.ActiveTable == null || PokerBridge.ActiveTable.GameObject == null ) return false;
		return GameObject.Id == PokerBridge.ActiveTable.GameObject.Id;
	}

	public int GetOccupiedCount()
	{
		int count = 0;
		for ( int i = 0; i < MaxSeats; i++ )
			if ( !string.IsNullOrEmpty( GetSeatSteamId( i ) ) ) count++;
		return count;
	}

	public int FindSeatForPlayer( string steamId )
	{
		for ( int i = 0; i < MaxSeats; i++ )
			if ( GetSeatSteamId( i ) == steamId ) return i;
		return -1;
	}

	private int FindEmptySeat()
	{
		for ( int i = 0; i < MaxSeats; i++ )
			if ( string.IsNullOrEmpty( GetSeatSteamId( i ) ) ) return i;
		return -1;
	}

	protected override void OnStart()
	{
		manager = GameObject.Components.Get<PokerManager>();
		if ( manager == null )
		{
			manager = GameObject.Components.Create<PokerManager>();
			manager.Table = this;
		}
	}

	protected override void OnUpdate()
	{
		if ( !playerInRange ) return;

		var localPlayer = Scene.GetAllComponents<PlayerController>()
			.FirstOrDefault( p => !p.IsProxy && p.GameObject.Network.Owner != null );

		if ( localPlayer == null ) return;

		// Don't allow during game (match Blackjack behavior)
		var gm = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if ( gm != null && gm.CurrentState != GameManager.GameState.WaitingInLobby )
		{
			string sid = Connection.Local?.SteamId.ToString() ?? "";
			if ( FindSeatForPlayer( sid ) >= 0 )
				LeaveTable( localPlayer );
			return;
		}

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
			Gizmo.Draw.Text( "Press E — Texas Hold'em", new Transform( WorldPosition + Vector3.Up * 50 ), "Consolas", 18 );
		}

		if ( Input.Pressed( "Use" ) )
		{
			if ( isSeated )
				LeaveTable( localPlayer );
			else if ( GetOccupiedCount() < MaxSeats )
				OpenBuyInDialog( localPlayer );
		}
	}

	private void OpenBuyInDialog( PlayerController player )
	{
		int seat = FindEmptySeat();
		if ( seat < 0 ) return;

		EnsureUIExists();
		FetchBalance();

		// Open the UI in spectate mode and show the buy-in dialog
		PokerBridge.Open( this, -1 );
		PokerBridge.PendingSeat = seat;
		PokerBridge.ShowBuyInDialog = true;
		PokerBridge.SelectedBuyIn = System.Math.Clamp( PokerBridge.CachedBalance / 2, PokerBridge.MinBuyIn, PokerBridge.MaxBuyIn );
		if ( PokerBridge.SelectedBuyIn < PokerBridge.MinBuyIn ) PokerBridge.SelectedBuyIn = PokerBridge.MinBuyIn;
	}

	/// <summary>
	/// Called from the buy-in dialog Confirm button. Validates client-side, then sends RPC.
	/// </summary>
	public void ConfirmBuyIn( int buyIn )
	{
		var localPlayer = Scene.GetAllComponents<PlayerController>()
			.FirstOrDefault( p => !p.IsProxy && p.GameObject.Network.Owner != null );
		if ( localPlayer == null ) return;

		int seat = PokerBridge.PendingSeat;
		if ( seat < 0 ) return;

		buyIn = System.Math.Clamp( buyIn, PokerBridge.MinBuyIn, PokerBridge.MaxBuyIn );
		if ( buyIn > PokerBridge.CachedBalance ) buyIn = PokerBridge.CachedBalance;
		if ( buyIn < PokerBridge.MinBuyIn ) return;

		string steamId = Connection.Local?.SteamId.ToString() ?? "";
		string displayName = localPlayer.GameObject.Root.Name.Replace( "Player - ", "" );

		// Deduct buy-in immediately for responsiveness; refunded on leave (chips - buyIn = session delta)
		PokerBridge.CachedBalance -= buyIn;
		PokerBridge.SessionNetChange -= buyIn;
		PokerBridge.LocalSeatIndex = seat;
		PokerBridge.ShowBuyInDialog = false;
		PokerBridge.PendingSeat = -1;

		BroadcastPlayerJoined( steamId, displayName, seat, buyIn );
		localPlayer.MountToPokerTable( this );

		if ( JoinSound != null )
		{
			var handle = Sound.Play( JoinSound );
			if ( handle != null ) handle.ListenLocal = true;
		}
	}

	public void CancelBuyIn()
	{
		PokerBridge.ShowBuyInDialog = false;
		PokerBridge.PendingSeat = -1;
		PokerBridge.Close();
	}

	public void LeaveTable( PlayerController player = null )
	{
		string steamId = Connection.Local?.SteamId.ToString() ?? "";
		int seat = FindSeatForPlayer( steamId );

		if ( seat < 0 )
		{
			// Was just spectating with the buy-in dialog open
			CancelBuyIn();
			return;
		}

		if ( player == null )
		{
			player = Scene.GetAllComponents<PlayerController>()
				.FirstOrDefault( p => !p.IsProxy && p.GameObject.Network.Owner != null );
		}

		// Cash out remaining chips at the table back into the session balance.
		// Buy-in was deducted from CachedBalance/SessionNetChange on join, so adding
		// the chips left on the table now yields the true session delta:
		//   net = chipsAtLeave - buyIn
		int chipsAtTable = (int)PokerBridge.Seats[seat].Chips;
		PokerBridge.CachedBalance += chipsAtTable;
		PokerBridge.SessionNetChange += chipsAtTable;

		// Remember balance before clearing state
		PokerBridge.LastKnownBalance = PokerBridge.CachedBalance;
		PokerBridge.LastLeaveTime = DateTime.UtcNow;

		// Commit net credit change to permanent stats
		int net = PokerBridge.SessionNetChange;
		if ( net > 0 )
			Sandbox.Services.Stats.Increment( "credits", net );
		else if ( net < 0 )
			Sandbox.Services.Stats.Increment( "credits_spent", -net );

		BroadcastPlayerLeft( steamId, seat );
		player?.UnmountFromStation();

		PokerBridge.Close();

		if ( LeaveSound != null )
		{
			var handle = Sound.Play( LeaveSound );
			if ( handle != null ) handle.ListenLocal = true;
		}
	}

	[Rpc.Broadcast]
	public void BroadcastPlayerJoined( string steamId, string name, int seatIndex, int buyIn )
	{
		// If the requested seat is already taken, find another empty one.
		// This prevents two clients from racing into the same seat when both
		// call FindEmptySeat() locally before the first RPC arrives.
		if ( !string.IsNullOrEmpty( GetSeatSteamId( seatIndex ) ) )
		{
			int alt = FindEmptySeat();
			if ( alt < 0 ) return; // table full — reject silently
			seatIndex = alt;
		}

		SetSeatRaw( seatIndex, steamId, name );
		if ( Networking.IsHost )
			SetSeatChips( seatIndex, buyIn );

		// If this broadcast is for the local player, update their seat index
		string localId = Connection.Local?.SteamId.ToString() ?? "";
		if ( steamId == localId )
			PokerBridge.LocalSeatIndex = seatIndex;

		if ( IsLocalActiveTable() )
		{
			PokerBridge.Seats[seatIndex].IsOccupied = true;
			PokerBridge.Seats[seatIndex].PlayerName = name;
			PokerBridge.Seats[seatIndex].SteamId = ulong.TryParse( steamId, out var id ) ? id : 0;
			PokerBridge.Seats[seatIndex].Chips = buyIn;
		}

		if ( Networking.IsHost )
			manager?.OnPlayerJoined( seatIndex, steamId, name, buyIn );
	}

	[Rpc.Broadcast]
	public void BroadcastPlayerLeft( string steamId, int seatIndex )
	{
		SetSeatRaw( seatIndex, "", "" );
		if ( Networking.IsHost )
			SetSeatChips( seatIndex, 0 );

		if ( IsLocalActiveTable() )
		{
			PokerBridge.Seats[seatIndex].IsOccupied = false;
			PokerBridge.Seats[seatIndex].PlayerName = "";
			PokerBridge.Seats[seatIndex].SteamId = 0;
			PokerBridge.Seats[seatIndex].ClearForNewHand();
			PokerBridge.Seats[seatIndex].Chips = 0;
		}

		if ( Networking.IsHost )
			manager?.OnPlayerLeft( seatIndex, steamId );
	}

	[Rpc.Broadcast]
	public void EjectAllPlayers()
	{
		string localId = Connection.Local?.SteamId.ToString() ?? "";
		int seat = FindSeatForPlayer( localId );
		if ( seat >= 0 )
		{
			var localPlayer = Scene.GetAllComponents<PlayerController>()
				.FirstOrDefault( p => !p.IsProxy && p.GameObject.Network.Owner != null );

			PokerBridge.LastKnownBalance = PokerBridge.CachedBalance;
			PokerBridge.LastLeaveTime = DateTime.UtcNow;

			int net = PokerBridge.SessionNetChange;
			if ( net > 0 )
				Sandbox.Services.Stats.Increment( "credits", net );
			else if ( net < 0 )
				Sandbox.Services.Stats.Increment( "credits_spent", -net );

			localPlayer?.UnmountFromStation();
			PokerBridge.Close();
		}

		if ( Networking.IsHost )
		{
			for ( int i = 0; i < MaxSeats; i++ )
			{
				SetSeatRaw( i, "", "" );
				SetSeatChips( i, 0 );
				PokerBridge.Seats[i].IsOccupied = false;
				PokerBridge.Seats[i].PlayerName = "";
				PokerBridge.Seats[i].SteamId = 0;
				PokerBridge.Seats[i].ClearForNewHand();
				PokerBridge.Seats[i].Chips = 0;
			}
			manager?.OnAllPlayersEjected();
		}
	}

	private void EnsureUIExists()
	{
		if ( pokerUIObject != null && pokerUIObject.IsValid ) return;

		pokerUIObject = Scene.CreateObject();
		pokerUIObject.Name = "Poker UI";

		var screenPanel = pokerUIObject.Components.Create<ScreenPanel>();
		screenPanel.ZIndex = 600;

		pokerUIObject.Components.Create<PokerUI>();
		Log.Info( "[PokerTable] Created PokerUI overlay" );
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

			long localSteamId = (long)(Connection.Local?.SteamId ?? 0);
			int earned = 0;
			int spent = 0;

			foreach ( var entry in creditsBoard.Entries )
			{
				if ( entry.SteamId == localSteamId ) { earned = (int)entry.Value; break; }
			}
			foreach ( var entry in spentBoard.Entries )
			{
				if ( entry.SteamId == localSteamId ) { spent = (int)entry.Value; break; }
			}

			// If we left a table recently, the API data is likely stale
			if ( PokerBridge.LastKnownBalance.HasValue
				&& PokerBridge.LastLeaveTime.HasValue
				&& (DateTime.UtcNow - PokerBridge.LastLeaveTime.Value).TotalMinutes < 2.5 )
			{
				PokerBridge.CachedBalance = PokerBridge.LastKnownBalance.Value;
				Log.Info( $"[PokerTable] Using remembered balance: {PokerBridge.CachedBalance} (API returned: {earned - spent})" );
			}
			else
			{
				PokerBridge.CachedBalance = earned - spent;
				PokerBridge.LastKnownBalance = null;
				PokerBridge.LastLeaveTime = null;
				Log.Info( $"[PokerTable] Balance: {PokerBridge.CachedBalance} (earned: {earned}, spent: {spent})" );
			}
		}
		catch ( System.Exception e )
		{
			Log.Warning( $"[PokerTable] Failed to fetch balance: {e.Message}" );
		}
	}

	// Sound helpers (called from PokerManager via RPC handlers)
	public void PlayCardSound() { if ( CardDealSound != null ) { var h = Sound.Play( CardDealSound ); if ( h != null ) h.ListenLocal = true; } }
	public void PlayChipSound() { if ( ChipSound != null ) { var h = Sound.Play( ChipSound ); if ( h != null ) h.ListenLocal = true; } }
	public void PlayWinSound() { if ( WinSound != null ) { var h = Sound.Play( WinSound ); if ( h != null ) h.ListenLocal = true; } }
	public void PlayLoseSound() { if ( LoseSound != null ) { var h = Sound.Play( LoseSound ); if ( h != null ) h.ListenLocal = true; } }

	void ITriggerListener.OnTriggerEnter( Collider other )
	{
		var player = other.GameObject.Components.Get<PlayerController>();
		if ( player != null && !player.IsProxy )
			playerInRange = true;
	}

	void ITriggerListener.OnTriggerExit( Collider other )
	{
		var player = other.GameObject.Components.Get<PlayerController>();
		if ( player != null && !player.IsProxy )
		{
			playerInRange = false;
			string localId = Connection.Local?.SteamId.ToString() ?? "";
			if ( FindSeatForPlayer( localId ) >= 0 )
				LeaveTable( player );
			else if ( PokerBridge.ShowBuyInDialog )
				CancelBuyIn();
		}
	}
}
