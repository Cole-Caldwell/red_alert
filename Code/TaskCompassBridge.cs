using Sandbox;
using System.Linq;

public static class TaskCompassBridge
{
    public static bool ShouldShow { get; set; } = false;
    public static string ActiveTaskId { get; set; } = "";
    public static float CameraYaw { get; set; } = 0f;
    public static Vector3 PlayerPosition { get; set; } = Vector3.Zero;
    public static Vector3 TaskPosition { get; set; } = Vector3.Zero;
    public static bool HasActiveTask { get; set; } = false;

    public static void Update( Scene scene )
    {
        var localPlayer = scene.GetAllComponents<PlayerController>()
            .FirstOrDefault( p => !p.IsProxy && p.GameObject.Network.Owner != null );

        if ( localPlayer == null || !localPlayer.IsInGame || !localPlayer.IsAlive )
        {
            ShouldShow = false;
            return;
        }

        if ( localPlayer.Role != PlayerController.PlayerRole.Citizen ||
             string.IsNullOrEmpty( localPlayer.CurrentActiveTaskId ) )
        {
            ShouldShow = false;
            return;
        }

        var camera = scene.GetAllComponents<CameraComponent>().FirstOrDefault();
        if ( camera == null )
        {
            ShouldShow = false;
            return;
        }

        ShouldShow = true;
        ActiveTaskId = localPlayer.CurrentActiveTaskId;
        CameraYaw = camera.WorldRotation.Yaw();
        PlayerPosition = localPlayer.WorldPosition;

        var taskStation = scene.GetAllComponents<TaskStation>()
            .FirstOrDefault( ts => ts.TaskId == localPlayer.CurrentActiveTaskId );

        if ( taskStation != null )
        {
            HasActiveTask = true;
            TaskPosition = taskStation.WorldPosition;
        }
        else
        {
            HasActiveTask = false;
        }
    }
}
