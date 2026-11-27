using System.Globalization;
using System.Text;
using Sandbox.Npcs;

/// <summary>Retail NPC grenade settings. The host's mount snapshot supplies both ballistics and presentation.</summary>
internal sealed record Titanfall2NpcGrenadeData( Titanfall2ProjectileSettings Projectile, float MinRange, float MaxRange, float Windup )
{
	internal const string Script = "scripts/weapons/mp_weapon_frag_grenade.txt";

	internal static Titanfall2NpcGrenadeData Read( Func<string, string> read )
	{
		var text = read( Script );
		if ( text is null || !Titanfall2VmtReader.TryReadBlocks( Encoding.UTF8.GetBytes( text ), out var blocks, out _ ) ) return null;
		var root = blocks.FirstOrDefault();
		if ( root is null ) return null;
		var values = new Dictionary<string, string>( root.Values, StringComparer.OrdinalIgnoreCase );
		if ( root.Children.FirstOrDefault( block => block.Name.Equals( "MP_BASE", StringComparison.OrdinalIgnoreCase ) ) is { } multiplayer )
			foreach ( var (key, value) in multiplayer.Values )
				if ( !value.Equals( "<KEEP_DEFAULT>", StringComparison.OrdinalIgnoreCase ) ) values[key] = value;
		float Number( string key, float fallback ) => float.TryParse( values.GetValueOrDefault( key ), NumberStyles.Float, CultureInfo.InvariantCulture, out var value )
			&& float.IsFinite( value ) ? value : fallback;
		var weapon = Titanfall2WeaponData.Read( Script, read, projectile: true, fallbackSpeed: 1100, ordnance: true,
			behavior: Titanfall2ProjectileBehavior.FragGrenade );
		if ( weapon is null ) return null;
		var minimum = Math.Clamp( Number( "npc_min_range", 600 ), 320, 2000 );
		return new( weapon.Projectile with { SplashDamage = Math.Clamp( Number( "npc_explosion_damage", 125 ), 1, 1000 ) },
			minimum, Math.Clamp( Number( "npc_max_range", 1200 ), minimum, 2400 ),
			Math.Clamp( Number( "toss_pullout_time", 0.35f ) + Number( "toss_time", 0.33f ), 0.2f, 2 ) );
	}

	/// <summary>Two constant-speed solutions. A failed low arc may be retried over a low wall; neither may pass a ceiling.</summary>
	internal static bool TryVelocity( Vector3 displacement, float speed, float gravity, bool high, out Vector3 velocity, out float seconds )
	{
		velocity = default; seconds = 0;
		if ( !displacement.IsFinite || !float.IsFinite( speed ) || !float.IsFinite( gravity ) || speed <= 0 || gravity <= 0 ) return false;
		var horizontal = displacement.WithZ( 0 );
		var distance = horizontal.Length;
		if ( distance < 1 ) return false;
		var square = speed * speed;
		var discriminant = square * square - gravity * (gravity * distance * distance + 2 * displacement.z * square);
		if ( discriminant < 0 || !float.IsFinite( discriminant ) ) return false;
		var slope = (square + (high ? 1 : -1) * MathF.Sqrt( discriminant )) / (gravity * distance);
		var horizontalSpeed = speed / MathF.Sqrt( 1 + slope * slope );
		seconds = distance / horizontalSpeed;
		velocity = horizontal.Normal * horizontalSpeed + Vector3.Up * (horizontalSpeed * slope);
		return velocity.IsFinite && float.IsFinite( seconds ) && seconds > 0;
	}
}

