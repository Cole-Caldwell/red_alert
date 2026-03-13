using Sandbox;

public static class TaskProgressBridge
{
	public class TaskProgressData
	{
		public string TaskName { get; set; } = "";
		public string TaskId { get; set; } = "";
		public float CompletionTime { get; set; } = 15f;
		public string PlayerName { get; set; } = "";
		public ulong PlayerOwnerId { get; set; } = 0; // NEW
	}
	
	private static TaskProgressData currentTask = null;
	
	public static void StartTask( string taskName, string taskId, float completionTime, string playerName, ulong playerOwnerId )
	{
		currentTask = new TaskProgressData
		{
			TaskName = taskName,
			TaskId = taskId,
			CompletionTime = completionTime,
			PlayerName = playerName,
			PlayerOwnerId = playerOwnerId // NEW
		};
		
		Log.Info( $"[TaskProgressBridge] Task started: {taskName} for {playerName} (OwnerId: {playerOwnerId})" );
	}

	public static SoundHandle ActiveTaskSound { get; set; }

	public static void StopTaskSound()
	{
		if ( ActiveTaskSound != null )
		{
			ActiveTaskSound.Stop();
			ActiveTaskSound = null;
		}
	}
	
	public static TaskProgressData GetCurrentTask()
	{
		return currentTask;
	}
	
	// Convenience property for easy access
	public static ulong PlayerOwnerId => currentTask?.PlayerOwnerId ?? 0;
	
	public static void ClearTask()
	{
		StopTaskSound();
		currentTask = null;
	}
}