using System;
using Sandbox;
using System.Collections.Generic;

public enum PokerPhase
{
	Idle,
	WaitingForPlayers,
	PostingBlinds,
	DealHoleCards,
	PreFlopBetting,
	DealFlop,
	FlopBetting,
	DealTurn,
	TurnBetting,
	DealRiver,
	RiverBetting,
	Showdown,
	Payout,
	Cleanup
}

public enum PokerActionType
{
	None,
	Fold,
	Check,
	Call,
	Bet,
	Raise,
	AllIn,
	SmallBlind,
	BigBlind
}

public class PokerSeatData
{
	public string PlayerName { get; set; } = "";
	public ulong SteamId { get; set; }
	public bool IsOccupied { get; set; }

	public long Chips { get; set; }
	public long ChipsInPotThisRound { get; set; }
	public long TotalContributedThisHand { get; set; }

	public bool HasFolded { get; set; }
	public bool IsAllIn { get; set; }
	public bool IsSittingOut { get; set; }     // joined mid-hand or out of chips

	public bool IsButton { get; set; }
	public bool IsSmallBlind { get; set; }
	public bool IsBigBlind { get; set; }

	// Hole cards: face-down placeholders for remote seats; real cards only for local player
	public List<CardData> HoleCards { get; set; } = new();

	public PokerActionType LastAction { get; set; }
	public long LastActionAmount { get; set; }
	public float LastActionTime { get; set; }   // RealTime.Now of last action — for fade

	// Showdown reveal
	public bool ShowdownRevealed { get; set; }
	public string ShowdownLabel { get; set; } = "";
	public PokerHandRank ShowdownRank { get; set; }

	public void ClearForNewHand()
	{
		ChipsInPotThisRound = 0;
		TotalContributedThisHand = 0;
		HasFolded = false;
		IsAllIn = false;
		IsButton = false;
		IsSmallBlind = false;
		IsBigBlind = false;
		HoleCards.Clear();
		LastAction = PokerActionType.None;
		LastActionAmount = 0;
		ShowdownRevealed = false;
		ShowdownLabel = "";
		ShowdownRank = PokerHandRank.HighCard;
	}
}

public static class PokerBridge
{
	public const int MaxSeats = 10;
	public const int SmallBlind = 10;
	public const int BigBlind = 25;
	public const int MinBuyIn = 250;
	public const int MaxBuyIn = 2500;

	// Visibility / table reference
	public static bool IsOpen { get; set; } = false;
	public static PokerTable ActiveTable { get; set; } = null;
	public static int LocalSeatIndex { get; set; } = -1;

	// Credit balance (mirrors BlackjackBridge pattern)
	public static int CachedBalance { get; set; } = 0;
	public static int SessionNetChange { get; set; } = 0;
	public static int? LastKnownBalance { get; set; } = null;
	public static DateTime? LastLeaveTime { get; set; } = null;

	// Buy-in dialog
	public static bool ShowBuyInDialog { get; set; } = false;
	public static int PendingSeat { get; set; } = -1;
	public static int SelectedBuyIn { get; set; } = 1000;

	// Seat data
	public static PokerSeatData[] Seats { get; set; } = new PokerSeatData[MaxSeats]
	{
		new(), new(), new(), new(), new(), new(), new(), new(), new(), new()
	};

	// Game state
	public static bool GameStarted { get; set; } = false; // true once Play is pressed, stays true for the session
	public static PokerPhase Phase { get; set; } = PokerPhase.Idle;
	public static List<CardData> CommunityCards { get; set; } = new();
	public static long MainPot { get; set; } = 0;
	public static List<long> SidePots { get; set; } = new();
	public static long CurrentBet { get; set; } = 0;       // highest bet in current round
	public static long MinRaise { get; set; } = BigBlind;
	public static int ButtonSeat { get; set; } = -1;
	public static int ActiveSeat { get; set; } = -1;
	public static float ActionDeadline { get; set; } = 0f; // RealTime.Now when current action times out
	public static float TurnDuration { get; set; } = 20f;

	// Local player's actual hole cards (only for the local seated player)
	public static List<CardData> LocalHoleCards { get; set; } = new();

	// Action availability for the local player (recomputed on phase / active seat changes)
	public static bool IsLocalPlayerTurn { get; set; }
	public static bool CanFold { get; set; }
	public static bool CanCheck { get; set; }
	public static bool CanCall { get; set; }
	public static bool CanBet { get; set; }
	public static bool CanRaise { get; set; }
	public static bool CanAllIn { get; set; }
	public static long CallAmount { get; set; }    // chips needed to call
	public static long MinBetAmount { get; set; }  // minimum legal opening bet
	public static long MinRaiseTo { get; set; }    // minimum legal raise-to amount
	public static long MaxRaiseTo { get; set; }    // local stack cap (all-in)
	public static long RaiseSliderValue { get; set; }

	public static string StatusMessage { get; set; } = "";
	public static string LastWinnerText { get; set; } = "";

	/// <summary>
	/// True when: local player is seated, no game session has started yet, and 2+ players are present.
	/// Once Play is pressed, GameStarted becomes true and hands auto-flow without needing Play again.
	/// </summary>
	public static bool CanStartGame
	{
		get
		{
			if ( GameStarted ) return false;
			if ( LocalSeatIndex < 0 ) return false;
			int count = 0;
			for ( int i = 0; i < MaxSeats; i++ )
				if ( Seats[i].IsOccupied && Seats[i].Chips >= BigBlind ) count++;
			return count >= 2;
		}
	}

