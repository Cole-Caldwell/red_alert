using Sandbox;
using Sandbox.UI;
using System.Linq;

public sealed class CameraMonitor : Component
{
	[Property] public string CameraId { get; set; } = "camera_01";
	[Property] public float PanelSize { get; set; } = 512f;

	private SecurityCamera linkedCamera;
	private bool isConnected = false;
	private float retryTimer = 0f;
	private CameraScreenPanel screenPanel;
	private bool panelVisible = false;

	protected override void OnStart()
	{
		ConnectToCamera();
	}

	private void ConnectToCamera()
	{
		linkedCamera = Scene.GetAllComponents<SecurityCamera>()
			.FirstOrDefault( c => c.CameraId == CameraId );

		if ( linkedCamera == null || linkedCamera.RenderTexture == null )
			return;

		isConnected = true;
	}

	public void ShowPanel()
	{
		if ( !isConnected ) return;

		if ( screenPanel == null )
		{
			screenPanel = new CameraScreenPanel( Scene.SceneWorld );
			screenPanel.PanelBounds = new Rect( -PanelSize / 2, -PanelSize / 2, PanelSize, PanelSize );
		}

		screenPanel.Transform = new Transform( WorldPosition, WorldRotation );
		screenPanel.CameraTexture = linkedCamera?.RenderTexture;
		panelVisible = true;
	}

	public void HidePanel()
	{
		if ( screenPanel != null )
		{
			screenPanel.Delete();
			screenPanel = null;
		}

		panelVisible = false;
	}

	protected override void OnUpdate()
	{
		if ( !isConnected )
		{
			retryTimer += Time.Delta;
			if ( retryTimer >= 2f )
			{
				retryTimer = 0f;
				ConnectToCamera();
			}
			return;
		}

		if ( panelVisible && screenPanel != null && linkedCamera?.RenderTexture != null )
		{
			screenPanel.Transform = new Transform( WorldPosition, WorldRotation );
			screenPanel.CameraTexture = linkedCamera.RenderTexture;
		}
	}

	protected override void OnDestroy()
	{
		HidePanel();
	}
}