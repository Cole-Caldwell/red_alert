using Sandbox;
using System.Linq;

public partial class RoleRevealUI : Component
{
	[Property] public float DisplayDuration { get; set; } = 5f;
	
	private float displayTimer = 0f;
	private bool isShowing = false;
	private string roleText = "";
	private Color roleColor = Color.White;

	protected override void OnUpdate()
	{
		if ( !isShowing )
			return;

		displayTimer -= Time.Delta;
		
		if ( displayTimer <= 0f )
		{
			isShowing = false;
			// Just mark for destruction, actual destroy happens below
		}

		if ( isShowing )
		{
			// Display the role reveal
			DisplayRoleReveal();
		}
		else if ( displayTimer <= 0f )
		{
			// Destroy after showing is done
			GameObject.Destroy();
		}
	}

	private void DisplayRoleReveal()
	{
		var camera = Scene.GetAllComponents<CameraComponent>().FirstOrDefault();
		if ( camera == null ) return;

		Vector3 centerPos = camera.WorldPosition + camera.WorldRotation.Forward * 400f;
		
		// Calculate fade
		float fadeProgress = displayTimer / DisplayDuration;
		float alpha = 1f;
		
		if ( fadeProgress < 0.3f )
		{
			// Fade out in last 30% of time
			alpha = fadeProgress / 0.3f;
		}
		
		// Main text
		Gizmo.Draw.Color = roleColor.WithAlpha( alpha );
		Gizmo.Draw.Text( 
			roleText, 
			new Transform( centerPos ), 
			"Poppins", 
			100 
		);
		
		// Subtitle based on role
		string subtitle = "";
		Color subtitleColor = Color.White;
		
		if ( roleText.Contains( "ANOMALY" ) )
		{
			subtitle = "Eliminate all Citizens";
			subtitleColor = Color.Red.WithAlpha( alpha * 0.8f );
		}
		else
		{
			subtitle = "Find and eject the Anomaly";
			subtitleColor = Color.Cyan.WithAlpha( alpha * 0.8f );
		}
		
		Gizmo.Draw.Color = subtitleColor;
		Gizmo.Draw.Text( 
			subtitle, 
			new Transform( centerPos + Vector3.Down * 60 ), 
			"Poppins", 
			40 
		);
	}

	public void ShowRole( PlayerController.PlayerRole role )
	{
		if ( role == PlayerController.PlayerRole.Anomaly )
		{
			roleText = "YOU ARE THE ANOMALY";
			roleColor = Color.Red;
		}
		else
		{
			roleText = "YOU ARE A CITIZEN";
			roleColor = Color.Cyan;
		}

		isShowing = true;
		displayTimer = DisplayDuration;
		
		Log.Info( $"Showing role: {roleText}" );
	}
}

