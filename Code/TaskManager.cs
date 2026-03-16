using Sandbox;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Manages task assignment and tracking for all players
/// Add this component to your GameController
/// </summary>
public class TaskManager : Component
{
	// Task assignment tracking
	private Dictionary<PlayerController, List<PlayerTask>> playerTasks = new Dictionary<PlayerController, List<PlayerTask>>();
	
	// Currently active task UI (one per player)
	private Dictionary<PlayerController, GameObject> activeTaskUIs = new Dictionary<PlayerController, GameObject>();

	// Add this property
	[Property] public SoundEvent TaskCompleteSound { get; set; }
	
	protected override void OnStart()
	{
		Log.Info( "TaskManager initialized" );
	}
	
	protected override void OnUpdate()
	{
	}
	
	public void AssignTasksToPlayers()
	{
		// Clear any stale task data from previous rounds
    	playerTasks.Clear();
    	activeTaskUIs.Clear();
		
		// Get all task stations in the scene
		var allStations = Scene.GetAllComponents<TaskStation>().ToList();
		
		if ( allStations.Count == 0 )
		{
			Log.Warning( "No task stations found in scene!" );
			return;
		}
		
		// Determine how many tasks to assign (max 3, but limited by available stations)
		int tasksToAssign = System.Math.Min( 3, allStations.Count );
		
		//Log.Info( $"Found {allStations.Count} task stations, will assign {tasksToAssign} tasks per player" );
		
		// Get all players
		var players = Scene.GetAllComponents<PlayerController>()
			.Where( p => p.GameObject.Network.Owner != null && p.IsInGame )
			.ToList();
		
		foreach ( var player in players )
		{
			// Only assign tasks to Citizens
			if ( player.Role != PlayerController.PlayerRole.Citizen )
			{
				//Log.Info( $"{player.PlayerName} is Anomaly - no tasks assigned" );
				continue;
			}
			
			// Randomly pick tasks (up to tasksToAssign)
			var shuffledStations = allStations.OrderBy( _ => Game.Random.Int( 0, 1000 ) ).ToList();
			var assignedTasks = new List<PlayerTask>();
			
			for ( int i = 0; i < tasksToAssign; i++ )
			{
				var taskData = shuffledStations[i].GetTaskData();
				assignedTasks.Add( new PlayerTask
				{
					Task = taskData,
					OrderIndex = i,
					IsCompleted = false,
					IsActive = (i == 0) // First task is active
				} );
			}
			
			playerTasks[player] = assignedTasks;
			
			//Log.Info( $"Assigned {assignedTasks.Count} tasks to {player.PlayerName}:" );
			foreach ( var task in assignedTasks )
			{
				//Log.Info( $"  {task.OrderIndex + 1}. {task.Task.TaskName}" );
			}
		}

		// Tell each player to show their task list UI
		foreach ( var kvp in playerTasks )
		{
			var player = kvp.Key;
			var tasks = kvp.Value;

			// Get the active task ID
			var activeTask = tasks.FirstOrDefault( t => t.IsActive );
			string activeTaskId = activeTask?.Task.TaskId ?? "";
			
			if ( activeTask != null )
			{
				//Log.Info( $"Set {player.PlayerName}'s active task to: {activeTaskId}" );
			}
			
			// Skip test dummies
			if ( player.GameObject.Network.Owner == null )
			{
				//Log.Info( $"Skipping task UI for test dummy: {player.PlayerName}" );
				continue;
			}
			
			// Convert to TaskListBridge format
			var taskInfoList = tasks.Select( t => new TaskListBridge.TaskInfo
			{
				TaskName = t.Task.TaskName,
				OrderIndex = t.OrderIndex,
				IsCompleted = t.IsCompleted,
				IsActive = t.IsActive
			} ).ToList();
			
			// Add detailed logging
			//Log.Info( $"=== TASK UI RPC DEBUG ===" );
			//Log.Info( $"Player: {player.PlayerName}" );
			//Log.Info( $"IsProxy: {player.IsProxy}" );
			//Log.Info( $"Owner: {player.GameObject.Network.Owner?.DisplayName}" );
			//Log.Info( $"Sending {taskInfoList.Count} tasks via RPC with ActiveTaskId: '{activeTaskId}'..." );
			
			player.ShowTaskListRpc( taskInfoList, activeTaskId );
			
			//Log.Info( $"=== END DEBUG ===" );
		}
	}
	
	public bool CanPlayerDoTask( PlayerController player, string taskId )
	{
		if ( !playerTasks.ContainsKey( player ) )
			return false;
		
		var tasks = playerTasks[player];
		var activeTask = tasks.FirstOrDefault( t => t.IsActive && !t.IsCompleted );
		
		return activeTask != null && activeTask.Task.TaskId == taskId;
	}
	
