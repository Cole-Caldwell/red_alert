using Sandbox;
using Sandbox.Citizen;
using System.Collections.Generic;

/// <summary>
/// Handles player clothing/cosmetics via the Dresser component.
/// Ensures all players see each other's Steam cosmetics.
///
/// SETUP on Player prefab:
///   1. Add a "Dresser" component to the player root
///   2. Add this "PlayerClothing" component to the player root
///   3. Drag the Body's SkinnedModelRenderer into the BodyRenderer property
///   4. Drag the Dresser component into the Dresser property
/// </summary>
public sealed class PlayerClothing : Component
{
	[Property] public Dresser Dresser { get; set; }
	[Property] public SkinnedModelRenderer BodyRenderer { get; set; }

	[Sync] public bool IsDressed { get; set; }

	private List<SkinnedModelRenderer> _clothingRenderers = new();
	private bool _calledOnDressed;

	protected override void OnStart()
	{
		// Auto-find references if not set in inspector
		if ( Dresser is null )
			Dresser = Components.GetInDescendantsOrSelf<Dresser>();

		if ( BodyRenderer is null )
			BodyRenderer = Components.GetInDescendantsOrSelf<SkinnedModelRenderer>();

		if ( Dresser is null )
		{
			Log.Warning( "PlayerClothing: No Dresser component found!" );
			return;
		}

		// Apply clothes on spawn
		ApplyClothes();
	}

	protected override void OnUpdate()
	{
		UpdateClothes();
	}

	/// <summary>
	/// Async clothing application - awaits Dresser.Apply() then signals completion.
	/// </summary>
	private async void ApplyClothes()
	{
		if ( Dresser is null ) return;

		Log.Info( $"PlayerClothing: Applying clothes for {GameObject.Name}" );

		await Dresser.Apply();

		IsDressed = true;
		Log.Info( $"PlayerClothing: Clothes applied for {GameObject.Name}" );

		// Broadcast to all clients so they rebuild their clothing renderer cache
		BroadcastClothingApplied();
	}

	/// <summary>
	/// RPC broadcast so all clients know to scan for clothing renderers.
	/// </summary>
	[Rpc.Broadcast]
	private void BroadcastClothingApplied()
	{
		Log.Info( $"PlayerClothing: Received clothing broadcast for {GameObject.Name}" );
		UpdateClothingRenderers();
	}

	/// <summary>
	/// Scans Body's children for clothing GameObjects created by the Dresser
	/// and caches their SkinnedModelRenderers.
	/// </summary>
	private void UpdateClothingRenderers()
	{
		if ( BodyRenderer is null ) return;

		_clothingRenderers.Clear();

		foreach ( var child in BodyRenderer.GameObject.Children )
		{
			if ( !child.IsValid() ) continue;
			if ( !child.Name.StartsWith( "Clothing" ) ) continue;

			var renderer = child.Components.Get<SkinnedModelRenderer>();
			if ( renderer is not null )
			{
				_clothingRenderers.Add( renderer );
			}
		}

		if ( !_calledOnDressed && _clothingRenderers.Count > 0 )
		{
			_calledOnDressed = true;
			Log.Info( $"PlayerClothing: Found {_clothingRenderers.Count} clothing items on {GameObject.Name}" );
		}
	}

	/// <summary>
	/// Runs every frame to ensure clothing renderers match body visibility.
	/// Also handles late-join by re-scanning if needed.
	/// </summary>
	private void UpdateClothes()
	{
		if ( !IsDressed ) return;
		if ( Dresser is not null && Dresser.IsDressing ) return;
		if ( BodyRenderer is null ) return;

		// If we haven't found clothing renderers yet, keep scanning
		// This handles late-join where the clothing spawns after a delay
		if ( _clothingRenderers.Count == 0 )
		{
			UpdateClothingRenderers();
		}

		// Sync clothing renderer visibility with body renderer
		foreach ( var renderer in _clothingRenderers )
		{
			if ( renderer is null || !renderer.IsValid() ) continue;
			renderer.RenderType = BodyRenderer.RenderType;
			renderer.Tint = BodyRenderer.Tint;
		}
	}
}
