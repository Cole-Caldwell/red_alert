using Sandbox;

/// <summary>
/// Defines a type of task that players can complete
/// </summary>
public class TaskData
{
	public string TaskId { get; set; } // Unique identifier (e.g., "download_data")
	public string TaskName { get; set; } // Display name (e.g., "Download Space Station Data")
	public TaskType Type { get; set; }
	public float CompletionTime { get; set; } // Seconds to complete
	
	public enum TaskType
	{
		ProgressBar,     // Hold E to fill bar
		ButtonSequence,  // Press buttons in order
		Decrypt,         // Follow keypad crack number order
		SliderMatch,      // Match slider position
		CollectSamples,   // Click moving targets
		MemoryMatch      // Memory card matching
	}
}

/// <summary>
/// Tracks a player's assigned task
/// </summary>
public class PlayerTask
{
	public TaskData Task { get; set; }
	public int OrderIndex { get; set; } // 0 = first task, 1 = second, 2 = third
	public bool IsCompleted { get; set; } = false;
	public bool IsActive { get; set; } = false; // Currently the task they should do
}

