using Sandbox;

public static class GameSettingsBridge
{
    public static bool IsOpen { get; set; } = false;
    public static int AnomalyCount { get; set; } = 1;
    public static int PlayerCount { get; set; } = 0;

    public static void Open()
    {
        IsOpen = true;
    }

    public static void Close()
    {
        IsOpen = false;
    }

    public static void SetAnomalyCount( int count )
    {
        AnomalyCount = count;
    }
}