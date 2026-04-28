using Sandbox;
using System.Linq;

public sealed class DailyBonusStation : Component, Component.ITriggerListener
{
	[Property] public SoundEvent OpenSound { get; set; }
	[Property] public SoundEvent DailySpinSound { get; set; }
	[Property] public SoundEvent DailyRewardSound { get; set; }

	private bool playerNearby = false;
	private PlayerController nearbyPlayer = null;
	private bool isSpinnerOpen = false;
	private GameObject spinnerObject = null;
	private bool isChecking = false;

	void ITriggerListener.OnTriggerEnter( Collider other )
	{
		var player = other.GameObject.Components.Get<PlayerController>();
		if ( player != null && !player.IsProxy )
		{
			playerNearby = true;
			nearbyPlayer = player;
		}
	}

	void ITriggerListener.OnTriggerExit( Collider other )
	{
		var player = other.GameObject.Components.Get<PlayerController>();
		if ( player != null && player == nearbyPlayer )
		{
			playerNearby = false;
			nearbyPlayer = null;
			CloseSpinner();
		}
	}

	protected override void OnUpdate()
	{
		if ( isSpinnerOpen && spinnerObject != null && !spinnerObject.IsValid() )
		{
			isSpinnerOpen = false;
			spinnerObject = null;
		}

		if ( !playerNearby || nearbyPlayer == null || nearbyPlayer.IsProxy )
			return;

		Gizmo.Draw.Color = Color.Yellow;
		Gizmo.Draw.Text( "Press E \u2014 Daily Bonus", new Transform( WorldPosition + Vector3.Up * 50 ), "Consolas", 18 );

		var gm = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if ( gm != null && gm.CurrentState != GameManager.GameState.WaitingInLobby )
			return;

		if ( Input.Pressed( "Use" ) && !isChecking )
		{
			if ( isSpinnerOpen )
			{
				CloseSpinner();
			}
			else
			{
				TryOpenSpinner();
			}
		}
	}

	private async void TryOpenSpinner()
	{
		if ( DailyBonusTracker.ClaimedThisSession )
		{
			ShowAlreadyClaimed();
			return;
		}

		isChecking = true;

		try
		{
			bool alreadyClaimed = await DailyBonusTracker.HasClaimedTodayAsync();

			if ( alreadyClaimed )
			{
				ShowAlreadyClaimed();
			}
			else
			{
				OpenSpinner();
			}
		}
		finally
		{
			isChecking = false;
		}
	}

	private void OpenSpinner()
	{
		if ( OpenSound != null )
		{
			var handle = Sound.Play( OpenSound );
			if ( handle != null )
			{
				handle.ListenLocal = true;
				handle.Volume = 0.5f;
			}
		}

		DailyBonusBridge.SpinSound = DailySpinSound;
		DailyBonusBridge.RewardSound = DailyRewardSound;

		spinnerObject = Scene.CreateObject();
		spinnerObject.Name = "Daily Spinner UI";
		var screenPanel = spinnerObject.Components.Create<ScreenPanel>();
		screenPanel.ZIndex = 950;
		spinnerObject.Components.Create<DailySpinnerUI>();
		isSpinnerOpen = true;
		Log.Info( "[DailyBonus] Showing daily spinner from station" );
	}

	private void ShowAlreadyClaimed()
	{
		var uiObject = Scene.CreateObject();
		uiObject.Name = "Daily Bonus Already Claimed";
		var screenPanel = uiObject.Components.Create<ScreenPanel>();
		screenPanel.ZIndex = 960;
		uiObject.Components.Create<DailyBonusClaimedUI>();
		Log.Info( "[DailyBonus] Already claimed for today" );
	}

	private void CloseSpinner()
	{
		if ( isSpinnerOpen && spinnerObject != null && spinnerObject.IsValid() )
		{
			spinnerObject.Destroy();
		}
		spinnerObject = null;
		isSpinnerOpen = false;
	}
}
