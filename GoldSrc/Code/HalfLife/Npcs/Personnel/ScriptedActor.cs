namespace GoldSrc.HalfLife;

[Hide]
public abstract class ScriptedActor : Npc
{
	[Property, Sync( SyncFlags.FromHost ), Change( nameof( SolidityChanged ) )] public bool NonSolid { get; set; }
	internal override float ModelOriginHeight => ModelPath?.EndsWith( "/player.mdl.vmdl", System.StringComparison.OrdinalIgnoreCase ) == true ||
		ModelPath?.EndsWith( "/holo.mdl.vmdl", System.StringComparison.OrdinalIgnoreCase ) == true ? 36 : 0;

	internal override void OnModelLoaded( ModelLoader loader )
	{
		Renderer.LocalPosition = Vector3.Up * ModelOriginHeight;
		EyeHeight = ModelOriginHeight + loader.EyePosition.z;
		Senses.HearingRange = 0;
		SetSolidity();
	}

	void SolidityChanged( bool oldValue, bool newValue ) => SetSolidity();

	void SetSolidity()
	{
		if ( GetComponentInChildren<BoxCollider>( true ) is { } hull ) hull.Enabled = !NonSolid;
		if ( GetComponentInChildren<ModelHitboxes>( true ) is { } hitboxes ) hitboxes.Enabled = !NonSolid;
	}

	internal override void IdleMovement() { }

	internal override void ModifyDamage( Component.IDamageable.DamageEvent damage )
	{
		if ( NonSolid ) damage.Cancelled = true;
		else base.ModifyDamage( damage );
	}
}