	public bool IsPlayerDoingTask( PlayerController player )
	{
		return activeTaskUIs.ContainsKey( player ) && activeTaskUIs[player] != null && activeTaskUIs[player].IsValid();
	}
	
	[Rpc.Broadcast]
	public void StartTask( PlayerController player, TaskStation station )
	{
		// Only show UI for the local player
		var localPlayer = Scene.GetAllComponents<PlayerController>()
			.FirstOrDefault( p => !p.IsProxy );
		
		if ( localPlayer != player )
		{
			return;
		}
		
		// Clean up old UI
		if ( activeTaskUIs.ContainsKey( player ) && activeTaskUIs[player] != null && activeTaskUIs[player].IsValid() )
		{
			activeTaskUIs[player].Destroy();
			activeTaskUIs.Remove( player );
		}
		
		// Get the player's network owner ID
		ulong ownerId = 0;
		if ( player.GameObject.Network?.Owner != null )
		{
			ownerId = player.GameObject.Network.Owner.SteamId;
		}
		
		// Send data to bridge (now includes owner ID)
		TaskProgressBridge.StartTask( 
			station.TaskName, 
			station.TaskId, 
			station.CompletionTime,
			player.PlayerName,
			ownerId  // NEW
		);

		// Play looping task interaction sound
		if ( station.TaskInteractSound != null )
		{
			TaskProgressBridge.StopTaskSound(); // Stop any existing sound first
			var handle = Sound.Play( station.TaskInteractSound );
			if ( handle != null )
			{
				handle.ListenLocal = true;
				handle.Volume = 0.5f;
			}
			TaskProgressBridge.ActiveTaskSound = handle;
		}
		
		// Find UI Screen
		var uiScreen = Scene.GetAllObjects( true )
			.FirstOrDefault( obj => obj.Name.Contains( "Info Screen" ) || obj.Components.Get<ScreenPanel>() != null );
		
		if ( uiScreen == null )
		{
			Log.Warning( "Could not find UI Screen!" );
			return;
		}
		
		// Create appropriate UI based on task type
		var uiObject = Scene.CreateObject();
		uiObject.SetParent( uiScreen );
		
		var taskData = station.GetTaskData();
		
		if ( taskData.Type == TaskData.TaskType.ButtonSequence )
		{
			uiObject.Name = "Task Button Sequence UI";
			var sequenceUI = uiObject.Components.Create<TaskButtonSequenceUI>();
			sequenceUI.InitializeFromBridge( this );
		}
		else if ( taskData.Type == TaskData.TaskType.SliderMatch )
		{
			uiObject.Name = "Task Slider Match UI";
			TaskProgressBridge.SliderMatchSound = station.SliderMatchSound;
			var sliderUI = uiObject.Components.Create<TaskSliderMatchUI>();
			sliderUI.InitializeFromBridge( this );
		}
		else if ( taskData.Type == TaskData.TaskType.CollectSamples )
		{
			uiObject.Name = "Task Collect Samples UI";
			var collectUI = uiObject.Components.Create<TaskCollectSamplesUI>();
			collectUI.InitializeFromBridge( this );
		}
		else if ( taskData.Type == TaskData.TaskType.MemoryMatch )
		{
			uiObject.Name = "Task Memory Match UI";
			TaskProgressBridge.MatchCorrectSound = station.MatchCorrectSound;
    		TaskProgressBridge.MatchIncorrectSound = station.MatchIncorrectSound;
			var memoryUI = uiObject.Components.Create<TaskMemoryMatchUI>();
			memoryUI.InitializeFromBridge( this );
		}
		else if ( taskData.Type == TaskData.TaskType.Decrypt )
		{
			uiObject.Name = "Task Decrypt UI";
			TaskProgressBridge.KeypadSound = station.KeypadBeepSound;
			var decryptUI = uiObject.Components.Create<TaskWireConnectUI>();
			decryptUI.InitializeFromBridge( this );
		}
		else // ProgressBar
		{
			uiObject.Name = "Task Progress UI";
			var screenPanel = uiObject.Components.Create<ScreenPanel>();
			screenPanel.ZIndex = 100;
			var progressUI = uiObject.Components.Create<TaskProgressUI>();
			progressUI.InitializeFromBridge( this );
		}
		
		activeTaskUIs[player] = uiObject;
		
		//Log.Info( $"{player.PlayerName} (OwnerId: {ownerId}) started task: {station.TaskName} (Type: {taskData.Type})" );
	}
	
