using System;
using Sandbox;
using System.Collections.Generic;

public enum BlackjackPhase
{
	Idle,
	WaitingForBets,
	Dealing,
	PlayerTurns,
	DealerTurn,
	Payout
}

public enum HandResult
{
	None,
	Win,
	Loss,
	Push,
	Blackjack
}

public struct CardData
{
	public int Suit;  // 0=Spades, 1=Hearts, 2=Diamonds, 3=Clubs
	public int Rank;  // 1=Ace, 2-10, 11=Jack, 12=Queen, 13=King
	public bool FaceDown;

	public CardData( int suit, int rank, bool faceDown = false )
	{
		Suit = suit;
		Rank = rank;
		FaceDown = faceDown;
	}

	public string GetRankText()
	{
		return Rank switch
		{
			1 => "A",
			11 => "J",
			12 => "Q",
			13 => "K",
			_ => Rank.ToString()
		};
	}

	public string GetSuitSymbol()
	{
		return Suit switch
		{
			0 => "♠",
			1 => "♥",
			2 => "♦",
			3 => "♣",
			_ => "?"
		};
	}

	public bool IsRed => Suit == 1 || Suit == 2;
}

public class PlayerSeatData
{
	public string PlayerName { get; set; } = "";
	public ulong SteamId { get; set; }
	public bool IsOccupied { get; set; }
	public List<List<CardData>> Hands { get; set; } = new();
	public List<int> HandScores { get; set; } = new();
	public List<int> HandBets { get; set; } = new();
	public List<HandResult> HandResults { get; set; } = new();
	public int ActiveHandIndex { get; set; }

	public void Clear()
	{
		Hands.Clear();
		HandScores.Clear();
		HandBets.Clear();
		HandResults.Clear();
		ActiveHandIndex = 0;
	}

	public void ClearForNewRound()
	{
		Hands.Clear();
		HandScores.Clear();
		HandBets.Clear();
		HandResults.Clear();
		ActiveHandIndex = 0;
	}
}

public static class BlackjackBridge
{
	// Visibility
	public static bool IsOpen { get; set; } = false;

	// Local player state
	public static int CachedBalance { get; set; } = 0;
	public static int SessionNetChange { get; set; } = 0;
	public static int CurrentBet { get; set; } = 0;

	// Remembered balance from last session to protect against stale API data on quick remount
	public static int? LastKnownBalance { get; set; } = null;
	public static DateTime? LastLeaveTime { get; set; } = null;
	public static int SelectedChipValue { get; set; } = 25;
	public static int LocalSeatIndex { get; set; } = -1;

	// Dealer state
	public static List<CardData> DealerCards { get; set; } = new();
	public static int DealerScore { get; set; } = 0;

	// Seats
	public static PlayerSeatData[] Seats { get; set; } = new PlayerSeatData[7]
	{
		new(), new(), new(), new(), new(), new(), new()
	};

	// Round state
	public static BlackjackPhase Phase { get; set; } = BlackjackPhase.Idle;
	public static int ActiveSeatIndex { get; set; } = -1;
	public static bool IsLocalPlayerTurn { get; set; } = false;
	public static bool CanHit { get; set; } = false;
	public static bool CanStand { get; set; } = false;
	public static bool CanDouble { get; set; } = false;
	public static bool CanSplit { get; set; } = false;
	public static bool InsuranceOffered { get; set; } = false;
	public static string StatusMessage { get; set; } = "";

	// Table reference for sending RPCs
	public static BlackjackTable ActiveTable { get; set; } = null;

	public static void Open( BlackjackTable table, int seatIndex )
	{
		IsOpen = true;
		ActiveTable = table;
		LocalSeatIndex = seatIndex;
		CurrentBet = 0;
		SelectedChipValue = 25;
		SessionNetChange = 0;

		// Refresh all seat data from this table's synced properties
		// to clear any stale data from previous sessions or other tables
		for ( int i = 0; i < 7; i++ )
		{
			string sid = table.GetSeatSteamId( i );
			string name = table.GetSeatName( i );
			bool occupied = !string.IsNullOrEmpty( sid );

			Seats[i].Clear();
			Seats[i].IsOccupied = occupied;
			Seats[i].PlayerName = name;
			Seats[i].SteamId = ulong.TryParse( sid, out var id ) ? id : 0;
		}

		// Reset round state
		DealerCards.Clear();
		DealerScore = 0;
		ActiveSeatIndex = -1;
		IsLocalPlayerTurn = false;
		CanHit = false;
		CanStand = false;
		CanDouble = false;
		CanSplit = false;
		InsuranceOffered = false;
		StatusMessage = "";
	}

	public static void Close()
	{
		IsOpen = false;
		ActiveTable = null;
		LocalSeatIndex = -1;
		CurrentBet = 0;
	}

	public static void Reset()
	{
		DealerCards.Clear();
		DealerScore = 0;
		ActiveSeatIndex = -1;
		IsLocalPlayerTurn = false;
		CanHit = false;
		CanStand = false;
		CanDouble = false;
		CanSplit = false;
		InsuranceOffered = false;
		StatusMessage = "";

		foreach ( var seat in Seats )
		{
			seat.ClearForNewRound();
		}
	}

	public static void SetPhase( BlackjackPhase phase )
	{
		Phase = phase;
		StatusMessage = phase switch
		{
			BlackjackPhase.Idle => "Waiting for players...",
			BlackjackPhase.WaitingForBets => "Place your bets!",
			BlackjackPhase.Dealing => "Dealing cards...",
			BlackjackPhase.PlayerTurns => IsLocalPlayerTurn ? "Your turn — Hit or Stand?" : "Waiting for other players...",
			BlackjackPhase.DealerTurn => "Dealer's turn...",
			BlackjackPhase.Payout => "Round complete!",
			_ => ""
		};
	}
}
