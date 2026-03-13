using Sandbox;
using System.Collections.Generic;

public static class TaskListBridge
{
	public class TaskInfo
	{
		public string TaskName { get; set; } = "";
		public int OrderIndex { get; set; } = 0;
		public bool IsCompleted { get; set; } = false;
		public bool IsActive { get; set; } = false;
	}
	
	private static List<TaskInfo> currentTasks = new();
	private static bool shouldShow = false;
	
	public static void UpdateTasks( List<TaskInfo> tasks )
	{
		currentTasks = tasks ?? new();
		Log.Info( $"[TaskListBridge] Updated with {currentTasks.Count} tasks" );
	}
	
	public static List<TaskInfo> GetTasks()
	{
		return currentTasks;
	}
	
	public static void SetShowTasks( bool show )
	{
		shouldShow = show;
		Log.Info( $"[TaskListBridge] SetShowTasks: {show}" );
	}
	
	public static bool ShouldShowTasks()
	{
		return shouldShow;
	}
	
	public static void ClearTasks()
	{
		currentTasks.Clear();
		shouldShow = false;
		Log.Info( "[TaskListBridge] Tasks cleared" );
	}
}