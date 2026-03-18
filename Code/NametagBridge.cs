using Sandbox;
using System.Collections.Generic;
using System.Linq;

public static class NametagBridge
{
    private static Dictionary<GameObject, float> lastSpeakTime = new();
    private static float holdDuration = 0.5f; // Stay visible for 0.5s after voice stops

    public static bool IsSpeaking( GameObject root )
    {
        var voice = root.Components.Get<Voice>( FindMode.EverythingInSelfAndDescendants );
        if ( voice != null && voice.Amplitude > 0.02f )
        {
            lastSpeakTime[root] = Time.Now;
            return true;
        }

        // Check if we're within the hold duration
        if ( lastSpeakTime.TryGetValue( root, out float lastTime ) )
        {
            if ( Time.Now - lastTime < holdDuration )
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsTyping( GameObject root )
    {
        var player = root.Components.Get<PlayerController>();
        if ( player != null )
        {
            return player.IsTyping;
        }
        return false;
    }

    public static bool IsCrouching( GameObject root )
    {
        var sboxController = root.Components.Get<Sandbox.PlayerController>();
        if ( sboxController != null )
        {
            return sboxController.IsDucking;
        }
        return false;
    }
}