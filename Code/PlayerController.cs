using Sandbox;
using System.Linq;

public partial class PlayerController : Component
{
	// Player Role System
	public enum PlayerRole
	{
		Citizen,
		Anomaly
	}

	[Property, Sync] public PlayerRole Role { get; set; } = PlayerRole.Citizen;
	[Property, Sync] public bool IsAlive { get; set; } = true;
	[Property, Sync] public string PlayerName { get; set; } = "Player";
	[Property, Sync] public bool IsInGame { get; set; } = false;
	[Property, Sync] public bool IsSpectating { get; set; } = false;
	[Property, Sync] public bool IsTyping { get; set; } = false;
	[Property] public GameObject RagdollPrefab { get; set; }
	[Property] public float XRayDuration { get; set; } = 20f;
	[Property] public float VanishCooldown { get; set; } = 90f;
	
	// Kill System (Anomaly only)
	[Property] public float KillCooldown { get; set; } = 10f;
	[Property] public float KillRange { get; set; } = 150f;
	[Property] public GameObject PlayerPrefab { get; set; }
	private float lastKillTime = 0f;

	// Purge System (Anomaly only)
	[Property] public float PurgeCooldown { get; set; } = 120f;
	[Property] public float PurgeDuration { get; set; } = 10f;
	[Property] public SoundEvent PurgeActivateSound { get; set; }
	[Property] public SoundEvent BlindedSound { get; set; }
	[Property] public SoundEvent DeathSound { get; set; }
	[Property] public SoundEvent KillSound { get; set; }
	[Property] public float MimicDuration { get; set; } = 15f;
	
	private bool mimicActive = false;
	private float mimicEndTime = 0f;
	private string originalName = "";

	private float lastPurgeTime = -999f;
	private bool isBlinded = false;
	private AnomalyAbilitiesUI anomalyUI = null;
	public string EquippedPurgeAbility { get; set; } = "blind";
	
	// X-Ray tracking
	private bool xRayActive = false;
	private float xRayEndTime = 0f;

	// Movement Settings
	[Property] public float WalkSpeed { get; set; } = 200f;
	[Property] public float RunSpeed { get; set; } = 350f;
	[Property] public float JumpStrength { get; set; } = 300f;
	[Property] public float Gravity { get; set; } = 800f;

	// Components
	private CharacterController characterController;
	private CameraComponent camera;
	private Voice voiceComponent;

	// Camera Station Mount System
	private CameraStation mountedStation = null;
	private bool isMountedToStation = false;
	
	// Movement State
	private Vector3 velocity;
	private bool isGrounded;

	// Store the player's current active task ID locally
	public string CurrentActiveTaskId { get; set; } = "";

	protected override void OnStart()
	{
		// Clear task list when player spawns (handles scene reloads and game restarts)
		if ( !IsProxy )
		{
			TaskListBridge.ClearTasks();
		}
		
		// Get or create character controller
		characterController = GameObject.Components.Get<CharacterController>();
		if ( characterController == null )
		{
			characterController = GameObject.Components.Create<CharacterController>();
		}

		// Find the camera
		camera = Scene.GetAllComponents<CameraComponent>().FirstOrDefault();
		
		Log.Info( $"PlayerController initialized for {PlayerName}" );

		// New players always start outside the game
		if ( !IsProxy )
		{
			IsInGame = false;
		}

		// Clear task list when player spawns (handles scene reloads)
		TaskListBridge.ClearTasks();

		// Get voice component
		voiceComponent = Components.Get<Voice>();
		if ( voiceComponent != null )
		{
			// Start with voice ENABLED (lobby state)
			voiceComponent.Enabled = true;
			Log.Info( $"Voice component found for {PlayerName}, starting enabled" );
		}

		// Force cleanup any stale anomaly UI on fresh join
		if ( !IsProxy )
		{
			var staleAnomalyUIs = Scene.GetAllComponents<AnomalyAbilitiesUI>().ToList();
			foreach ( var ui in staleAnomalyUIs )
			{
				if ( ui != null && ui.IsValid() )
				{
					ui.GameObject.Destroy();
					Log.Info( $"[OnStart] Destroyed stale AnomalyAbilitiesUI for {PlayerName}" );
				}
			}
			anomalyUI = null;
		}

		// Ensure no stale UI shows on fresh join
		if ( !IsProxy )
		{
			// Hide anomaly UI if it somehow exists
			if ( anomalyUI != null && anomalyUI.IsValid() )
			{
				anomalyUI.GameObject.Destroy();
				anomalyUI = null;
			}

			// Reset role to citizen (default)
			Role = PlayerRole.Citizen;
		}
	}

	protected override void OnUpdate()
	{
		// Don't control if this is not our player
		if ( IsProxy )
			return;
		
		// Draw X-Ray markers if active
		if ( xRayActive )
		{
			if ( Time.Now >= xRayEndTime )
			{
				xRayActive = false;
				Log.Info( "[X-Ray] Vision ended" );
			}
			else
			{
				DrawXRayMarkers();
			}
		}

		// Check mimic timer
		if ( mimicActive )
		{
			if ( Time.Now >= mimicEndTime )
			{
				mimicActive = false;
				RemoveMimicRpc();
			}
		}

		// Dead players can't move
		if ( !IsAlive )
		{
			return;
		}

		// If mounted to camera station, handle station controls only
		if ( isMountedToStation )
		{
			// Left click cycles cameras
			if ( Input.Pressed( "attack1" ) )
			{
				mountedStation?.NextCamera();
			}

			// E key unmounts
			if ( Input.Pressed( "Use" ) )
			{
				mountedStation?.Unmount();
			}

			// Don't allow movement while mounted
			return;
		}

		// Handle movement
		HandleMovement();

		// Handle E key press based on role
		if ( Input.Pressed( "Use" ) )
		{
    		if ( Role == PlayerRole.Anomaly )
			{
				// Anomaly prioritizes kill over camera station
				AttemptKill();
				if ( !AttemptKill() )
				{
					CheckCameraStation();
				}
			}
			else
			{
				if ( !CheckCameraStation() )
				{
					CheckReadyTerminal();
				}
			}
		}

		// Handle F key press for Anomaly Purge
		if ( Input.Pressed( "Flashlight" ) ) // F key
		{
			if ( Role == PlayerRole.Anomaly )
			{
				AttemptPurge();
			}
		}
	}

