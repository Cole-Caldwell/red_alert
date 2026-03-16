using Sandbox;
using System.Linq;

public partial class CountdownUI : Component
{
	private float initialDisplayTimer = 0f;
	private bool showingInitialMessage = false;
	private float initialDuration = 3f;

	private bool showingCountdown = false;
	private int lastDisplayedSecond = -1;
	private float countdownFadeTimer = 0f;

	private bool isDestroying = false;

	protected override void OnUpdate()
	{
		if ( isDestroying ) return;

		// Check if countdown was cancelled (terminal reset)
		var terminal = Scene.GetAllComponents<ReadyTerminal>().FirstOrDefault();
		if ( terminal == null )
		{
			GameObject.Destroy();
			return;
		}

		if ( showingInitialMessage )
		{
			DisplayInitialMessage();
			initialDisplayTimer -= Time.Delta;
			if ( initialDisplayTimer <= 0f )
			{
				showingInitialMessage = false;
			}
		}

		// Get countdown timer from terminal
		float remaining = GetCountdownRemaining();

		if ( remaining <= 0f && !showingInitialMessage )
		{
			// Game started, destroy
			isDestroying = true;
			GameObject.Destroy();
			return;
		}

		// Show second-by-second countdown in last 10 seconds
		if ( remaining <= 10f && remaining > 0f )
		{
			int currentSecond = (int)System.Math.Ceiling( remaining );

			if ( currentSecond != lastDisplayedSecond )
			{
				lastDisplayedSecond = currentSecond;
				countdownFadeTimer = 0.9f;
			}

			if ( countdownFadeTimer > 0f )
			{
				showingCountdown = true;
				DisplayCountdownNumber( currentSecond );
				countdownFadeTimer -= Time.Delta;
			}
		}
	}

	private float GetCountdownRemaining()
	{
		var terminal = Scene.GetAllComponents<ReadyTerminal>().FirstOrDefault();
		if ( terminal == null ) return 0f;

		return CountdownBridge.TimeRemaining;
	}

	private void DisplayInitialMessage()
	{
		var camera = Scene.GetAllComponents<CameraComponent>().FirstOrDefault();
		if ( camera == null ) return;

		Vector3 centerPos = camera.WorldPosition + camera.WorldRotation.Forward * 400f;

		float fadeProgress = initialDisplayTimer / initialDuration;
		float alpha = 1f;

		if ( fadeProgress < 0.3f )
		{
			alpha = fadeProgress / 0.3f;
		}

		// Main text
		Gizmo.Draw.Color = Color.Green.WithAlpha( alpha );
		Gizmo.Draw.Text(
			"COUNTDOWN INITIATED",
			new Transform( centerPos ),
			"Consolas",
			80
		);

		// Subtitle
		Gizmo.Draw.Color = Color.Green.WithAlpha( alpha * 0.6f );
		Gizmo.Draw.Text(
			"Game Starting Soon",
			new Transform( centerPos + Vector3.Down * 55 ),
			"Consolas",
			36
		);
	}

	private void DisplayCountdownNumber( int seconds )
	{
		var camera = Scene.GetAllComponents<CameraComponent>().FirstOrDefault();
		if ( camera == null ) return;

		Vector3 centerPos = camera.WorldPosition + camera.WorldRotation.Forward * 400f;

		// Scale and fade effect - number starts big and fades
		float fadeAlpha = countdownFadeTimer / 0.9f;
		float scale = 100f + (1f - fadeAlpha) * 20f;

		// Color shifts from white to red as it gets lower
		Color numColor;
		if ( seconds <= 3 )
		{
			numColor = Color.Red.WithAlpha( fadeAlpha );
		}
		else if ( seconds <= 5 )
		{
			numColor = Color.Yellow.WithAlpha( fadeAlpha );
		}
		else
		{
			numColor = Color.White.WithAlpha( fadeAlpha );
		}

		Gizmo.Draw.Color = numColor;
		Gizmo.Draw.Text(
			seconds.ToString(),
			new Transform( centerPos ),
			"Consolas",
			(int)scale
		);
	}

	public void ShowInitialMessage()
	{
		showingInitialMessage = true;
		initialDisplayTimer = initialDuration;
		Log.Info( "[CountdownUI] Showing countdown initiated message" );
	}
}

/// <summary>
/// Simple static bridge so CountdownUI can read the timer value
/// Updated by ReadyTerminal each frame during countdown
/// </summary>
public static class CountdownBridge
{
	public static float TimeRemaining { get; set; } = 0f;
	public static bool IsActive { get; set; } = false;
}