using Chassis = Titanfall2TitanKit.Chassis;

internal static class Titanfall2DefensePresentation
{
	internal enum Hit { Bullet, ProjectileOrBlast, Break, Sword }
	internal readonly record struct Cue( string FirstPerson, string ThirdPerson )
	{
		internal bool IsLocal( bool localPilot ) => localPilot && !string.IsNullOrEmpty( FirstPerson );
		internal string Select( bool localPilot ) => IsLocal( localPilot ) ? FirstPerson : ThirdPerson;
	}
	internal readonly record struct Guard( string Effect, Cue Enter, Cue Loop, Cue Exit );
	internal readonly record struct Impact( string Effect, string Sound );

	internal static readonly Cue VortexEnd = new( "vortex_shield_end_1P", "vortex_shield_end_3P" );
	internal static readonly Cue VortexThrow = new( "vortex_shield_throw_1P", "vortex_shield_throw_3P" );
	internal const string VortexTracer = "weapon_tracers_vortex";

	internal static Guard ForGuard( Chassis kit ) => kit switch
	{
		Chassis.Ion => new( "wpn_vortex_chargingCP_titan", new( "vortex_shield_start_1P", "vortex_shield_start_3P" ),
			new( "vortex_shield_loop_1P", "vortex_shield_loop_3P" ), default ), // ReleaseDefenseVolume owns the two Ion endcaps.
		Chassis.Legion => new( "P_titan_gun_shield_3P", new( "weapon_predator_mountedshield_start_1p", "weapon_predator_mountedshield_start_3p" ),
			default, new( "weapon_predator_mountedshield_stop_1p", "weapon_predator_mountedshield_stop_3p" ) ),
		Chassis.Scorch => new( "P_wpn_HeatShield", new( "heat_shield_1p_start", "heat_shield_3p_start" ),
			new( "heat_shield_1p_loop", "heat_shield_3p_loop" ), new( "heat_shield_1p_end", "heat_shield_3p_end" ) ),
		// Ordinary Sword Block is a pose, not a projected shield. Sword Core owns its weapon glow.
		Chassis.Ronin => new( null, new( null, "ronin_sword_draw_3p" ), default, default ),
		_ => default
	};

	internal static Impact ForImpact( Chassis kit, Hit hit ) => (kit, hit) switch
	{
		(Chassis.Legion, Hit.Break) => new( "P_xo_armor_break_CP", "titan_energyshield_down" ),
		// The script's default 3P TitanShieldWall.* impact events were not found in the local general bank.
		(Chassis.Legion, Hit.Bullet) => new( "P_impact_xo_shield_CP", null ),
		(Chassis.Legion, Hit.ProjectileOrBlast) => new( "P_impact_exp_med_xo_shield_CP", null ),
		(Chassis.Scorch, Hit.Bullet) => new( "P_wpn_HeatShield_impact", "heat_shield_stop_bullet" ),
		(Chassis.Scorch, Hit.ProjectileOrBlast) => new( "P_wpn_HeatShield_impact", "heat_shield_stop_projectile" ),
		(Chassis.Ion, Hit.Bullet or Hit.ProjectileOrBlast) => new( "wpn_vortex_shield_impact_titan", "Vortex_Shield_AbsorbBulletSmall" ),
		(Chassis.Ronin, Hit.Sword) => new( "P_impact_xo_sword", "ronin_sword_bullet_impacts" ),
		_ => default
	};

	// _vortex.nut GetTriLerpColor: control point 1 is RGB in 0..255, not a world-space endpoint.
	internal static Vector3 ChargeColor( float remaining )
	{
		var spent = float.IsFinite( remaining ) ? 1f - Math.Clamp( remaining, 0f, 1f ) : 0f;
		var full = new Vector3( 115, 247, 255 );
		var mid = new Vector3( 200, 128, 80 );
		var empty = new Vector3( 200, 80, 80 );
		return spent <= 0.75f ? Vector3.Lerp( full, mid, spent / 0.75f )
			: Vector3.Lerp( mid, empty, Math.Clamp( (spent - 0.75f) / 0.20f, 0f, 1f ) );
	}

	internal static bool SwordImpactIsInRetailArc( Vector3 fromSword, Vector3 forward ) => fromSword.IsFinite && fromSword.LengthSquared > 1f
		&& Vector3.Dot( fromSword.Normal, forward ) >= MathF.Cos( 150f * MathF.PI / 180f );
}

