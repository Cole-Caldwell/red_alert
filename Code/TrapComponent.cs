using Sandbox;
using System.Linq;

public class TrapComponent : Component
{
	[Property] public float TriggerRadius { get; set; } = 120f;

	public string TrapId { get; set; } = "";
	public ulong OwnerSteamId { get; set; } = 0;

	private bool triggered = false;

	protected override void OnUpdate()
	{
		if ( triggered ) return;
		if ( !Networking.IsHost ) return;

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

		var owner = Scene.GetAllComponents<PlayerController>()
			.FirstOrDefault( p => p.GameObject.Network.Owner != null && p.GameObject.Network.Owner.SteamId == OwnerSteamId );

		if ( owner == null ) return;

		triggered = true;
		owner.KillPlayer( victim, true );
		owner.DestroyTrapRpc( TrapId );

		Log.Info( $"[Trapper] Trap {TrapId} killed {victim.PlayerName}" );
	}
}
