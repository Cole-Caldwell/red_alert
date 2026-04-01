using Sandbox;
using System;

public static class DailyBonusTracker
{
	private const string FileName = "daily_bonus.txt";

	public static bool HasClaimedToday()
	{
		try
		{
			if ( !FileSystem.Data.FileExists( FileName ) )
				return false;

			var lastDate = FileSystem.Data.ReadAllText( FileName ).Trim();
			var today = DateTime.UtcNow.ToString( "yyyy-MM-dd" );
			return lastDate == today;
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
			var today = DateTime.UtcNow.ToString( "yyyy-MM-dd" );
			FileSystem.Data.WriteAllText( FileName, today );
			Log.Info( $"[DailyBonus] Marked claimed for {today}" );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[DailyBonus] Failed to save claim: {e.Message}" );
		}
	}
}