public sealed partial class Titanfall2TitanKit
{
	GameObject _defensePresentationRoot;
	Titanfall2Npc _defensePresentationActor;
	Chassis _defensePresentationChassis;
	Titanfall2ParticleControlPoints _defensePresentationPoints;
	Titanfall2SoundEventEmitter _defensePresentationEmitter;
	GameObject _defensePresentationPilot;
	bool _defensePresentationLocal;
	bool _defensePresentationWaitingForClear;
	TimeUntil _nextDefenseImpactPresentation;

	bool DefenseLocalPilot => Actor.Seat is { Active: true, IsEmbarking: false, LocalPilot: { Active: true } pilot }
		&& pilot.Mode is Sandbox.Movement.SitMoveMode;
	bool CanShowDefensePresentation => Actor.IsValid() && Actor.Active && Actor.TitanCanAct && !Actor.IsInTermination
		&& Actor.Seat is not { IsEmbarking: true };

	// Called from the Kit update before the host-only gameplay branch: proxies consume the same synced guard state.
	void UpdateDefensePresentation()
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer ) return;
		var profile = Titanfall2DefensePresentation.ForGuard( Kit );
		var sameOwner = Actor == _defensePresentationActor && Kit == _defensePresentationChassis;
		var canShow = CanShowDefensePresentation;
		var pilot = canShow ? Actor.Seat?.GetOccupant()?.GameObject : null;
		var samePilot = pilot == _defensePresentationPilot;
		var active = canShow && GuardRemaining > 0 && profile.Enter.ThirdPerson is not null
			&& (Kit != Chassis.Legion || GunShieldHealth > 0) && (Kit != Chassis.Ronin || RoninBlocking);
		if ( !active )
		{
			// End cues are for ordinary release/expiry (or shield break), not death, termination, disable or seat changes.
			if ( sameOwner && samePilot && canShow && _defensePresentationRoot.IsValid() )
				PlayDefenseCue( profile.Exit, _defensePresentationRoot.WorldPosition );
			ClearDefensePresentation();
			return;
		}
		if ( !sameOwner ) ClearDefensePresentation();
		else if ( _defensePresentationWaitingForClear ) return;
		else if ( !samePilot )
		{
			// Do not replay enter/loop for the new pilot while the last owner's guard still replicates as active.
			ClearDefensePresentation();
			_defensePresentationActor = Actor;
			_defensePresentationChassis = Kit;
			_defensePresentationWaitingForClear = true;
			return;
		}

		var local = DefenseLocalPilot;
		var pose = Kit == Chassis.Ronin ? Actor.WorldTransform : new Transform( DefenseCenter, DefenseFacing );
		if ( !_defensePresentationRoot.IsValid() )
		{
			_defensePresentationActor = Actor;
			_defensePresentationChassis = Kit;
			_defensePresentationPilot = pilot;
			_defensePresentationLocal = local;
			_defensePresentationRoot = new GameObject( Actor.GameObject, false, DefenseName );
			_defensePresentationRoot.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			_defensePresentationRoot.WorldTransform = pose;
			if ( profile.Effect is not null )
			{
				AddDefenseParticle( _defensePresentationRoot, profile.Effect );
				_defensePresentationPoints = _defensePresentationRoot.AddComponent<Titanfall2ParticleControlPoints>();
			}
			UpdateDefenseChargeColor();
			_defensePresentationEmitter = _defensePresentationRoot.AddComponent<Titanfall2SoundEventEmitter>();
			_defensePresentationRoot.Enabled = true;
			PlayGuardCue( profile.Enter, local, sustain: false );
			PlayGuardCue( profile.Loop, local, sustain: true );
			return;
		}

		_defensePresentationRoot.WorldTransform = pose;
		UpdateDefenseChargeColor();
		if ( local != _defensePresentationLocal )
		{
			_defensePresentationLocal = local;
			_defensePresentationEmitter.StopAll();
			PlayGuardCue( profile.Loop, local, sustain: true );
		}
	}

	void UpdateDefenseChargeColor()
	{
		if ( !_defensePresentationPoints.IsValid() ) return;
		var fraction = Kit switch
		{
			Chassis.Ion => (float)SharedEnergy / SharedEnergyMaximum,
			Chassis.Legion => GunShieldHealth / Titanfall2TitanDefense.GunShieldHealth,
			_ => ThermalShieldMeter
		};
		_defensePresentationPoints.SetControlPoint( 1, Titanfall2DefensePresentation.ChargeColor( fraction ) );
	}

	void PlayGuardCue( Titanfall2DefensePresentation.Cue cue, bool local, bool sustain )
	{
		if ( cue.Select( local ) is { } sound )
			_defensePresentationEmitter.Play( Actor.MountIdent, sound, cue.IsLocal( local ), _defensePresentationRoot.WorldPosition, sustain );
	}

	void ClearDefensePresentation()
	{
		if ( _defensePresentationEmitter.IsValid() ) _defensePresentationEmitter.StopAll();
		if ( _defensePresentationRoot.IsValid() ) _defensePresentationRoot.Destroy();
		_defensePresentationRoot = null;
		_defensePresentationActor = null;
		_defensePresentationPilot = null;
		_defensePresentationWaitingForClear = false;
		_defensePresentationPoints = null;
		_defensePresentationEmitter = null;
	}

	void AddDefenseParticle( GameObject root, string effect )
	{
		var retail = root.AddComponent<Titanfall2PlasmaFlight>();
		retail.MountIdent = Actor.MountIdent;
		retail.EffectName = effect;
	}

	void PlayDefenseCue( Titanfall2DefensePresentation.Cue cue, Vector3 point )
	{
		var local = DefenseLocalPilot;
		if ( cue.Select( local ) is { } name )
			Titanfall2SoundEventEmitter.OneShot( Scene, Actor.MountIdent, name, point, cue.IsLocal( local ) );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayVortexReleaseCue( Vector3 point, bool threw )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !Actor.IsValid() ) return;
		// A release RPC can arrive before GuardRemaining sync; stop the prior loop immediately either way.
		if ( _defensePresentationChassis == Chassis.Ion && _defensePresentationEmitter.IsValid() ) _defensePresentationEmitter.StopAll();
		PlayDefenseCue( threw ? Titanfall2DefensePresentation.VortexThrow : Titanfall2DefensePresentation.VortexEnd, point );
	}

	// Integration hook: only call for actual Ronin damage reduction, never for rejected/unblocked damage.
	void PresentRoninBlockImpact( in DamageInfo damage )
	{
		if ( !Networking.IsHost || IsProxy || Kit != Chassis.Ronin || !RoninBlocking || GuardRemaining <= 0 || !CanShowDefensePresentation ) return;
		var weaponPose = Actor.Renderer.IsValid() ? Actor.Renderer.GetAttachment( "PROPGUN" ) : null;
		var incoming = damage.Origin - (weaponPose?.Position ?? Eye);
		if ( !Titanfall2DefensePresentation.SwordImpactIsInRetailArc( incoming, weaponPose?.Rotation.Forward ?? Actor.WorldRotation.Forward ) ) return;
		var rotation = Rotation.LookAt( incoming ) * Rotation.FromPitch( 90 );
		PresentDefenseImpact( damage.Position + incoming.Normal * 200f, rotation, Titanfall2DefensePresentation.Hit.Sword );
	}

	void PresentDefenseImpact( Vector3 point, Rotation facing, Titanfall2DefensePresentation.Hit kind )
	{
		if ( !point.IsFinite ) return;
		// Keep shotgun/multi-hit bursts from spawning unbounded local effects. Breaks always get their own cue.
		if ( kind != Titanfall2DefensePresentation.Hit.Break && _nextDefenseImpactPresentation > 0 ) return;
		_nextDefenseImpactPresentation = 0.06f;
		PlayDefenseImpact( Kit, point, facing, kind );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayDefenseImpact( Chassis kit, Vector3 point, Rotation facing, Titanfall2DefensePresentation.Hit kind )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !Actor.IsValid() ) return;
		var impact = Titanfall2DefensePresentation.ForImpact( kit, kind );
		if ( impact.Effect is null ) return;
		var effect = new GameObject( Scene, false, impact.Effect );
		effect.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		effect.WorldTransform = new Transform( point, facing );
		if ( kind == Titanfall2DefensePresentation.Hit.Sword || kit == Chassis.Legion && kind != Titanfall2DefensePresentation.Hit.Break )
		{
			var points = effect.AddComponent<Titanfall2ParticleControlPoints>();
			points.SetControlPoint( 1, kind == Titanfall2DefensePresentation.Hit.Sword ? new Vector3( 255, 255, 255 )
				: Titanfall2DefensePresentation.ChargeColor( 1f ) );
		}
		AddDefenseParticle( effect, impact.Effect );
		var cleanup = effect.AddComponent<TemporaryEffect>(); cleanup.DestroyAfterSeconds = 1.2f; cleanup.WaitForChildEffects = false;
		effect.Enabled = true;
		if ( impact.Sound is { } sound ) Titanfall2SoundEventEmitter.OneShot( Scene, Actor.MountIdent, sound, point );
	}
}
