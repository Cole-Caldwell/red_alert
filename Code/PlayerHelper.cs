using Sandbox;
using System.Linq;

public static class PlayerHelper
{
	public static ulong GetNetworkOwnerId( PlayerController player )
	{
		if ( player?.GameObject?.Network?.Owner != null )
		{
			return player.GameObject.Network.Owner.SteamId;
		}
		return 0;
	}
	
	public static PlayerController GetLocalPlayer()
	{
		return Game.ActiveScene.GetAllComponents<PlayerController>()
			.FirstOrDefault( p => !p.IsProxy );
	}
}