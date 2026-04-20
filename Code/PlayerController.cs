using Sandbox;
using System.Collections.Generic;
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
	[Property, Sync] public string EquippedPerkId { get; set; } = "";
	[Property] public GameObject RagdollPrefab { get; set; }
	[Property] public GameObject TrapPrefab { get; set; }
	[Property] public float XRayDuration { get; set; } = 20f;
	[Property] public float VanishCooldown { get; set; } = 90f;

	// Round tracking for currency
	[Sync] public int RoundKills { get; set; } = 0;
	public int RoundTasksCompleted { get; set; } = 0;
	public int RoundCorrectVotes { get; set; } = 0;
	public bool RoundWon { get; set; } = false;
	public PlayerRole RoundRole { get; set; } = PlayerRole.Citizen;

	// Saved credit data from host
	private int savedKills, savedKillCredits;
	private int savedTasks, savedTaskCredits;
	private int savedVotes, savedVoteCredits;
	private bool savedWon;
	private int savedWinCredits, savedTotalCredits;
	private bool hasPendingCredits = false;
	
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
	[Property] public SoundEvent PerkActivateSound { get; set; }
	[Property] public SoundEvent DailySpinSound { get; set; }
	[Property] public SoundEvent DailyRewardSound { get; set; }
	[Property] public float MimicDuration { get; set; } = 15f;
	
	public string LastKillVictimName { get; set; } = "";

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

	// Perk System
	private bool perkActive = false;
	private float perkEndTime = 0f;
	private string activePerkId = "";
	private float originalWalkSpeed = 0f;
	private float originalRunSpeed = 0f;
	private RoleRevealTag roleRevealTag = null;
	private bool ironWillResistedThisRound = false;
	private List<(string Name, Vector3 Position)> lastKnownSnapshots = new();
	private PlayerController trackerTagTarget = null;

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

	// Blackjack Table Mount System
	private BlackjackTable mountedBlackjackTable = null;

	// Poker Table Mount System
	private PokerTable mountedPokerTable = null;
	
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

			// Check for daily login bonus
			CheckDailyBonus();
		}
	}

	private async void CheckDailyBonus()
	{
		// Wait for the game to fully load and for the stats service
		// to fetch this player's current values from the backend.
		await GameTask.DelaySeconds( 5f );

		if ( await DailyBonusTracker.HasClaimedTodayAsync() )
			return;

		// Pass sounds to the spinner via bridge
		DailyBonusBridge.SpinSound = DailySpinSound;
		DailyBonusBridge.RewardSound = DailyRewardSound;

		// Show the daily spinner UI
		var uiObject = Scene.CreateObject();
		uiObject.Name = "Daily Spinner UI";
		var screenPanel = uiObject.Components.Create<ScreenPanel>();
		screenPanel.ZIndex = 950;
		uiObject.Components.Create<DailySpinnerUI>();
		Log.Info( "[DailyBonus] Showing daily spinner" );
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

		// Draw Tracker Tag marker if target is alive
		if ( trackerTagTarget != null )
		{
			if ( trackerTagTarget.IsValid() && trackerTagTarget.IsAlive && trackerTagTarget.IsInGame )
			{
				DrawTrackerTagMarker();
			}
			else
			{
				trackerTagTarget = null;
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

		// Draw Last Known markers while perk is active
		if ( perkActive && activePerkId == "last_known" )
		{
			DrawLastKnownMarkers();
		}

		// Check perk timer
		if ( perkActive && Time.Now >= perkEndTime )
		{
			EndPerkEffect();
		}

		// Dead players can't move
		if ( !IsAlive )
		{
			return;
		}

		// If mounted to camera station or blackjack table, handle accordingly
		if ( isMountedToStation )
		{
			if ( mountedBlackjackTable != null )
			{
				// E key handled by BlackjackTable.OnUpdate (avoids same-frame double trigger)
				// All other blackjack input handled via UI buttons
				return;
			}

			if ( mountedPokerTable != null )
			{
				// E key handled by PokerTable.OnUpdate
				return;
			}

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

		// Handle G key press for Perk activation
		if ( Input.Pressed( "Drop" ) ) // G key
		{
			AttemptPerkActivation();
		}

		// Update perk bridge HUD state (after input handling so activation is reflected immediately)
		PerkBridge.IsPerkActive = perkActive;
		PerkBridge.PerkTimeRemaining = perkActive ? System.Math.Max( 0f, perkEndTime - Time.Now ) : 0f;
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

			if ( perkHudUI != null && perkHudUI.IsValid() )
			{
				perkHudUI.GameObject.Destroy();
				perkHudUI = null;
			}

			if ( silenceTargetUI != null && silenceTargetUI.IsValid() )
			{
				silenceTargetUI.GameObject.Destroy();
				silenceTargetUI = null;
			}

			PerkBridge.SilenceUIOpen = false;
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

	public void MountToBlackjackTable( BlackjackTable table )
	{
		mountedBlackjackTable = table;
		isMountedToStation = true;
		Log.Info( $"[PlayerController] {PlayerName} sat at blackjack table" );
	}

	public void MountToPokerTable( PokerTable table )
	{
		mountedPokerTable = table;
		isMountedToStation = true;
		Log.Info( $"[PlayerController] {PlayerName} sat at poker table" );
	}

	public void UnmountFromStation()
	{
		mountedStation = null;
		mountedBlackjackTable = null;
		mountedPokerTable = null;
		isMountedToStation = false;
		Log.Info( $"[PlayerController] {PlayerName} unmounted from station" );
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
				// Shield perk: block the kill but still trigger cooldown
				if ( nearbyPlayers.EquippedPerkId == "shield" )
				{
					lastKillTime = Time.Now;

					if ( anomalyUI != null && anomalyUI.IsValid() )
					{
						anomalyUI.SetKillCooldown( KillCooldown, lastKillTime );
					}

					// Notify both players
					nearbyPlayers.ShieldBlockedRpc();
					ShowShieldBlockedAnomalyRpc();

					// Consume the target's shield perk
					nearbyPlayers.ConsumeShieldPerkRpc();

					Log.Info( "[Shield] Kill blocked by Shield perk!" );
					return false;
				}

				KillPlayer( nearbyPlayers );
				lastKillTime = Time.Now;

				if ( anomalyUI != null && anomalyUI.IsValid() )
				{
					anomalyUI.SetKillCooldown( KillCooldown, lastKillTime );
				}

				Log.Info( "Kill successful!" );
				RoundKills++;
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

	// === PERK SYSTEM ===

	private void AttemptPerkActivation()
	{
		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if ( gameManager == null ) return;

		// Must have a perk equipped and not already used
		if ( PerkBridge.PerkUsedThisRound || !PerkBridge.HasPerkEquipped() )
			return;

		var perk = PerkBridge.GetEquippedPerk();
		if ( perk == null || perk.Activation != PerkActivation.Active )
			return;

		// Role check
		if ( perk.Role == PerkRole.CitizenOnly && Role != PlayerRole.Citizen )
			return;
		if ( perk.Role == PerkRole.AnomalyOnly && Role != PlayerRole.Anomaly )
			return;

		// Silence is the only perk that's used during a meeting — all others require InGame.
		if ( perk.Id == "silence" )
		{
			if ( gameManager.CurrentState != GameManager.GameState.Voting ) return;
			if ( !IsAlive || !IsInGame ) return;
			// Toggle the target-selection UI. Perk is not charged until a target is actually picked.
			PerkBridge.SilenceUIOpen = !PerkBridge.SilenceUIOpen;
			return;
		}

		if ( gameManager.CurrentState != GameManager.GameState.InGame )
			return;

		if ( !IsAlive || !IsInGame )
			return;

		// Quick Fix requires an assigned task
		if ( perk.Id == "quick_fix" )
		{
			if ( string.IsNullOrEmpty( CurrentActiveTaskId ) )
			{
				Log.Info( "[Perk] Quick Fix failed - no active task" );
				return;
			}
		}

		// Tracker Tag requires a nearby player in range
		if ( perk.Id == "tracker_tag" )
		{
			var nearestTarget = Scene.GetAllComponents<PlayerController>()
				.Where( p => p != this && p.IsAlive && p.IsInGame )
				.OrderBy( p => Vector3.DistanceBetween( WorldPosition, p.WorldPosition ) )
				.FirstOrDefault();

			if ( nearestTarget == null || Vector3.DistanceBetween( WorldPosition, nearestTarget.WorldPosition ) > KillRange )
			{
				Log.Info( "[Perk] Tracker Tag failed - no player in range" );
				return;
			}
		}

		// Mark perk as used and charge credits
		PerkBridge.MarkPerkUsed();
		activePerkId = perk.Id;
		Sandbox.Services.Stats.Increment( "credits_spent", perk.Cost );
		Log.Info( $"[Perk] Activated: {perk.Name} (charged {perk.Cost} credits)" );

		// Play perk activation sound
		if ( PerkActivateSound != null )
		{
			var handle = Sound.Play( PerkActivateSound );
		}

		// Execute the perk
		switch ( perk.Id )
		{
			case "quiet_steps":
				ActivateQuietSteps();
				break;
			case "speed_boost":
				ActivateSpeedBoost();
				break;
			case "quick_fix":
				ActivateQuickFix();
				break;
			case "emergency_recall":
				ActivateEmergencyRecall();
				break;
			case "last_known":
				ActivateLastKnown();
				break;
			case "surge":
				ActivateSurge();
				break;
			case "tracker_tag":
				ActivateTrackerTag();
				break;
			case "phantom_cloak":
				ActivatePhantomCloak();
				break;
		}

		// Unequip the perk so it won't be charged again next round
		PerkBridge.UnequipPerk();
		EquippedPerkId = "";
	}

	private void ActivateQuietSteps()
	{
		// Broadcast to all clients so nobody hears our footsteps
		SetQuietStepsRpc( true );

		perkActive = true;
		perkEndTime = Time.Now + 30f;
		Log.Info( "[Perk] Quiet Steps active for 30 seconds" );
	}

	[Rpc.Broadcast]
	private void SetQuietStepsRpc( bool silent )
	{
		var footstepController = GameObject.Components.Get<Sandbox.PlayerController>();
		if ( footstepController != null )
		{
			footstepController.EnableFootstepSounds = !silent;
		}
	}

	private void ActivateSpeedBoost()
	{
		var footstepController = GameObject.Components.Get<Sandbox.PlayerController>();
		if ( footstepController != null )
		{
			originalWalkSpeed = footstepController.WalkSpeed;
			originalRunSpeed = footstepController.RunSpeed;
			footstepController.WalkSpeed = originalWalkSpeed * 1.5f;
			footstepController.RunSpeed = originalRunSpeed * 1.5f;
		}

		perkActive = true;
		perkEndTime = Time.Now + 15f;
		Log.Info( "[Perk] Speed Boost active for 15 seconds" );
	}

	private void ActivateQuickFix()
	{
		if ( string.IsNullOrEmpty( CurrentActiveTaskId ) ) return;

		// Get our own network owner ID
		ulong ownerId = GameObject.Network?.Owner?.SteamId ?? 0;
		if ( ownerId == 0 ) return;

		string taskId = CurrentActiveTaskId;

		// Complete the task via the same host-authoritative path
		var taskManager = Scene.GetAllComponents<TaskManager>().FirstOrDefault();
		if ( taskManager != null )
		{
			taskManager.CompleteTaskByNetworkId( ownerId, taskId );
		}

		// Clear any open task minigame UI
		TaskProgressBridge.ClearTask();

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

		Log.Info( $"[Perk] Quick Fix - instantly completed task: {taskId}" );
	}

	private void ActivateEmergencyRecall()
	{
		var recallSpawns = Scene.GetAllObjects( true )
			.Where( obj => obj.Tags != null && obj.Tags.Has( "emergencyrecall" ) )
			.ToList();

		if ( recallSpawns.Count == 0 )
		{
			Log.Warning( "[Perk] Emergency Recall failed - no emergencyrecall spawn found!" );
			return;
		}

		var spawn = recallSpawns[Game.Random.Int( 0, recallSpawns.Count - 1 )];
		GameObject.WorldPosition = spawn.WorldPosition;
		Log.Info( "[Perk] Emergency Recall - teleported to emergency recall point" );
	}

	private void ActivateLastKnown()
	{
		// Snapshot all other alive players' current positions
		lastKnownSnapshots.Clear();

		var players = Scene.GetAllComponents<PlayerController>()
			.Where( p => p != this && p.IsAlive && p.IsInGame )
			.ToList();

		foreach ( var player in players )
		{
			string displayName = player.GameObject.Root.Name.Replace( "Player - ", "" );
			lastKnownSnapshots.Add( (displayName, player.WorldPosition) );
		}

		perkActive = true;
		perkEndTime = Time.Now + 5f;
		Log.Info( $"[Perk] Last Known active for 5 seconds - captured {lastKnownSnapshots.Count} positions" );
	}

	private void DrawLastKnownMarkers()
	{
		foreach ( var snapshot in lastKnownSnapshots )
		{
			Vector3 markerPos = snapshot.Position + Vector3.Up * 80;
			float distance = Vector3.DistanceBetween( WorldPosition, snapshot.Position );
			string distText = distance >= 1000 ? $"{(distance / 1000f):F1}km" : $"{(int)distance}m";

			// Yellow diamond marker above last known position
			Gizmo.Draw.Color = new Color( 1f, 0.85f, 0.15f, 0.9f );
			Gizmo.Draw.SolidSphere( markerPos, 8f );

			// Player name
			Gizmo.Draw.Color = new Color( 1f, 0.9f, 0.3f, 0.85f );
			Gizmo.Draw.Text( snapshot.Name, new Transform( markerPos + Vector3.Up * 20 ), "Consolas", 16 );

			// Distance
			Gizmo.Draw.Color = new Color( 1f, 0.85f, 0.2f, 0.6f );
			Gizmo.Draw.Text( distText, new Transform( markerPos + Vector3.Up * 5 ), "Consolas", 12 );

			// Vertical line from ground to marker
			Gizmo.Draw.Color = new Color( 1f, 0.8f, 0.1f, 0.3f );
			Gizmo.Draw.Line( snapshot.Position, markerPos );
		}
	}

	private void ActivateSurge()
	{
		lastPurgeTime = -999f;

		if ( anomalyUI != null && anomalyUI.IsValid() )
		{
			anomalyUI.SetPurgeCooldown( GetPurgeCooldownForAbility(), lastPurgeTime );
		}

		Log.Info( "[Perk] Surge - purge cooldown reset!" );
	}

	private void ActivateTrackerTag()
	{
		var nearestTarget = Scene.GetAllComponents<PlayerController>()
			.Where( p => p != this && p.IsAlive && p.IsInGame )
			.OrderBy( p => Vector3.DistanceBetween( WorldPosition, p.WorldPosition ) )
			.FirstOrDefault();

		if ( nearestTarget == null || Vector3.DistanceBetween( WorldPosition, nearestTarget.WorldPosition ) > KillRange )
			return;

		trackerTagTarget = nearestTarget;
		string targetName = nearestTarget.GameObject.Root.Name.Replace( "Player - ", "" );
		Log.Info( $"[Perk] Tracker Tag - tagged {targetName} for the rest of the round" );
	}

	private void DrawTrackerTagMarker()
	{
		if ( trackerTagTarget == null || !trackerTagTarget.IsValid() ) return;

		Vector3 targetPos = trackerTagTarget.WorldPosition + Vector3.Up * 80;
		float distance = Vector3.DistanceBetween( WorldPosition, trackerTagTarget.WorldPosition );
		string distText = distance >= 1000 ? $"{(distance / 1000f):F1}km" : $"{(int)distance}m";
		string displayName = trackerTagTarget.GameObject.Root.Name.Replace( "Player - ", "" );

		// Cyan diamond marker above player head
		Gizmo.Draw.Color = new Color( 0.2f, 0.9f, 1f, 0.9f );
		Gizmo.Draw.SolidSphere( targetPos, 8f );

		// Player name
		Gizmo.Draw.Color = new Color( 0.3f, 0.95f, 1f, 0.85f );
		Gizmo.Draw.Text( displayName, new Transform( targetPos + Vector3.Up * 20 ), "Consolas", 16 );

		// Distance
		Gizmo.Draw.Color = new Color( 0.2f, 0.85f, 1f, 0.6f );
		Gizmo.Draw.Text( distText, new Transform( targetPos + Vector3.Up * 5 ), "Consolas", 12 );

		// Vertical line from ground to marker
		Gizmo.Draw.Color = new Color( 0.1f, 0.8f, 1f, 0.3f );
		Gizmo.Draw.Line( trackerTagTarget.WorldPosition, targetPos );
	}

	private void ActivatePhantomCloak()
	{
		SetPhantomCloakRpc( true );

		perkActive = true;
		perkEndTime = Time.Now + 30f;
		Log.Info( "[Perk] Phantom Cloak active for 30 seconds" );
	}

	[Rpc.Broadcast]
	private void SetPhantomCloakRpc( bool invisible )
	{
		// The owner can still see themselves
		if ( !IsProxy ) return;

		foreach ( var r in GameObject.Components.GetAll<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
			r.Enabled = !invisible;
		foreach ( var r in GameObject.Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
			r.Enabled = !invisible;

		var nametag = GameObject.Components.Get<PlayerNametag>( FindMode.EverythingInSelfAndDescendants );
		if ( nametag != null )
			nametag.Enabled = !invisible;
	}

	public float GetPerkTimeRemaining()
	{
		if ( !perkActive ) return 0f;
		return System.Math.Max( 0f, perkEndTime - Time.Now );
	}

	private void EndPerkEffect()
	{
		if ( !perkActive ) return;
		perkActive = false;

		switch ( activePerkId )
		{
			case "quiet_steps":
				SetQuietStepsRpc( false );
				Log.Info( "[Perk] Quiet Steps ended" );
				break;

			case "speed_boost":
				var speedCtrl = GameObject.Components.Get<Sandbox.PlayerController>();
				if ( speedCtrl != null )
				{
					speedCtrl.WalkSpeed = originalWalkSpeed;
					speedCtrl.RunSpeed = originalRunSpeed;
				}
				Log.Info( "[Perk] Speed Boost ended" );
				break;

			case "last_known":
				lastKnownSnapshots.Clear();
				Log.Info( "[Perk] Last Known ended" );
				break;

			case "phantom_cloak":
				SetPhantomCloakRpc( false );
				Log.Info( "[Perk] Phantom Cloak ended" );
				break;
		}

		activePerkId = "";
	}

	[Rpc.Owner]
	public void CleanupActivePerksForMeeting()
	{
		// End the active perk effect (restores visuals, speed, sounds, etc.)
		if ( perkActive )
		{
			EndPerkEffect();
		}
		perkActive = false;
		activePerkId = "";
		xRayActive = false;

		PerkBridge.IsPerkActive = false;
		PerkBridge.PerkTimeRemaining = 0f;

		// If a timed perk was equipped (active type, not passive), the meeting consumed it.
		// Destroy the HUD since the perk is gone.
		var equippedPerk = PerkRegistry.GetById( EquippedPerkId );
		if ( equippedPerk != null && equippedPerk.Activation == PerkActivation.Active && PerkBridge.PerkUsedThisRound )
		{
			if ( perkHudUI != null && perkHudUI.IsValid() )
			{
				perkHudUI.GameObject.Destroy();
				perkHudUI = null;
			}
			PerkBridge.ActivePerkName = "";
		}
	}

	// Called by SilenceTargetUI when the anomaly clicks a citizen.
	// The perk is charged here — not on G press — so skipping never consumes it.
	public void CommitSilenceOnTarget( ulong targetSteamId )
	{
		if ( Role != PlayerRole.Anomaly ) return;
		if ( PerkBridge.PerkUsedThisRound ) return;
		if ( PerkBridge.EquippedPerkId != "silence" ) return;

		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if ( gameManager == null || gameManager.CurrentState != GameManager.GameState.Voting )
			return;

		var silencePerk = PerkRegistry.GetById( "silence" );
		if ( silencePerk == null ) return;

		// Ask host to apply the silence. Host validates and broadcasts.
		ulong callerSteamId = GameObject.Network?.Owner?.SteamId ?? 0;
		if ( callerSteamId == 0 ) return;
		gameManager.RequestSilencePlayerRpc( callerSteamId, targetSteamId );

		// Charge credits and consume the perk locally.
		PerkBridge.MarkPerkUsed();
		activePerkId = "silence";
		Sandbox.Services.Stats.Increment( "credits_spent", silencePerk.Cost );
		PerkBridge.UnequipPerk();
		EquippedPerkId = "";
		PerkBridge.SilenceUIOpen = false;

		if ( PerkActivateSound != null )
			Sound.Play( PerkActivateSound );

		Log.Info( $"[Perk] Silence committed on SteamId {targetSteamId}" );
	}

	[Rpc.Owner]
	public void ActivateRevealPerkRpc( PlayerController revealedPlayer, bool isAnomaly )
	{
		// Only process if we have Reveal equipped and haven't used it yet
		if ( PerkBridge.PerkUsedThisRound || PerkBridge.EquippedPerkId != "reveal" )
			return;

		var revealPerk = PerkRegistry.GetById( "reveal" );
		if ( revealPerk == null ) return;

		// Mark perk as used and charge credits
		PerkBridge.MarkPerkUsed();
		Sandbox.Services.Stats.Increment( "credits_spent", revealPerk.Cost );
		PerkBridge.UnequipPerk();
		EquippedPerkId = "";

		if ( revealedPlayer == null || !revealedPlayer.IsValid() ) return;

		// Create the role reveal tag above the revealed player's head (local only)
		var tagObj = Scene.CreateObject();
		tagObj.Name = "Role Reveal Tag";
		var worldPanel = tagObj.Components.Create<WorldPanel>();
		worldPanel.PanelSize = new Vector2( 800, 100 );
		worldPanel.RenderScale = 2f;
		roleRevealTag = tagObj.Components.Create<RoleRevealTag>();
		roleRevealTag.TargetPlayer = revealedPlayer.GameObject;
		roleRevealTag.IsAnomaly = isAnomaly;

		// Play perk activation sound
		if ( PerkActivateSound != null )
		{
			var handle = Sound.Play( PerkActivateSound );
		}

		Log.Info( $"[Perk] Reveal activated - {revealedPlayer.PlayerName} is {(isAnomaly ? "ANOMALY" : "CITIZEN")}" );
	}

	public void CleanupRevealTag()
	{
		if ( roleRevealTag != null && roleRevealTag.IsValid() )
		{
			roleRevealTag.GameObject.Destroy();
			roleRevealTag = null;
		}
	}

	[Rpc.Owner]
	public void ShieldBlockedRpc()
	{
		var uiObject = Scene.CreateObject();
		uiObject.Name = "Shield Notification UI";
		var notification = uiObject.Components.Create<ShieldNotificationUI>();
		notification.Message = "SHIELD BLOCKED THE ATTACK";
	}

	[Rpc.Owner]
	private void ShowShieldBlockedAnomalyRpc()
	{
		var uiObject = Scene.CreateObject();
		uiObject.Name = "Shield Notification UI";
		var notification = uiObject.Components.Create<ShieldNotificationUI>();
		notification.Message = "KILL BLOCKED BY SHIELD";
	}

	[Rpc.Owner]
	public void ConsumeSecondChancePerkRpc()
	{
		if ( PerkBridge.EquippedPerkId != "second_chance" ) return;

		var secondChancePerk = PerkRegistry.GetById( "second_chance" );
		if ( secondChancePerk != null )
		{
			PerkBridge.MarkPerkUsed();
			Sandbox.Services.Stats.Increment( "credits_spent", secondChancePerk.Cost );
			PerkBridge.UnequipPerk();
			EquippedPerkId = "";
			// Play perk activation sound
			if ( PerkActivateSound != null )
			{
				var handle = Sound.Play( PerkActivateSound );
			}

			Log.Info( "[Perk] Second Chance consumed - role hidden from vote result" );
		}
	}

	[Rpc.Owner]
	public void ConsumeShieldPerkRpc()
	{
		if ( PerkBridge.EquippedPerkId != "shield" ) return;

		var shieldPerk = PerkRegistry.GetById( "shield" );
		if ( shieldPerk != null )
		{
			PerkBridge.MarkPerkUsed();
			Sandbox.Services.Stats.Increment( "credits_spent", shieldPerk.Cost );
			PerkBridge.UnequipPerk();
			EquippedPerkId = "";
			// Play perk activation sound
			if ( PerkActivateSound != null )
			{
				var handle = Sound.Play( PerkActivateSound );
			}

			Log.Info( "[Perk] Shield consumed - blocked one kill attempt" );
		}
	}

	[Rpc.Broadcast]
	public void KillPlayer( PlayerController target, bool byTrap = false )
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
		// Must include disabled components: BecomeGhostRpc can race ahead of this broadcast on uninvolved clients and disable the victim's renderers before we copy them
		var targetRenderer = target.GameObject.Components.Get<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants );

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

						// Same reason as targetRenderer above: BecomeGhostRpc may have already disabled these
						var childRenderer = child.Components.Get<SkinnedModelRenderer>( FindMode.EverythingInSelf );
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

			// Track this kill for the dissolve ability (local anomaly only)
			if ( !IsProxy && Role == PlayerRole.Anomaly )
			{
				LastKillVictimName = target.GameObject.Root.Name.Replace( "Player - ", "" );
			}
		}

		if ( Networking.IsHost )
		{
			if ( target.GameObject.Network.Owner != null )
			{
				target.PlayDeathSoundRpc();
				string killerDisplayName = GameObject.Root.Name.Replace( "Player - ", "" );
				target.ShowDeathUIRpc( killerDisplayName, byTrap );
			}

			var taskManager = Scene.GetAllComponents<TaskManager>().FirstOrDefault();
			if ( taskManager != null )
			{
				taskManager.ClearPlayerTasks( target );
			}

			target.BecomeGhostRpc();
		}
	}

	[Rpc.Owner]
	public void ShowDeathUIRpc( string killerName, bool byTrap )
	{
		// Create death UI
		var uiObject = Scene.CreateObject();
		uiObject.Name = "Death UI";
		var deathUI = uiObject.Components.Create<DeathOverlayUI>();
		deathUI.Show( killerName, byTrap );
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
			trackerTagTarget = null;

			if ( perkHudUI != null && perkHudUI.IsValid() )
			{
				perkHudUI.GameObject.Destroy();
				perkHudUI = null;
			}

			if ( silenceTargetUI != null && silenceTargetUI.IsValid() )
			{
				silenceTargetUI.GameObject.Destroy();
				silenceTargetUI = null;
			}

			PerkBridge.SilenceUIOpen = false;

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
		trackerTagTarget = null;

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

		// Hide perk HUD and reset perk state
		if ( perkHudUI != null && perkHudUI.IsValid() )
		{
			perkHudUI.GameObject.Destroy();
			perkHudUI = null;
		}
		if ( perkActive )
		{
			EndPerkEffect();
		}
		perkActive = false;
		activePerkId = "";
		trackerTagTarget = null;
		PerkBridge.IsPerkActive = false;
		PerkBridge.PerkTimeRemaining = 0f;
		PerkBridge.ActivePerkName = "";
		ironWillResistedThisRound = false;
		CleanupRevealTag();

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

		// Host-only: these are [Rpc.Owner] routed RPCs - if every client runs this loop,
		// each target gets N invocations and UI/sound effects stack N times.
		if ( Networking.IsHost )
		{
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
		}

		// Double Kill: reset the anomaly's kill cooldown
		if ( abilityId == "doublekill" )
		{
			var localPlayer = Scene.GetAllComponents<PlayerController>()
				.FirstOrDefault( p => !p.IsProxy );
			
			if ( localPlayer != null && localPlayer == this )
			{
				localPlayer.ResetKillCooldown();
			}
		}

		// X-ray: only the anomaly sees outlines (local effect only)
		if ( abilityId == "xray" )
		{
			var localPlayer = Scene.GetAllComponents<PlayerController>()
				.FirstOrDefault( p => !p.IsProxy );
			
			if ( localPlayer != null && localPlayer == this )
			{
				localPlayer.StartXRayEffect();
			}
		}

		// Vanish: teleport the anomaly to a random vanish spawn
		if ( abilityId == "vanish" )
		{
			var localPlayer = Scene.GetAllComponents<PlayerController>()
				.FirstOrDefault( p => !p.IsProxy );
			
			if ( localPlayer != null && localPlayer == this )
			{
				localPlayer.ActivateVanish();
			}
		}

		if ( abilityId == "dissolve" )
		{
			var localPlayer = Scene.GetAllComponents<PlayerController>()
				.FirstOrDefault( p => !p.IsProxy );
			
			if ( localPlayer != null && localPlayer == this )
			{
				localPlayer.ActivateDissolve();
			}
		}

		// Mimic: anomaly copies a random citizen's appearance
		if ( abilityId == "mimic" )
		{
			var localPlayer = Scene.GetAllComponents<PlayerController>()
				.FirstOrDefault( p => !p.IsProxy );

			if ( localPlayer != null && localPlayer == this )
			{
				localPlayer.StartMimicEffect();
			}
		}

		// Trapper: anomaly places a persistent trap at their feet
		if ( abilityId == "trapper" )
		{
			var localPlayer = Scene.GetAllComponents<PlayerController>()
				.FirstOrDefault( p => !p.IsProxy );

			if ( localPlayer != null && localPlayer == this )
			{
				localPlayer.ActivateTrapper();
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
		// Paranoia Immunity: immune to all purge abilities for the entire round
		if ( PerkBridge.EquippedPerkId == "paranoia_immunity" )
		{
			// Play perk activation sound
			if ( PerkActivateSound != null )
			{
				var handle = Sound.Play( PerkActivateSound );
			}

			Log.Info( "[Perk] Paranoia Immunity blocked blackout!" );
			return;
		}

		// Iron Will already resisted a blind this round — ignore duplicate RPC calls
		if ( ironWillResistedThisRound )
			return;

		// Iron Will: resist blackout effect
		if ( !PerkBridge.PerkUsedThisRound && PerkBridge.EquippedPerkId == "iron_will" )
		{
			var ironWillPerk = PerkRegistry.GetById( "iron_will" );
			if ( ironWillPerk != null )
			{
				ironWillResistedThisRound = true;
				PerkBridge.MarkPerkUsed();
				Sandbox.Services.Stats.Increment( "credits_spent", ironWillPerk.Cost );
				PerkBridge.UnequipPerk();
				EquippedPerkId = "";
				// Play perk activation sound
				if ( PerkActivateSound != null )
				{
					var handle = Sound.Play( PerkActivateSound );
				}

				Log.Info( "[Perk] Iron Will resisted blackout!" );
			}
			return;
		}

		isBlinded = true;

		// Play blinded sound
		if ( BlindedSound != null )
		{
			var handle = Sound.Play( BlindedSound );
			if ( handle != null )
			{
				handle.ListenLocal = true;
				handle.Volume = 0.075f;
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
			.Where( p => p.EquippedPerkId != "paranoia_immunity" )
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

	public void ActivateDissolve()
	{
		if ( string.IsNullOrEmpty( LastKillVictimName ) )
		{
			Log.Warning( "[Dissolve] No recent kill to dissolve!" );
			return;
		}

		DissolveBodyByNameRpc( LastKillVictimName );
		LastKillVictimName = "";
		
		Log.Info( $"[Dissolve] Dissolving body of {LastKillVictimName}!" );
	}

	[Rpc.Broadcast]
	private void DissolveBodyByNameRpc( string victimName )
	{
		// Find all ragdolls with a DeadBody component matching the victim name
		var allObjects = Scene.GetAllObjects( true );
		
		foreach ( var obj in allObjects )
		{
			if ( !obj.IsValid() ) continue;
			
			var deadBody = obj.Components.Get<DeadBody>();
			if ( deadBody != null && deadBody.VictimName == victimName )
			{
				obj.Destroy();
				Log.Info( $"[Dissolve] Destroyed body of {victimName} on {(Networking.IsHost ? "HOST" : "CLIENT")}" );
				return;
			}
		}
		
		// Also check ragdoll-tagged objects without DeadBody (fallback)
		var ragdolls = allObjects.Where( obj => obj.Tags.Has( "ragdoll" ) ).ToList();
		foreach ( var ragdoll in ragdolls )
		{
			if ( !ragdoll.IsValid() ) continue;
			
			var deadBody = ragdoll.Components.Get<DeadBody>();
			if ( deadBody != null && deadBody.VictimName == victimName )
			{
				ragdoll.Destroy();
				Log.Info( $"[Dissolve] Destroyed tagged body of {victimName} on {(Networking.IsHost ? "HOST" : "CLIENT")}" );
				return;
			}
		}
		
		Log.Warning( $"[Dissolve] Could not find body of {victimName}" );
	}

	[Rpc.Broadcast]
	private void VanishTeleportRpc( Vector3 position )
	{
		GameObject.WorldPosition = position;
	}

	public void ActivateTrapper()
	{
		if ( TrapPrefab == null )
		{
			Log.Warning( "[Trapper] TrapPrefab not set!" );
			return;
		}

		var ownerSteamId = GameObject.Network.Owner?.SteamId ?? 0;
		var trapId = System.Guid.NewGuid().ToString();

		SpawnTrapRpc( WorldPosition, trapId, ownerSteamId );

		Log.Info( $"[Trapper] Placed trap {trapId} at {WorldPosition}" );
	}

	[Rpc.Broadcast]
	private void SpawnTrapRpc( Vector3 position, string trapId, ulong ownerSteamId )
	{
		var playerWithPrefab = Scene.GetAllComponents<PlayerController>()
			.FirstOrDefault( p => p.TrapPrefab != null );

		if ( playerWithPrefab?.TrapPrefab == null )
		{
			Log.Warning( "[Trapper] No TrapPrefab reference found!" );
			return;
		}

		var trap = playerWithPrefab.TrapPrefab.Clone();
		trap.NetworkMode = NetworkMode.Never;
		trap.WorldPosition = position;

		var trapComp = trap.Components.Get<TrapComponent>();
		if ( trapComp != null )
		{
			trapComp.TrapId = trapId;
			trapComp.OwnerSteamId = ownerSteamId;
		}
		else
		{
			Log.Warning( "[Trapper] Spawned trap prefab has no TrapComponent!" );
		}
	}

	[Rpc.Broadcast]
	public void DestroyTrapRpc( string trapId )
	{
		var traps = Scene.GetAllComponents<TrapComponent>().ToList();
		foreach ( var t in traps )
		{
			if ( t.TrapId == trapId )
			{
				t.GameObject.Destroy();
				return;
			}
		}
	}

	[Rpc.Broadcast]
	public void PlayTrapWarningRpc( string trapId )
	{
		var traps = Scene.GetAllComponents<TrapComponent>().ToList();
		foreach ( var t in traps )
		{
			if ( t.TrapId == trapId )
			{
				if ( t.WarningSound != null )
					Sound.Play( t.WarningSound, t.WorldPosition );
				return;
			}
		}
	}

	public void StartMimicEffect()
	{
		var myOwner = GameObject.Network.Owner;
		if ( myOwner == null ) return;

		var citizens = Scene.GetAllComponents<PlayerController>()
			.Where( p => p.IsAlive && p.IsInGame && p.Role == PlayerRole.Citizen )
			.Where( p => p.GameObject.Network.Owner != null && p.GameObject.Network.Owner.SteamId != myOwner.SteamId )
			.Where( p => p.EquippedPerkId != "paranoia_immunity" )
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

	private PerkHudUI perkHudUI = null;
	private SilenceTargetUI silenceTargetUI = null;

	[Rpc.Owner]
	public void ShowPerkHudRpc()
	{
		// Sync perk from local bridge to synced property
		EquippedPerkId = PerkBridge.EquippedPerkId;
		PerkBridge.ResetForNewRound();

		if ( string.IsNullOrEmpty( EquippedPerkId ) ) return;

		var perk = PerkRegistry.GetById( EquippedPerkId );
		if ( perk == null ) return;

		// Role validation — don't show HUD for wrong-role perks
		if ( perk.Role == PerkRole.CitizenOnly && Role != PlayerRole.Citizen ) return;
		if ( perk.Role == PerkRole.AnomalyOnly && Role != PlayerRole.Anomaly ) return;

		// Store perk name and type for HUD display (persists after unequip)
		PerkBridge.ActivePerkName = perk.Name;
		PerkBridge.IsPassivePerk = perk.Activation == PerkActivation.Passive;

		if ( perk.Id == "silence" && (silenceTargetUI == null || !silenceTargetUI.IsValid()) )
		{
			var silenceObj = Scene.CreateObject();
			silenceObj.Name = "Silence Target UI";
			var silenceScreen = silenceObj.Components.Create<ScreenPanel>();
			silenceScreen.ZIndex = 210;
			silenceTargetUI = silenceObj.Components.Create<SilenceTargetUI>();
		}

		if ( perkHudUI != null && perkHudUI.IsValid() )
			return;

		var uiObject = Scene.CreateObject();
		uiObject.Name = "Perk HUD UI";
		perkHudUI = uiObject.Components.Create<PerkHudUI>();

		Log.Info( $"[Perk] HUD shown for perk: {perk.Name}" );
	}

	[Rpc.Owner]
	public void HidePerkHudRpc()
	{
		if ( perkHudUI != null && perkHudUI.IsValid() )
		{
			perkHudUI.GameObject.Destroy();
			perkHudUI = null;
		}

		if ( silenceTargetUI != null && silenceTargetUI.IsValid() )
		{
			silenceTargetUI.GameObject.Destroy();
			silenceTargetUI = null;
		}

		// Reset perk state
		if ( perkActive )
		{
			EndPerkEffect();
		}
		perkActive = false;
		activePerkId = "";
		ironWillResistedThisRound = false;
		trackerTagTarget = null;
		PerkBridge.IsPerkActive = false;
		PerkBridge.PerkTimeRemaining = 0f;
		PerkBridge.ActivePerkName = "";
		PerkBridge.IsPassivePerk = false;
		PerkBridge.ResetSilenceForMeeting();
		CleanupRevealTag();
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

	[Rpc.Owner]
	public void AdminGiveCreditsRpc( int amount )
	{
		Sandbox.Services.Stats.Increment( "credits", amount );
		Log.Info( $"[Admin] Received {amount} credits from host" );
	}

	[Rpc.Owner]
	public void AdminAdjustCasinoWonRpc( int amount )
	{
		Sandbox.Services.Stats.Increment( "casino_won", amount );
		Log.Info( $"[Admin] casino_won adjusted by {amount} by host" );
	}

	[Rpc.Owner]
	public void ReceiveRoundCreditsRpc( int kills, int killCreds, int tasks, int taskCreds, int votes, int voteCreds, bool won, int winCreds, int total )
	{
		// Persist to stats immediately while data is fresh
		if ( total > 0 )
		{
			Sandbox.Services.Stats.Increment( "credits", total );
			Log.Info( $"[Credits] Awarded {total} credits" );
		}

		// Save for UI display after lobby return
		savedKills = kills;
		savedKillCredits = killCreds;
		savedTasks = tasks;
		savedTaskCredits = taskCreds;
		savedVotes = votes;
		savedVoteCredits = voteCreds;
		savedWon = won;
		savedWinCredits = winCreds;
		savedTotalCredits = total;
		hasPendingCredits = true;
	}

	public void ShowPendingCreditsUI()
	{
		if ( !hasPendingCredits ) return;
		hasPendingCredits = false;

		CreditsSummaryBridge.CreditSound = Scene.GetAllComponents<GameManager>().FirstOrDefault()?.CreditsSummarySound;

		var uiObject = Scene.CreateObject();
		uiObject.Name = "Credits Summary UI";
		var screenPanel = uiObject.Components.Create<ScreenPanel>();
		screenPanel.ZIndex = 800;
		var summaryUI = uiObject.Components.Create<CreditsSummaryUI>();
		summaryUI.ShowSummary(
			savedKills, savedKillCredits,
			savedTasks, savedTaskCredits,
			savedVotes, savedVoteCredits,
			savedWon, savedWinCredits,
			savedTotalCredits
		);
	}
}