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
	int _hitscanRequestSequence;
	int _lastHitscanRequestSequence = -1;
	PlayerController _hitscanRequestOwner;
	TimeUntil _nextHitscanRequest;

	float HitDamage( Vector3 origin, Vector3 position, GameObject target ) => DamageProfile.Evaluate(
		origin.Distance( position ), target?.GetComponentInParent<Titanfall2Npc>() is { IsTitan: true }, false, Ballistics.Damage, TitanDamage );

	void FireHitscan()
	{
		if ( IsProxy ) return;
		var aim = AimRay;
		if ( Networking.IsHost ) ResolveHitscan( aim, _adsBlend );
		else if ( IsHeld && IsActive ) RequestHitscan( unchecked( ++_hitscanRequestSequence ), aim.Forward, _adsBlend );
	}

	// A whole trigger pull reaches the host, including shots that miss actors. Native
	// client hit claims omit those rays, so they cannot implement a spatial vortex.
	[Rpc.Host( NetFlags.OwnerOnly )]
	void RequestHitscan( int sequence, Vector3 direction, float zoom )
	{
		if ( !Networking.IsHost || UsesRetailSmartAmmo || UsesRetailCharge ) return;
		var owner = Owner;
		if ( !owner.IsValid() || Inventory?.ActiveItem != this ) return;
		if ( _hitscanRequestOwner != owner ) { _hitscanRequestOwner = owner; _lastHitscanRequestSequence = -1; }
		if ( sequence <= _lastHitscanRequestSequence ) return;
		_lastHitscanRequestSequence = sequence;
		// Small arrival jitter allowance, accumulated into the next deadline rather
		// than granting extra sustained fire rate. Duplicates remain strictly rejected.
		var slack = Math.Clamp( (SpinUp.Enabled ? SpinUp.Interval( _spinUp.HostFraction( this ), PrimaryDelay ) : PrimaryDelay) * 0.25f, 0.003f, 0.02f );
		if ( IsReloading || Projectile.Enabled || _nextHitscanRequest > slack
			|| !direction.IsFinite || direction.LengthSquared is < 0.25f or > 4f || !float.IsFinite( zoom ) || zoom is < 0 or > 1 ) return;
		if ( Vector3.Dot( owner.EyeTransform.Forward, direction.Normal ) < 0.35f ) return;
		// The client supplies view direction only. Host eyes establish the shot origin;
		// damage, ammo, spread and all shield contacts come from host-owned state.
		ResolveHitscan( new Ray( owner.EyePosition, direction.Normal ), zoom );
	}

	void ResolveHitscan( Ray aim, float zoom, Sandbox.Npcs.BaseNpc smartTarget = null )
	{
		if ( UsesRetailCharge && !IsDispatchingChargeShot ) return;
		if ( !Networking.IsHost || !Active || Projectile.Enabled || IsReloading || !CanAcceptRechamberShot() || !aim.Position.IsFinite || !aim.Forward.IsFinite
			|| aim.Forward.LengthSquared < 0.25f ) return;
		if ( IsHeld && (!IsActive || !HasRoundAmmo() || !_heat.CanAcceptShot( this )
			|| !(UsesRetailCharge ? _charge.TryTakeAmmo( this, Firing.Cost ) : TakePrimaryAmmo( Firing.Cost ))) ) return;
		_heat.AcceptShot( this );
		_nextHitscanRequest = MathF.Max( 0, _nextHitscanRequest ) + MathF.Max( _spinUp.AcceptHostShot( this, zoom ), 0.015f );
		if ( Clip1 > 0 ) RecordRechamberShot( _nextHitscanRequest );
		var config = Ballistics;
		var volley = new ShotEffect[Math.Clamp( config.Pellets, 1, 12 )];
		var spread = smartTarget.IsValid() ? Vector2.Zero
			: SpreadProfile.Enabled ? RetailSpread( zoom ) : base.CurrentSpread * float.Lerp( 1f, 0.25f, zoom );
		for ( var i = 0; i < volley.Length; i++ )
		{
			var ray = aim with { Forward = aim.Forward.WithAimCone( spread.x, spread.y ) };
			var hit = BulletTrace( ray, config.Range, config.Radius ).Run();
			if ( !Titanfall2TitanDefense.TryInterceptHitscan( Scene, ref hit, Attacker, GameObject,
				new Titanfall2DefenseShot( config.Damage, TitanDamage, config.Force, DamageProfile, Range: config.Range ) ) )
			{
				var tags = new TagSet(); tags.Add( "bullet" );
				var damage = Titanfall2WeaponSmartAmmoController.ComputeBulletDamage( this, HitDamage( hit.StartPosition, hit.HitPosition, hit.GameObject ), hit.GameObject, smartTarget );
				Titanfall2HostDamage.ShootBullet( hit, damage, config.Force, Attacker, GameObject, tags );
			}
			volley[i] = new ShotEffect( hit.EndPosition, hit.Hit, hit.Normal, hit.GameObject, hit.Surface, NoEvents: i > 0 );
		}
		PlayHostHitscan( volley, zoom );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayHostHitscan( ShotEffect[] volley, float zoom )
	{
		// Base ShootEffects excludes the remote owning client because it assumes local
		// prediction. This path deliberately acknowledges once to every peer, owner included.
		TimeSinceShoot = 0;
		RecordSpreadShot( zoom );
		foreach ( var shot in volley ) OnShootEffects( shot );
	}

	// All TF2 hitscan now comes through RequestHitscan/ResolveHitscan. Accepting legacy
	// per-hit claims as well would provide a second damage route around shield arbitration.
	protected override bool OnValidateShotClaim( in ShotClaim claim ) => false;
}
