using Sandbox;
using System.Collections.Generic;
using System.Linq;

public sealed class ChatSystem : Component
{
    public static ChatSystem Instance { get; private set; }

    public bool ChatEnabled { get; set; } = true;
    public List<ChatMessage> Messages { get; set; } = new();

    [Property] public int MaxMessages { get; set; } = 30;
    [Property] public float MessageLifetime { get; set; } = 10f;
    [Property] public bool TTSEnabled { get; set; } = true;
    [Property] public int MaxMessageLength { get; set; } = 100;

    protected override void OnStart()
    {
        Instance = this;
        Log.Info( "[ChatTTS] Chat system initialized" );
    }

    protected override void OnUpdate()
    {
        Messages.RemoveAll( m => (Time.Now - m.Time) > MessageLifetime );
    }

    public void SendMessage( string text )
    {
        if ( string.IsNullOrWhiteSpace( text ) ) return;

        // Enforce character limit
        if ( text.Length > MaxMessageLength )
        {
            text = text.Substring( 0, MaxMessageLength );
        }

        var name = Connection.Local?.DisplayName ?? "Player";
        BroadcastMessage( name, text );
    }

    [Rpc.Broadcast]
    private void BroadcastMessage( string playerName, string text )
    {
        Messages.Add( new ChatMessage
        {
            PlayerName = playerName,
            Text = text,
            Time = Time.Now
        } );

        while ( Messages.Count > MaxMessages )
            Messages.RemoveAt( 0 );

        // Play TTS on all clients
        if ( TTSEnabled )
        {
            try
            {
                var synth = new Sandbox.Speech.Synthesizer();
                synth.WithText( text );
                synth.WithRate( 1 );
                var handle = synth.Play();
                handle.Volume = 25f;
            }
            catch ( System.Exception e )
            {
                Log.Warning( $"[ChatTTS] TTS failed: {e.Message}" );
            }
        }
    }
}

public class ChatMessage
{
    public string PlayerName { get; set; }
    public string Text { get; set; }
    public float Time { get; set; }
}