using Sandbox;
using System.Collections.Generic;
using System.Linq;

public static class SilenceBridge
{
	public class TargetData
	{
		public string Name { get; set; } = "";
		public ulong SteamId { get; set; } = 0;
	}

	public static bool ShouldShowUI( Scene scene )
	{
		if ( !PerkBridge.SilenceUIOpen ) return false;
		if ( PerkBridge.EquippedPerkId != "silence" ) return false;
		if ( PerkBridge.PerkUsedThisRound ) return false;

		var gm = scene.GetAllComponents<GameManager>().FirstOrDefault();
		if ( gm == null || gm.CurrentState != GameManager.GameState.Voting ) return false;

		var local = scene.GetAllComponents<PlayerController>()
			.FirstOrDefault( p => !p.IsProxy && p.GameObject.Network.Owner != null );
		if ( local == null ) return false;
		if ( local.Role != PlayerController.PlayerRole.Anomaly ) return false;
		if ( !local.IsAlive || !local.IsInGame ) return false;
		return true;
	}

	public static List<TargetData> GetCitizenTargets( Scene scene )
	{
		var list = new List<TargetData>();
		var players = scene.GetAllComponents<PlayerController>()
			.Where( p => p.GameObject.Network.Owner != null
				&& p.Role == PlayerController.PlayerRole.Citizen
				&& p.IsAlive && p.IsInGame && !p.IsSpectating )
			.ToList();

		foreach ( var p in players )
		{
			list.Add( new TargetData
			{
				Name = GetDisplayName( p ),
				SteamId = p.GameObject.Network.Owner.SteamId
			} );
		}
		return list;
	}

	private static string GetDisplayName( PlayerController player )
	{
		string name = player.GameObject.Root.Name.Replace( "Player - ", "" );
		if ( string.IsNullOrEmpty( name ) || name == player.GameObject.Root.Name )
			name = player.PlayerName;
		return name;
	}

	public static void CommitSilence( Scene scene, ulong targetSteamId )
	{
		var local = scene.GetAllComponents<PlayerController>()
			.FirstOrDefault( p => !p.IsProxy && p.GameObject.Network.Owner != null );
		if ( local == null ) return;

		local.CommitSilenceOnTarget( targetSteamId );
		PerkBridge.SilenceUIOpen = false;
	}
}
