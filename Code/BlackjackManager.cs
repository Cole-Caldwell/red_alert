using Sandbox;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public sealed class BlackjackManager : Component
{
	[Property] public BlackjackTable Table { get; set; }

	private const int MinBet = 5;
	private const int MaxBet = 2000;
	private const int DeckCount = 6;
	private const float BetTimeout = 30f;
	private const float TurnTimeout = 20f;
	private const float PayoutDisplayTime = 3f;

	// Shoe
	private List<int> shoe = new();
	private int shoeIndex = 0;
	private int cutCardPosition = 0;

	// Host-side seat state
	private SeatState[] seats = new SeatState[7];
	private List<int> dealerHand = new();
	private BlackjackPhase currentPhase = BlackjackPhase.Idle;
	private int activeSeatIndex = -1;
	private int activeHandIndex = 0;
	private float phaseTimer = 0f;
	private float turnTimer = 0f;
	private bool roundInProgress = false;
	private bool betTimerStarted = false;
	private float betStatusBroadcastTimer = 0f;
	private Dictionary<string, int> pendingWagers = new();

	private class SeatState
	{
		public string SteamId = "";
		public string PlayerName = "";
		public bool IsOccupied = false;
		public List<List<int>> Hands = new();
		public List<int> Bets = new();
		public int ActiveHandIndex = 0;
		public bool HasInsurance = false;
		public int InsuranceBet = 0;
		public bool HasBet = false;
		public bool IsSplitAces = false;

		public void Clear()
		{
			Hands.Clear();
			Bets.Clear();
			ActiveHandIndex = 0;
			HasInsurance = false;
			InsuranceBet = 0;
			HasBet = false;
			IsSplitAces = false;
		}
	}

	protected override void OnStart()
	{
		for ( int i = 0; i < 7; i++ )
			seats[i] = new SeatState();

		ShuffleShoe();
	}

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost ) return;

		switch ( currentPhase )
		{
			case BlackjackPhase.Idle:
				UpdateIdle();
				break;
			case BlackjackPhase.WaitingForBets:
				UpdateWaitingForBets();
				break;
			case BlackjackPhase.PlayerTurns:
				UpdatePlayerTurns();
				break;
		}
	}

	// --- State Updates (Host) ---

	private void UpdateIdle()
	{
		// Start a new round if any player is seated
		int occupiedCount = 0;
		for ( int i = 0; i < 7; i++ )
		{
			if ( seats[i].IsOccupied )
				occupiedCount++;
		}

		if ( occupiedCount > 0 && !roundInProgress )
		{
			StartBettingPhase();
		}
	}

	private void UpdateWaitingForBets()
	{
		// Check if all seated players have placed bets
		bool allBet = true;
		bool anyBet = false;
		int occupiedCount = 0;

		for ( int i = 0; i < 7; i++ )
		{
			if ( !seats[i].IsOccupied ) continue;
			occupiedCount++;
			if ( seats[i].HasBet ) anyBet = true;
			else allBet = false;
		}

		// Nobody seated anymore — end round
		if ( occupiedCount == 0 )
		{
			EndRound();
			return;
		}

		// Start countdown only after first bet is placed
		if ( anyBet && !betTimerStarted )
		{
			betTimerStarted = true;
			phaseTimer = BetTimeout;
		}

		// Only tick timer after it has started
		if ( betTimerStarted )
			phaseTimer -= Time.Delta;

		// All seated players have bet — deal immediately
		if ( allBet && anyBet )
		{
			_ = DealInitialCards();
		}
		// Timeout expired, at least one person bet — deal for those who bet
		else if ( betTimerStarted && phaseTimer <= 0f && anyBet )
		{
			_ = DealInitialCards();
		}
		// Broadcast bet status periodically during countdown
		else if ( betTimerStarted )
		{
			betStatusBroadcastTimer -= Time.Delta;
			if ( betStatusBroadcastTimer <= 0f )
			{
				int remaining = 0;
				for ( int i = 0; i < 7; i++ )
				{
					if ( seats[i].IsOccupied && !seats[i].HasBet ) remaining++;
				}
				BroadcastBetStatus( remaining, phaseTimer );
				betStatusBroadcastTimer = 1f;
			}
		}
	}

	private void UpdatePlayerTurns()
	{
		turnTimer -= Time.Delta;
		if ( turnTimer <= 0f )
		{
			// Auto-stand on timeout
			AutoStandCurrentPlayer();
		}
	}

	// --- Round Flow ---

	private void StartBettingPhase()
	{
		roundInProgress = true;
		phaseTimer = BetTimeout;
		betTimerStarted = false;
		betStatusBroadcastTimer = 0f;

		for ( int i = 0; i < 7; i++ )
		{
			seats[i].Clear();
		}
		dealerHand.Clear();
		pendingWagers.Clear();

		currentPhase = BlackjackPhase.WaitingForBets;
		BroadcastPhaseChange( (int)BlackjackPhase.WaitingForBets, "" );
	}

	private async Task DealInitialCards()
	{
		currentPhase = BlackjackPhase.Dealing;
		BroadcastPhaseChange( (int)BlackjackPhase.Dealing, "Dealing cards..." );

		// Remove players who didn't bet
		for ( int i = 0; i < 7; i++ )
		{
			if ( seats[i].IsOccupied && !seats[i].HasBet )
			{
				seats[i].Hands.Clear();
			}
		}

		// Check if shoe needs reshuffling
		if ( shoeIndex >= cutCardPosition )
			ShuffleShoe();

		// Deal first card to each player
		for ( int i = 0; i < 7; i++ )
		{
			if ( !seats[i].IsOccupied || !seats[i].HasBet ) continue;

			int card = DrawCard();
			seats[i].Hands.Add( new List<int> { card } );
			var cd = ToCardData( card, false );
			BroadcastDealCard( i, 0, cd.Suit, cd.Rank, false );
			await Task.DelayRealtimeSeconds( 0.3f );
		}

		// Dealer first card (face up)
		int dealerCard1 = DrawCard();
		dealerHand.Add( dealerCard1 );
		var dc1 = ToCardData( dealerCard1, false );
		BroadcastDealerCard( dc1.Suit, dc1.Rank, false );
		await Task.DelayRealtimeSeconds( 0.3f );

		// Deal second card to each player
		for ( int i = 0; i < 7; i++ )
		{
			if ( !seats[i].IsOccupied || !seats[i].HasBet ) continue;

			int card = DrawCard();
			seats[i].Hands[0].Add( card );
			var cd = ToCardData( card, false );
			BroadcastDealCard( i, 0, cd.Suit, cd.Rank, false );
			await Task.DelayRealtimeSeconds( 0.3f );
		}

		// Dealer second card (face down — send hidden)
		int dealerCard2 = DrawCard();
		dealerHand.Add( dealerCard2 );
		BroadcastDealerCard( -1, -1, true );
		await Task.DelayRealtimeSeconds( 0.3f );

		// Sync scores
		for ( int i = 0; i < 7; i++ )
		{
			if ( !seats[i].IsOccupied || !seats[i].HasBet ) continue;
			var (score, _) = CalculateScore( seats[i].Hands[0] );
			BroadcastUpdateScore( i, 0, score );
		}

		// Show dealer's visible score (first card only)
		int dealerVisible = CardValue( dealerCard1 );
		BroadcastDealerScore( dealerVisible );

		// Check for insurance (dealer shows Ace)
		if ( GetRank( dealerCard1 ) == 1 )
		{
			await HandleInsurance();
		}

		// Check for dealer blackjack
		var (dealerScore, _) = CalculateScore( dealerHand );
		if ( dealerScore == 21 )
		{
			await HandleDealerBlackjack();
			return;
		}

		// Natural blackjacks are detected during player turns (AdvanceToNextPlayer skips them)
		// and resolved in ResolvePayout — no early BroadcastHandResult to avoid double sound

		// Start player turns
		StartPlayerTurns();
	}

	private async Task HandleInsurance()
	{
		BroadcastOfferInsurance();
		await Task.DelayRealtimeSeconds( 8f );
		BroadcastCloseInsurance();
	}

	private async Task HandleDealerBlackjack()
	{
		// Reveal hole card
		var dc2 = ToCardData( dealerHand[1], false );
		BroadcastRevealHoleCard( dc2.Suit, dc2.Rank );

		var (score, _) = CalculateScore( dealerHand );
		BroadcastDealerScore( score );

		await Task.DelayRealtimeSeconds( 1f );

		// Resolve all hands
		for ( int i = 0; i < 7; i++ )
		{
			if ( !seats[i].IsOccupied || !seats[i].HasBet ) continue;

			var (pScore, _) = CalculateScore( seats[i].Hands[0] );

			if ( pScore == 21 && seats[i].Hands[0].Count == 2 )
			{
				// Player also has blackjack — push, return bet
				BroadcastHandResult( i, 0, (int)HandResult.Push, seats[i].Bets[0] );
				BroadcastCreditChange( seats[i].SteamId, seats[i].Bets[0], true );
			}
			else
			{
				// Player loses — bet already deducted
				BroadcastHandResult( i, 0, (int)HandResult.Loss, 0 );
			}

			// Pay insurance if taken (2:1 payout = stake back + 2x profit)
			if ( seats[i].HasInsurance )
			{
				int insurancePay = seats[i].InsuranceBet * 3;
				BroadcastCreditChange( seats[i].SteamId, insurancePay, true );
			}
		}

		await Task.DelayRealtimeSeconds( PayoutDisplayTime );
		EndRound();
	}

	private void StartPlayerTurns()
	{
		currentPhase = BlackjackPhase.PlayerTurns;
		activeSeatIndex = -1;
		activeHandIndex = 0;
		AdvanceToNextPlayer();
	}

	private void AdvanceToNextPlayer()
	{
		// Move to next hand if current seat has more hands (split)
		if ( activeSeatIndex >= 0 && activeSeatIndex < 7 )
		{
			var seat = seats[activeSeatIndex];
			if ( seat.IsOccupied && seat.HasBet && activeHandIndex + 1 < seat.Hands.Count )
			{
				activeHandIndex++;
				turnTimer = TurnTimeout;
				NotifyPlayerTurn();
				return;
			}
		}

		// Find next seat with cards
		activeHandIndex = 0;
		for ( int i = activeSeatIndex + 1; i < 7; i++ )
		{
			if ( !seats[i].IsOccupied || !seats[i].HasBet ) continue;
			if ( seats[i].Hands.Count == 0 ) continue;

			// Skip if player has natural blackjack
			var (score, _) = CalculateScore( seats[i].Hands[0] );
			if ( score == 21 && seats[i].Hands[0].Count == 2 && seats[i].Hands.Count == 1 )
				continue;

			activeSeatIndex = i;
			turnTimer = TurnTimeout;
			NotifyPlayerTurn();
			return;
		}

		// No more players — dealer's turn
		_ = DealerTurn();
	}

	private void NotifyPlayerTurn()
	{
		var seat = seats[activeSeatIndex];
		var hand = seat.Hands[activeHandIndex];
		var (score, _) = CalculateScore( hand );

		bool canHit = score < 21;
		bool canStand = true;
		bool canDouble = hand.Count == 2 && seat.Bets.Count > activeHandIndex;
		bool canSplit = hand.Count == 2 && GetRank( hand[0] ) == GetRank( hand[1] ) && seat.Hands.Count == 1;

		// Can't hit split aces
		if ( seat.IsSplitAces )
		{
			canHit = false;
			canDouble = false;
		}

		BroadcastNotifyTurn( activeSeatIndex, activeHandIndex, canHit, canStand, canDouble, canSplit, seat.SteamId );
	}

	private async Task DealerTurn()
	{
		currentPhase = BlackjackPhase.DealerTurn;
		BroadcastPhaseChange( (int)BlackjackPhase.DealerTurn, "Dealer's turn..." );

		// Check if any players still have active (non-busted, non-blackjack) hands
		bool anyActive = false;
		for ( int i = 0; i < 7; i++ )
		{
			if ( !seats[i].IsOccupied || !seats[i].HasBet ) continue;
			for ( int h = 0; h < seats[i].Hands.Count; h++ )
			{
				var (s, _) = CalculateScore( seats[i].Hands[h] );
				if ( s <= 21 && !( s == 21 && seats[i].Hands[h].Count == 2 && seats[i].Hands.Count == 1 ) )
				{
					anyActive = true;
					break;
				}
			}
			if ( anyActive ) break;
		}

		// Reveal hole card
		var dc2 = ToCardData( dealerHand[1], false );
		BroadcastRevealHoleCard( dc2.Suit, dc2.Rank );
		await Task.DelayRealtimeSeconds( 0.5f );

		if ( anyActive )
		{
			// Dealer draws: hit on soft 17 and below
			while ( true )
			{
				var (score, isSoft) = CalculateScore( dealerHand );
				BroadcastDealerScore( score );

				if ( score > 21 ) break; // Bust
				if ( score > 17 ) break; // Stand on hard 18+
				if ( score == 17 && !isSoft ) break; // Stand on hard 17

				// Hit
				await Task.DelayRealtimeSeconds( 0.7f );
				int card = DrawCard();
				dealerHand.Add( card );
				var cd = ToCardData( card, false );
				BroadcastDealerCard( cd.Suit, cd.Rank, false );
			}
		}

		var (finalScore, _) = CalculateScore( dealerHand );
		BroadcastDealerScore( finalScore );

		await Task.DelayRealtimeSeconds( 0.5f );

		// Payout
		await ResolvePayout( finalScore );
	}

	private async Task ResolvePayout( int dealerScore )
	{
		currentPhase = BlackjackPhase.Payout;
		BroadcastPhaseChange( (int)BlackjackPhase.Payout, "Round complete!" );

		bool dealerBust = dealerScore > 21;

		for ( int i = 0; i < 7; i++ )
		{
			if ( !seats[i].IsOccupied || !seats[i].HasBet ) continue;

			for ( int h = 0; h < seats[i].Hands.Count; h++ )
			{
				var (pScore, _) = CalculateScore( seats[i].Hands[h] );
				int bet = h < seats[i].Bets.Count ? seats[i].Bets[h] : 0;
				HandResult result;
				int payout = 0;

				// Skip busted hands — already resolved during player turns
				if ( pScore > 21 )
					continue;

				// Natural blackjack
				if ( pScore == 21 && seats[i].Hands[h].Count == 2 && seats[i].Hands.Count == 1 )
				{
					// 3:2 payout
					payout = bet + (int)(bet * 1.5f);
					result = HandResult.Blackjack;
				}
				else if ( dealerBust )
				{
					// Dealer bust, player wins
					result = HandResult.Win;
					payout = bet * 2;
				}
				else if ( pScore > dealerScore )
				{
					result = HandResult.Win;
					payout = bet * 2;
				}
				else if ( pScore == dealerScore )
				{
					result = HandResult.Push;
					payout = bet;
				}
				else
				{
					result = HandResult.Loss;
					payout = 0;
				}

				BroadcastHandResult( i, h, (int)result, payout );

				// Return payout to player (bet was already deducted when placed)
				if ( payout > 0 )
				{
					BroadcastCreditChange( seats[i].SteamId, payout, true );
				}
			}
		}

		await Task.DelayRealtimeSeconds( PayoutDisplayTime );
		EndRound();
	}

	private void EndRound()
	{
		roundInProgress = false;
		currentPhase = BlackjackPhase.Idle;
		activeSeatIndex = -1;
		activeHandIndex = 0;
		betTimerStarted = false;
		betStatusBroadcastTimer = 0f;
		pendingWagers.Clear();

		BroadcastPhaseChange( (int)BlackjackPhase.Idle, "Waiting for next round..." );
		BroadcastResetRound();
	}

	private void AutoStandCurrentPlayer()
	{
		if ( activeSeatIndex < 0 || activeSeatIndex >= 7 ) return;
		AdvanceToNextPlayer();
	}

	// --- Player Action RPCs (called by clients) ---

	[Rpc.Broadcast]
	public void RequestPlaceBet( string steamId, int amount )
	{
		if ( !Networking.IsHost ) return;
		if ( currentPhase != BlackjackPhase.WaitingForBets ) return;

		int seatIndex = -1;
		for ( int i = 0; i < 7; i++ )
		{
			if ( seats[i].SteamId == steamId )
			{
				seatIndex = i;
				break;
			}
		}
		if ( seatIndex < 0 ) return;
		if ( seats[seatIndex].HasBet ) return;

		if ( amount < MinBet || amount > MaxBet ) return;

		seats[seatIndex].HasBet = true;
		seats[seatIndex].Bets.Add( amount );

		int pendingTotal = pendingWagers.ContainsKey( steamId ) ? pendingWagers[steamId] : 0;
		pendingWagers[steamId] = pendingTotal + amount;

		BroadcastConfirmBet( seatIndex, amount );
	}

	[Rpc.Broadcast]
	public void RequestHit( string steamId )
	{
		if ( !Networking.IsHost ) return;
		if ( currentPhase != BlackjackPhase.PlayerTurns ) return;
		if ( activeSeatIndex < 0 || seats[activeSeatIndex].SteamId != steamId ) return;

		var seat = seats[activeSeatIndex];
		var hand = seat.Hands[activeHandIndex];

		int card = DrawCard();
		hand.Add( card );

		var cd = ToCardData( card, false );
		BroadcastDealCard( activeSeatIndex, activeHandIndex, cd.Suit, cd.Rank, false );

		var (score, _) = CalculateScore( hand );
		BroadcastUpdateScore( activeSeatIndex, activeHandIndex, score );

		if ( score >= 21 )
		{
			// Bust or 21 — move on
			if ( score > 21 )
				BroadcastHandResult( activeSeatIndex, activeHandIndex, (int)HandResult.Loss, 0 );

			AdvanceToNextPlayer();
		}
		else
		{
			turnTimer = TurnTimeout;
			NotifyPlayerTurn();
		}
	}

	[Rpc.Broadcast]
	public void RequestStand( string steamId )
	{
		if ( !Networking.IsHost ) return;
		if ( currentPhase != BlackjackPhase.PlayerTurns ) return;
		if ( activeSeatIndex < 0 || seats[activeSeatIndex].SteamId != steamId ) return;

		AdvanceToNextPlayer();
	}

	[Rpc.Broadcast]
	public void RequestDouble( string steamId )
	{
		if ( !Networking.IsHost ) return;
		if ( currentPhase != BlackjackPhase.PlayerTurns ) return;
		if ( activeSeatIndex < 0 || seats[activeSeatIndex].SteamId != steamId ) return;

		var seat = seats[activeSeatIndex];
		var hand = seat.Hands[activeHandIndex];

		if ( hand.Count != 2 ) return;

		// Double the bet
		int originalBet = seat.Bets[activeHandIndex];
		seat.Bets[activeHandIndex] = originalBet * 2;

		int pendingTotal = pendingWagers.ContainsKey( steamId ) ? pendingWagers[steamId] : 0;
		pendingWagers[steamId] = pendingTotal + originalBet;

		BroadcastConfirmBet( activeSeatIndex, originalBet * 2 );

		// Deal exactly one card
		int card = DrawCard();
		hand.Add( card );

		var cd = ToCardData( card, false );
		BroadcastDealCard( activeSeatIndex, activeHandIndex, cd.Suit, cd.Rank, false );

		var (score, _2) = CalculateScore( hand );
		BroadcastUpdateScore( activeSeatIndex, activeHandIndex, score );

		if ( score > 21 )
			BroadcastHandResult( activeSeatIndex, activeHandIndex, (int)HandResult.Loss, 0 );

		// Auto-stand after double
		AdvanceToNextPlayer();
	}

	[Rpc.Broadcast]
	public void RequestSplit( string steamId )
	{
		if ( !Networking.IsHost ) return;
		if ( currentPhase != BlackjackPhase.PlayerTurns ) return;
		if ( activeSeatIndex < 0 || seats[activeSeatIndex].SteamId != steamId ) return;

		var seat = seats[activeSeatIndex];
		if ( seat.Hands.Count != 1 ) return; // No re-splitting

		var hand = seat.Hands[0];
		if ( hand.Count != 2 ) return;
		if ( GetRank( hand[0] ) != GetRank( hand[1] ) ) return;

		bool isAces = GetRank( hand[0] ) == 1;
		seat.IsSplitAces = isAces;

		// Create second hand
		int card2 = hand[1];
		hand.RemoveAt( 1 );

		var newHand = new List<int> { card2 };
		seat.Hands.Add( newHand );

		// Add matching bet for second hand
		int originalBet = seat.Bets[0];
		seat.Bets.Add( originalBet );

		int pendingTotal = pendingWagers.ContainsKey( steamId ) ? pendingWagers[steamId] : 0;
		pendingWagers[steamId] = pendingTotal + originalBet;

		// Deal one card to each hand
		int newCard1 = DrawCard();
		hand.Add( newCard1 );
		var cd1 = ToCardData( newCard1, false );

		int newCard2 = DrawCard();
		newHand.Add( newCard2 );
		var cd2 = ToCardData( newCard2, false );

		BroadcastSplit( activeSeatIndex, ToCardData( card2, false ).Suit, ToCardData( card2, false ).Rank,
			cd1.Suit, cd1.Rank, cd2.Suit, cd2.Rank, originalBet );

		// Update scores
		var (score1, _) = CalculateScore( hand );
		var (score2, _2) = CalculateScore( newHand );
		BroadcastUpdateScore( activeSeatIndex, 0, score1 );
		BroadcastUpdateScore( activeSeatIndex, 1, score2 );

		if ( isAces )
		{
			// Split aces: one card each, then move on
			AdvanceToNextPlayer();
		}
		else
		{
			// Continue with first hand
			activeHandIndex = 0;
			turnTimer = TurnTimeout;
			NotifyPlayerTurn();
		}
	}

	[Rpc.Broadcast]
	public void RequestInsurance( string steamId, bool accept )
	{
		if ( !Networking.IsHost ) return;

		int seatIndex = -1;
		for ( int i = 0; i < 7; i++ )
		{
			if ( seats[i].SteamId == steamId )
			{
				seatIndex = i;
				break;
			}
		}
		if ( seatIndex < 0 ) return;

		if ( accept )
		{
			int insuranceCost = seats[seatIndex].Bets[0] / 2;
			seats[seatIndex].HasInsurance = true;
			seats[seatIndex].InsuranceBet = insuranceCost;

			int pendingTotal = pendingWagers.ContainsKey( steamId ) ? pendingWagers[steamId] : 0;
			pendingWagers[steamId] = pendingTotal + insuranceCost;
		}
	}

	// --- Player Join/Leave Callbacks (from BlackjackTable) ---

	public void OnPlayerJoined( int seatIndex, string steamId, string name )
	{
		seats[seatIndex].SteamId = steamId;
		seats[seatIndex].PlayerName = name;
		seats[seatIndex].IsOccupied = true;
	}

	public void OnPlayerLeft( int seatIndex, string steamId )
	{
		var seat = seats[seatIndex];

		// Bet was already deducted when placed — no additional deduction needed on leave

		seat.IsOccupied = false;
		seat.SteamId = "";
		seat.PlayerName = "";
		seat.Clear();

		// If this was the active player, advance
		if ( currentPhase == BlackjackPhase.PlayerTurns && activeSeatIndex == seatIndex )
		{
			AdvanceToNextPlayer();
		}

		// If no players left, end round
		bool anyOccupied = false;
		for ( int i = 0; i < 7; i++ )
		{
			if ( seats[i].IsOccupied ) { anyOccupied = true; break; }
		}
		if ( !anyOccupied && roundInProgress )
		{
			EndRound();
		}
	}

	public void OnAllPlayersEjected()
	{
		for ( int i = 0; i < 7; i++ )
		{
			seats[i].IsOccupied = false;
			seats[i].SteamId = "";
			seats[i].PlayerName = "";
			seats[i].Clear();
		}
		if ( roundInProgress )
			EndRound();
	}

	// --- Shoe Management ---

	private void ShuffleShoe()
	{
		shoe.Clear();
		for ( int d = 0; d < DeckCount; d++ )
		{
			for ( int c = 0; c < 52; c++ )
			{
				shoe.Add( c );
			}
		}

		// Fisher-Yates shuffle
		var rng = new System.Random();
		for ( int i = shoe.Count - 1; i > 0; i-- )
		{
			int j = rng.Next( i + 1 );
			(shoe[i], shoe[j]) = (shoe[j], shoe[i]);
		}

		shoeIndex = 0;
		cutCardPosition = (int)(shoe.Count * 0.75f);

		Log.Info( $"[BlackjackManager] Shuffled {shoe.Count} cards, cut at {cutCardPosition}" );
	}

	private int DrawCard()
	{
		if ( shoeIndex >= shoe.Count )
			ShuffleShoe();

		return shoe[shoeIndex++];
	}

	// --- Card Helpers ---

	public static int GetSuit( int card ) => card / 13;
	public static int GetRank( int card ) => (card % 13) + 1; // 1=Ace, 2-10, 11=J, 12=Q, 13=K

	public static int CardValue( int card )
	{
		int rank = GetRank( card );
		if ( rank == 1 ) return 11;
		if ( rank >= 11 ) return 10;
		return rank;
	}

	private (int score, bool isSoft) CalculateScore( List<int> hand )
	{
		int total = 0;
		int aces = 0;

		foreach ( var card in hand )
		{
			int rank = GetRank( card );
			if ( rank == 1 ) { aces++; total += 11; }
			else if ( rank >= 11 ) total += 10;
			else total += rank;
		}

		while ( total > 21 && aces > 0 )
		{
			total -= 10;
			aces--;
		}

		return (total, aces > 0);
	}

	private CardData ToCardData( int card, bool faceDown )
	{
		return new CardData( GetSuit( card ), GetRank( card ), faceDown );
	}

	// --- Broadcast RPCs (host → all clients) ---

	private bool IsLocalTable()
	{
		return BlackjackBridge.ActiveTable != null && this.Table == BlackjackBridge.ActiveTable;
	}

	[Rpc.Broadcast]
	private void BroadcastPhaseChange( int phase, string message )
	{
		if ( !IsLocalTable() ) return;
		var p = (BlackjackPhase)phase;
		BlackjackBridge.SetPhase( p );
		if ( !string.IsNullOrEmpty( message ) )
			BlackjackBridge.StatusMessage = message;
	}

	[Rpc.Broadcast]
	private void BroadcastDealCard( int seatIndex, int handIndex, int suit, int rank, bool faceDown )
	{
		if ( !IsLocalTable() ) return;
		var card = new CardData( suit, rank, faceDown );
		var seat = BlackjackBridge.Seats[seatIndex];

		while ( seat.Hands.Count <= handIndex )
			seat.Hands.Add( new List<CardData>() );

		seat.Hands[handIndex].Add( card );

		// Play sound on local client
		if ( BlackjackBridge.IsOpen )
			BlackjackBridge.ActiveTable?.PlayCardSound();
	}

	[Rpc.Broadcast]
	private void BroadcastDealerCard( int suit, int rank, bool faceDown )
	{
		if ( !IsLocalTable() ) return;
		var card = new CardData( suit, rank, faceDown );
		BlackjackBridge.DealerCards.Add( card );

		if ( BlackjackBridge.IsOpen )
			BlackjackBridge.ActiveTable?.PlayCardSound();
	}

	[Rpc.Broadcast]
	private void BroadcastRevealHoleCard( int suit, int rank )
	{
		if ( !IsLocalTable() ) return;
		if ( BlackjackBridge.DealerCards.Count >= 2 )
		{
			BlackjackBridge.DealerCards[1] = new CardData( suit, rank, false );
		}
	}

	[Rpc.Broadcast]
	private void BroadcastDealerScore( int score )
	{
		if ( !IsLocalTable() ) return;
		BlackjackBridge.DealerScore = score;
	}

	[Rpc.Broadcast]
	private void BroadcastUpdateScore( int seatIndex, int handIndex, int score )
	{
		if ( !IsLocalTable() ) return;
		var seat = BlackjackBridge.Seats[seatIndex];
		while ( seat.HandScores.Count <= handIndex )
			seat.HandScores.Add( 0 );
		seat.HandScores[handIndex] = score;
	}

	[Rpc.Broadcast]
	private void BroadcastBetStatus( int remainingCount, float timeLeft )
	{
		if ( !IsLocalTable() ) return;
		if ( remainingCount > 0 && timeLeft > 0 )
			BlackjackBridge.StatusMessage = $"Waiting for {remainingCount} player(s) to bet... ({(int)timeLeft}s)";
		else
			BlackjackBridge.StatusMessage = "Place your bets!";
	}

	[Rpc.Broadcast]
	private void BroadcastConfirmBet( int seatIndex, int amount )
	{
		if ( !IsLocalTable() ) return;
		var seat = BlackjackBridge.Seats[seatIndex];
		while ( seat.HandBets.Count == 0 )
			seat.HandBets.Add( 0 );
		seat.HandBets[seat.HandBets.Count - 1] = amount;

		if ( BlackjackBridge.IsOpen )
			BlackjackBridge.ActiveTable?.PlayChipSound();
	}

	[Rpc.Broadcast]
	private void BroadcastNotifyTurn( int seatIndex, int handIndex, bool canHit, bool canStand, bool canDouble, bool canSplit, string activeSteamId )
	{
		if ( !IsLocalTable() ) return;
		BlackjackBridge.ActiveSeatIndex = seatIndex;
		BlackjackBridge.Seats[seatIndex].ActiveHandIndex = handIndex;

		string localId = Connection.Local?.SteamId.ToString() ?? "";
		bool isLocal = localId == activeSteamId;
		BlackjackBridge.IsLocalPlayerTurn = isLocal;
		BlackjackBridge.CanHit = isLocal && canHit;
		BlackjackBridge.CanStand = isLocal && canStand;
		BlackjackBridge.CanDouble = isLocal && canDouble;
		BlackjackBridge.CanSplit = isLocal && canSplit;

		BlackjackBridge.StatusMessage = isLocal ? "Your turn — Hit or Stand?" : $"{BlackjackBridge.Seats[seatIndex].PlayerName}'s turn...";
	}

	[Rpc.Broadcast]
	private void BroadcastHandResult( int seatIndex, int handIndex, int result, int payout )
	{
		if ( !IsLocalTable() ) return;
		var seat = BlackjackBridge.Seats[seatIndex];
		while ( seat.HandResults.Count <= handIndex )
			seat.HandResults.Add( HandResult.None );
		seat.HandResults[handIndex] = (HandResult)result;

		// Play win/lose sound for local player
		string localId = Connection.Local?.SteamId.ToString() ?? "";
		if ( seat.SteamId.ToString() == localId && BlackjackBridge.IsOpen )
		{
			if ( result == (int)HandResult.Win || result == (int)HandResult.Blackjack )
				BlackjackBridge.ActiveTable?.PlayWinSound();
			else if ( result == (int)HandResult.Loss )
				BlackjackBridge.ActiveTable?.PlayLoseSound();
		}
	}

	[Rpc.Broadcast]
	private void BroadcastCreditChange( string steamId, int amount, bool isWinning )
	{
		string localId = Connection.Local?.SteamId.ToString() ?? "";
		if ( localId != steamId ) return;

		if ( isWinning )
		{
			BlackjackBridge.CachedBalance += amount;
			BlackjackBridge.SessionNetChange += amount;
		}
		else
		{
			BlackjackBridge.CachedBalance -= amount;
			BlackjackBridge.SessionNetChange -= amount;
		}
	}

	[Rpc.Broadcast]
	private void BroadcastOfferInsurance()
	{
		if ( !IsLocalTable() ) return;
		BlackjackBridge.InsuranceOffered = true;
		BlackjackBridge.StatusMessage = "Insurance? Dealer shows Ace.";
	}

	[Rpc.Broadcast]
	private void BroadcastCloseInsurance()
	{
		if ( !IsLocalTable() ) return;
		BlackjackBridge.InsuranceOffered = false;
	}

	[Rpc.Broadcast]
	private void BroadcastSplit( int seatIndex, int card2Suit, int card2Rank,
		int newCard1Suit, int newCard1Rank, int newCard2Suit, int newCard2Rank, int bet )
	{
		if ( !IsLocalTable() ) return;
		var seat = BlackjackBridge.Seats[seatIndex];

		// Rebuild hands for display
		if ( seat.Hands.Count > 0 && seat.Hands[0].Count >= 2 )
		{
			var firstCard = seat.Hands[0][0];
			var secondCard = new CardData( card2Suit, card2Rank, false );

			seat.Hands.Clear();
			seat.Hands.Add( new List<CardData> { firstCard, new CardData( newCard1Suit, newCard1Rank, false ) } );
			seat.Hands.Add( new List<CardData> { secondCard, new CardData( newCard2Suit, newCard2Rank, false ) } );
		}

		// Add bet for second hand
		seat.HandBets.Add( bet );

		if ( BlackjackBridge.IsOpen )
			BlackjackBridge.ActiveTable?.PlayChipSound();
	}

	[Rpc.Broadcast]
	private void BroadcastResetRound()
	{
		if ( !IsLocalTable() ) return;
		BlackjackBridge.Reset();
	}
}
