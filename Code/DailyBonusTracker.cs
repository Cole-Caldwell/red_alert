using Sandbox;
using System;
using System.Threading.Tasks;

public static class DailyBonusTracker
{
	private const string StatName = "daily_bonus_date";

	private static int TodayAsInt()
	{
		var now = DateTime.UtcNow;
		return now.Year * 10000 + now.Month * 100 + now.Day;
	}

	public static async Task<bool> HasClaimedTodayAsync()
	{
		try
		{
			var board = Sandbox.Services.Leaderboards.GetFromStat( Game.Ident, StatName );
			board.SetAggregationMax();
			board.CenterOnMe();
			board.MaxEntries = 1;
			await board.Refresh();

			long mySteamId = (long)Game.SteamId;
			int lastClaim = 0;
			foreach ( var entry in board.Entries )
			{
				if ( entry.SteamId == mySteamId )
				{
					lastClaim = (int)entry.Value;
					break;
				}
			}

			int today = TodayAsInt();
			Log.Info( $"[DailyBonus] HasClaimedToday check: stored={lastClaim} today={today}" );
			return lastClaim >= today;
		}
		catch ( Exception e )
		{
			Log.Warning( $"[DailyBonus] Failed to check claim status: {e.Message}" );
			return false;
		}
	}

	public static void MarkClaimed()
	{
		try
		{
			int today = TodayAsInt();
			// The stat uses Max aggregation on the backend; today's date int is
			// always larger than any previous claim, so this surfaces today's date
			// as the highest value for this player.
			Sandbox.Services.Stats.Increment( StatName, today );
			Log.Info( $"[DailyBonus] Increment({StatName}, {today}) called" );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[DailyBonus] Failed to save claim: {e.Message}" );
		}
	}
}
