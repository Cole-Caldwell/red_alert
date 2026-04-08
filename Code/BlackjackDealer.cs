using Sandbox;


[Title( "Blackjack Dealer" )]
[Category( "Blackjack" )]
public sealed class BlackjackDealer : Component
{
	[Property] public string DealerName { get; set; } = "Dealer";

	private SkinnedModelRenderer bodyRenderer;
	private int clothingIndex;

	protected override void OnStart()
	{
		// Set root name so PlayerNametag picks it up
		GameObject.Name = DealerName;
		// Create body child with citizen model
		var bodyObj = new GameObject( true, "Body" );
		bodyObj.Parent = GameObject;
		bodyRenderer = bodyObj.Components.Create<SkinnedModelRenderer>();
		bodyRenderer.Model = Model.Load( "models/citizen/citizen.vmdl" );

		// Hardcoded dealer outfit
		AddClothing( "models/citizen_clothes/shirt/waistcoat_and_shirt/models/waistcoat_and_shirt.vmdl_c" );
		AddClothing( "models/citizen_clothes/hair/big_scruffy_bread/models/big_scruffy_beard.vmdl_c" );
		AddClothing( "models/citizen_clothes/trousers/smarttrousers/smarttrousers.vmdl_c" );
		AddClothing( "models/citizen_clothes/shoes/boots/models/black_boots.vmdl_c" );
		AddClothing( "models/citizen_clothes/hat/cowboy_hat/models/cowboy_hat.vmdl_c" );

		// Create nametag (same as player prefab: Sandbox.WorldPanel + PlayerNametag)
		var nametagObj = new GameObject( true, "Nametag" );
		nametagObj.Parent = GameObject;
		nametagObj.LocalPosition = new Vector3( 0, 0, 80 );

		var worldPanel = nametagObj.Components.Create<Sandbox.WorldPanel>();
		worldPanel.PanelSize = new Vector2( 1000, 1000 );
		worldPanel.LookAtCamera = false;

		nametagObj.Components.Create<PlayerNametag>();
	}

	private void AddClothing( string modelPath )
	{
		var model = Model.Load( modelPath );
		if ( model is null ) return;

		var clothingObj = new GameObject( true, $"Clothing_{clothingIndex++}" );
		clothingObj.Parent = bodyRenderer.GameObject;

		var renderer = clothingObj.Components.Create<SkinnedModelRenderer>();
		renderer.Model = model;
		renderer.BoneMergeTarget = bodyRenderer;
	}
}
