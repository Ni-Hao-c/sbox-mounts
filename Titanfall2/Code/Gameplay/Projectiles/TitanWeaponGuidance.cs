public sealed partial class Titanfall2Projectile
{
	GameObject _designatedHomingTarget;
	bool _hasDesignatedHomingTarget;
	Vector3 _designatedHomingPoint;
	bool _hasDesignatedHomingPoint;
	internal bool IsCoreProjectile { get; set; }

	void TagTitanWeaponDamage( TagSet tags )
	{
		if ( Settings.ImpactTable == "40mm_bullet" ) tags.Add( "titan_40mm" );
		if ( IsCoreProjectile ) tags.Add( "titan-core" );
	}

	/// <summary>Tone missiles retain their marked target; they never acquire an unmarked neighbour.</summary>
	internal void DesignateHomingTarget( GameObject target )
	{
		if ( !Networking.IsHost || IsProxy ) return;
		_hasDesignatedHomingTarget = true;
		_designatedHomingTarget = target;
		_hasDesignatedHomingPoint = false;
	}

	/// <summary>Salvo Core follows its host's aim point, retaining the last point after the core ends.</summary>
	internal void DesignateHomingPoint( Vector3 point )
	{
		if ( !Networking.IsHost || IsProxy || !point.IsFinite ) return;
		_hasDesignatedHomingPoint = true;
		_designatedHomingPoint = point;
		_hasDesignatedHomingTarget = false;
	}

	bool TrySteerDesignatedTarget( float dt )
	{
		if ( _hasDesignatedHomingPoint )
		{
			var delta = _designatedHomingPoint - WorldPosition;
			if ( delta.LengthSquared > 1 && Velocity.IsFinite && Velocity.Length > 10 )
				Velocity = Vector3.Lerp( Velocity.Normal, delta.Normal, Math.Clamp( dt * 5.5f, 0, 1 ) ).Normal * Velocity.Length;
			return true;
		}
		if ( !_hasDesignatedHomingTarget ) return false;
		var target = _designatedHomingTarget?.GetComponentInParent<Titanfall2Npc>();
		if ( !target.IsValid() || target.Dead || !Velocity.IsFinite || Velocity.Length < 10 ) return true;
		var source = _ignore?.GetComponentInParent<Titanfall2Npc>();
		if ( !source.IsValid() || !source.IsHostileToFaction( target.Faction ) ) return true;
		var center = target.WorldPosition + Vector3.Up * (target.BodyHeight * 0.55f);
		var desired = center - WorldPosition;
		if ( desired.LengthSquared < 1 ) return true;
		Velocity = Vector3.Lerp( Velocity.Normal, desired.Normal, Math.Clamp( dt * 5.5f, 0, 1 ) ).Normal * Velocity.Length;
		return true;
	}
}
