using Sandbox;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public sealed class PokerManager : Component
{
	[Property] public PokerTable Table { get; set; }

	private const int MaxSeats = 10;
	private const int SmallBlind = PokerBridge.SmallBlind;
	private const int BigBlind = PokerBridge.BigBlind;
	private const float TurnTimeout = 20f;
	private const float ShowdownDisplayTime = 6f;
	private const float CleanupDelay = 2f;
	private const float DealDelay = 0.35f;
	private const float CommunityCardDelay = 0.6f;
	private const float AllInRunoutDelay = 1.2f;

	// Host-side state
	private PokerSeat[] seats = new PokerSeat[MaxSeats];
	private List<int> deck = new();
	private int deckIndex = 0;
	private List<int> communityCards = new();
	private List<Pot> pots = new();

	private PokerPhase currentPhase = PokerPhase.Idle;
	private int buttonSeat = -1;
	private int activeSeat = -1;
	private long currentBet = 0;
	private long minRaise = BigBlind;
	private int lastAggressor = -1;
	private float turnTimer = 0f;
	private bool handInProgress = false;
	private bool dealingInProgress = false;
	private bool cleanupInProgress = false;
	private bool gameSessionActive = false; // true after Play pressed, auto-starts subsequent hands

	private class PokerSeat
	{
		public string SteamId = "";
		public string PlayerName = "";
		public bool IsOccupied = false;
		public long Chips = 0;

		public List<int> HoleCards = new();
		public long ChipsInPotThisRound = 0;
		public long TotalContributedThisHand = 0;
		public bool HasFolded = false;
		public bool IsAllIn = false;
		public bool HasActedThisRound = false;
		public bool IsSittingOut = false; // joined mid-hand or busted

		public void ClearForNewHand()
		{
			HoleCards.Clear();
			ChipsInPotThisRound = 0;
			TotalContributedThisHand = 0;
			HasFolded = false;
			IsAllIn = false;
			HasActedThisRound = false;
		}
	}

	private class Pot
	{
		public long Amount;
		public HashSet<int> EligibleSeats = new();
	}

	protected override void OnStart()
	{
		for ( int i = 0; i < MaxSeats; i++ )
			seats[i] = new PokerSeat();
	}

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost ) return;

		// Tick the action timer for the active player
		if ( handInProgress && activeSeat >= 0 && IsBettingPhase( currentPhase ) && !dealingInProgress )
		{
			turnTimer -= Time.Delta;
			if ( turnTimer <= 0f )
			{
				ForceFold( activeSeat );
			}
		}

		// Transition from Cleanup (or any non-hand state) to WaitingForPlayers
		if ( !handInProgress && !cleanupInProgress && currentPhase != PokerPhase.WaitingForPlayers )
		{
			currentPhase = PokerPhase.WaitingForPlayers;
			BroadcastPhaseChange( (int)PokerPhase.WaitingForPlayers );
		}

		// Auto-start next hand if a game session is active (Play was pressed)
		if ( gameSessionActive && !handInProgress && !cleanupInProgress && currentPhase == PokerPhase.WaitingForPlayers )
		{
			if ( CanStartHand() )
			{
				_ = RunHand();
			}
			else
			{
				// Not enough players to continue — end the session
				gameSessionActive = false;
				BroadcastGameSessionEnd();
			}
		}
	}

	private static bool IsBettingPhase( PokerPhase p )
	{
		return p == PokerPhase.PreFlopBetting || p == PokerPhase.FlopBetting
			|| p == PokerPhase.TurnBetting || p == PokerPhase.RiverBetting;
	}

	// === Hand lifecycle ===

	private bool CanStartHand()
	{
		int eligible = 0;
		for ( int i = 0; i < MaxSeats; i++ )
		{
			if ( seats[i].IsOccupied && seats[i].Chips >= BigBlind )
				eligible++;
		}
		return eligible >= 2;
	}

	[Rpc.Broadcast]
	public void RequestStartGame( string steamId )
	{
		if ( !Networking.IsHost ) return;
		if ( handInProgress || cleanupInProgress || gameSessionActive ) return;
		if ( !CanStartHand() ) return;

		// Verify the requester is actually seated at this table
		bool found = false;
		for ( int i = 0; i < MaxSeats; i++ )
		{
			if ( seats[i].IsOccupied && seats[i].SteamId == steamId ) { found = true; break; }
		}
		if ( !found ) return;

		gameSessionActive = true;
		BroadcastGameSessionStart();
		_ = RunHand();
	}

	private async Task RunHand()
	{
		handInProgress = true;

		// --- Cleanup previous hand state ---
		for ( int i = 0; i < MaxSeats; i++ )
		{
			seats[i].ClearForNewHand();
			// A player who joined mid-hand previously is now eligible to play this hand.
			seats[i].IsSittingOut = !seats[i].IsOccupied || seats[i].Chips < BigBlind;
		}
		communityCards.Clear();
		pots.Clear();
		currentBet = 0;
		minRaise = BigBlind;
		lastAggressor = -1;
		ShuffleDeck();

		BroadcastResetHand();

		// --- Posting blinds ---
		AdvanceButton();
		int sbSeat, bbSeat;
		PickBlinds( out sbSeat, out bbSeat );

		currentPhase = PokerPhase.PostingBlinds;
		BroadcastPhaseChange( (int)PokerPhase.PostingBlinds );
		BroadcastButtonMove( buttonSeat, sbSeat, bbSeat );

		PostBlind( sbSeat, SmallBlind, PokerActionType.SmallBlind );
		PostBlind( bbSeat, BigBlind, PokerActionType.BigBlind );
		// currentBet is now set correctly inside PostBlind
		minRaise = BigBlind;
		lastAggressor = bbSeat;

		await Task.DelayRealtimeSeconds( 0.4f );

		// --- Deal hole cards ---
		currentPhase = PokerPhase.DealHoleCards;
		BroadcastPhaseChange( (int)PokerPhase.DealHoleCards );
		dealingInProgress = true;

		// Standard poker deal: one card at a time around the table, twice.
		for ( int round = 0; round < 2; round++ )
		{
			int s = NextLiveSeat( buttonSeat );
			int dealt = 0;
			while ( dealt < CountLivePlayers() )
			{
				if ( !seats[s].IsSittingOut && seats[s].IsOccupied )
				{
					int card = DrawCard();
					seats[s].HoleCards.Add( card );
					dealt++;
				}
				s = NextSeat( s );
				if ( dealt >= CountLivePlayers() ) break;
			}
			await Task.DelayRealtimeSeconds( DealDelay );
		}

		// Send each player their own hole cards (filtered RPC), and broadcast face-down placeholders
		for ( int i = 0; i < MaxSeats; i++ )
		{
			if ( seats[i].IsSittingOut || !seats[i].IsOccupied || seats[i].HoleCards.Count < 2 ) continue;

			BroadcastHoleCardPlaceholders( i );
			SendHoleCardsToPlayer( i, seats[i].SteamId, seats[i].HoleCards[0], seats[i].HoleCards[1] );
		}

		dealingInProgress = false;

		// --- Pre-flop betting ---
		await BettingRound( PokerPhase.PreFlopBetting, FirstToActPreflop() );
		if ( !handInProgress ) return;
		if ( ShouldShortCircuitToShowdown() ) { await DealRemainingAndShowdown(); return; }

		// --- Flop ---
		await DealCommunityCards( PokerPhase.DealFlop, 3 );
		if ( !handInProgress ) return;
		await BettingRound( PokerPhase.FlopBetting, FirstToActPostflop() );
		if ( !handInProgress ) return;
		if ( ShouldShortCircuitToShowdown() ) { await DealRemainingAndShowdown(); return; }

		// --- Turn ---
		await DealCommunityCards( PokerPhase.DealTurn, 1 );
		if ( !handInProgress ) return;
		await BettingRound( PokerPhase.TurnBetting, FirstToActPostflop() );
		if ( !handInProgress ) return;
		if ( ShouldShortCircuitToShowdown() ) { await DealRemainingAndShowdown(); return; }

		// --- River ---
		await DealCommunityCards( PokerPhase.DealRiver, 1 );
		if ( !handInProgress ) return;
		await BettingRound( PokerPhase.RiverBetting, FirstToActPostflop() );
		if ( !handInProgress ) return;

		await Showdown();
	}

	private bool ShouldShortCircuitToShowdown()
	{
		// True if everyone still in is all-in (no more betting possible)
		int notAllIn = 0;
		int notFolded = 0;
		for ( int i = 0; i < MaxSeats; i++ )
		{
			if ( !seats[i].IsOccupied || seats[i].IsSittingOut || seats[i].HasFolded ) continue;
			notFolded++;
			if ( !seats[i].IsAllIn ) notAllIn++;
		}
		return notFolded >= 2 && notAllIn <= 1;
	}

	private async Task DealRemainingAndShowdown()
	{
		// Deal whatever community cards remain with delay between each, then showdown.
		while ( communityCards.Count < 5 && handInProgress )
		{
			int needed = communityCards.Count == 0 ? 3 : 1;
			await DealCommunityCards( communityCards.Count == 0 ? PokerPhase.DealFlop : (communityCards.Count == 3 ? PokerPhase.DealTurn : PokerPhase.DealRiver), needed );
			await Task.DelayRealtimeSeconds( AllInRunoutDelay );
		}
		await Showdown();
	}

	private async Task DealCommunityCards( PokerPhase dealPhase, int count )
	{
		dealingInProgress = true;
		currentPhase = dealPhase;
		BroadcastPhaseChange( (int)dealPhase );

		// Burn one card
		DrawCard();

		var dealt = new List<int>();
		for ( int i = 0; i < count; i++ )
		{
			int card = DrawCard();
			communityCards.Add( card );
			dealt.Add( card );
		}

		int[] suitsRanks = new int[dealt.Count * 2];
		for ( int i = 0; i < dealt.Count; i++ )
		{
			suitsRanks[i * 2] = GetSuit( dealt[i] );
			suitsRanks[i * 2 + 1] = GetRank( dealt[i] );
		}
		BroadcastDealCommunityCards( suitsRanks );
		await Task.DelayRealtimeSeconds( CommunityCardDelay );

		// Reset round-level bet state
		for ( int i = 0; i < MaxSeats; i++ )
		{
			seats[i].ChipsInPotThisRound = 0;
			seats[i].HasActedThisRound = false;
		}
		currentBet = 0;
		minRaise = BigBlind;
		lastAggressor = -1;

		BroadcastRoundReset();
		dealingInProgress = false;
	}

	private async Task BettingRound( PokerPhase phase, int firstToAct )
	{
		currentPhase = phase;
		BroadcastPhaseChange( (int)phase );

		activeSeat = firstToAct;
		// If first-to-act is unable to act (folded/all-in/sitting out), advance.
		if ( !CanAct( activeSeat ) )
			activeSeat = NextActor( activeSeat );

		while ( handInProgress && activeSeat >= 0 && !IsBettingRoundComplete() )
		{
			turnTimer = TurnTimeout;
			BroadcastActiveSeat( activeSeat, turnTimer );

			// Wait for an action (handled by request RPCs which advance activeSeat)
			int waitSeat = activeSeat;
			while ( handInProgress && activeSeat == waitSeat && !IsBettingRoundComplete() )
			{
				await Task.DelayRealtimeSeconds( 0.05f );
				if ( !handInProgress ) return;
			}
		}

		// Round closed — clear active seat broadcast
		activeSeat = -1;
		BroadcastActiveSeat( -1, 0f );
	}

	private bool IsBettingRoundComplete()
	{
		// If only one non-folded player remains, hand ends immediately.
		int notFolded = 0;
		for ( int i = 0; i < MaxSeats; i++ )
		{
			if ( !seats[i].IsOccupied || seats[i].IsSittingOut ) continue;
			if ( !seats[i].HasFolded ) notFolded++;
		}
		if ( notFolded < 2 )
		{
			handInProgress = false; // signal RunHand to bail
			_ = AwardSinglePotWinner();
			return true;
		}

		// All non-folded, non-all-in seats must have acted and matched the current bet.
		for ( int i = 0; i < MaxSeats; i++ )
		{
			var s = seats[i];
			if ( !s.IsOccupied || s.IsSittingOut || s.HasFolded || s.IsAllIn ) continue;
			if ( !s.HasActedThisRound ) return false;
			if ( s.ChipsInPotThisRound < currentBet ) return false;
		}
		return true;
	}

	private async Task AwardSinglePotWinner()
	{
		cleanupInProgress = true;

		int winner = -1;
		for ( int i = 0; i < MaxSeats; i++ )
		{
			if ( !seats[i].IsOccupied || seats[i].IsSittingOut || seats[i].HasFolded ) continue;
			winner = i; break;
		}
		if ( winner < 0 ) { EndHand(); return; }

		long total = 0;
		for ( int i = 0; i < MaxSeats; i++ )
			total += seats[i].TotalContributedThisHand;

		seats[winner].Chips += total;
		Table?.SetSeatChips( winner, seats[winner].Chips );

		BroadcastPotUpdate( total, System.Array.Empty<long>() );
		BroadcastWinPot( new[] { winner }, new[] { total }, 0, "Wins by fold" );
		BroadcastCreditChange( seats[winner].SteamId, (int)total, true );

		await Task.DelayRealtimeSeconds( 3f );
		EndHand();
	}

	private async Task Showdown()
	{
		currentPhase = PokerPhase.Showdown;
		BroadcastPhaseChange( (int)PokerPhase.Showdown );

		// Build pots from contributions
		BuildSidePots();

		// Reveal everyone's hole cards (non-folded players)
		var revealList = new List<(int seat, int s1, int r1, int s2, int r2, string label, int rank)>();
		for ( int i = 0; i < MaxSeats; i++ )
		{
			var s = seats[i];
			if ( !s.IsOccupied || s.IsSittingOut || s.HasFolded ) continue;
			if ( s.HoleCards.Count < 2 ) continue;

			var seven = new List<CardData>();
			seven.Add( ToCardData( s.HoleCards[0] ) );
			seven.Add( ToCardData( s.HoleCards[1] ) );
			foreach ( var cc in communityCards ) seven.Add( ToCardData( cc ) );

			var score = PokerHandEvaluator.Evaluate( seven );
			revealList.Add( (i, GetSuit( s.HoleCards[0] ), GetRank( s.HoleCards[0] ),
				GetSuit( s.HoleCards[1] ), GetRank( s.HoleCards[1] ),
				score.Label, (int)score.Rank) );
		}

		foreach ( var r in revealList )
		{
			BroadcastShowdownReveal( r.seat, r.s1, r.r1, r.s2, r.r2, r.label, r.rank );
			await Task.DelayRealtimeSeconds( 0.4f );
		}

		// Award each pot
		long[] potAmounts = pots.Select( p => p.Amount ).ToArray();
		BroadcastPotUpdate( potAmounts.Length > 0 ? potAmounts[0] : 0,
			potAmounts.Length > 1 ? potAmounts.Skip( 1 ).ToArray() : System.Array.Empty<long>() );

		await Task.DelayRealtimeSeconds( 0.6f );

		currentPhase = PokerPhase.Payout;
		BroadcastPhaseChange( (int)PokerPhase.Payout );

		// Track the overall winner(s) for sound purposes
		var overallWinners = new HashSet<int>();

		for ( int p = 0; p < pots.Count; p++ )
		{
			var pot = pots[p];

			// Count eligible non-folded contenders for this pot
			int contenders = 0;
			foreach ( var seatIdx in pot.EligibleSeats )
				if ( !seats[seatIdx].HasFolded ) contenders++;

			// Find best score among eligible non-folded seats
			long bestScore = -1;
			var winners = new List<int>();
			string winLabel = "";

			foreach ( var seatIdx in pot.EligibleSeats )
			{
				if ( seats[seatIdx].HasFolded ) continue;
				var seven = new List<CardData>();
				seven.Add( ToCardData( seats[seatIdx].HoleCards[0] ) );
				seven.Add( ToCardData( seats[seatIdx].HoleCards[1] ) );
				foreach ( var cc in communityCards ) seven.Add( ToCardData( cc ) );

				var score = PokerHandEvaluator.Evaluate( seven );
				if ( score.Score > bestScore )
				{
					bestScore = score.Score;
					winners.Clear();
					winners.Add( seatIdx );
					winLabel = score.Label;
				}
				else if ( score.Score == bestScore )
				{
					winners.Add( seatIdx );
				}
			}

			if ( winners.Count == 0 ) continue;

			long share = pot.Amount / winners.Count;
			long remainder = pot.Amount - share * winners.Count;

			// Odd chip goes to first winner clockwise from button
			int oddChipWinner = winners[0];
			if ( remainder > 0 )
			{
				int s = NextSeat( buttonSeat );
				while ( true )
				{
					if ( winners.Contains( s ) ) { oddChipWinner = s; break; }
					s = NextSeat( s );
					if ( s == buttonSeat ) break;
				}
			}

			var amounts = new long[winners.Count];
			for ( int wi = 0; wi < winners.Count; wi++ )
			{
				amounts[wi] = share + (winners[wi] == oddChipWinner ? remainder : 0);
				seats[winners[wi]].Chips += amounts[wi];
				Table?.SetSeatChips( winners[wi], seats[winners[wi]].Chips );
				BroadcastCreditChange( seats[winners[wi]].SteamId, (int)amounts[wi], true );
			}

			// Only show the win banner for contested pots (more than 1 contender).
			// Uncontested side pots (returning uncalled bets) are awarded silently.
			if ( contenders > 1 )
			{
				foreach ( var w in winners ) overallWinners.Add( w );
				BroadcastWinPot( winners.ToArray(), amounts, p, winLabel );
				await Task.DelayRealtimeSeconds( 0.7f );
			}
		}

		// Play win/lose sounds once after all pots are awarded
		if ( overallWinners.Count > 0 )
			BroadcastShowdownSounds( overallWinners.ToArray() );

		await Task.DelayRealtimeSeconds( ShowdownDisplayTime );
		EndHand();
	}

	private void BuildSidePots()
	{
		pots.Clear();
		// Snapshot remaining contributions and which seats are folded
		var remaining = new long[MaxSeats];
		for ( int i = 0; i < MaxSeats; i++ )
			remaining[i] = seats[i].TotalContributedThisHand;

		while ( true )
		{
			// Find smallest non-zero contribution among non-folded seats
			long min = long.MaxValue;
			bool any = false;
			for ( int i = 0; i < MaxSeats; i++ )
			{
				if ( seats[i].HasFolded || seats[i].IsSittingOut ) continue;
				if ( remaining[i] <= 0 ) continue;
				if ( remaining[i] < min ) { min = remaining[i]; any = true; }
			}
			if ( !any ) break;

			var pot = new Pot();
			for ( int i = 0; i < MaxSeats; i++ )
			{
				if ( remaining[i] <= 0 ) continue;
				long take = System.Math.Min( remaining[i], min );
				pot.Amount += take;
				remaining[i] -= take;
				if ( !seats[i].HasFolded && !seats[i].IsSittingOut )
					pot.EligibleSeats.Add( i );
			}
			pots.Add( pot );
		}

		// Pick up any leftover folded contributions (shouldn't normally happen, but just in case)
		long leftover = 0;
		for ( int i = 0; i < MaxSeats; i++ )
			leftover += remaining[i];
		if ( leftover > 0 && pots.Count > 0 )
			pots[pots.Count - 1].Amount += leftover;
	}

	private async void EndHand()
	{
		handInProgress = false;
		cleanupInProgress = true;
		activeSeat = -1;
		currentPhase = PokerPhase.Cleanup;
		BroadcastPhaseChange( (int)PokerPhase.Cleanup );

		// Give players time to see the hand result before busting anyone out
		await Task.DelayRealtimeSeconds( 3f );

		// Auto-leave anyone who can't post BB next hand
		for ( int i = 0; i < MaxSeats; i++ )
		{
			if ( seats[i].IsOccupied && seats[i].Chips < BigBlind )
			{
				BroadcastBustOut( seats[i].SteamId );
				Table?.BroadcastPlayerLeft( seats[i].SteamId, i );
			}
		}

		BroadcastResetHand();
		cleanupInProgress = false;
	}

	// === Blinds / button ===

	private void AdvanceButton()
	{
		// Find next occupied seat clockwise from current button (or seat 0 if none)
		int start = buttonSeat < 0 ? -1 : buttonSeat;
		for ( int step = 1; step <= MaxSeats; step++ )
		{
			int s = (start + step + MaxSeats) % MaxSeats;
			if ( seats[s].IsOccupied && !seats[s].IsSittingOut )
			{
				buttonSeat = s;
				return;
			}
		}
	}

	private void PickBlinds( out int sbSeat, out int bbSeat )
	{
		int liveCount = CountLivePlayers();
		if ( liveCount == 2 )
		{
			// Heads-up: button is small blind
			sbSeat = buttonSeat;
			bbSeat = NextLiveSeat( buttonSeat );
		}
		else
		{
			sbSeat = NextLiveSeat( buttonSeat );
			bbSeat = NextLiveSeat( sbSeat );
		}
	}

	private void PostBlind( int seatIdx, long amount, PokerActionType actionType )
	{
		var s = seats[seatIdx];
		long actual = System.Math.Min( amount, s.Chips );
		s.Chips -= actual;
		s.ChipsInPotThisRound += actual;
		s.TotalContributedThisHand += actual;
		if ( s.Chips == 0 ) s.IsAllIn = true;

		// Update currentBet immediately so the broadcast sends the correct value
		if ( s.ChipsInPotThisRound > currentBet )
			currentBet = s.ChipsInPotThisRound;

		Table?.SetSeatChips( seatIdx, s.Chips );

		// Track wager for casino leaderboard
		BroadcastWagerChange( s.SteamId, (int)actual );

		BroadcastBetAction( seatIdx, (int)actionType, actual, currentBet, s.Chips );
	}

	private int FirstToActPreflop()
	{
		int liveCount = CountLivePlayers();
		if ( liveCount == 2 )
		{
			// Heads-up: button (= SB) acts first preflop
			return buttonSeat;
		}
		// Seat after BB (UTG)
		int sb = NextLiveSeat( buttonSeat );
		int bb = NextLiveSeat( sb );
		return NextLiveSeat( bb );
	}

	private int FirstToActPostflop()
	{
		int liveCount = CountLivePlayers();
		if ( liveCount == 2 )
		{
			// Heads-up: BB acts first postflop
			return NextLiveSeat( buttonSeat );
		}
		return NextLiveSeat( buttonSeat );
	}

	// === Action handlers (called from RPCs) ===

	private void HandleFold( int seatIdx )
	{
		var s = seats[seatIdx];
		s.HasFolded = true;
		s.HasActedThisRound = true;
		BroadcastBetAction( seatIdx, (int)PokerActionType.Fold, 0, currentBet, s.Chips );
		AdvanceTurn();
	}

	private void HandleCheck( int seatIdx )
	{
		var s = seats[seatIdx];
		if ( s.ChipsInPotThisRound != currentBet ) return;
		s.HasActedThisRound = true;
		BroadcastBetAction( seatIdx, (int)PokerActionType.Check, 0, currentBet, s.Chips );
		AdvanceTurn();
	}

	private void HandleCall( int seatIdx )
	{
		var s = seats[seatIdx];
		long owed = currentBet - s.ChipsInPotThisRound;
		if ( owed <= 0 ) { HandleCheck( seatIdx ); return; }

		long pay = System.Math.Min( owed, s.Chips );
		s.Chips -= pay;
		s.ChipsInPotThisRound += pay;
		s.TotalContributedThisHand += pay;
		s.HasActedThisRound = true;
		if ( s.Chips == 0 ) s.IsAllIn = true;

		Table?.SetSeatChips( seatIdx, s.Chips );
		BroadcastWagerChange( s.SteamId, (int)pay );
		BroadcastBetAction( seatIdx, (int)PokerActionType.Call, pay, currentBet, s.Chips );
		AdvanceTurn();
	}

	private void HandleBetOrRaise( int seatIdx, long raiseTo )
	{
		var s = seats[seatIdx];

		// "Bet" when currentBet == 0, otherwise "raise"
		bool isOpeningBet = currentBet == 0;
		long minLegal = isOpeningBet ? BigBlind : currentBet + minRaise;
		long maxAffordable = s.ChipsInPotThisRound + s.Chips;

		if ( raiseTo > maxAffordable ) raiseTo = maxAffordable;

		bool isShortAllIn = raiseTo < minLegal;
		if ( isShortAllIn && raiseTo < maxAffordable ) return; // illegal undersized bet
		if ( raiseTo <= currentBet && raiseTo < maxAffordable ) return;

		long add = raiseTo - s.ChipsInPotThisRound;
		if ( add <= 0 ) return;

		s.Chips -= add;
		s.ChipsInPotThisRound = raiseTo;
		s.TotalContributedThisHand += add;
		s.HasActedThisRound = true;
		if ( s.Chips == 0 ) s.IsAllIn = true;

		long previousBet = currentBet;
		long raiseAmount = raiseTo - previousBet;

		// Full raise (>= min raise) reopens action; short all-in does not.
		if ( !isShortAllIn || isOpeningBet )
		{
			currentBet = raiseTo;
			if ( raiseAmount >= minRaise ) minRaise = raiseAmount;
			lastAggressor = seatIdx;

			// Reset HasActedThisRound for other live players
			for ( int i = 0; i < MaxSeats; i++ )
			{
				if ( i == seatIdx ) continue;
				if ( !seats[i].IsOccupied || seats[i].IsSittingOut ) continue;
				if ( seats[i].HasFolded || seats[i].IsAllIn ) continue;
				seats[i].HasActedThisRound = false;
			}
		}
		else
		{
			// Short all-in: just bumps up currentBet to whatever was bet, but doesn't reopen action
			if ( raiseTo > currentBet ) currentBet = raiseTo;
		}

		Table?.SetSeatChips( seatIdx, s.Chips );
		BroadcastWagerChange( s.SteamId, (int)add );

		var actionType = isOpeningBet ? PokerActionType.Bet
			: (s.IsAllIn ? PokerActionType.AllIn : PokerActionType.Raise);
		BroadcastBetAction( seatIdx, (int)actionType, raiseTo, currentBet, s.Chips );
		AdvanceTurn();
	}

	private void HandleAllIn( int seatIdx )
	{
		var s = seats[seatIdx];
		long raiseTo = s.ChipsInPotThisRound + s.Chips;
		HandleBetOrRaise( seatIdx, raiseTo );
	}

	private void ForceFold( int seatIdx )
	{
		HandleFold( seatIdx );
	}

	private void AdvanceTurn()
	{
		if ( IsBettingRoundComplete() )
		{
			activeSeat = -1;
			return;
		}
		activeSeat = NextActor( activeSeat );
		turnTimer = TurnTimeout;
		BroadcastActiveSeat( activeSeat, turnTimer );
	}

	private bool CanAct( int seatIdx )
	{
		if ( seatIdx < 0 || seatIdx >= MaxSeats ) return false;
		var s = seats[seatIdx];
		return s.IsOccupied && !s.IsSittingOut && !s.HasFolded && !s.IsAllIn;
	}

	private int NextActor( int from )
	{
		for ( int step = 1; step <= MaxSeats; step++ )
		{
			int s = (from + step) % MaxSeats;
			if ( CanAct( s ) ) return s;
		}
		return -1;
	}

	private int NextLiveSeat( int from )
	{
		for ( int step = 1; step <= MaxSeats; step++ )
		{
			int s = (from + step) % MaxSeats;
			if ( seats[s].IsOccupied && !seats[s].IsSittingOut ) return s;
		}
		return from;
	}

	private int NextSeat( int from )
	{
		for ( int step = 1; step <= MaxSeats; step++ )
		{
			int s = (from + step) % MaxSeats;
			if ( seats[s].IsOccupied ) return s;
		}
		return from;
	}

	private int CountLivePlayers()
	{
		int count = 0;
		for ( int i = 0; i < MaxSeats; i++ )
			if ( seats[i].IsOccupied && !seats[i].IsSittingOut ) count++;
		return count;
	}

	// === Player Action RPCs (client → host) ===

	[Rpc.Broadcast]
	public void RequestFold( string steamId )
	{
		if ( !Networking.IsHost ) return;
		int seat = FindSeatByValidActor( steamId );
		if ( seat < 0 ) return;
		HandleFold( seat );
	}

	[Rpc.Broadcast]
	public void RequestCheck( string steamId )
	{
		if ( !Networking.IsHost ) return;
		int seat = FindSeatByValidActor( steamId );
		if ( seat < 0 ) return;
		if ( seats[seat].ChipsInPotThisRound != currentBet ) return;
		HandleCheck( seat );
	}

	[Rpc.Broadcast]
	public void RequestCall( string steamId )
	{
		if ( !Networking.IsHost ) return;
		int seat = FindSeatByValidActor( steamId );
		if ( seat < 0 ) return;
		HandleCall( seat );
	}

	[Rpc.Broadcast]
	public void RequestBet( string steamId, long amount )
	{
		if ( !Networking.IsHost ) return;
		int seat = FindSeatByValidActor( steamId );
		if ( seat < 0 ) return;
		HandleBetOrRaise( seat, amount );
	}

	[Rpc.Broadcast]
	public void RequestRaise( string steamId, long raiseTo )
	{
		if ( !Networking.IsHost ) return;
		int seat = FindSeatByValidActor( steamId );
		if ( seat < 0 ) return;
		HandleBetOrRaise( seat, raiseTo );
	}

	[Rpc.Broadcast]
	public void RequestAllIn( string steamId )
	{
		if ( !Networking.IsHost ) return;
		int seat = FindSeatByValidActor( steamId );
		if ( seat < 0 ) return;
		HandleAllIn( seat );
	}

	private int FindSeatByValidActor( string steamId )
	{
		if ( !IsBettingPhase( currentPhase ) ) return -1;
		if ( activeSeat < 0 ) return -1;
		if ( seats[activeSeat].SteamId != steamId ) return -1;
		return activeSeat;
	}

	// === Player join/leave callbacks (from PokerTable) ===

	public void OnPlayerJoined( int seatIndex, string steamId, string name, int buyIn )
	{
		seats[seatIndex].SteamId = steamId;
		seats[seatIndex].PlayerName = name;
		seats[seatIndex].IsOccupied = true;
		seats[seatIndex].Chips = buyIn;
		// New players sit out the current hand and join the next one in cleanup
		seats[seatIndex].IsSittingOut = handInProgress;
		Table?.SetSeatChips( seatIndex, buyIn );

		// Re-broadcast current phase so the newly joined client gets synced
		BroadcastPhaseChange( (int)currentPhase );
	}

	public void OnPlayerLeft( int seatIndex, string steamId )
	{
		var s = seats[seatIndex];

		// Treat as fold if mid-hand
		if ( handInProgress && !s.HasFolded )
		{
			s.HasFolded = true;
			if ( activeSeat == seatIndex )
				AdvanceTurn();
		}

		s.IsOccupied = false;
		s.SteamId = "";
		s.PlayerName = "";
		s.Chips = 0;
		s.ClearForNewHand();

		Table?.SetSeatChips( seatIndex, 0 );

		// Hand may need to terminate if too few players remain
		if ( handInProgress )
		{
			int remaining = 0;
			for ( int i = 0; i < MaxSeats; i++ )
				if ( seats[i].IsOccupied && !seats[i].HasFolded && !seats[i].IsSittingOut ) remaining++;
			if ( remaining < 2 )
			{
				IsBettingRoundComplete(); // triggers single-winner award
			}
		}
	}

	public void OnAllPlayersEjected()
	{
		for ( int i = 0; i < MaxSeats; i++ )
		{
			seats[i].IsOccupied = false;
			seats[i].SteamId = "";
			seats[i].PlayerName = "";
			seats[i].Chips = 0;
			seats[i].ClearForNewHand();
		}
		handInProgress = false;
		cleanupInProgress = false;
		dealingInProgress = false;
		gameSessionActive = false;
		currentPhase = PokerPhase.Idle;
	}

	// === Deck management ===

	private void ShuffleDeck()
	{
		deck.Clear();
		for ( int c = 0; c < 52; c++ ) deck.Add( c );
		var rng = new System.Random();
		for ( int i = deck.Count - 1; i > 0; i-- )
		{
			int j = rng.Next( i + 1 );
			(deck[i], deck[j]) = (deck[j], deck[i]);
		}
		deckIndex = 0;
	}

	private int DrawCard()
	{
		if ( deckIndex >= deck.Count ) ShuffleDeck();
		return deck[deckIndex++];
	}

	public static int GetSuit( int card ) => card / 13;
	public static int GetRank( int card ) => (card % 13) + 1; // 1=A, 2-10, 11=J, 12=Q, 13=K
	private static CardData ToCardData( int card ) => new CardData( GetSuit( card ), GetRank( card ), false );

	// === Broadcast RPCs ===

	private bool IsLocalTable()
	{
		if ( PokerBridge.ActiveTable == null || Table == null ) return false;
		if ( Table.GameObject == null || PokerBridge.ActiveTable.GameObject == null ) return false;
		return Table.GameObject.Id == PokerBridge.ActiveTable.GameObject.Id;
	}

	[Rpc.Broadcast]
	private void BroadcastPhaseChange( int phase )
	{
		if ( !IsLocalTable() ) return;
		PokerBridge.SetPhase( (PokerPhase)phase );
	}

	[Rpc.Broadcast]
	private void BroadcastGameSessionStart()
	{
		if ( !IsLocalTable() ) return;
		PokerBridge.GameStarted = true;
	}

	[Rpc.Broadcast]
	private void BroadcastGameSessionEnd()
	{
		if ( !IsLocalTable() ) return;
		PokerBridge.GameStarted = false;
	}

	[Rpc.Broadcast]
	private void BroadcastResetHand()
	{
		if ( !IsLocalTable() ) return;
		PokerBridge.ResetForNewHand();
	}

	[Rpc.Broadcast]
	private void BroadcastRoundReset()
	{
		if ( !IsLocalTable() ) return;
		// Reset per-round bet tracking on all clients for the new betting round
		for ( int i = 0; i < PokerBridge.MaxSeats; i++ )
			PokerBridge.Seats[i].ChipsInPotThisRound = 0;
		PokerBridge.CurrentBet = 0;
	}

	[Rpc.Broadcast]
	private void BroadcastButtonMove( int btn, int sb, int bb )
	{
		if ( !IsLocalTable() ) return;
		PokerBridge.ButtonSeat = btn;
		for ( int i = 0; i < PokerBridge.MaxSeats; i++ )
		{
			PokerBridge.Seats[i].IsButton = (i == btn);
			PokerBridge.Seats[i].IsSmallBlind = (i == sb);
			PokerBridge.Seats[i].IsBigBlind = (i == bb);
		}
	}

	[Rpc.Broadcast]
	private void BroadcastDealCommunityCards( int[] suitsRanks )
	{
		if ( !IsLocalTable() ) return;
		for ( int i = 0; i < suitsRanks.Length / 2; i++ )
		{
			PokerBridge.CommunityCards.Add( new CardData( suitsRanks[i * 2], suitsRanks[i * 2 + 1], false ) );
		}
		if ( PokerBridge.IsOpen )
			PokerBridge.ActiveTable?.PlayCardSound();
	}

	[Rpc.Broadcast]
	private void BroadcastBetAction( int seatIdx, int actionType, long amount, long newCurrentBet, long seatChipsRemaining )
	{
		if ( !IsLocalTable() ) return;
		var seat = PokerBridge.Seats[seatIdx];
		seat.LastAction = (PokerActionType)actionType;
		seat.LastActionAmount = amount;
		seat.LastActionTime = RealTime.Now;
		seat.Chips = seatChipsRemaining;

		var act = (PokerActionType)actionType;
		if ( act == PokerActionType.Fold ) seat.HasFolded = true;
		if ( act == PokerActionType.AllIn ) seat.IsAllIn = true;

		// Recompute contribution this round (best-effort client view)
		if ( act == PokerActionType.Call || act == PokerActionType.Bet || act == PokerActionType.Raise
			|| act == PokerActionType.AllIn || act == PokerActionType.SmallBlind || act == PokerActionType.BigBlind )
		{
			long previousInRound = seat.ChipsInPotThisRound;
			if ( act == PokerActionType.Call )
				seat.ChipsInPotThisRound = newCurrentBet;
			else if ( act == PokerActionType.Bet || act == PokerActionType.Raise || act == PokerActionType.AllIn )
				seat.ChipsInPotThisRound = amount;
			else // SmallBlind / BigBlind
				seat.ChipsInPotThisRound += amount;

			long delta = seat.ChipsInPotThisRound - previousInRound;
			if ( delta > 0 ) seat.TotalContributedThisHand += delta;

			// Update local pot view (sum of all contributions)
			long mainPot = 0;
			for ( int i = 0; i < PokerBridge.MaxSeats; i++ )
				mainPot += PokerBridge.Seats[i].TotalContributedThisHand;
			PokerBridge.MainPot = mainPot;
		}

		PokerBridge.CurrentBet = newCurrentBet;

		if ( PokerBridge.IsOpen && (act == PokerActionType.Bet || act == PokerActionType.Raise || act == PokerActionType.Call || act == PokerActionType.AllIn || act == PokerActionType.SmallBlind || act == PokerActionType.BigBlind) )
			PokerBridge.ActiveTable?.PlayChipSound();

		// Recompute local actions if this changes the bet level
		PokerBridge.RecomputeLocalActions( PokerBridge.ActiveSeat, PokerBridge.CurrentBet, PokerBridge.MinRaise );
	}

	private void BroadcastActiveSeat( int seatIdx, float duration )
	{
		// Send host-authoritative values so clients compute actions correctly
		BroadcastActiveSeatRpc( seatIdx, duration, currentBet, minRaise );
	}

	[Rpc.Broadcast]
	private void BroadcastActiveSeatRpc( int seatIdx, float duration, long hostCurrentBet, long hostMinRaise )
	{
		if ( !IsLocalTable() ) return;
		PokerBridge.ActiveSeat = seatIdx;
		PokerBridge.ActionDeadline = RealTime.Now + duration;
		PokerBridge.TurnDuration = duration;
		PokerBridge.CurrentBet = hostCurrentBet;
		PokerBridge.MinRaise = hostMinRaise;
		PokerBridge.RecomputeLocalActions( seatIdx, hostCurrentBet, hostMinRaise );
	}

	[Rpc.Broadcast]
	private void BroadcastPotUpdate( long mainPot, long[] sidePots )
	{
		if ( !IsLocalTable() ) return;
		PokerBridge.MainPot = mainPot;
		PokerBridge.SidePots.Clear();
		if ( sidePots != null )
			foreach ( var sp in sidePots ) PokerBridge.SidePots.Add( sp );
	}

	[Rpc.Broadcast]
	private void BroadcastShowdownReveal( int seatIdx, int s1, int r1, int s2, int r2, string label, int rank )
	{
		if ( !IsLocalTable() ) return;
		var seat = PokerBridge.Seats[seatIdx];
		seat.HoleCards.Clear();
		seat.HoleCards.Add( new CardData( s1, r1, false ) );
		seat.HoleCards.Add( new CardData( s2, r2, false ) );
		seat.ShowdownRevealed = true;
		seat.ShowdownLabel = label;
		seat.ShowdownRank = (PokerHandRank)rank;
	}

	[Rpc.Broadcast]
	private void BroadcastWinPot( int[] winningSeats, long[] amounts, int potIndex, string label )
	{
		if ( !IsLocalTable() ) return;
		string parts = "";
		for ( int i = 0; i < winningSeats.Length; i++ )
		{
			var name = PokerBridge.Seats[winningSeats[i]].PlayerName;
			parts += $"{name} wins {amounts[i]:N0}";
			if ( i < winningSeats.Length - 1 ) parts += ", ";
		}
		PokerBridge.LastWinnerText = string.IsNullOrEmpty( label ) ? parts : $"{parts} — {label}";
	}

	[Rpc.Broadcast]
	private void BroadcastShowdownSounds( int[] winnerSeats )
	{
		if ( !IsLocalTable() ) return;
		if ( !PokerBridge.IsOpen ) return;

		string localId = Connection.Local?.SteamId.ToString() ?? "";
		bool isLocalWinner = false;
		for ( int i = 0; i < winnerSeats.Length; i++ )
		{
			if ( PokerBridge.Seats[winnerSeats[i]].SteamId.ToString() == localId )
			{
				isLocalWinner = true; break;
			}
		}

		if ( isLocalWinner )
			PokerBridge.ActiveTable?.PlayWinSound();
		else if ( PokerBridge.LocalSeatIndex >= 0 && !PokerBridge.Seats[PokerBridge.LocalSeatIndex].HasFolded )
			PokerBridge.ActiveTable?.PlayLoseSound();
	}

	[Rpc.Broadcast]
	private void BroadcastCreditChange( string steamId, int amount, bool isWinning )
	{
		string localId = Connection.Local?.SteamId.ToString() ?? "";
		if ( localId != steamId ) return;

		if ( isWinning )
		{
			PokerBridge.CachedBalance += amount;
			PokerBridge.SessionNetChange += amount;
			Sandbox.Services.Stats.Increment( "casino_won", amount );
		}
		else
		{
			PokerBridge.CachedBalance -= amount;
			PokerBridge.SessionNetChange -= amount;
		}
	}

	[Rpc.Broadcast]
	private void BroadcastWagerChange( string steamId, int amount )
	{
		string localId = Connection.Local?.SteamId.ToString() ?? "";
		if ( localId != steamId ) return;
		// Chips moving from stack to pot — track as wagered for the casino leaderboard
		Sandbox.Services.Stats.Increment( "credits_wagered", amount );
	}

	[Rpc.Broadcast]
	private void BroadcastBustOut( string steamId )
	{
		string localId = Connection.Local?.SteamId.ToString() ?? "";
		if ( localId != steamId ) return;

		// Local player busted — unmount, commit credits, close UI
		var localPlayer = Scene.GetAllComponents<PlayerController>()
			.FirstOrDefault( p => !p.IsProxy && p.GameObject.Network.Owner != null );

		// Commit session credit changes
		PokerBridge.LastKnownBalance = PokerBridge.CachedBalance;
		PokerBridge.LastLeaveTime = System.DateTime.UtcNow;

		int net = PokerBridge.SessionNetChange;
		if ( net > 0 )
			Sandbox.Services.Stats.Increment( "credits", net );
		else if ( net < 0 )
			Sandbox.Services.Stats.Increment( "credits_spent", -net );

		localPlayer?.UnmountFromStation();
		PokerBridge.Close();
	}

	private void BroadcastHoleCardPlaceholders( int seatIdx )
	{
		BroadcastSetHoleCardPlaceholdersRpc( seatIdx );
	}

	[Rpc.Broadcast]
	private void BroadcastSetHoleCardPlaceholdersRpc( int seatIdx )
	{
		if ( !IsLocalTable() ) return;
		var seat = PokerBridge.Seats[seatIdx];
		seat.HoleCards.Clear();
		seat.HoleCards.Add( new CardData( 0, 0, true ) );
		seat.HoleCards.Add( new CardData( 0, 0, true ) );
	}

	private void SendHoleCardsToPlayer( int seatIdx, string steamId, int card1, int card2 )
	{
		// Filter the broadcast to only the matching connection so other players never see the cards.
		var conn = Connection.All.FirstOrDefault( c => c.SteamId.ToString() == steamId );
		if ( conn == null ) return;

		using ( Rpc.FilterInclude( c => c == conn ) )
		{
			SendHoleCardsRpc( seatIdx, GetSuit( card1 ), GetRank( card1 ), GetSuit( card2 ), GetRank( card2 ) );
		}
	}

	[Rpc.Broadcast]
	private void SendHoleCardsRpc( int seatIdx, int s1, int r1, int s2, int r2 )
	{
		if ( !IsLocalTable() ) return;
		PokerBridge.LocalHoleCards.Clear();
		PokerBridge.LocalHoleCards.Add( new CardData( s1, r1, false ) );
		PokerBridge.LocalHoleCards.Add( new CardData( s2, r2, false ) );
		// Also overlay onto the seat's hole cards so they render face-up at the local seat
		var seat = PokerBridge.Seats[seatIdx];
		seat.HoleCards.Clear();
		seat.HoleCards.Add( new CardData( s1, r1, false ) );
		seat.HoleCards.Add( new CardData( s2, r2, false ) );
		if ( PokerBridge.IsOpen )
			PokerBridge.ActiveTable?.PlayCardSound();
	}
}
