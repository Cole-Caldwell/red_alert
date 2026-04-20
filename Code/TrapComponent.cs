using Sandbox;
using System.Linq;

public class TrapComponent : Component
{
	[Property] public float TriggerRadius { get; set; } = 120f;
	[Property] public SoundEvent WarningSound { get; set; }
	[Property] public float WarningDuration { get; set; } = 0.8f;

	public string TrapId { get; set; } = "";
	public ulong OwnerSteamId { get; set; } = 0;

	private bool triggered = false;
	private PlayerController armedVictim = null;
	private TimeSince timeSinceArmed = 0f;

	protected override void OnUpdate()
	{
		if ( triggered ) return;
		if ( !Networking.IsHost ) return;

		if ( armedVictim != null )
		{
			bool stillValid = armedVictim.IsValid && armedVictim.IsAlive && armedVictim.IsInGame
				&& Vector3.DistanceBetween( WorldPosition, armedVictim.WorldPosition ) <= TriggerRadius;

			if ( !stillValid )
			{
				armedVictim = null;
				return;
			}

			if ( timeSinceArmed >= WarningDuration )
			{
				var owner = Scene.GetAllComponents<PlayerController>()
					.FirstOrDefault( p => p.GameObject.Network.Owner != null && p.GameObject.Network.Owner.SteamId == OwnerSteamId );

				if ( owner == null ) return;

				triggered = true;
				owner.KillPlayer( armedVictim, true );
				owner.DestroyTrapRpc( TrapId );

				Log.Info( $"[Trapper] Trap {TrapId} killed {armedVictim.PlayerName}" );
			}
			return;
		}

		PlayerController victim = null;
		foreach ( var p in Scene.GetAllComponents<PlayerController>() )
		{
			if ( p == null || !p.IsAlive || !p.IsInGame ) continue;
			if ( p.Role != PlayerController.PlayerRole.Citizen ) continue;
			if ( Vector3.DistanceBetween( WorldPosition, p.WorldPosition ) > TriggerRadius ) continue;
			victim = p;
			break;
		}

		if ( victim == null ) return;

		armedVictim = victim;
		timeSinceArmed = 0f;

		var ownerForSound = Scene.GetAllComponents<PlayerController>()
			.FirstOrDefault( p => p.GameObject.Network.Owner != null && p.GameObject.Network.Owner.SteamId == OwnerSteamId );
		ownerForSound?.PlayTrapWarningRpc( TrapId );
	}
}
