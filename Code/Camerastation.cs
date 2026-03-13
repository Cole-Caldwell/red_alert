using Sandbox;
using System.Collections.Generic;
using System.Linq;

public sealed class CameraStation : Component, Component.ITriggerListener
{
	[Property] public List<string> CameraIds { get; set; } = new();
	[Property] public float InteractRange { get; set; } = 150f;
	[Property] public SoundEvent MountSound { get; set; }
	[Property] public SoundEvent CycleCameraSound { get; set; }

	[Sync] public bool IsOccupied { get; set; } = false;
	[Sync] public string OccupantName { get; set; } = "";

	private List<SecurityCamera> controlledCameras = new();
	private PlayerController mountedPlayer = null;
	private int currentCameraIndex = 0;

	protected override void OnStart()
	{
		var allCameras = Scene.GetAllComponents<SecurityCamera>().ToList();

		if ( CameraIds.Count == 0 )
			controlledCameras = allCameras;
		else
			controlledCameras = allCameras.Where( c => CameraIds.Contains( c.CameraId ) ).ToList();

		controlledCameras = controlledCameras.OrderBy( c => c.CameraId ).ToList();

		//Log.Info( $"[CameraStation] Managing {controlledCameras.Count} cameras" );
	}

	public bool TryInteract( PlayerController player )
	{
		// If this player is already mounted, unmount
		if ( mountedPlayer == player )
		{
			Unmount();
			return true;
		}

		// If someone else is using it, deny
		if ( IsOccupied )
		{
			//Log.Info( $"[CameraStation] {player.PlayerName} tried to use station but {OccupantName} is using it" );
			return true;
		}

		Mount( player );
		return true;
	}

	public void NextCamera()
	{
		if ( mountedPlayer == null || controlledCameras.Count == 0 ) return;

		// Deactivate current
		controlledCameras[currentCameraIndex].Deactivate();
		var monitors = Scene.GetAllComponents<CameraMonitor>().ToList();
		var currentMonitor = monitors.FirstOrDefault( m => m.CameraId == controlledCameras[currentCameraIndex].CameraId );
		currentMonitor?.HidePanel();

		// Advance
		currentCameraIndex = ( currentCameraIndex + 1 ) % controlledCameras.Count;

		// Activate new
		controlledCameras[currentCameraIndex].Activate();
		var newMonitor = monitors.FirstOrDefault( m => m.CameraId == controlledCameras[currentCameraIndex].CameraId );
		newMonitor?.ShowPanel();

		//Log.Info( $"[CameraStation] Switched to {controlledCameras[currentCameraIndex].DisplayName} ({currentCameraIndex + 1}/{controlledCameras.Count})" );

		if ( CycleCameraSound != null )
			Sound.Play( CycleCameraSound, WorldPosition );
	}

	private void Mount( PlayerController player )
	{
		mountedPlayer = player;
		currentCameraIndex = 0;

		// Get actual display name the same way nametag does
		string displayName = player.GameObject.Root.Name.Replace( "Player - ", "" );
		BroadcastOccupied( true, displayName );

		player.MountToStation( this );

		if ( MountSound != null )
			Sound.Play( MountSound, WorldPosition );

		if ( controlledCameras.Count > 0 )
		{
			controlledCameras[0].Activate();
			var monitor = Scene.GetAllComponents<CameraMonitor>()
				.FirstOrDefault( m => m.CameraId == controlledCameras[0].CameraId );
			monitor?.ShowPanel();
		}

		//Log.Info( $"[CameraStation] {player.PlayerName} mounted - viewing {controlledCameras[0]?.DisplayName} (1/{controlledCameras.Count})" );
	}

	public void Unmount()
	{
		if ( mountedPlayer == null ) return;

		var playerName = mountedPlayer.PlayerName;

		if ( controlledCameras.Count > 0 && currentCameraIndex < controlledCameras.Count )
		{
			controlledCameras[currentCameraIndex].Deactivate();
			var monitor = Scene.GetAllComponents<CameraMonitor>()
				.FirstOrDefault( m => m.CameraId == controlledCameras[currentCameraIndex].CameraId );
			monitor?.HidePanel();
		}

		mountedPlayer.UnmountFromStation();
		mountedPlayer = null;
		currentCameraIndex = 0;

		// Sync unoccupied state
		BroadcastOccupied( false, "" );

		//Log.Info( $"[CameraStation] {playerName} unmounted" );
	}

	[Rpc.Broadcast]
	private void BroadcastOccupied( bool occupied, string playerName )
	{
		IsOccupied = occupied;
		OccupantName = playerName;
	}

	// Safety net: if player leaves the trigger area, auto-unmount
	void ITriggerListener.OnTriggerExit( Collider other )
	{
		var player = other.GameObject.Components.Get<PlayerController>();
		if ( player == null ) return;

		// Only unmount if this player is the one mounted
		if ( mountedPlayer == player )
		{
			//Log.Info( $"[CameraStation] {player.PlayerName} left trigger area - auto unmounting" );
			Unmount();
		}
	}

	void ITriggerListener.OnTriggerEnter( Collider other )
	{
		// Not needed but required by interface
	}

	protected override void OnUpdate()
	{
		// Only show gizmo when a player is nearby
		var localPlayer = Scene.GetAllComponents<PlayerController>()
			.FirstOrDefault( p => !p.IsProxy && p.GameObject.Network.Owner != null );

		if ( localPlayer == null ) return;

		float distance = Vector3.DistanceBetween( localPlayer.WorldPosition, WorldPosition );
		float gizmoRange = 300f; // Adjust this value as needed

		if ( distance > gizmoRange ) return;

		if ( IsOccupied )
		{
			Gizmo.Draw.Color = Color.Red;
			Gizmo.Draw.Text( $"IN USE - {OccupantName}\nClick to change camera", new Transform( WorldPosition + Vector3.Up * 50 ) );
		}
		else
		{
			Gizmo.Draw.Color = Color.Gray;
			Gizmo.Draw.Text( "SECURITY CAMERAS\nPress E to view", new Transform( WorldPosition + Vector3.Up * 50 ) );
		}
	}
}