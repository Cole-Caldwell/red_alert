using Sandbox;
using System.Linq;

/// <summary>
/// Place this component on each TaskStation GameObject (or a child of it).
/// It creates a local-only WorldPanel screen that shows "No Signal" or "Signal Aligned"
/// based on the local player's task assignments.
/// 
/// This component does NOT modify any existing task logic.
/// It only reads from TaskListBridge to determine screen state.
/// 
/// Setup:
///   - Add this component to each TaskStation (or a child positioned where the screen should appear)
///   - Set ScreenTaskId to match the TaskStation's TaskId
///   - Adjust PanelSize and PanelOffset to position the screen on your model
/// </summary>
public sealed class TaskScreenDisplay : Component
{
	/// <summary>
	/// Must match the TaskStation's TaskId on this GameObject
	/// </summary>
	[Property] public string ScreenTaskId { get; set; } = "";

	/// <summary>
	/// Size of the WorldPanel screen in world units
	/// </summary>
	[Property] public Vector2 PanelSize { get; set; } = new Vector2( 40f, 30f );

	/// <summary>
	/// Offset from this GameObject's position (local space)
	/// </summary>
	[Property] public Vector3 PanelOffset { get; set; } = new Vector3( 0, 0, 30 );

	/// <summary>
	/// Rotation offset for the panel (local angles)
	/// </summary>
	[Property] public Angles PanelRotation { get; set; } = new Angles( 0, 0, 0 );

	private TaskScreenPanel screenPanel = null;
	private bool isShowingScreen = false;
	private bool isCompleted = false;
	private float alignedTimer = 0f;
	private const float AlignedDisplayTime = 10f;
	private bool hasBeenDismissed = false;

	protected override void OnStart()
	{
		// Auto-detect TaskId from sibling TaskStation if not set
		if ( string.IsNullOrEmpty( ScreenTaskId ) )
		{
			var station = GameObject.Components.Get<TaskStation>();
			if ( station == null )
				station = GameObject.Parent?.Components.Get<TaskStation>();

			if ( station != null )
			{
				ScreenTaskId = station.TaskId;
				//Log.Info( $"[TaskScreenDisplay] Auto-detected TaskId: {ScreenTaskId}" );
			}
		}
	}

	protected override void OnUpdate()
	{
		// Reset dismissed state when tasks are cleared (new round)
		if ( hasBeenDismissed )
		{
			if ( !TaskListBridge.ShouldShowTasks() )
			{
				hasBeenDismissed = false;
				isCompleted = false;
			}
			return;
		}

		// Check local player's task list from the bridge
		if ( !TaskListBridge.ShouldShowTasks() )
		{
			// No tasks showing = game not active or player is anomaly, hide screen
			if ( isShowingScreen )
			{
				HideScreen();
			}
			hasBeenDismissed = false;
			return;
		}

		var tasks = TaskListBridge.GetTasks();
		if ( tasks == null || tasks.Count == 0 )
		{
			if ( isShowingScreen )
				HideScreen();
			return;
		}

		// Check if this TaskId is in the local player's assigned tasks
		var matchingTask = tasks.FirstOrDefault( t => IsTaskIdMatch( t ) );

		if ( matchingTask != null && Time.Now % 2f < Time.Delta )
		{
			//Log.Info( $"[TaskScreenDisplay] {ScreenTaskId} - IsCompleted: {matchingTask.IsCompleted}, IsActive: {matchingTask.IsActive}, localCompleted: {isCompleted}" );
		}

		if ( matchingTask == null )
		{
			// This station is not in the player's task list, hide screen
			if ( isShowingScreen )
				HideScreen();
			return;
		}

		// Task exists in player's list - show appropriate state
		if ( !isShowingScreen )
		{
			ShowScreen();
		}

		// Update state based on completion
		if ( matchingTask.IsCompleted && !isCompleted )
		{
			isCompleted = true;
			alignedTimer = AlignedDisplayTime;
			if ( screenPanel != null )
			{
				screenPanel.CurrentState = TaskScreenPanel.ScreenState.Aligned;
				//Log.Info( $"[TaskScreenDisplay] {ScreenTaskId} -> SIGNAL ALIGNED" );
			}
		}
		else if ( !matchingTask.IsCompleted && !isCompleted )
		{
			if ( screenPanel != null )
				screenPanel.CurrentState = TaskScreenPanel.ScreenState.NoSignal;
		}

		// Update panel transform
		if ( screenPanel != null )
		{
			UpdatePanelTransform();
		}

		// Countdown aligned display timer
		if ( isCompleted && isShowingScreen )
		{
			alignedTimer -= Time.Delta;
			if ( alignedTimer <= 0f )
			{
				hasBeenDismissed = true;
				HideScreen();
				//Log.Info( $"[TaskScreenDisplay] {ScreenTaskId} -> Aligned timer expired, screen hidden" );
			}
		}
	}

	/// <summary>
	/// Match task by checking if the TaskName corresponds to this station's TaskId.
	/// TaskListBridge doesn't store TaskId directly, so we match via TaskStation lookup.
	/// </summary>
	private bool IsTaskIdMatch( TaskListBridge.TaskInfo task )
	{
		// TaskListBridge stores TaskName, not TaskId
		// Find the TaskStation with our ScreenTaskId and compare names
		var station = Scene.GetAllComponents<TaskStation>()
			.FirstOrDefault( s => s.TaskId == ScreenTaskId );

		if ( station == null ) return false;

		return task.TaskName == station.TaskName;
	}

	private void ShowScreen()
	{
		if ( screenPanel != null ) return;

		screenPanel = new TaskScreenPanel( Scene.SceneWorld );
		screenPanel.PanelBounds = new Rect( -640, -480, 1280, 960 );
		screenPanel.CurrentState = TaskScreenPanel.ScreenState.NoSignal;

		isShowingScreen = true;
		isCompleted = false;

		UpdatePanelTransform();

		//Log.Info( $"[TaskScreenDisplay] Screen created for {ScreenTaskId}" );
	}

	private void HideScreen()
	{
		if ( screenPanel != null )
		{
			screenPanel.Delete( true );
			screenPanel = null;
		}

		isShowingScreen = false;
		isCompleted = false;

		//Log.Info( $"[TaskScreenDisplay] Screen hidden for {ScreenTaskId}" );
	}

	private void UpdatePanelTransform()
	{
		if ( screenPanel == null ) return;

		var worldPos = WorldPosition + WorldRotation * PanelOffset;
		var worldRot = WorldRotation * PanelRotation.ToRotation();

		// Scale: PanelSize.x world units / PanelBounds width pixels
		float scale = PanelSize.x / screenPanel.PanelBounds.Width;
		screenPanel.Transform = new Transform( worldPos, worldRot, scale );
	}

	protected override void OnDestroy()
	{
		HideScreen();
	}

	/// <summary>
	/// Call this to force cleanup (e.g., when game ends)
	/// </summary>
	public static void CleanupAllScreens( Scene scene )
	{
		foreach ( var display in scene.GetAllComponents<TaskScreenDisplay>() )
		{
			display.HideScreen();
		}
		//Log.Info( "[TaskScreenDisplay] All task screens cleaned up" );
	}
}
