using Sandbox;
using System.Linq;

public class SlidingDoor : Component
{
	[Property] public string MapDoorName { get; set; } = "";
	[Property] public Vector3 OpenOffset { get; set; } = new Vector3( 0, 0, 150 );
	[Property] public float OpenSpeed { get; set; } = 2f;
	[Property] public float CloseDelay { get; set; } = 2f;
	[Property] public SoundEvent OpenSound { get; set; }
	[Property] public SoundEvent CloseSound { get; set; }

	[Sync] public bool IsOpen { get; set; } = false;

	private Vector3 closedPosition;
	private Vector3 openPosition;
	private bool initialized = false;
	private float closeTimer = 0f;
	private int playersInTrigger = 0;
	private bool lastKnownOpenState = false;

	protected override void OnStart()
	{
		TryInitialize();
		lastKnownOpenState = IsOpen;
	}

	private bool TryInitialize()
	{
		if ( initialized ) return true;

		// Only destroy map door if a name is specified
		if ( !string.IsNullOrEmpty( MapDoorName ) )
		{
			var allObjects = Scene.GetAllObjects( true );
			var mapDoor = allObjects.FirstOrDefault( obj => obj.Name == MapDoorName );

			if ( mapDoor == null )
			{
				Log.Warning( $"[SlidingDoor] Could not find map door: {MapDoorName}" );
				return false;
			}

			mapDoor.Destroy();
			//Log.Info( $"[SlidingDoor] Destroyed map door: {MapDoorName}" );
		}

		// Use this DoorController's own position as the closed position
		closedPosition = WorldPosition;
		openPosition = closedPosition + OpenOffset;

		initialized = true;
		//Log.Info( $"[SlidingDoor] {GameObject.Name} ready on {(Networking.IsHost ? "HOST" : "CLIENT")} at {closedPosition}" );
		return true;
	}

	protected override void OnUpdate()
	{
		if ( !TryInitialize() ) return;

		// Detect state change and play sound
		if ( IsOpen != lastKnownOpenState )
		{
			if ( IsOpen && OpenSound != null )
				Sound.Play( OpenSound, WorldPosition );
			else if ( !IsOpen && CloseSound != null )
				Sound.Play( CloseSound, WorldPosition );

			lastKnownOpenState = IsOpen;
		}

		// Lerp this GameObject toward target position
		Vector3 targetPos = IsOpen ? openPosition : closedPosition;
		float dist = Vector3.DistanceBetween( WorldPosition, targetPos );

		if ( dist > 0.5f )
		{
			WorldPosition = Vector3.Lerp( WorldPosition, targetPos, OpenSpeed * Time.Delta );
		}
		else if ( dist > 0f )
		{
			WorldPosition = targetPos;
		}

		// Auto-close timer (host only)
		if ( Networking.IsHost && IsOpen && playersInTrigger == 0 )
		{
			closeTimer += Time.Delta;
			if ( closeTimer >= CloseDelay )
				CloseDoor();
		}
	}

	public void OpenDoor()
	{
		if ( IsOpen )
		{
			closeTimer = 0f;
			return;
		}

		BroadcastSetOpen( true );
	}

	public void CloseDoor()
	{
		if ( !IsOpen ) return;

		BroadcastSetOpen( false );
	}

	[Rpc.Broadcast]
	private void BroadcastSetOpen( bool open )
	{
		//Log.Info( $"[SlidingDoor] BroadcastSetOpen on {(Networking.IsHost ? "HOST" : "CLIENT")} - {GameObject.Name} = {open}" );
		IsOpen = open;
		closeTimer = 0f;
	}

	public void PlayerEntered()
	{
		playersInTrigger++;
		OpenDoor();
	}

	public void PlayerExited()
	{
		playersInTrigger--;
		if ( playersInTrigger < 0 ) playersInTrigger = 0;
		closeTimer = 0f;
	}
}

public class DoorTrigger : Component, Component.ITriggerListener
{
	[Property] public GameObject DoorObject { get; set; }

	private SlidingDoor door;
	private int playersInside = 0;

	protected override void OnStart()
	{
		if ( DoorObject != null )
		{
			door = DoorObject.Components.Get<SlidingDoor>();
			if ( door == null )
				Log.Warning( $"DoorTrigger: No SlidingDoor component found on {DoorObject.Name}" );
		}
		else
		{
			Log.Warning( "DoorTrigger: No door object assigned!" );
		}
	}

	void ITriggerListener.OnTriggerEnter( Collider other )
	{
		var player = other.GameObject.Components.Get<PlayerController>();
		if ( player != null && player.IsAlive )
		{
			playersInside++;
			//Log.Info( $"[DoorTrigger] Player {player.PlayerName} entered" );
			door?.PlayerEntered();
		}
	}

	void ITriggerListener.OnTriggerExit( Collider other )
	{
		var player = other.GameObject.Components.Get<PlayerController>();
		if ( player != null )
		{
			playersInside--;
			if ( playersInside <= 0 )
				playersInside = 0;

			//Log.Info( $"[DoorTrigger] Player {player.PlayerName} exited" );
			door?.PlayerExited();
		}
	}
}