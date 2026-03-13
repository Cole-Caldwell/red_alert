using Sandbox;
using System.Linq;

/// <summary>
/// Place this component on a button GameObject in the scene.
/// When a player approaches and presses R, it triggers an emergency meeting.
/// Has a configurable cooldown that starts after voting ends and the game resumes.
/// </summary>
public sealed class EmergencyButton : Component
{
	[Property] public float InteractRange { get; set; } = 150f;
	[Property] public float Cooldown { get; set; } = 90f;
	[Property] public float GizmoRange { get; set; } = 300f;

	private bool isOnCooldown = false;
	private float cooldownEndTime = 0f;
	private bool usedThisRound = false;
	private bool wasVoting = false;

	protected override void OnUpdate()
	{
		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if ( gameManager == null ) return;

		// Track voting -> InGame transition to start cooldown
		if ( wasVoting && gameManager.CurrentState == GameManager.GameState.InGame && usedThisRound )
		{
			BroadcastStartCooldown( Time.Now + Cooldown );
			wasVoting = false;
		}

		if ( gameManager.CurrentState == GameManager.GameState.Voting )
		{
			wasVoting = true;
		}

		// Reset for new round
		if ( gameManager.CurrentState == GameManager.GameState.WaitingInLobby )
		{
			usedThisRound = false;
			wasVoting = false;
			isOnCooldown = false;
			cooldownEndTime = 0f;
		}

		// Check cooldown expiry
		if ( isOnCooldown && Time.Now >= cooldownEndTime )
		{
			isOnCooldown = false;
			Log.Info( "[EmergencyButton] Cooldown expired" );
		}

		// Only the local player interacts
		var localPlayer = Scene.GetAllComponents<PlayerController>()
			.FirstOrDefault( p => !p.IsProxy && p.IsAlive && p.GameObject.Network.Owner != null );

		if ( localPlayer == null ) return;

		if ( gameManager.CurrentState != GameManager.GameState.InGame ) return;

		float distance = Vector3.DistanceBetween( localPlayer.WorldPosition, WorldPosition );

		if ( distance <= GizmoRange )
		{
			DrawGizmo();
		}

		if ( distance <= InteractRange && Input.Pressed( "Reload" ) )
		{
			TryCallMeeting( localPlayer );
		}
	}

	private void DrawGizmo()
	{
		if ( isOnCooldown )
		{
			float remaining = cooldownEndTime - Time.Now;
			if ( remaining < 0 ) remaining = 0;

			Gizmo.Draw.Color = Color.Yellow;
			Gizmo.Draw.Text( $"Emergency Meeting Cooldown\n{remaining:F0} seconds", new Transform( WorldPosition + Vector3.Up * 30 ) );
		}
		else
		{
			Gizmo.Draw.Color = Color.Red;
			Gizmo.Draw.Text( "Press R\nForce Emergency Meeting", new Transform( WorldPosition + Vector3.Up * 30 ) );
		}
	}

	private void TryCallMeeting( PlayerController caller )
	{
		if ( isOnCooldown )
		{
			float remaining = cooldownEndTime - Time.Now;
			Log.Info( $"[EmergencyButton] Cooldown active! {remaining:F0} seconds remaining" );
			return;
		}

		Log.Info( $"[EmergencyButton] {caller.PlayerName} pressed the emergency button!" );

		BroadcastButtonPressed();

		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if ( gameManager != null )
		{
			gameManager.TriggerEmergencyMeeting( caller, null );
		}
	}

	[Rpc.Broadcast]
	private void BroadcastButtonPressed()
	{
		usedThisRound = true;
		wasVoting = false;
		Log.Info( "[EmergencyButton] Button pressed - all clients notified" );
	}

	[Rpc.Broadcast]
	private void BroadcastStartCooldown( float endTime )
	{
		isOnCooldown = true;
		cooldownEndTime = endTime;
		Log.Info( $"[EmergencyButton] Cooldown started on {(Networking.IsHost ? "HOST" : "CLIENT")} - ends at {endTime:F0}" );
	}

	public void ResetCooldown()
	{
		isOnCooldown = false;
		cooldownEndTime = 0f;
	}
}