	private void CheckReadyTerminal()
	{
		var terminals = Scene.GetAllComponents<ReadyTerminal>();
		
		foreach ( var terminal in terminals )
		{
			float distance = Vector3.DistanceBetween( WorldPosition, terminal.WorldPosition );
			
			if ( distance <= 150f )
			{
				// Check if a game is in progress - offer spectating instead
				var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
				if ( gameManager != null && gameManager.CurrentState != GameManager.GameState.WaitingInLobby )
				{
					// Start spectating
					RequestSpectateRpc();
					return;
				}

				if ( GameObject.Network.Owner == null )
				{
					Log.Warning( $"[ReadyTerminal] {PlayerName} has no network owner - cannot ready up" );
					return;
				}

				var uniqueId = GameObject.Network.Owner?.SteamId.ToString() ?? PlayerName;
				terminal.PlayerReadyUp( uniqueId );
				return;
			}
		}
	}

	[Rpc.Owner]
	public void ShowReadyFeedbackRpc( bool isReady )
	{
		// Destroy any existing feedback UI first
		var existingFeedback = Scene.GetAllObjects( true )
			.Where( obj => obj.Name == "Ready Feedback UI" )
			.ToList();
		
		foreach ( var existing in existingFeedback )
		{
			if ( existing != null && existing.IsValid() )
				existing.Destroy();
		}

		var uiObject = Scene.CreateObject();
		uiObject.Name = "Ready Feedback UI";
		var feedback = uiObject.Components.Create<ReadyFeedbackUI>();
		
		if ( isReady )
		{
			feedback.ShowReady();
		}
		else
		{
			feedback.ShowUnready();
		}
	}

	[Rpc.Broadcast]
	public void RequestSpectateRpc()
	{
		if ( !Networking.IsHost ) return;

		if ( IsInGame || IsSpectating ) return;

		// Determine spawn location based on current game state
		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		string spawnTag = "GameSpawn";

		if ( gameManager != null && gameManager.CurrentState == GameManager.GameState.Voting )
		{
			spawnTag = "meetingspawn";
		}

		var spawns = Scene.GetAllObjects( true )
			.Where( obj => obj.Tags != null && obj.Tags.Has( spawnTag ) )
			.ToList();

		Vector3 spawnPos = WorldPosition;
		if ( spawns.Count > 0 )
		{
			spawnPos = spawns[Game.Random.Int( 0, spawns.Count - 1 )].WorldPosition;
		}

		// Broadcast state change and teleport to all clients
		EnterSpectatorModeRpc( spawnPos );
	}

