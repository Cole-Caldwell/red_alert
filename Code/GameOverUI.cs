using Sandbox;
using System.Linq;

public partial class GameOverUI : Component
{
	[Property] public float DisplayDuration { get; set; } = 6f;
	
	private float displayTimer = 0f;
	private bool isShowing = false;
	private string mainText = "";
	private string subtitleText = "";
	private Color mainColor = Color.White;
	private Color subtitleColor = Color.White;

	protected override void OnUpdate()
	{
		if ( !isShowing )
			return;

		displayTimer -= Time.Delta;
		
		if ( displayTimer <= 0f )
		{
			isShowing = false;
		}

		if ( isShowing )
		{
			DisplayWinScreen();
		}
		else
		{
			GameObject.Destroy();
		}
	}

	private void DisplayWinScreen()
	{
		var camera = Scene.GetAllComponents<CameraComponent>().FirstOrDefault();
		if ( camera == null ) return;

		Vector3 centerPos = camera.WorldPosition + camera.WorldRotation.Forward * 400f;
		
		// Calculate fade
		float fadeProgress = displayTimer / DisplayDuration;
		float alpha = 1f;
		
		if ( fadeProgress < 0.3f )
		{
			alpha = fadeProgress / 0.3f;
		}
		
		// Main text
		Gizmo.Draw.Color = mainColor.WithAlpha( alpha );
		Gizmo.Draw.Text( 
			mainText, 
			new Transform( centerPos ), 
			"Consolas", 
			90 
		);
		
		// Subtitle
		Gizmo.Draw.Color = subtitleColor.WithAlpha( alpha * 0.7f );
		Gizmo.Draw.Text( 
			subtitleText, 
			new Transform( centerPos + Vector3.Down * 60 ), 
			"Consolas", 
			36 
		);
	}

	public void ShowCitizensWin()
	{
		mainText = "CITIZENS WIN!";
		subtitleText = "The Anomaly has been defeated";
		mainColor = Color.Cyan;
		subtitleColor = Color.Cyan;

		isShowing = true;
		displayTimer = DisplayDuration;

		// Play sound
		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if ( gameManager?.CitizensWinSound != null )
		{
			Sound.Play( gameManager.CitizensWinSound );
		}

		Log.Info( "[GameOverUI] Citizens Win!" );
	}

	public void ShowAnomalyWins( string anomalyName )
	{
		mainText = "ANOMALY WINS!";
		subtitleText = "Citizens have been eliminated";
		mainColor = Color.Red;
		subtitleColor = Color.Red;

		isShowing = true;
		displayTimer = DisplayDuration;

		// Play sound
		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if ( gameManager?.AnomalyWinSound != null )
		{
			Sound.Play( gameManager.AnomalyWinSound );
		}

		Log.Info( $"[GameOverUI] Anomaly Wins! ({anomalyName})" );
	}
}