public sealed partial class Titanfall2Npc
{
	[Property] public bool UseGrenades { get; set; } = true;
	[Property] public int GrenadeCount { get; set; } = 2;
	[Property] public float GrenadeCooldown { get; set; } = 14;
	[Sync( SyncFlags.FromHost )] public int GrenadesThrown { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool ThrowingGrenade { get; private set; }
	[Sync( SyncFlags.FromHost )] public float GrenadeWindupSeconds { get; private set; } = 0.68f;
	TimeUntil _grenadeCheck;
	TimeUntil _grenadeCooldown;
	TimeUntil _grenadeRelease;
	TimeUntil _grenadeOpportunity;
	GameObject _grenadeObservedTarget;
	Vector3 _grenadeLanding;
	Titanfall2NpcGrenadeData _pendingGrenade;

	internal static bool SupportsGrenades( string model ) => model?.Contains( "/humans/grunts/", StringComparison.OrdinalIgnoreCase ) == true
		|| model?.Contains( "/robots/spectre/", StringComparison.OrdinalIgnoreCase ) == true;
	bool GrenadeReady => Networking.IsHost && !IsProxy && !Dead && CanFight && UseGrenades && !IsTitan && !MeleeOnly
		&& NavigationMode == Navigation.Ground && Seat is not { IsOccupied: true } && SupportsGrenades( ModelPath );
	Vector3 GrenadeHand => Eye + WorldRotation.Forward * (BodyRadius + 8) + WorldRotation.Left * 7;

	// Called before the ordinary fire/movement decisions, including the first tick after losing a contact.
	bool TickNpcOrdnance()
	{
		if ( !GrenadeReady || !HasCombatContact || _meleePending ) { CancelNpcOrdnance(); return false; }
		if ( ThrowingGrenade )
		{
			StopCombatMove();
			if ( _grenadeRelease > 0 ) return true;
			var profile = _pendingGrenade;
			CancelNpcOrdnance();
			// Recheck friendly positions and the actual release point. Enemy movement never steers the committed throw.
			if ( profile is not null && GrenadeAreaSafe( _grenadeLanding, profile.Projectile.SplashRadius )
				&& TryGrenadeArc( _grenadeLanding, profile, out var velocity ) )
			{
				var settings = profile.Projectile with { MountIdent = MountIdent,
					ModelPath = profile.Projectile.ModelPath is { } model ? $"mount://{MountIdent ?? "titanfall2"}/{model}.vmdl" : null };
				if ( Titanfall2Projectile.Launch( Scene, new Ray( GrenadeHand, velocity.Normal ), settings, 0, 0, GameObject, GameObject, GameObject ) is not null )
				{
					GrenadesThrown++;
					_grenadeCooldown = Math.Clamp( GrenadeCooldown, 4, 120 );
					PlayGrenadeThrowSound();
				}
			}
			return true;
		}
		if ( _seesContact && _grenadeObservedTarget != _target )
		{
			_grenadeObservedTarget = _target;
			_grenadeOpportunity = 1.5f;
		}
		if ( _grenadeCheck > 0 || _grenadeCooldown > 0 || _grenadeOpportunity > 0 || _reaction > 0
			|| Reloading || GrenadesThrown >= Math.Clamp( GrenadeCount, 0, 8 ) || _grenadeObservedTarget != _target ) return false;
		_grenadeCheck = 0.8f;
		// Recent visual memory is valid for a flush-out throw. Radio reports alone cannot authorize it.
		if ( !_seesContact && _contactAge > 1.5f ) return false;
		if ( Sandbox.Mounting.Directory.Get( MountIdent ?? "titanfall2" ) is not Titanfall2Mount mount
			|| mount.GetNpcGrenadeData() is not { } data ) return false;
		var position = _lastContact.Value + Vector3.Up * 8;
		return TryStartGrenade( position, data );
	}

	internal bool TryStartGrenade( Vector3 landing, Titanfall2NpcGrenadeData profile )
	{
		if ( !GrenadeReady || ThrowingGrenade || profile is null || !landing.IsFinite || _grenadeCooldown > 0
			|| GrenadesThrown >= Math.Clamp( GrenadeCount, 0, 8 ) || _meleePending ) return false;
		var distance = landing.Distance( WorldPosition );
		if ( distance < profile.MinRange || distance > profile.MaxRange || !GrenadeAreaSafe( landing, profile.Projectile.SplashRadius ) ) return false;
		var live = Scene.GetAllComponents<Titanfall2Projectile>().Count( shot => shot.Settings.Behavior == Titanfall2ProjectileBehavior.FragGrenade );
		var pending = Scene.GetAllComponents<Titanfall2Npc>().Count( npc => npc.ThrowingGrenade );
		if ( live + pending >= 6 ) return false;
		// Avoid multiple squadmates throwing grenades at the exact same spot simultaneously.
		if ( Scene.GetAllComponents<Titanfall2Npc>().Any( ally => ally != this && ally.ThrowingGrenade
			&& !IsHostileToFaction( ally.Faction ) && ally._grenadeLanding.Distance( landing ) < 240f ) ) return false;
		if ( !TryGrenadeArc( landing, profile, out _ ) ) return false;
		_grenadeLanding = landing;
		_pendingGrenade = profile;
		_grenadeRelease = profile.Windup;
		GrenadeWindupSeconds = profile.Windup;
		ThrowingGrenade = true;
		_nextShot = profile.Windup + 0.2f;
		StopCombatMove();
		return true;
	}

	internal void CancelNpcOrdnance()
	{
		ThrowingGrenade = false;
		_pendingGrenade = null;
	}

	protected override void OnDisabled()
	{
		CancelNpcOrdnance();
		_grenadeObservedTarget = null;
		base.OnDisabled();
	}

	bool GrenadeAreaSafe( Vector3 landing, float radius )
	{
		var safeDistance = MathF.Max( 160, radius + 64 );
		if ( WorldPosition.Distance( landing ) < safeDistance ) return false;
		foreach ( var ally in Scene.GetAllComponents<BaseNpc>() )
		{
			if ( ally == this || !ally.IsValid() || ally.Health <= 0 || IsHostileToFaction( ally.Faction ) ) continue;
			if ( ally.WorldPosition.Distance( landing ) < safeDistance ) return false;
		}
		if ( Friendly || !AttackPlayers )
			foreach ( var player in Scene.GetAllComponents<PlayerController>() )
				if ( player.WorldPosition.Distance( landing ) < safeDistance ) return false;
		return true;
	}

	bool TryGrenadeArc( Vector3 landing, Titanfall2NpcGrenadeData profile, out Vector3 result )
	{
		result = default;
		var origin = GrenadeHand;
		var gravity = 800 * profile.Projectile.GravityScale;
		var start = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( Eye, origin ).Radius( profile.Projectile.Radius )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Grenade );
		if ( start.Hit || start.StartedSolid ) return false;
		for ( var arc = 0; arc < 2; arc++ )
		{
			if ( !Titanfall2NpcGrenadeData.TryVelocity( landing - origin, profile.Projectile.Speed, gravity, arc == 1, out var velocity, out var duration )
				|| duration >= profile.Projectile.FuseSeconds - 0.35f ) continue;
			var steps = Math.Clamp( (int)MathF.Ceiling( duration / 0.08f ), 4, 40 );
			var previous = origin;
			var clear = true;
			for ( var i = 1; i <= steps; i++ )
			{
				var time = duration * i / steps;
				var point = origin + velocity * time + Vector3.Down * (0.5f * gravity * time * time);
				var trace = Scene.Trace.Ray( previous, point ).Radius( profile.Projectile.Radius ).UseHitPosition()
					.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "projectile" );
				if ( _target.IsValid() ) trace = trace.IgnoreGameObjectHierarchy( _target );
				var hit = Titanfall2CollisionQueries.Run( trace, Titanfall2TraceMask.Grenade );
				// A sampled impact is acceptable only at the intended landing patch, with a supporting surface.
				if ( hit.StartedSolid || (hit.Hit && (hit.HitPosition.Distance( landing ) > 24 || hit.Normal.z < 0.5f)) ) { clear = false; break; }
				previous = point;
			}
			if ( !clear ) continue;
			result = velocity;
			return true;
		}
		return false;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayGrenadeThrowSound() => Titanfall2GameplayVisuals.PlayFireSound( Scene, MountIdent ?? "titanfall2", "Weapon_FragGrenade_Throw_3P", GrenadeHand );
}
