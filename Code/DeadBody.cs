using Sandbox;
using System.Linq;

public partial class DeadBody : Component
{
	[Property, Sync] public string VictimName { get; set; } = "Unknown";
	[Property, Sync] public PlayerController.PlayerRole VictimRole { get; set; }
	[Property] public float ReportRange { get; set; } = 150f;

	private bool hasBeenReported = false;

	protected override void OnStart()
	{
		//Log.Info( $"DeadBody component added to {VictimName}'s ragdoll" );
		// NO ragdoll creation - the player IS the ragdoll now!
	}

	protected override void OnUpdate()
	{
		if ( hasBeenReported ) return;

		// Find local player
		var localPlayer = Scene.GetAllComponents<PlayerController>()
			.FirstOrDefault( p => !p.IsProxy && p.IsAlive );

		if ( localPlayer == null ) return;

		float distance = Vector3.DistanceBetween( WorldPosition, localPlayer.WorldPosition );

		// Only show gizmo and allow report when within range
		if ( distance > ReportRange ) return;

		Gizmo.Draw.Color = Color.Red;
		Gizmo.Draw.Text( $"Press R to Report {VictimName}", new Transform( WorldPosition + Vector3.Up * 80 ), "Poppins", 20 );

		if ( Input.Pressed( "Reload" ) )
		{
			ReportBody( localPlayer );
		}
	}

	[Rpc.Broadcast]
	public void ReportBody( PlayerController reporter )
	{
		if ( hasBeenReported )
			return;

		hasBeenReported = true;
		Log.Info( $"{reporter.PlayerName} reported {VictimName}'s body!" );

		// Get the game manager and trigger emergency meeting
		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if ( gameManager != null )
		{
			gameManager.TriggerEmergencyMeeting( reporter, this );
		}
	}
}