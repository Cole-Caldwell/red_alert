using Sandbox;
using System;

public sealed class MenuCameraRotator : Component
{
	[Property] public float YawSpeed { get; set; } = 3f;
	[Property] public float PitchAmplitude { get; set; } = 8f;
	[Property] public float PitchSpeed { get; set; } = 0.15f;
	[Property] public int NearStarCount { get; set; } = 40;
	[Property] public int FarStarCount { get; set; } = 80;

	private float yawAccumulator = 0f;
	private float timeAccumulator = 0f;
	private Rotation baseRotation;

	protected override void OnStart()
	{
		baseRotation = WorldRotation;
		SpawnStarField();
	}

	protected override void OnUpdate()
	{
		timeAccumulator += Time.Delta;
		yawAccumulator += YawSpeed * Time.Delta;

		float pitch = MathF.Sin( timeAccumulator * PitchSpeed ) * PitchAmplitude;
		float roll = MathF.Sin( timeAccumulator * PitchSpeed * 0.7f ) * 2f;

		WorldRotation = baseRotation * Rotation.FromYaw( yawAccumulator ) * Rotation.FromPitch( pitch ) * Rotation.FromRoll( roll );
	}

	private void SpawnStarField()
	{
		var rng = new Random( 42 );

		for ( int i = 0; i < NearStarCount; i++ )
		{
			float dist = rng.Float( 800f, 2500f );
			float scale = rng.Float( 0.05f, 0.2f );
			float brightness = rng.Float( 8f, 25f );
			SpawnStar( rng, dist, scale, brightness );
		}

		for ( int i = 0; i < FarStarCount; i++ )
		{
			float dist = rng.Float( 3000f, 8000f );
			float scale = rng.Float( 0.15f, 0.5f );
			float brightness = rng.Float( 5f, 15f );
			SpawnStar( rng, dist, scale, brightness );
		}
	}

	private void SpawnStar( Random rng, float distance, float scale, float brightness )
	{
		var dir = new Vector3(
			rng.Float( -1f, 1f ),
			rng.Float( -1f, 1f ),
			rng.Float( -1f, 1f )
		).Normal;

		var pos = WorldPosition + dir * distance;

		var go = new GameObject( true, "Star" );
		go.WorldPosition = pos;
		go.WorldScale = scale;

		var model = go.AddComponent<ModelRenderer>();
		model.Model = Model.Load( "models/dev/sphere.vmdl" );
		model.Tint = StarColor( rng ) * brightness;
		model.RenderType = ModelRenderer.ShadowRenderType.Off;
	}

	private Color StarColor( Random rng )
	{
		float warmth = rng.Float( 0f, 1f );

		if ( warmth < 0.7f )
			return new Color( 1f, 1f, 1f );
		else if ( warmth < 0.85f )
			return new Color( 0.9f, 0.95f, 1f );
		else
			return new Color( 1f, 0.97f, 0.9f );
	}
}