	public void CompleteTask( PlayerController player, string taskId )
	{
		if ( !playerTasks.ContainsKey( player ) )
		{
			Log.Warning( $"[CompleteTask] Player {player.PlayerName} not in playerTasks dictionary!" );
			return;
		}
		
		var tasks = playerTasks[player];
		var completedTask = tasks.FirstOrDefault( t => t.Task.TaskId == taskId && t.IsActive );
		
		if ( completedTask != null )
		{
			completedTask.IsCompleted = true;
			completedTask.IsActive = false;
			
			//Log.Info( $"[CompleteTask] {player.PlayerName} completed task: {taskId}" );
			
			// Activate next task
			var nextTask = tasks.FirstOrDefault( t => !t.IsCompleted );
			if ( nextTask != null )
			{
				nextTask.IsActive = true;
				player.CurrentActiveTaskId = nextTask.Task.TaskId; // Update active task ID
				//Log.Info( $"{player.PlayerName} completed task! Next task: {nextTask.Task.TaskName}" );
			}
			else
			{
				player.CurrentActiveTaskId = ""; // No more tasks
				//Log.Info( $"{player.PlayerName} completed ALL tasks!" );
			}
			
			// Remove active UI reference
			if ( activeTaskUIs.ContainsKey( player ) )
			{
				activeTaskUIs.Remove( player );
			}

			TaskProgressBridge.StopTaskSound();
			
			// Refresh the task list UI with updated tasks
			if ( player.GameObject.Network.Owner != null )
			{
				var taskInfoList = tasks.Select( t => new TaskListBridge.TaskInfo
				{
					TaskName = t.Task.TaskName,
					OrderIndex = t.OrderIndex,
					IsCompleted = t.IsCompleted,
					IsActive = t.IsActive
				} ).ToList();
				
				// Get the new active task ID
				var newActiveTask = tasks.FirstOrDefault( t => t.IsActive );
				string newActiveTaskId = newActiveTask?.Task.TaskId ?? "";
				
				//Log.Info( $"[CompleteTask] Sending updated task list to {player.PlayerName} ({taskInfoList.Count} tasks), NewActiveTaskId: '{newActiveTaskId}'" );
				player.ShowTaskListRpc( taskInfoList, newActiveTaskId );
			}
		}
		else
		{
			Log.Warning( $"[CompleteTask] Could not find active task {taskId} for {player.PlayerName}" );
		}
	}

	[Rpc.Broadcast]
	public void CompleteTaskByNetworkId( ulong ownerId, string taskId )
	{
		//Log.Info( $"[CompleteTaskByNetworkId] Called on IsHost: {Networking.IsHost}, OwnerId: {ownerId}, TaskId: {taskId}" );
		
		// Only host processes the completion
		if ( !Networking.IsHost )
		{
			//Log.Info( $"[CompleteTaskByNetworkId] Not host - skipping" );
			return;
		}
		
		// Find the player by their network owner ID
		var player = Scene.GetAllComponents<PlayerController>()
			.FirstOrDefault( p => p.GameObject.Network.Owner != null && p.GameObject.Network.Owner.SteamId == ownerId );
		
		if ( player != null )
		{
			//Log.Info( $"[CompleteTaskByNetworkId] Found player: {player.PlayerName} (OwnerId: {ownerId}), calling CompleteTask" );

			// Don't complete tasks for dead players
			if ( !player.IsAlive )
			{
				Log.Info( $"[CompleteTaskByNetworkId] Player {player.PlayerName} is dead - ignoring task completion" );
				return;
			}
			
			CompleteTask( player, taskId );

			// Play completion sound - call RPC on the player object
			if ( player.GameObject.Network.Owner != null )
			{
				//Log.Info( $"[CompleteTaskByNetworkId] Calling PlayTaskCompleteSoundRpc for {player.PlayerName}" );
				player.PlayTaskCompleteSoundRpc();
			}
		}
		else
		{
			Log.Warning( $"[CompleteTaskByNetworkId] Could not find player with OwnerId: {ownerId}" );
		}
	}

	public bool HasPlayerCompletedAllTasks( PlayerController player )
	{
		if ( !playerTasks.ContainsKey( player ) )
			return false;
		
		var tasks = playerTasks[player];
		return tasks.Count > 0 && tasks.All( t => t.IsCompleted );
	}
	
	public List<PlayerTask> GetPlayerTasks( PlayerController player )
	{
		if ( playerTasks.ContainsKey( player ) )
			return playerTasks[player];
		
		return new List<PlayerTask>();
	}

	public void ClearPlayerTasks( PlayerController player )
	{
		if ( playerTasks.ContainsKey( player ) )
		{
			playerTasks.Remove( player );
			Log.Info( $"[TaskManager] Cleared tasks for {player.PlayerName}" );
		}

		if ( activeTaskUIs.ContainsKey( player ) )
		{
			if ( activeTaskUIs[player] != null && activeTaskUIs[player].IsValid() )
			{
				activeTaskUIs[player].Destroy();
			}
			activeTaskUIs.Remove( player );
		}
	}
}

