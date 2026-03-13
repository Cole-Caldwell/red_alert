using Sandbox;
using System.Linq;

public class TaskStation : Component, Component.ITriggerListener
{
	[Property] public string TaskId { get; set; } = "download_data";
	[Property] public string TaskName { get; set; } = "Download Space Station Data";
	[Property] public float CompletionTime { get; set; } = 15f;
	[Property] public float InteractionRange { get; set; } = 150f;
	[Property] public TaskData.TaskType TaskType { get; set; } = TaskData.TaskType.ProgressBar;
	[Property] public SoundEvent TaskInteractSound { get; set; }
	
	private bool playerNearby = false;
	private PlayerController nearbyPlayer = null;
	
	protected override void OnStart()
	{
		Log.Info( $"TaskStation '{TaskName}' initialized at {WorldPosition}" );
	}
	
	protected override void OnUpdate()
	{
		// Only the local player checks for input
		if ( playerNearby && nearbyPlayer != null && !nearbyPlayer.IsProxy )
		{
			// CRITICAL: Anomalies can't do tasks!
			if ( nearbyPlayer.Role == PlayerController.PlayerRole.Anomaly )
			{
				return; // Don't show prompt or allow interaction
			}
			
			// Debug log the player's current active task
			if ( Time.Now % 2f < Time.Delta ) // Every 2 seconds
			{
				//Log.Info( $"[TaskStation {TaskName}] Player nearby: {nearbyPlayer.PlayerName}, ActiveTaskId: '{nearbyPlayer.CurrentActiveTaskId}', ThisTaskId: '{TaskId}', Match: {nearbyPlayer.CurrentActiveTaskId == TaskId}" );
			}
			
			// Check if this task matches the player's active task
			bool isActiveTask = (nearbyPlayer.CurrentActiveTaskId == TaskId);
			
			// Also check if they're already doing a task
			var taskManager = Scene.GetAllComponents<TaskManager>().FirstOrDefault();
			bool alreadyDoingTask = taskManager != null && taskManager.IsPlayerDoingTask( nearbyPlayer );
			
			// Only show prompt and allow interaction if this is the active task and not already doing one
			if ( isActiveTask && !alreadyDoingTask )
			{
				// Show prompt
				DrawTaskPrompt();
				
				// Check for E key press
				if ( Input.Pressed( "Use" ) )
				{
					//Log.Info( $"[TaskStation] {nearbyPlayer.PlayerName} pressed E at {TaskName}" );
					
					// Call RPC on player to attempt starting task
					nearbyPlayer.AttemptStartTaskRpc( TaskId );
				}
			}
		}
	}
	
	private void DrawTaskPrompt()
	{
		var camera = Scene.GetAllComponents<CameraComponent>().FirstOrDefault();
		if ( camera == null ) return;
		
		Vector3 promptPos = WorldPosition + Vector3.Up * 40;
		
		Gizmo.Draw.Color = Color.Cyan;
		Gizmo.Draw.Text( $"Press E: {TaskName}", new Transform( promptPos ), "Poppins", 20 );
	}
	
	void ITriggerListener.OnTriggerEnter( Collider other )
	{
		// Get the player
		var player = other.GameObject.Components.Get<PlayerController>();
		if ( player != null && player.IsAlive && !player.IsProxy )
		{
			playerNearby = true;
			nearbyPlayer = player;
			//Log.Info( $"[TaskStation] {player.PlayerName} entered trigger at {TaskName}" );
		}
	}
	
	void ITriggerListener.OnTriggerExit( Collider other )
	{
		var player = other.GameObject.Components.Get<PlayerController>();
		
		if ( player != null && player == nearbyPlayer )
		{
			playerNearby = false;
			nearbyPlayer = null;

			// Close any open task UI for this player
			if ( !player.IsProxy )
			{
				TaskProgressBridge.ClearTask();

				var taskMinigameNames = new[]
				{
					"Task Button Sequence UI",
					"Task Slider Match UI",
					"Task Collect Samples UI",
					"Task Memory Match UI",
					"Task Wire Connect UI",
					"Task Progress UI"
				};

				var taskUIs = Scene.GetAllObjects( true )
					.Where( obj => taskMinigameNames.Contains( obj.Name ) )
					.ToList();

				foreach ( var ui in taskUIs )
				{
					if ( ui != null && ui.IsValid() )
						ui.Destroy();
				}
			}
		}
	}
	
	public TaskData GetTaskData()
	{
		return new TaskData
		{
			TaskId = TaskId,
			TaskName = TaskName,
			Type = TaskType,
			CompletionTime = CompletionTime
		};
	}
}