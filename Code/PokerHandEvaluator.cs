using System.Collections.Generic;
using System.Linq;

public enum PokerHandRank
{
	HighCard = 0,
	Pair = 1,
	TwoPair = 2,
	ThreeOfAKind = 3,
	Straight = 4,
	Flush = 5,
	FullHouse = 6,
	FourOfAKind = 7,
	StraightFlush = 8,
	RoyalFlush = 9
}

public struct PokerHandScore
{
	public PokerHandRank Rank;
	public long Score;            // packed rank + 5 tiebreaker ranks for direct comparison
	public CardData[] BestFive;   // the 5 cards that make the hand
	public string Label;          // human-readable description, e.g. "Full House, Kings over Sevens"
}

/// <summary>
/// Texas Hold'em hand evaluator. Evaluates the best 5-card hand from a 7-card pool
/// (2 hole cards + 5 community cards). Uses an enumerate-and-score approach over
/// C(7,5) = 21 combinations. Score is packed into a single long for fast comparison.
/// </summary>
public static class PokerHandEvaluator
{
	// CardData uses Rank 1=Ace, 2-10, 11=J, 12=Q, 13=K. Internally we treat Ace as 14
	// (high), but also handle the wheel A-2-3-4-5 as a 5-high straight.

	private static int Hi( int rank ) => rank == 1 ? 14 : rank;

	public static PokerHandScore Evaluate( IEnumerable<CardData> sevenCards )
	{
		var cards = sevenCards.ToArray();
		PokerHandScore best = default;
		best.Score = -1;

		var combo = new CardData[5];
		int n = cards.Length;

		// Enumerate all C(n,5) combinations (n is typically 7).
		for ( int a = 0; a < n - 4; a++ )
		for ( int b = a + 1; b < n - 3; b++ )
		for ( int c = b + 1; c < n - 2; c++ )
		for ( int d = c + 1; d < n - 1; d++ )
		for ( int e = d + 1; e < n; e++ )
		{
			combo[0] = cards[a]; combo[1] = cards[b]; combo[2] = cards[c]; combo[3] = cards[d]; combo[4] = cards[e];
			var score = ScoreFive( combo );
			if ( score.Score > best.Score )
			{
				best = score;
				best.BestFive = new CardData[5] { combo[0], combo[1], combo[2], combo[3], combo[4] };
			}
		}

		best.Label = DescribeHand( best.Rank, best.BestFive );
		return best;
	}

	private static PokerHandScore ScoreFive( CardData[] five )
	{
		// Sort high-rank descending (treating Ace as 14).
		var ranks = new int[5];
		for ( int i = 0; i < 5; i++ ) ranks[i] = Hi( five[i].Rank );
		System.Array.Sort( ranks );
		System.Array.Reverse( ranks );

		bool flush = five[0].Suit == five[1].Suit && five[1].Suit == five[2].Suit && five[2].Suit == five[3].Suit && five[3].Suit == five[4].Suit;

		// Straight detection
		int straightHigh = 0;
		bool isStraight = false;
		// Standard straight: 5 distinct consecutive ranks
		if ( ranks[0] - ranks[4] == 4 && ranks[0] != ranks[1] && ranks[1] != ranks[2] && ranks[2] != ranks[3] && ranks[3] != ranks[4] )
		{
			isStraight = true;
			straightHigh = ranks[0];
		}
		// Wheel: A-2-3-4-5 (Ace counted low)
		else if ( ranks[0] == 14 && ranks[1] == 5 && ranks[2] == 4 && ranks[3] == 3 && ranks[4] == 2 )
		{
			isStraight = true;
			straightHigh = 5;
		}

		// Count rank groups
		var counts = new Dictionary<int, int>();
		foreach ( var r in ranks )
			counts[r] = counts.GetValueOrDefault( r ) + 1;

		// Sort groups by (count desc, rank desc)
		var groups = counts.Select( kv => (Rank: kv.Key, Count: kv.Value) )
			.OrderByDescending( g => g.Count )
			.ThenByDescending( g => g.Rank )
			.ToArray();

		PokerHandRank rank;
		int k1 = 0, k2 = 0, k3 = 0, k4 = 0, k5 = 0;

		if ( isStraight && flush )
		{
			rank = (straightHigh == 14) ? PokerHandRank.RoyalFlush : PokerHandRank.StraightFlush;
			k1 = straightHigh;
		}
		else if ( groups[0].Count == 4 )
		{
			rank = PokerHandRank.FourOfAKind;
			k1 = groups[0].Rank;
			k2 = groups[1].Rank; // kicker
		}
		else if ( groups[0].Count == 3 && groups[1].Count == 2 )
		{
			rank = PokerHandRank.FullHouse;
			k1 = groups[0].Rank; // trips
			k2 = groups[1].Rank; // pair
		}
		else if ( flush )
		{
			rank = PokerHandRank.Flush;
			k1 = ranks[0]; k2 = ranks[1]; k3 = ranks[2]; k4 = ranks[3]; k5 = ranks[4];
		}
		else if ( isStraight )
		{
			rank = PokerHandRank.Straight;
			k1 = straightHigh;
		}
		else if ( groups[0].Count == 3 )
		{
			rank = PokerHandRank.ThreeOfAKind;
			k1 = groups[0].Rank;
			k2 = groups[1].Rank; // best kicker
			k3 = groups[2].Rank; // second kicker
		}
		else if ( groups[0].Count == 2 && groups[1].Count == 2 )
		{
			rank = PokerHandRank.TwoPair;
			k1 = groups[0].Rank; // higher pair
			k2 = groups[1].Rank; // lower pair
			k3 = groups[2].Rank; // kicker
		}
		else if ( groups[0].Count == 2 )
		{
			rank = PokerHandRank.Pair;
			k1 = groups[0].Rank;
			k2 = groups[1].Rank;
			k3 = groups[2].Rank;
			k4 = groups[3].Rank;
		}
		else
		{
			rank = PokerHandRank.HighCard;
			k1 = ranks[0]; k2 = ranks[1]; k3 = ranks[2]; k4 = ranks[3]; k5 = ranks[4];
		}

		long score = ((long)rank << 40)
			| ((long)k1 << 32)
			| ((long)k2 << 24)
			| ((long)k3 << 16)
			| ((long)k4 << 8)
			| (long)k5;

		return new PokerHandScore { Rank = rank, Score = score };
	}