	[Rpc.Broadcast]
	private void EnterSpectatorModeRpc( Vector3 position )
	{
		IsSpectating = true;
		IsInGame = true;
		IsAlive = false;

		GameObject.WorldPosition = position;

		// Ghost the player visuals
		var nametag = GameObject.Components.Get<PlayerNametag>( FindMode.EverythingInSelfAndDescendants );
		if ( nametag != null )
			nametag.Enabled = false;

		foreach ( var r in GameObject.Components.GetAll<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
			r.Enabled = false;
		foreach ( var r in GameObject.Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
			r.Enabled = false;

		var dresser = GameObject.Components.Get<Dresser>( FindMode.EverythingInSelfAndDescendants );
		if ( dresser != null )
			dresser.Enabled = false;

		foreach ( var c in GameObject.Components.GetAll<Collider>( FindMode.EverythingInSelfAndDescendants ) )
			c.Enabled = false;

		var footstepController = GameObject.Components.Get<Sandbox.PlayerController>();
		if ( footstepController != null )
		{
			footstepController.EnableFootstepSounds = false;
			footstepController.WalkSpeed = 400f;
			footstepController.RunSpeed = 600f;
		}

		if ( !IsProxy )
		{
			if ( ChatSystem.Instance != null )
				ChatSystem.Instance.ChatEnabled = false;

			var voiceComp = Components.Get<Voice>();
			if ( voiceComp != null )
				voiceComp.Enabled = false;
		}

		Log.Info( $"{PlayerName} entered spectator mode at {position}" );
	}

	private bool CheckCameraStation()
	{
		var stations = Scene.GetAllComponents<CameraStation>();

		foreach ( var station in stations )
		{
			float distance = Vector3.DistanceBetween( WorldPosition, station.WorldPosition );

			if ( distance <= 150f )
			{
				return station.TryInteract( this );
			}
		}

		return false;
	}

	public void MountToStation( CameraStation station )
	{
		mountedStation = station;
		isMountedToStation = true;
		Log.Info( $"[PlayerController] {PlayerName} mounted to camera station" );
	}

	public void UnmountFromStation()
	{
		mountedStation = null;
		isMountedToStation = false;
		Log.Info( $"[PlayerController] {PlayerName} unmounted from camera station" );
	}

	[Rpc.Broadcast]
	public void SetInGameRpc( bool inGame )
	{
		IsInGame = inGame;
	}

	private void HandleMovement()
	{
		if ( characterController == null )
		{
			characterController = GameObject.Components.Get<CharacterController>( FindMode.EverythingInSelfAndDescendants );
			if ( characterController == null ) return;
		}

		if ( !characterController.Enabled )
			return;

		// Get camera rotation for movement direction
		var cameraRotation = camera != null ? camera.WorldRotation : Rotation.Identity;

		// Get movement input (WASD)
		var wishDir = Vector3.Zero;
		if ( Input.Down( "Forward" ) ) wishDir += cameraRotation.Forward;
		if ( Input.Down( "Backward" ) ) wishDir += cameraRotation.Backward;
		if ( Input.Down( "Left" ) ) wishDir += cameraRotation.Left;
		if ( Input.Down( "Right" ) ) wishDir += cameraRotation.Right;

		// Normalize to prevent faster diagonal movement
		if ( !wishDir.IsNearZeroLength )
			wishDir = wishDir.Normal;

		// Choose speed based on sprint
		float currentSpeed = Input.Down( "Run" ) ? RunSpeed : WalkSpeed;

		// Apply horizontal movement
		wishDir *= currentSpeed;

		// Check if grounded
		isGrounded = characterController.IsOnGround;

		// Apply gravity
		if ( !isGrounded )
		{
			velocity += Vector3.Down * Gravity * Time.Delta;
		}
		else
		{
			velocity = velocity.WithZ( 0 );

			// Jump
			if ( Input.Down( "Jump" ) )
			{
				velocity = velocity.WithZ( JumpStrength );
			}
		}

		// Combine horizontal movement with vertical velocity
		var finalVelocity = wishDir + velocity.WithX( 0 ).WithY( 0 );

		// Move the character
		characterController.Velocity = finalVelocity;
	}

	[Rpc.Broadcast]
	public void AssignRoleRpc( PlayerRole assignedRole )
	{
		Role = assignedRole;
		IsAlive = true;
	}

	[Rpc.Owner]
	public void ShowRoleRevealRpc( PlayerRole assignedRole )
	{
		// Find GameManager to get sound events
		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if ( gameManager == null )
		{
			Log.Warning( "Could not find GameManager for role sounds!" );
			return;
		}

		// Create role reveal UI
		var uiObject = Scene.CreateObject();
		uiObject.Name = "Role Reveal UI";
		var roleUI = uiObject.Components.Create<RoleRevealUI>();
		roleUI.ShowRole( assignedRole );

		// Play role-specific sound
		SoundEvent roleSound = null;
		
		if ( assignedRole == PlayerRole.Anomaly )
		{
			roleSound = gameManager.AnomalyRoleSound;
		}
		else
		{
			roleSound = gameManager.CitizenRoleSound;
		}
		
		if ( roleSound != null )
		{
			var handle = Sound.Play( roleSound );
			if ( handle != null )
			{
				handle.ListenLocal = true;
				handle.Volume = 1.0f;
			}
			Log.Info( $"Playing role sound for {assignedRole}" );
		}
	}

	[Rpc.Owner]
	public void ShowTaskListRpc( List<TaskListBridge.TaskInfo> taskList, string activeTaskId )
	{
		// Anomalies don't get tasks
		if ( Role == PlayerRole.Anomaly )
		{
			TaskListBridge.ClearTasks();
			TaskListBridge.SetShowTasks( false );
			CurrentActiveTaskId = "";
			return;
		}
		
		// Set the active task ID
		CurrentActiveTaskId = activeTaskId;
		
		// If no tasks, hide the UI
		if ( taskList == null || taskList.Count == 0 )
		{
			TaskListBridge.SetShowTasks( false );
			return;
		}
		
		// Update bridge with the provided task list
		TaskListBridge.UpdateTasks( taskList );
		TaskListBridge.SetShowTasks( true );
	}

	[Rpc.Broadcast]
	public void AttemptStartTaskRpc( string taskId )
	{
		// Only host validates and starts the task
		if ( !Networking.IsHost )
			return;
		
		var taskManager = Scene.GetAllComponents<TaskManager>().FirstOrDefault();
		if ( taskManager == null )
		{
			Log.Error( "[AttemptStartTaskRpc] TaskManager not found!" );
			return;
		}
		
		// Check if player can do this task
		bool canDoTask = taskManager.CanPlayerDoTask( this, taskId );
		bool alreadyDoingTask = taskManager.IsPlayerDoingTask( this );
		
		if ( canDoTask && !alreadyDoingTask )
		{
			// Find the task station
			var station = Scene.GetAllComponents<TaskStation>()
				.FirstOrDefault( s => s.TaskId == taskId );
			
			if ( station != null )
			{
				taskManager.StartTask( this, station );
			}
			else
			{
				Log.Warning( $"[AttemptStartTaskRpc] Could not find task station with ID: {taskId}" );
			}
		}
		else
		{
			Log.Warning( $"[AttemptStartTaskRpc] Cannot start task - CanDoTask: {canDoTask}, AlreadyDoingTask: {alreadyDoingTask}" );
		}
	}

	[Rpc.Owner]
	public void PlayTaskCompleteSoundRpc()
	{
		// Find TaskManager to get the sound
		var taskManager = Scene.GetAllComponents<TaskManager>().FirstOrDefault();
		if ( taskManager?.TaskCompleteSound != null )
		{
			var handle = Sound.Play( taskManager.TaskCompleteSound );
			if ( handle != null )
			{
				handle.ListenLocal = true; // Force 2D UI sound
				handle.Volume = 1.0f;
			}
			Log.Info( "Playing task complete sound" );
		}
		else
		{
			Log.Warning( "No task complete sound configured!" );
		}
	}

	[Rpc.Owner]
	public void ClearTaskListRpc()
	{
		Log.Info( $"[ClearTaskListRpc] IsHost: {Networking.IsHost}, PlayerName: {PlayerName}" );
		TaskListBridge.ClearTasks();
		HideAnomalyAbilitiesRpc(); // Hide Anomaly UI when returning to lobby
	}

	[Rpc.Broadcast]
	public void SetVoiceChatEnabled( bool enabled )
	{
		if ( voiceComponent != null )
		{
			voiceComponent.Enabled = enabled;
		}
	}

	public bool AttemptKill()
	{
		// Only Anomalies can kill
		if ( Role != PlayerRole.Anomaly )
		{
			Log.Warning( "[DEBUG] BLOCKED: Not an Anomaly!" );
			return false;
		}

		// Can't kill if dead
		if ( !IsAlive )
		{
			Log.Warning( "[DEBUG] BLOCKED: Player is dead!" );
			return false;
		}

		// Check if game is active
		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if ( gameManager == null || gameManager.CurrentState != GameManager.GameState.InGame )
		{
			Log.Info( "Cannot kill - game is not active!" );
			return false;
		}
			
		// Check cooldown
		float timeSinceLastKill = Time.Now - lastKillTime;
		if ( timeSinceLastKill < KillCooldown )
		{
			float timeRemaining = KillCooldown - timeSinceLastKill;
			Log.Info( $"Kill on cooldown! Wait {timeRemaining:F1} more seconds" );
			return false;
		}

		// Find nearby players to kill (include proxies)
		var nearbyPlayers = Scene.GetAllComponents<PlayerController>()
			.Where( p => p != this )           // Not ourselves
			.Where( p => p.IsAlive )          // Still alive
			.Where( p => p.Role != PlayerRole.Anomaly )
			.OrderBy( p => Vector3.DistanceBetween( WorldPosition, p.WorldPosition ) )
			.FirstOrDefault();

		// Check if player is in range
		if ( nearbyPlayers != null )
		{
			float distance = Vector3.DistanceBetween( WorldPosition, nearbyPlayers.WorldPosition );

			if ( distance <= KillRange )
			{
				KillPlayer( nearbyPlayers );
				lastKillTime = Time.Now;

				if ( anomalyUI != null && anomalyUI.IsValid() )
				{
					anomalyUI.SetKillCooldown( KillCooldown, lastKillTime );
				}

				Log.Info( "Kill successful!" );
				return true;
			}
		}
		return false;
	}

	private void AttemptPurge()
	{
		// Check if game is active
		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if ( gameManager == null || gameManager.CurrentState != GameManager.GameState.InGame )
		{
			Log.Info( "Cannot purge - game is not active!" );
			return;
		}
		
		// Check cooldown (varies by ability)
		float activeCooldown = GetPurgeCooldownForAbility();
		float timeSincePurge = Time.Now - lastPurgeTime;
		if ( timeSincePurge < activeCooldown )
		{
			float timeRemaining = activeCooldown - timeSincePurge;
			Log.Info( $"Purge on cooldown! Wait {timeRemaining:F1} more seconds" );
			return;
		}
		
		// Execute purge
		lastPurgeTime = Time.Now;

		// Update UI cooldown
		if ( anomalyUI != null && anomalyUI.IsValid() )
		{
			anomalyUI.SetPurgeCooldown( activeCooldown, lastPurgeTime );
		}
		
		Log.Info( $"[AttemptPurge] EquippedPurgeAbility: '{EquippedPurgeAbility}', Bridge: '{PurgeProgressionBridge.EquippedAbilityId}'" );
		// Call RPC to execute purge
		ExecutePurgeRpc( EquippedPurgeAbility );
	}

	private float GetPurgeCooldownForAbility()
	{
		switch ( EquippedPurgeAbility )
		{
			case "vanish":
				return VanishCooldown;
			default:
				return PurgeCooldown;
		}
	}

	[Rpc.Broadcast]
	public void KillPlayer( PlayerController target )
	{
		if ( !target.IsAlive )
			return;

		target.IsAlive = false;

		// Play kill sound for the anomaly
		if ( !IsProxy )
		{
			if ( KillSound != null )
			{
				var handle = Sound.Play( KillSound );
				if ( handle != null )
				{
					handle.ListenLocal = true;
					handle.Volume = 1.0f;
				}
			}
		}

		// SAVE DEATH POSITION AND RENDERER BEFORE ANYTHING ELSE
		var deathPosition = target.WorldPosition;
		var deathRotation = target.WorldRotation;
		var targetRenderer = target.GameObject.Components.GetInDescendants<SkinnedModelRenderer>();

		// ALL CLIENTS spawn their own ragdoll (local visual, no networking needed)
		var playerWithPrefab = Scene.GetAllComponents<PlayerController>()
			.FirstOrDefault( p => p.RagdollPrefab != null );

		if ( playerWithPrefab?.RagdollPrefab != null )
		{
			var ragdoll = playerWithPrefab.RagdollPrefab.Clone();
			ragdoll.NetworkMode = NetworkMode.Never;
			ragdoll.WorldPosition = deathPosition;
			ragdoll.WorldRotation = deathRotation;

			// Set up renderer
			var ragdollRenderer = ragdoll.Components.Get<SkinnedModelRenderer>();
			if ( ragdollRenderer != null )
			{
				ragdollRenderer.Enabled = true;
				ragdollRenderer.UseAnimGraph = false;

				if ( targetRenderer != null )
				{
					ragdollRenderer.Model = targetRenderer.Model;
					ragdollRenderer.MaterialGroup = targetRenderer.MaterialGroup;
					ragdollRenderer.Tint = targetRenderer.Tint;
				}

				// Clone existing clothing renderers from the target player
				if ( targetRenderer != null )
				{
					int clothingCount = 0;
					foreach ( var child in targetRenderer.GameObject.Children )
					{
						if ( !child.IsValid() || !child.Name.StartsWith( "Clothing" ) ) continue;
						
						var childRenderer = child.Components.Get<SkinnedModelRenderer>();
						if ( childRenderer == null ) continue;
						
						var clothingObj = new GameObject( true, child.Name );
						clothingObj.Parent = ragdollRenderer.GameObject;
						
						var clothingRenderer = clothingObj.Components.Create<SkinnedModelRenderer>();
						clothingRenderer.Model = childRenderer.Model;
						clothingRenderer.BoneMergeTarget = ragdollRenderer;
						clothingRenderer.MaterialGroup = childRenderer.MaterialGroup;
						clothingRenderer.Tint = childRenderer.Tint;
						clothingRenderer.UseAnimGraph = false;
						
						clothingCount++;
					}
				}
			}

			// Physics - keep enabled, copy bones
			var modelPhysics = ragdoll.Components.Get<ModelPhysics>();
			if ( modelPhysics != null && ragdollRenderer != null )
			{
				modelPhysics.Model = ragdollRenderer.Model;
				modelPhysics.Renderer = ragdollRenderer;

				if ( targetRenderer != null )
				{
					modelPhysics.CopyBonesFrom( targetRenderer, true );
				}
			}

			var deadBody = ragdoll.Components.Get<DeadBody>();
			if ( deadBody != null )
			{
				deadBody.VictimName = target.GameObject.Root.Name.Replace( "Player - ", "" );
    			deadBody.VictimRole = target.Role;
			}

			// Only host registers for cleanup
			if ( Networking.IsHost )
			{
				var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
				gameManager?.RegisterDeadBody( ragdoll );
			}
		}

		// NOW ghost the player (after ragdoll has copied their bones)
		if ( target.GameObject.Network.Owner != null )
		{
			target.PlayDeathSoundRpc();
			target.ShowDeathUIRpc();
		}

		// Clear tasks for the killed player
		var taskManager = Scene.GetAllComponents<TaskManager>().FirstOrDefault();
		if ( taskManager != null )
		{
			taskManager.ClearPlayerTasks( target );
		}

		target.BecomeGhostRpc();
	}

	[Rpc.Owner]
	public void ShowDeathUIRpc()
	{
		// Create death UI
		var uiObject = Scene.CreateObject();
		uiObject.Name = "Death UI";
		var deathUI = uiObject.Components.Create<DeathOverlayUI>();
		deathUI.Show();
	}

	[Rpc.Owner]
	public void PlayDeathSoundRpc()
	{
		if ( DeathSound != null )
		{
			var handle = Sound.Play( DeathSound );
			if ( handle != null )
			{
				handle.ListenLocal = true;
				handle.Volume = 1.0f;
			}
		}
	}

	[Rpc.Broadcast]
	public void BecomeGhostRpc()
	{
		// DO NOT disable CharacterController - OnUpdate's !IsAlive check handles movement
		// DO NOT disable Rigidbody
		
		// Hide nametag
		var nametag = GameObject.Components.Get<PlayerNametag>( FindMode.EverythingInSelfAndDescendants );
		if ( nametag != null )
			nametag.Enabled = false;
		
		// Hide ALL renderers
		foreach ( var r in GameObject.Components.GetAll<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
			r.Enabled = false;
		foreach ( var r in GameObject.Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
			r.Enabled = false;
		
		// Disable Dresser
		var dresser = GameObject.Components.Get<Dresser>( FindMode.EverythingInSelfAndDescendants );
		if ( dresser != null )
			dresser.Enabled = false;
		
		// Disable colliders so living players can walk through
		foreach ( var c in GameObject.Components.GetAll<Collider>( FindMode.EverythingInSelfAndDescendants ) )
			c.Enabled = false;
		
		var footstepController = GameObject.Components.Get<Sandbox.PlayerController>();
		if ( footstepController != null )
		{
			footstepController.EnableFootstepSounds = false;
		}

		// Increase ghost movement speed
		footstepController.WalkSpeed = 220f;
		footstepController.RunSpeed = 540f;

		// Clear active task UI and task assignments for dead player
		if ( !IsProxy )
		{
			// Close any open task UI
			TaskProgressBridge.ClearTask();
			
			// Clear task list UI
			TaskListBridge.ClearTasks();
			TaskListBridge.SetShowTasks( false );

			xRayActive = false;
			
			// Clear active task ID
			CurrentActiveTaskId = "";

			if ( ChatSystem.Instance != null )
    			ChatSystem.Instance.ChatEnabled = false;
			
			// Ensure voice is disabled for ghost
			if ( voiceComponent != null )
				voiceComponent.Enabled = false;

			// Destroy only active task minigame UIs (not the task list)
			var taskMinigameNames = new[]
			{
				"Task Button Sequence UI",
				"Task Slider Match UI",
				"Task Collect Samples UI",
				"Task Memory Match UI",
				"Task Decrypt UI",
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

	[Rpc.Broadcast]
	public void RestorePlayerVisuals( Vector3 spawnPosition )
	{
		// Reset ability cooldowns for next game
		lastKillTime = 0f;
		lastPurgeTime = -999f;
		Log.Info( $"[RestoreVisuals] Restoring {PlayerName}, IsProxy: {IsProxy}" );
		
		// Set alive so OnUpdate allows movement
		IsAlive = true;
		Role = PlayerRole.Citizen;
		IsInGame = false;
		IsSpectating = false;
		xRayActive = false;

		// Clean up mimic if active
		mimicActive = false;
		foreach ( var child in GameObject.Children.ToList() )
		{
			if ( child.IsValid() && child.Name == "MimicDisguise" )
			{
				child.Destroy();
			}
		}
		// Restore renderer tint in case mimic was active
		var mimicCheckRenderer = GameObject.Components.GetInDescendants<SkinnedModelRenderer>();
		if ( mimicCheckRenderer != null )
		{
			mimicCheckRenderer.Tint = Color.White;
		}
		if ( !string.IsNullOrEmpty( originalName ) )
		{
			GameObject.Root.Name = originalName;
		}
		
		// Teleport
		GameObject.WorldPosition = spawnPosition;
		
		// Re-enable renderers
		foreach ( var r in GameObject.Components.GetAll<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
			r.Enabled = true;
		foreach ( var r in GameObject.Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
			r.Enabled = true;
		
		// Re-enable Dresser
		var dresser = GameObject.Components.Get<Dresser>( FindMode.EverythingInSelfAndDescendants );
		if ( dresser != null )
			dresser.Enabled = true;
		
		// Re-enable nametag
		var nametag = GameObject.Components.Get<PlayerNametag>( FindMode.EverythingInSelfAndDescendants );
		if ( nametag != null )
			nametag.Enabled = true;
		
		// Re-enable colliders
		foreach ( var c in GameObject.Components.GetAll<Collider>( FindMode.EverythingInSelfAndDescendants ) )
			c.Enabled = true;
		
		var footstepController = GameObject.Components.Get<Sandbox.PlayerController>();
		if ( footstepController != null )
		{
			footstepController.EnableFootstepSounds = true;
		}

		// Reset to normal movement speed
		footstepController.WalkSpeed = 110f;
		footstepController.RunSpeed = 270f;
		
		Log.Info( $"[RestoreVisuals] {PlayerName} restored" );
	}

	[Rpc.Owner]
	public void CleanupAllUIRpc()
	{
		Log.Info( $"[CleanupAllUI] Cleaning up UI for {PlayerName}" );
		
		// Destroy death overlay
		foreach ( var ui in Scene.GetAllComponents<DeathOverlayUI>().ToList() )
		{
			if ( ui != null && ui.IsValid() )
				ui.GameObject.Destroy();
		}
		
		// Destroy voting UI
		foreach ( var ui in Scene.GetAllComponents<VotingUI>().ToList() )
		{
			if ( ui != null && ui.IsValid() )
				ui.GameObject.Destroy();
		}
		
		// Clear tasks
		TaskListBridge.ClearTasks();
		TaskListBridge.SetShowTasks( false );
		CurrentActiveTaskId = "";
		
		// Hide anomaly UI
		if ( anomalyUI != null && anomalyUI.IsValid() )
		{
			anomalyUI.GameObject.Destroy();
			anomalyUI = null;
		}
		
		Log.Info( $"[CleanupAllUI] All UI cleaned for {PlayerName}" );
	}

	// Helper to get all descendant GameObjects
	private List<GameObject> GetAllDescendants( GameObject obj )
	{
		var descendants = new List<GameObject>();
		foreach ( var child in obj.Children )
		{
			descendants.Add( child );
			descendants.AddRange( GetAllDescendants( child ) );
		}
		return descendants;
	}

	// Helper method to recursively disable all colliders
	private void DisableCollidersRecursive( GameObject obj )
	{
		foreach ( var child in obj.Children )
		{
			var colliders = child.Components.GetAll<Collider>();
			foreach ( var collider in colliders )
			{
				collider.Enabled = false;
			}
			
			// Continue recursively
			DisableCollidersRecursive( child );
		}
	}

	// Helper method to recursively enable all colliders
	private void EnableCollidersRecursive( GameObject obj )
	{
		foreach ( var child in obj.Children )
		{
			var colliders = child.Components.GetAll<Collider>();
			foreach ( var collider in colliders )
			{
				collider.Enabled = true;
			}
			
			// Continue recursively
			EnableCollidersRecursive( child );
		}
	}

	[Rpc.Broadcast]
	private void ExecutePurgeRpc( string abilityId )
	{
		Log.Info( $"[ExecutePurgeRpc] Running on {(Networking.IsHost ? "HOST" : "CLIENT")}, abilityId: '{abilityId}'" );

		var allPlayers = Scene.GetAllComponents<PlayerController>();

		foreach ( var player in allPlayers )
		{
			if ( player.GameObject.Network.Owner == null || !player.IsInGame )
				continue;
			
			if ( player.Role == PlayerRole.Anomaly )
			{
				// Mimic shows its own UI with target name from StartMimicEffect
				if ( abilityId != "mimic" )
				{
					player.ShowPurgeActivatedRpc( abilityId );
				}
			}
			else if ( player.Role == PlayerRole.Citizen && player.IsAlive )
			{
				if ( abilityId == "blind" )
				{
					player.BlindPlayerRpc();
				}
			}
		}

		// Double Kill: reset the anomaly's kill cooldown
		if ( abilityId == "doublekill" )
		{
			var localPlayer = Scene.GetAllComponents<PlayerController>()
				.FirstOrDefault( p => !p.IsProxy );
			
			if ( localPlayer != null && localPlayer.Role == PlayerRole.Anomaly )
			{
				localPlayer.ResetKillCooldown();
			}
		}

		// X-ray: only the anomaly sees outlines (local effect only)
		if ( abilityId == "xray" )
		{
			var localPlayer = Scene.GetAllComponents<PlayerController>()
				.FirstOrDefault( p => !p.IsProxy );
			
			if ( localPlayer != null && localPlayer.Role == PlayerRole.Anomaly )
			{
				localPlayer.StartXRayEffect();
			}
		}

		// Vanish: teleport the anomaly to a random vanish spawn
		if ( abilityId == "vanish" )
		{
			var localPlayer = Scene.GetAllComponents<PlayerController>()
				.FirstOrDefault( p => !p.IsProxy );
			
			if ( localPlayer != null && localPlayer.Role == PlayerRole.Anomaly )
			{
				localPlayer.ActivateVanish();
			}
		}

		// Mimic: anomaly copies a random citizen's appearance
		if ( abilityId == "mimic" )
		{
			var localPlayer = Scene.GetAllComponents<PlayerController>()
				.FirstOrDefault( p => !p.IsProxy );
			
			if ( localPlayer != null && localPlayer.Role == PlayerRole.Anomaly )
			{
				localPlayer.StartMimicEffect();
			}
		}
	}

	[Rpc.Owner]
	private void ShowPurgeActivatedRpc( string abilityId, string targetName = "" )
	{
		if ( PurgeActivateSound != null )
		{
			var handle = Sound.Play( PurgeActivateSound );
			if ( handle != null )
			{
				handle.ListenLocal = true;
				handle.Volume = 0.8f;
			}
		}
		
		var uiObject = Scene.CreateObject();
		uiObject.Name = "Purge Activated UI";
		var purgeUI = uiObject.Components.Create<PurgeActivatedUI>();
		purgeUI.Show( abilityId );
	}

	[Rpc.Owner]
	private void BlindPlayerRpc()
	{
		isBlinded = true;
		
		// Play blinded sound
		if ( BlindedSound != null )
		{
			var handle = Sound.Play( BlindedSound );
			if ( handle != null )
			{
				handle.ListenLocal = true;
				handle.Volume = 0.1f;
			}
		}
		
		// Create blind overlay UI
		var uiObject = Scene.CreateObject();
		uiObject.Name = "Blind Overlay UI";
		var blindUI = uiObject.Components.Create<BlindOverlayUI>();
		blindUI.StartBlind( PurgeDuration );
		
		// Auto-remove blind after duration
		RemoveBlindAfterDelay();
	}

	private void DrawXRayMarkers()
	{
		var citizens = Scene.GetAllComponents<PlayerController>()
			.Where( p => p != this && p.IsAlive && p.IsInGame && p.Role == PlayerRole.Citizen )
			.ToList();

		if ( citizens.Count == 0 )
		{
			Log.Info( "[X-Ray] No citizens found to highlight!" );
			return;
		}
		foreach ( var citizen in citizens )
		{
			Vector3 targetPos = citizen.WorldPosition + Vector3.Up * 80;
			float distance = Vector3.DistanceBetween( WorldPosition, citizen.WorldPosition );
			string distText = distance >= 1000 ? $"{(distance / 1000f):F1}km" : $"{(int)distance}m";
			string displayName = citizen.GameObject.Root.Name.Replace( "Player - ", "" );

			// Red diamond marker above player head
			Gizmo.Draw.Color = new Color( 1f, 0.15f, 0.15f, 0.9f );
			Gizmo.Draw.SolidSphere( targetPos, 8f );

			// Player name
			Gizmo.Draw.Color = new Color( 1f, 0.3f, 0.3f, 0.85f );
			Gizmo.Draw.Text( displayName, new Transform( targetPos + Vector3.Up * 20 ), "Consolas", 16 );

			// Distance
			Gizmo.Draw.Color = new Color( 1f, 0.2f, 0.2f, 0.6f );
			Gizmo.Draw.Text( distText, new Transform( targetPos + Vector3.Up * 5 ), "Consolas", 12 );

			// Vertical line from ground to marker
			Gizmo.Draw.Color = new Color( 1f, 0.1f, 0.1f, 0.3f );
			Gizmo.Draw.Line( citizen.WorldPosition, targetPos );
		}
	}

	public void StartXRayEffect()
	{
		xRayActive = true;
		xRayEndTime = Time.Now + XRayDuration;
		Log.Info( $"[X-Ray] Started, duration: {XRayDuration}s" );
	}

	public void ResetKillCooldown()
	{
		lastKillTime = 0f;
		
		if ( anomalyUI != null && anomalyUI.IsValid() )
		{
			anomalyUI.SetKillCooldown( KillCooldown, 0f );
		}
		
		Log.Info( "[DoubleKill] Kill cooldown reset!" );
	}

	public void ActivateVanish()
	{
		var vanishSpawns = Scene.GetAllObjects( true )
			.Where( obj => obj.Tags != null && obj.Tags.Has( "vanish" ) )
			.ToList();

		if ( vanishSpawns.Count == 0 )
		{
			Log.Warning( "[Vanish] No vanish spawn points found!" );
			return;
		}

		var targetSpawn = vanishSpawns[Game.Random.Int( 0, vanishSpawns.Count - 1 )];
		
		// Teleport via broadcast so all clients see it
		VanishTeleportRpc( targetSpawn.WorldPosition );
		
		Log.Info( $"[Vanish] Teleported to vanish spawn at {targetSpawn.WorldPosition}" );
	}

	[Rpc.Broadcast]
	private void VanishTeleportRpc( Vector3 position )
	{
		GameObject.WorldPosition = position;
	}

	public void StartMimicEffect()
	{
		var myOwner = GameObject.Network.Owner;
		if ( myOwner == null ) return;

		var citizens = Scene.GetAllComponents<PlayerController>()
			.Where( p => p.IsAlive && p.IsInGame && p.Role == PlayerRole.Citizen )
			.Where( p => p.GameObject.Network.Owner != null && p.GameObject.Network.Owner.SteamId != myOwner.SteamId )
			.ToList();

		if ( citizens.Count == 0 )
		{
			Log.Warning( "[Mimic] No alive citizens to mimic!" );
			return;
		}

		var target = citizens[Game.Random.Int( 0, citizens.Count - 1 )];
		ulong targetSteamId = target.GameObject.Network.Owner?.SteamId ?? 0;
		string targetName = target.GameObject.Root.Name;

		originalName = GameObject.Root.Name;

		ApplyMimicRpc( targetSteamId, targetName );

		mimicActive = true;
		mimicEndTime = Time.Now + MimicDuration;

		// Show mimic-specific UI with target name
		string mimicDisplayName = targetName.Replace( "Player - ", "" );
		ShowMimicActivatedUI( mimicDisplayName );

		Log.Info( $"[Mimic] Now disguised as {target.PlayerName} for {MimicDuration}s" );
	}

	private void ShowMimicActivatedUI( string targetDisplayName )
	{
		if ( PurgeActivateSound != null )
		{
			var handle = Sound.Play( PurgeActivateSound );
			if ( handle != null )
			{
				handle.ListenLocal = true;
				handle.Volume = 0.8f;
			}
		}

		var uiObject = Scene.CreateObject();
		uiObject.Name = "Purge Activated UI";
		var purgeUI = uiObject.Components.Create<PurgeActivatedUI>();
		purgeUI.ShowMimic( targetDisplayName );
	}

	[Rpc.Broadcast]
	private void ApplyMimicRpc( ulong targetSteamId, string targetRootName )
	{
		var target = Scene.GetAllComponents<PlayerController>()
			.FirstOrDefault( p => p.GameObject.Network.Owner != null 
				&& p.GameObject.Network.Owner.SteamId == targetSteamId );

		if ( target == null )
		{
			Log.Warning( "[Mimic] Could not find target player to mimic" );
			return;
		}

		if ( string.IsNullOrEmpty( originalName ) )
			originalName = GameObject.Root.Name;

		var myRenderer = GameObject.Components.GetInDescendants<SkinnedModelRenderer>();

		// Hide all anomaly renderers on all clients
		foreach ( var r in GameObject.Components.GetAll<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
			r.Enabled = false;
		foreach ( var r in GameObject.Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
			r.Enabled = false;

		// Re-enable the main renderer as transparent (keeps animations running)
		if ( myRenderer != null )
		{
			myRenderer.Enabled = true;
			myRenderer.Tint = Color.Transparent;
		}

		// Only create visible mimic model on OTHER clients (not the anomaly's own screen)
		if ( IsProxy )
		{
			var mimicContainer = new GameObject( true, "MimicDisguise" );
			mimicContainer.Parent = GameObject;
			mimicContainer.LocalPosition = Vector3.Zero;
			mimicContainer.LocalRotation = Rotation.Identity;

			var targetRenderer = target.GameObject.Components.GetInDescendants<SkinnedModelRenderer>();
			if ( targetRenderer != null && myRenderer != null )
			{
				var mimicRenderer = mimicContainer.Components.Create<SkinnedModelRenderer>();
				mimicRenderer.Model = targetRenderer.Model;
				mimicRenderer.MaterialGroup = targetRenderer.MaterialGroup;
				mimicRenderer.Tint = targetRenderer.Tint;
				mimicRenderer.BoneMergeTarget = myRenderer;

				foreach ( var child in targetRenderer.GameObject.Children )
				{
					if ( !child.IsValid() || !child.Name.StartsWith( "Clothing" ) ) continue;
					var childRenderer = child.Components.Get<SkinnedModelRenderer>();
					if ( childRenderer == null ) continue;

					var clothingObj = new GameObject( true, "Clothing_Mimic" );
					clothingObj.Parent = mimicContainer;

					var clothingRenderer = clothingObj.Components.Create<SkinnedModelRenderer>();
					clothingRenderer.Model = childRenderer.Model;
					clothingRenderer.BoneMergeTarget = mimicRenderer;
					clothingRenderer.MaterialGroup = childRenderer.MaterialGroup;
					clothingRenderer.Tint = childRenderer.Tint;
				}
			}
		}

		// Change nametag name on all clients
		GameObject.Root.Name = targetRootName;

		Log.Info( $"[Mimic] Applied disguise as {targetRootName} on {(Networking.IsHost ? "HOST" : "CLIENT")}, IsProxy: {IsProxy}" );
	}

	[Rpc.Broadcast]
	public void RemoveMimicRpc()
	{
		mimicActive = false;

		// Destroy the mimic container
		foreach ( var child in GameObject.Children.ToList() )
		{
			if ( child.IsValid() && child.Name == "MimicDisguise" )
			{
				child.Destroy();
			}
		}

		// Only restore renderer visibility if the player is alive
		if ( IsAlive && !IsSpectating )
		{
			var myRenderer = GameObject.Components.GetInDescendants<SkinnedModelRenderer>();
			if ( myRenderer != null )
			{
				myRenderer.Tint = Color.White;
			}

			foreach ( var r in GameObject.Components.GetAll<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
				r.Enabled = true;
			foreach ( var r in GameObject.Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
				r.Enabled = true;
		}

		// Restore original name
		if ( !string.IsNullOrEmpty( originalName ) )
		{
			GameObject.Root.Name = originalName;
		}

		Log.Info( $"[Mimic] Disguise removed on {(Networking.IsHost ? "HOST" : "CLIENT")}, IsAlive: {IsAlive}" );
	}

	[Rpc.Owner]
	public void ForceAbilityCooldownRpc( float duration )
	{
		lastKillTime = Time.Now - KillCooldown + duration;

		if ( anomalyUI != null && anomalyUI.IsValid() )
		{
			anomalyUI.SetKillCooldown( duration, Time.Now );
		}
	}

	[Rpc.Owner]
	public void ShowAnomalyAbilitiesRpc()
	{
		if ( Role != PlayerRole.Anomaly )
			return;

		if ( anomalyUI != null && anomalyUI.IsValid() )
			return;
		
		// Sync equipped ability from progression bridge
		EquippedPurgeAbility = PurgeProgressionBridge.EquippedAbilityId;
		originalName = GameObject.Root.Name;
		
		var uiObject = Scene.CreateObject();
		uiObject.Name = "Anomaly Abilities UI";
		anomalyUI = uiObject.Components.Create<AnomalyAbilitiesUI>();
		anomalyUI.SetPurgeCooldown( GetPurgeCooldownForAbility(), lastPurgeTime );
		anomalyUI.SetKillCooldown( KillCooldown, lastKillTime );
	}

	[Rpc.Owner]
	public void HideAnomalyAbilitiesRpc()
	{
		if ( anomalyUI != null && anomalyUI.IsValid() )
		{
			anomalyUI.GameObject.Destroy();
			anomalyUI = null;
		}
	}

	private async void RemoveBlindAfterDelay()
	{
		await GameTask.DelaySeconds( PurgeDuration );
		isBlinded = false;
	}

	[Rpc.Owner]
	public void EndBlindEffectRpc()
	{
		isBlinded = false;
		
		// Find and destroy blind overlay
		var blindUI = Scene.GetAllComponents<BlindOverlayUI>().FirstOrDefault();
		if ( blindUI != null )
		{
			blindUI.GameObject.Destroy();
		}
	}
}