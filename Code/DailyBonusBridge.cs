using Sandbox;

public static class DailyBonusBridge
{
	public static SoundEvent SpinSound { get; set; }
	public static SoundEvent RewardSound { get; set; }

	public static void PlaySpinSound()
	{
		if ( SpinSound != null )
		{
			var handle = Sound.Play( SpinSound );
			if ( handle != null )
			{
				handle.ListenLocal = true;
				handle.Volume = 1.0f;
			}
		}
	}

	public static void PlayRewardSound()
	{
		if ( RewardSound != null )
		{
			var handle = Sound.Play( RewardSound );
			if ( handle != null )
			{
				handle.ListenLocal = true;
				handle.Volume = 1.0f;
			}
		}
	}
}
