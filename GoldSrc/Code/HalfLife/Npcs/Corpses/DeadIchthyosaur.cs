namespace GoldSrc.HalfLife;

[Hide]
public sealed class DeadIchthyosaur : NpcCorpse
{
	internal override string FinalSequence => "bellyup";
	float _speed;

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost ) return;
		var water = WaterBody.Find( Scene, WorldPosition );
		if ( !water.IsValid() )
		{
			base.OnFixedUpdate();
			return;
		}
		var shallow = water.ToSurface( WorldPosition ).z > -8;
		_speed *= System.MathF.Pow( 0.8f, 10 * Time.Delta );
		_speed = (_speed + (shallow ? -80 : 80) * Time.Delta).Clamp( -64, 64 );
		var dest = WorldPosition + Vector3.Up * _speed * Time.Delta;
		var trace = Scene.Trace.Ray( WorldPosition, dest ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
		WorldPosition = trace.Hit ? trace.EndPosition : dest;
	}
}
