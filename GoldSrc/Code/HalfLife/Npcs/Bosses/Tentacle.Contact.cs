namespace GoldSrc.HalfLife;

public sealed partial class Tentacle
{
	HitboxSet.Box[] _contacts;
	Vector3?[] _previousCenters;
	TimeUntil _nextSample, _nextTouch;
	int _tipDamage = 20;
	Vector3 Tip => Renderer.GetAttachment( "attachment1" )?.Position ?? WorldPosition;

	void CheckContact()
	{
		if ( _nextSample > 0 || _playing is null ) return;
		_nextSample = 0.05f;
		for ( int i = 0; i < _contacts.Length; i++ )
		{
			var hitbox = _contacts[i];
			if ( hitbox.Shape is not BBox box || !Renderer.TryGetBoneTransform( hitbox.Bone, out var transform ) ) continue;
			var center = transform.PointToWorld( box.Center );
			var start = _previousCenters[i] ?? center;
			_previousCenters[i] = center;
			if ( _nextTouch > 0 ) continue;
			var half = box.Extents * transform.Scale;
			var trace = Scene.Trace.Box( new BBox( -half, half ), start, center ).Rotated( transform.Rotation ).IgnoreGameObjectHierarchy( GameObject );
			foreach ( var hit in trace.RunAll() )
			{
				var victim = hit.GameObject?.GetComponentInParent<Component.IDamageable>();
				if ( victim is null or Tentacle ) continue;
				var damage = new DamageInfo { Attacker = GameObject, Weapon = GameObject, Origin = WorldPosition, Position = center,
					Damage = hitbox.Tags.Has( "hitgroup_3" ) ? _tipDamage : 20 };
				damage.Tags.Add( "crush" );
				victim.OnDamage( damage );
				_nextTouch = 0.5f;
				break;
			}
		}
	}

	internal override void HandleActionEvent( int id )
	{
		if ( id == 3 ) _tipDamage = 200;
		if ( id == 4 ) _tipDamage = 25;
		if ( id is 1 or 2 or 6 )
		{
			var sample = ImpactSurface switch
			{
				SurfaceSound.Silo => "tentacle/te_strike",
				SurfaceSound.Dirt => "player/pl_dirt",
				SurfaceSound.Water => "player/pl_slosh",
				_ => null
			};
			EmitSound( sample, Tip, id == 1 ? 1 : Game.Random.Float( 0.3f, 0.5f ) );
		}
		if ( id is 7 or 8 or 9 )
			EmitSound( id == 7 ? "tentacle/te_roar" : id == 8 ? "tentacle/te_search" : "tentacle/te_move", Tip );
	}
}
