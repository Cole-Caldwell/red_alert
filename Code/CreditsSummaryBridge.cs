using Sandbox;
using System.Linq;

public static class CreditsSummaryBridge
{
    public static SoundEvent CreditSound { get; set; }

    public static void PlaySound( Scene scene )
    {
        if ( CreditSound != null )
        {
            var handle = Sound.Play( CreditSound );
            if ( handle != null )
            {
                handle.ListenLocal = true;
                handle.Volume = 1.0f;
            }
        }
    }
}