	private static string DescribeHand( PokerHandRank rank, CardData[] five )
	{
		if ( five == null || five.Length == 0 ) return rank.ToString();

		string RankName( int hi, bool plural = false )
		{
			string n = hi switch
			{
				14 => "Ace", 13 => "King", 12 => "Queen", 11 => "Jack",
				10 => "Ten", 9 => "Nine", 8 => "Eight", 7 => "Seven",
				6 => "Six", 5 => "Five", 4 => "Four", 3 => "Three", 2 => "Two",
				_ => "?"
			};
			if ( plural )
			{
				if ( n == "Six" ) return "Sixes";
				return n + "s";
			}
			return n;
		}

		var counts = new Dictionary<int, int>();
		foreach ( var c in five )
		{
			int r = Hi( c.Rank );
			counts[r] = counts.GetValueOrDefault( r ) + 1;
		}
		var groups = counts.Select( kv => (Rank: kv.Key, Count: kv.Value) )
			.OrderByDescending( g => g.Count ).ThenByDescending( g => g.Rank ).ToArray();

		switch ( rank )
		{
			case PokerHandRank.RoyalFlush: return "Royal Flush";
			case PokerHandRank.StraightFlush: return $"Straight Flush, {RankName( StraightHighOf( five ) )} High";
			case PokerHandRank.FourOfAKind: return $"Four of a Kind, {RankName( groups[0].Rank, true )}";
			case PokerHandRank.FullHouse: return $"Full House, {RankName( groups[0].Rank, true )} over {RankName( groups[1].Rank, true )}";
			case PokerHandRank.Flush: return $"Flush, {RankName( MaxRank( five ) )} High";
			case PokerHandRank.Straight: return $"Straight, {RankName( MaxRank( five ) )} High";
			case PokerHandRank.ThreeOfAKind: return $"Three of a Kind, {RankName( groups[0].Rank, true )}";
			case PokerHandRank.TwoPair: return $"Two Pair, {RankName( groups[0].Rank, true )} and {RankName( groups[1].Rank, true )}";
			case PokerHandRank.Pair: return $"Pair of {RankName( groups[0].Rank, true )}";
			default: return $"High Card, {RankName( MaxRank( five ) )}";
		}
	}

	private static int StraightHighOf( CardData[] five )
	{
		// Detect wheel A-2-3-4-5 (returns 5) vs normal straight (returns max rank).
		var ranks = new int[5];
		for ( int i = 0; i < 5; i++ ) ranks[i] = Hi( five[i].Rank );
		System.Array.Sort( ranks );
		System.Array.Reverse( ranks );
		if ( ranks[0] == 14 && ranks[1] == 5 && ranks[2] == 4 && ranks[3] == 3 && ranks[4] == 2 )
			return 5;
		return ranks[0];
	}

	private static int MaxRank( CardData[] cards )
	{
		int max = 0;
		foreach ( var c in cards )
		{
			int h = Hi( c.Rank );
			if ( h > max ) max = h;
		}
		return max;
	}
}
