using System;
using System.Collections.Generic;
using Sandbox;

[Title( "JukeBox" )]
[Category( "Audio" )]
public sealed class JukeBoxComponent : Component
{
	#region Playlist

	[Property, Group( "Playlist" ), Title( "Songs" )]
	public List<string> Songs { get; set; } = new List<string>();

	#endregion

	#region Options

	[Property, Group( "Options" ), Title( "Shuffle" )]
	public bool Shuffle { get; set; } = false;

	[Property, Group( "Options" ), Title( "Loop Playlist" )]
	public bool LoopPlaylist { get; set; } = true;

	[Property, Group( "Options" ), Title( "Volume" )]
	[Range( 0f, 1f )]
	public float Volume { get; set; } = 0.8f;

	[Property, Group( "Options" ), Title( "Play On Start" )]
	public bool PlayOnStart { get; set; } = true;

	[Property, Group( "Options" ), Title( "Mute" )]
	public bool Mute { get; set; } = false;

	#endregion

	#region Internal state

	SoundHandle _currentHandle;
	List<int> _playOrder = new List<int>();
	int _playIndex = -1;
	bool _playing;

	#endregion

	/// <summary>Start playback from the first track (or random if Shuffle). Does nothing if Mute or no songs.</summary>
	public void Play()
	{
		if ( Mute || Songs == null || Songs.Count == 0 ) return;
		if ( GameObject == null || !GameObject.IsValid ) return;
		Stop();
		BuildPlayOrder();
		_playIndex = 0;
		PlayCurrent();
		_playing = true;
	}

	/// <summary>Stop playback and clear the current track.</summary>
	public void Stop()
	{
		try
		{
			if ( _currentHandle.IsPlaying )
				_currentHandle.Stop();
		}
		catch { /* handle may be invalid during destroy or scene unload */ }
		_currentHandle = default;
		_playIndex = -1;
		_playing = false;
	}

	/// <summary>Skip to the next track. If at end and Loop Playlist, wraps to start.</summary>
	public void SkipTrack()
	{
		if ( Songs == null || Songs.Count == 0 ) return;
		try
		{
			if ( _currentHandle.IsPlaying )
				_currentHandle.Stop();
		}
		catch { }
		_currentHandle = default;
		AdvanceIndex();
		if ( _playIndex >= 0 )
			PlayCurrent();
		else
			_playing = false;
	}

	void BuildPlayOrder()
	{
		_playOrder.Clear();
		if ( Songs == null ) return;
		int n = Songs.Count;
		for ( int i = 0; i < n; i++ )
			_playOrder.Add( i );
		if ( Shuffle && n > 1 && Game.Random != null )
		{
			for ( int i = n - 1; i > 0; i-- )
			{
				int j = Game.Random.Int( 0, i );
				(_playOrder[i], _playOrder[j]) = (_playOrder[j], _playOrder[i]);
			}
		}
	}

	void AdvanceIndex()
	{
		if ( _playOrder.Count == 0 ) { _playIndex = -1; return; }
		_playIndex++;
		if ( _playIndex >= _playOrder.Count )
		{
			if ( LoopPlaylist )
			{
				if ( Shuffle )
					BuildPlayOrder();
				_playIndex = 0;
			}
			else
				_playIndex = -1;
		}
	}

	void PlayCurrent()
	{
		if ( Mute || Songs == null || Songs.Count == 0 ) return;
		int maxAttempts = _playOrder.Count > 0 ? _playOrder.Count : 1;
		for ( int attempt = 0; attempt < maxAttempts; attempt++ )
		{
			if ( _playIndex < 0 || _playIndex >= _playOrder.Count ) return;
			int songIndex = _playOrder[_playIndex];
			if ( songIndex < 0 || songIndex >= Songs.Count ) { AdvanceIndex(); continue; }
			string path = Songs[songIndex];
			if ( string.IsNullOrWhiteSpace( path ) ) { AdvanceIndex(); continue; }
			try
			{
				var ev = new SoundEvent( path );
				_currentHandle = GameObject.PlaySound( ev, Vector3.Zero );
				if ( _currentHandle.IsPlaying )
					_currentHandle.Volume = Mute ? 0f : Volume;
				return;
			}
			catch
			{
				AdvanceIndex();
			}
		}
	}

	protected override void OnStart()
	{
		try
		{
			if ( PlayOnStart && GameObject != null && GameObject.IsValid )
				Play();
		}
		catch ( Exception ex )
		{
			Log.Warning( $"JukeBox OnStart: {ex.Message}" );
		}
	}

	protected override void OnUpdate()
	{
		if ( !_playing || Mute ) return;
		if ( Songs == null || Songs.Count == 0 ) return;
		try
		{
			if ( _currentHandle.IsPlaying ) return;
		}
		catch { _currentHandle = default; }
		_currentHandle = default;
		AdvanceIndex();
		if ( _playIndex >= 0 )
			PlayCurrent();
		else
			_playing = false;
	}

	protected override void OnDestroy()
	{
		try
		{
			if ( _currentHandle.IsPlaying )
				_currentHandle.Stop();
		}
		catch { /* handle invalid during destroy */ }
		_currentHandle = default;
		_playing = false;
	}
}