	public static void Open( PokerTable table, int seatIndex )
	{
		IsOpen = true;
		ActiveTable = table;
		LocalSeatIndex = seatIndex;
		SessionNetChange = 0;
		ShowBuyInDialog = false;
		PendingSeat = -1;

		// Refresh seat data from table sync
		for ( int i = 0; i < MaxSeats; i++ )
		{
			string sid = table.GetSeatSteamId( i );
			string name = table.GetSeatName( i );
			long chips = table.GetSeatChips( i );
			bool occupied = !string.IsNullOrEmpty( sid );

			Seats[i].ClearForNewHand();
			Seats[i].IsOccupied = occupied;
			Seats[i].PlayerName = name;
			Seats[i].SteamId = ulong.TryParse( sid, out var id ) ? id : 0;
			Seats[i].Chips = chips;
		}

		CommunityCards.Clear();
		LocalHoleCards.Clear();
		MainPot = 0;
		SidePots.Clear();
		CurrentBet = 0;
		MinRaise = BigBlind;
		ButtonSeat = -1;
		ActiveSeat = -1;
		ResetLocalActions();
		GameStarted = false;
		Phase = PokerPhase.WaitingForPlayers;
		StatusMessage = "Waiting for players...";
		LastWinnerText = "";
	}

	public static void Close()
	{
		IsOpen = false;
		ActiveTable = null;
		LocalSeatIndex = -1;
		GameStarted = false;
		ShowBuyInDialog = false;
		PendingSeat = -1;
		LocalHoleCards.Clear();
		CommunityCards.Clear();
		ResetLocalActions();
	}

	public static void ResetForNewHand()
	{
		CommunityCards.Clear();
		LocalHoleCards.Clear();
		MainPot = 0;
		SidePots.Clear();
		CurrentBet = 0;
		MinRaise = BigBlind;
		LastWinnerText = "";
		foreach ( var seat in Seats )
			seat.ClearForNewHand();
		ResetLocalActions();
	}

	public static void ResetLocalActions()
	{
		IsLocalPlayerTurn = false;
		CanFold = false;
		CanCheck = false;
		CanCall = false;
		CanBet = false;
		CanRaise = false;
		CanAllIn = false;
		CallAmount = 0;
		MinBetAmount = BigBlind;
		MinRaiseTo = BigBlind * 2;
		MaxRaiseTo = 0;
		RaiseSliderValue = BigBlind * 2;
	}

	/// <summary>
	/// Recomputes which actions the local player can take. Called by RPC handlers
	/// after the host signals a new active seat or new bet level.
	/// </summary>
	public static void RecomputeLocalActions( int activeSeat, long currentBet, long minRaise )
	{
		ResetLocalActions();
		if ( LocalSeatIndex < 0 || LocalSeatIndex >= MaxSeats ) return;
		if ( activeSeat != LocalSeatIndex ) return;

		var seat = Seats[LocalSeatIndex];
		if ( seat == null || !seat.IsOccupied || seat.HasFolded || seat.IsAllIn ) return;

		IsLocalPlayerTurn = true;
		CanFold = true;

		long owedToCall = currentBet - seat.ChipsInPotThisRound;
		if ( owedToCall < 0 ) owedToCall = 0;

		if ( owedToCall == 0 )
		{
			CanCheck = true;
			if ( currentBet > 0 )
			{
				// There's an existing bet (e.g. BB option preflop) — this is a Raise, not a Bet
				long minRaiseTo = currentBet + minRaise;
				if ( seat.ChipsInPotThisRound + seat.Chips >= minRaiseTo )
				{
					CanRaise = true;
					MinRaiseTo = minRaiseTo;
				}
			}
			else if ( seat.Chips >= BigBlind )
			{
				// No bet yet — this is an opening Bet
				CanBet = true;
				MinBetAmount = BigBlind;
			}
		}
		else
		{
			// Owe chips — can call (or all-in if short)
			if ( seat.Chips >= owedToCall )
			{
				CanCall = true;
				CallAmount = owedToCall;

				// Can raise if a full raise is possible
				long minRaiseTo = currentBet + minRaise;
				if ( seat.ChipsInPotThisRound + seat.Chips >= minRaiseTo )
				{
					CanRaise = true;
					MinRaiseTo = minRaiseTo;
				}
			}
			else
			{
				CallAmount = seat.Chips; // call all-in for less
				CanCall = true;
			}
		}

		// Always can shove if any chips remain
		if ( seat.Chips > 0 ) CanAllIn = true;

		MaxRaiseTo = seat.ChipsInPotThisRound + seat.Chips;
		RaiseSliderValue = System.Math.Max( MinRaiseTo, RaiseSliderValue );
		if ( RaiseSliderValue > MaxRaiseTo ) RaiseSliderValue = MaxRaiseTo;
	}

	public static void SetPhase( PokerPhase phase )
	{
		Phase = phase;
		StatusMessage = phase switch
		{
			PokerPhase.Idle => "Table closed",
			PokerPhase.WaitingForPlayers => "Waiting for players...",
			PokerPhase.PostingBlinds => "Posting blinds...",
			PokerPhase.DealHoleCards => "Dealing hole cards...",
			PokerPhase.PreFlopBetting => "Pre-flop betting",
			PokerPhase.DealFlop => "Dealing flop...",
			PokerPhase.FlopBetting => "Flop betting",
			PokerPhase.DealTurn => "Dealing turn...",
			PokerPhase.TurnBetting => "Turn betting",
			PokerPhase.DealRiver => "Dealing river...",
			PokerPhase.RiverBetting => "River betting",
			PokerPhase.Showdown => "Showdown!",
			PokerPhase.Payout => "Awarding pot...",
			PokerPhase.Cleanup => "Next hand starting...",
			_ => ""
		};
	}
}
