using Sandbox;

public sealed class SecurityCamera : Component
{
    [Property] public string CameraId { get; set; } = "camera_01";
    [Property] public string DisplayName { get; set; } = "Camera 1";
    [Property] public int RenderResolution { get; set; } = 512;
    [Property] public bool IsActive { get; set; } = false;
    [Property] public int FrameSkip { get; set; } = 1;

    public CameraComponent CameraComponent { get; private set; }
    public Texture RenderTexture { get; private set; }

    private int frameCounter = 0;

    protected override void OnAwake()
    {
        CameraComponent = GameObject.Components.Get<CameraComponent>( FindMode.EverythingInSelfAndDescendants );

        if ( CameraComponent != null )
        {
            CameraComponent.Enabled = false;
        }
    }

    protected override void OnStart()
    {
        if ( CameraComponent == null )
        {
            CameraComponent = GameObject.Components.Get<CameraComponent>( FindMode.EverythingInSelfAndDescendants );
        }

        if ( CameraComponent == null )
        {
            Log.Warning( $"[SecurityCamera] {CameraId}: No CameraComponent found!" );
            return;
        }

        CameraComponent.Enabled = false;

        RenderTexture = Texture.CreateRenderTarget(
            $"security_cam_{CameraId}",
            ImageFormat.RGBA8888,
            new Vector2( RenderResolution )
        );
    }

    protected override void OnUpdate()
    {
        if ( CameraComponent == null || RenderTexture == null ) return;
        if ( !IsActive ) return;

        // Frame skip for performance
        if ( FrameSkip > 0 )
        {
            frameCounter++;
            if ( frameCounter % ( FrameSkip + 1 ) != 0 )
                return;
        }

        // Temporarily enable, render, then disable
        CameraComponent.Enabled = true;
        CameraComponent.RenderToTexture( RenderTexture );
        CameraComponent.Enabled = false;

        if ( Time.Now % 2f < Time.Delta )
            Log.Info( $"[SecurityCamera] {CameraId} rendering frame" );
    }

    public void Activate()
    {
        IsActive = true;
        Log.Info( $"[SecurityCamera] {CameraId} activated" );
    }

    public void Deactivate()
    {
        IsActive = false;
        Log.Info( $"[SecurityCamera] {CameraId} deactivated" );
    }

    protected override void OnDestroy()
    {
        RenderTexture?.Dispose();
        RenderTexture = null;
    }
}