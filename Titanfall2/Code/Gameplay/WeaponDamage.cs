/// <summary>Two linear falloff segments, followed by a constant distant damage value.</summary>
public record struct Titanfall2DamageCurve
{
	public float Near { get; set; }
	public float Far { get; set; }
	public float VeryFar { get; set; }
	public float NearDistance { get; set; }
	public float FarDistance { get; set; }
	public float VeryFarDistance { get; set; }

	public readonly float Evaluate( float distance )
	{
		if ( !float.IsFinite( distance ) ) return 0;
		if ( distance <= NearDistance ) return Near;
		if ( distance < FarDistance ) return float.Lerp( Near, Far, (distance - NearDistance) / (FarDistance - NearDistance) );
		if ( distance < VeryFarDistance ) return float.Lerp( Far, VeryFar, (distance - FarDistance) / (VeryFarDistance - FarDistance) );
		return VeryFar;
	}
}

/// <summary>Retail player/NPC and infantry/Titan damage. References preserve host damage tuning and pellet division.</summary>
public record struct Titanfall2DamageProfile
{
	public bool Enabled { get; set; }
	public Titanfall2DamageCurve Player { get; set; }
	public Titanfall2DamageCurve PlayerTitan { get; set; }
	public Titanfall2DamageCurve Npc { get; set; }
	public Titanfall2DamageCurve NpcTitan { get; set; }
	public float ReferenceDamage { get; set; }
	public float ReferenceTitanDamage { get; set; }

	public readonly float Evaluate( float distance, bool titan, bool npc, float damage, float titanDamage )
	{
		var nominal = titan ? titanDamage : damage;
		if ( !float.IsFinite( nominal ) || nominal <= 0 ) return 0;
		if ( !Enabled ) return nominal;
		var curve = titan ? (npc ? NpcTitan : PlayerTitan) : npc ? Npc : Player;
		var reference = titan ? ReferenceTitanDamage : ReferenceDamage;
		if ( !float.IsFinite( reference ) || reference <= 0 ) return 0;
		var result = curve.Evaluate( distance ) * nominal / reference;
		return float.IsFinite( result ) ? MathF.Max( 0, result ) : 0;
	}
}

internal sealed partial record Titanfall2WeaponData
{
	internal Titanfall2DamageProfile DamageProfile { get; init; }

	static Titanfall2DamageProfile ReadDamageProfile( Func<string, float, float> number, float damage, float titanDamage )
	{
		Titanfall2DamageCurve Curve( string prefix, string armor, Titanfall2DamageCurve fallback )
		{
			var authoredNear = number( prefix + "damage_near_value" + armor, float.NaN );
			var authoredFar = number( prefix + "damage_far_value" + armor, float.NaN );
			var near = Math.Clamp( float.IsFinite( authoredNear ) ? authoredNear : fallback.Near, 0, 10000 );
			var far = Math.Clamp( float.IsFinite( authoredFar ) ? authoredFar : float.IsFinite( authoredNear ) ? near : fallback.Far, 0, 10000 );
			var nearDistance = Math.Clamp( number( prefix + "damage_near_distance", fallback.NearDistance ), 0, 65536 );
			var farDistance = Math.Clamp( number( prefix + "damage_far_distance", fallback.FarDistance ), nearDistance, 65536 );
			return new()
			{
				Near = near, Far = far,
				VeryFar = Math.Clamp( number( prefix + "damage_very_far_value" + armor,
					float.IsFinite( authoredNear ) || float.IsFinite( authoredFar ) ? far : fallback.VeryFar ), 0, 10000 ),
				NearDistance = nearDistance, FarDistance = farDistance,
				VeryFarDistance = Math.Clamp( number( prefix + "damage_very_far_distance", fallback.VeryFarDistance ), farDistance, 65536 )
			};
		}
		var player = Curve( "", "", new() { Near = damage } );
		var heavy = Curve( "", "_titanarmor", player );
		return new()
		{
			Enabled = true, Player = player, PlayerTitan = heavy,
			Npc = Curve( "npc_", "", player ), NpcTitan = Curve( "npc_", "_titanarmor", heavy ),
			ReferenceDamage = damage, ReferenceTitanDamage = titanDamage
		};
	}
}

public sealed partial class Titanfall2Weapon
{
	[Property] public Titanfall2DamageProfile DamageProfile { get; set; }

	float HitDamage( Vector3 origin, Vector3 position, GameObject target ) => DamageProfile.Evaluate(
		origin.Distance( position ), target?.GetComponentInParent<Titanfall2Npc>() is { IsTitan: true }, false, Ballistics.Damage, TitanDamage );

	void FireHitscan()
	{
		if ( !DamageProfile.Enabled ) { base.PrimaryAttack(); return; }
		if ( IsHeld && !TakePrimaryAmmo() ) return;
		var config = Ballistics;
		var aim = AimRay;
		var volley = new ShotEffect[Math.Clamp( config.Pellets, 1, 12 )];
		for ( var i = 0; i < volley.Length; i++ )
		{
			var spread = CurrentSpread;
			var ray = aim with { Forward = aim.Forward.WithAimCone( spread.x, spread.y ) };
			var hit = BulletTrace( ray, config.Range, config.Radius ).Run();
			ShootBullet( hit, HitDamage( hit.StartPosition, hit.HitPosition, hit.GameObject ), config.Force );
			volley[i] = new ShotEffect( hit.EndPosition, hit.Hit, hit.Normal, hit.GameObject, hit.Surface, NoEvents: i > 0 );
		}
		TimeSinceShoot = 0;
		ShootEffects( volley );
	}

	// Each pellet can have a different distance and armor curve. The engine routes
	// these individual claims; the host checks their damage against its own profile.
	protected override bool OnValidateShotClaim( in ShotClaim claim )
	{
		if ( !DamageProfile.Enabled ) return base.OnValidateShotClaim( claim );
		if ( Projectile.Enabled || claim.Pellets is not { Length: 1 } || !float.IsFinite( claim.Damage ) ) return false;
		var pellet = claim.Pellets[0];
		if ( !pellet.HitObject.IsValid() || !pellet.Origin.IsFinite || !pellet.Position.IsFinite ) return false;
		return MathF.Abs( claim.Damage - HitDamage( pellet.Origin, pellet.Position, pellet.HitObject ) ) < 0.01f;
	}
}
