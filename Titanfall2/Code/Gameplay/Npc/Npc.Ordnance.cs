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

/// <summary>Composed NPC frag-grenade evaluation, squad spacing, arc validation, and windup/release controller.</summary>
internal sealed class Titanfall2NpcOrdnanceController
{
	TimeUntil _check;
	TimeUntil _cooldown;
	TimeUntil _release;
	TimeUntil _opportunity;
	GameObject _observedTarget;
	Titanfall2NpcGrenadeData _pending;

	internal Vector3 Landing { get; private set; }

	internal static bool SupportsGrenades( string model ) => model?.Contains( "/humans/grunts/", StringComparison.OrdinalIgnoreCase ) == true
		|| model?.Contains( "/robots/spectre/", StringComparison.OrdinalIgnoreCase ) == true;

	static bool IsReady( Titanfall2Npc npc ) => Networking.IsHost && !npc.IsProxy && !npc.Dead && npc.CanFight && npc.UseGrenades && !npc.IsTitan && !npc.MeleeOnly
		&& npc.NavigationMode == Titanfall2Npc.Navigation.Ground && npc.Seat is not { IsOccupied: true } && SupportsGrenades( npc.ModelPath );

	internal Vector3 GrenadeHand( Titanfall2Npc npc ) => npc.EyePositionInternal + npc.WorldRotation.Forward * (npc.BodyRadius + 8) + npc.WorldRotation.Left * 7;

	// Called before the ordinary fire/movement decisions, including the first tick after losing a contact.
	internal bool Tick( Titanfall2Npc npc )
	{
		if ( !IsReady( npc ) || !npc.HasCombatContact || npc.MeleePending ) { Cancel( npc ); return false; }
		if ( npc.ThrowingGrenade )
		{
			npc.StopCombatMove();
			if ( _release > 0 ) return true;
			var profile = _pending;
			Cancel( npc );
			// Recheck friendly positions and the actual release point. Enemy movement never steers the committed throw.
			if ( profile is not null && AreaSafe( npc, Landing, profile.Projectile.SplashRadius )
				&& TryArc( npc, Landing, profile, out var velocity ) )
			{
				var settings = profile.Projectile with { MountIdent = npc.MountIdent,
					ModelPath = profile.Projectile.ModelPath is { } model ? $"mount://{npc.MountIdent ?? "titanfall2"}/{model}.vmdl" : null };
				if ( Titanfall2Projectile.Launch( npc.Scene, new Ray( GrenadeHand( npc ), velocity.Normal ), settings, 0, 0, npc.GameObject, npc.GameObject, npc.GameObject ) is not null )
				{
					npc.IncrementGrenadesThrownHost();
					_cooldown = Math.Clamp( npc.GrenadeCooldown, 4, 120 );
					npc.BroadcastGrenadeThrowSound();
				}
			}
			return true;
		}
		if ( npc.SeesContact && _observedTarget != npc.CurrentTarget )
		{
			_observedTarget = npc.CurrentTarget;
			_opportunity = 1.5f;
		}
		if ( _check > 0 || _cooldown > 0 || _opportunity > 0 || !npc.ReactionReady
			|| npc.Reloading || npc.GrenadesThrown >= Math.Clamp( npc.GrenadeCount, 0, 8 ) || _observedTarget != npc.CurrentTarget ) return false;
		_check = 0.8f;
		// Recent visual memory is valid for a flush-out throw. Radio reports alone cannot authorize it.
		if ( !npc.SeesContact && npc.VisualContactAgeSeconds > 1.5f ) return false;
		if ( Sandbox.Mounting.Directory.Get( npc.MountIdent ?? "titanfall2" ) is not Titanfall2Mount mount
			|| mount.GetNpcGrenadeData() is not { } data ) return false;
		var position = npc.LastContactPoint.Value + Vector3.Up * 8;
		return TryStart( npc, position, data );
	}

	internal bool TryStart( Titanfall2Npc npc, Vector3 landing, Titanfall2NpcGrenadeData profile )
	{
		if ( !IsReady( npc ) || npc.ThrowingGrenade || profile is null || !landing.IsFinite || _cooldown > 0
			|| npc.GrenadesThrown >= Math.Clamp( npc.GrenadeCount, 0, 8 ) || npc.MeleePending ) return false;
		var distance = landing.Distance( npc.WorldPosition );
		if ( distance < profile.MinRange || distance > profile.MaxRange || !AreaSafe( npc, landing, profile.Projectile.SplashRadius ) ) return false;
		var live = npc.Scene.GetAllComponents<Titanfall2Projectile>().Count( shot => shot.Settings.Behavior == Titanfall2ProjectileBehavior.FragGrenade );
		var pending = npc.Scene.GetAllComponents<Titanfall2Npc>().Count( other => other.ThrowingGrenade );
		if ( live + pending >= 6 ) return false;
		// Avoid multiple squadmates throwing grenades at the exact same spot simultaneously.
		if ( npc.Scene.GetAllComponents<Titanfall2Npc>().Any( ally => ally != npc && ally.ThrowingGrenade
			&& !npc.IsHostileToFaction( ally.Faction ) && ally.GrenadeLanding.Distance( landing ) < 240f ) ) return false;
		if ( !TryArc( npc, landing, profile, out _ ) ) return false;
		Landing = landing;
		_pending = profile;
		_release = profile.Windup;
		npc.SetGrenadeWindupHost( profile.Windup );
		npc.SetThrowingGrenadeHost( true );
		npc.SetNextShotDelay( profile.Windup + 0.2f );
		npc.StopCombatMove();
		return true;
	}

	internal void Cancel( Titanfall2Npc npc )
	{
		npc.SetThrowingGrenadeHost( false );
		_pending = null;
	}

	internal void OnDisabled( Titanfall2Npc npc )
	{
		Cancel( npc );
		_observedTarget = null;
	}

	static bool AreaSafe( Titanfall2Npc npc, Vector3 landing, float radius )
	{
		var safeDistance = MathF.Max( 160, radius + 64 );
		if ( npc.WorldPosition.Distance( landing ) < safeDistance ) return false;
		foreach ( var ally in npc.Scene.GetAllComponents<BaseNpc>() )
		{
			if ( ally == npc || !ally.IsValid() || ally.Health <= 0 || npc.IsHostileToFaction( ally.Faction ) ) continue;
			if ( ally.WorldPosition.Distance( landing ) < safeDistance ) return false;
		}
		if ( npc.Friendly || !npc.AttackPlayers )
			foreach ( var player in npc.Scene.GetAllComponents<PlayerController>() )
				if ( player.WorldPosition.Distance( landing ) < safeDistance ) return false;
		return true;
	}

	bool TryArc( Titanfall2Npc npc, Vector3 landing, Titanfall2NpcGrenadeData profile, out Vector3 result )
	{
		result = default;
		var origin = GrenadeHand( npc );
		var gravity = 800 * profile.Projectile.GravityScale;
		var start = Titanfall2CollisionQueries.Run( npc.Scene.Trace.Ray( npc.EyePositionInternal, origin ).Radius( profile.Projectile.Radius )
			.IgnoreGameObjectHierarchy( npc.GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Grenade );
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
				var trace = npc.Scene.Trace.Ray( previous, point ).Radius( profile.Projectile.Radius ).UseHitPosition()
					.IgnoreGameObjectHierarchy( npc.GameObject ).WithoutTags( "trigger", "projectile" );
				if ( npc.CurrentTarget.IsValid() ) trace = trace.IgnoreGameObjectHierarchy( npc.CurrentTarget );
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
}
