using Chassis = Titanfall2TitanKit.Chassis;

public sealed partial class Titanfall2TitanCockpitView
{
	const int ScorchArcMaxPoints = 36;
	const int ScorchRingSegments = 16;
	const float ScorchRingRadius = 26f;

	GameObject _scorchArcRoot;
	LineRenderer _scorchArcLine;
	readonly List<GameObject> _scorchArcNodes = [];
	readonly List<Vector3> _scorchArcSamples = new( ScorchArcMaxPoints );

	GameObject _scorchRingRoot;
	LineRenderer _scorchRingLine;
	readonly List<GameObject> _scorchRingNodes = [];

	GameObject _adsAudioRoot;
	Titanfall2SoundEventEmitter _adsAudio;
	Chassis _adsAudioChassis = Chassis.None;
	int _lastNorthstarChargeLevel;
	bool _wasNorthstarCharging;
	bool _wasNorthstarFull;
	float _lastLegionSpin;
	bool _wasLegionSpinning;

	void UpdateAdsPreview( Titanfall2Npc actor, Titanfall2TitanKit kit, Titanfall2TitanCore core )
	{
		var pilot = GetPilot();
		if ( !pilot.IsValid() || !actor.IsValid() || !kit.IsValid() )
		{
			ClearAdsPreview();
			return;
		}

		UpdateScorchArcPreview( pilot, actor, kit, core );
		UpdatePrimaryAdsAudio( pilot, actor, kit, core );
	}

	void UpdateScorchArcPreview( PlayerController pilot, Titanfall2Npc actor, Titanfall2TitanKit kit, Titanfall2TitanCore core )
	{
		var showArc = kit.Kit == Chassis.Scorch
			&& kit.PrimaryZoomFraction > 0.15f
			&& !actor.Reloading
			&& !kit.DefenseVolumeActive
			&& core is not { BlocksPrimary: true }
			&& actor.Projectile.Enabled
			&& actor.Projectile.Speed > 1f;

		if ( !showArc )
		{
			SetScorchArcVisible( false );
			return;
		}

		EnsureScorchArcVisuals();
		if ( !_scorchArcLine.IsValid() ) return;

		var eyeRay = pilot.EyeTransform.ForwardRay;
		var origin = actor.PrimaryMuzzlePosition;
		var range = MathF.Max( 256f, actor.AttackRange );
		var aimHit = Titanfall2CollisionQueries.Run(
			Scene.Trace.Ray( eyeRay, range ).IgnoreGameObjectHierarchy( actor.GameObject ).UseHitboxes().WithoutTags( "trigger", "projectile" ),
			Titanfall2TraceMask.Shot );
		var launchDir = (aimHit.EndPosition - origin).LengthSquared > 1f
			? (aimHit.EndPosition - origin).Normal
			: eyeRay.Forward.Normal;

		var lifetime = Math.Clamp( actor.Projectile.Lifetime > 0f ? actor.Projectile.Lifetime : 3f, 0.5f, 3.5f );
		var hasImpact = Titanfall2TitanPrimaryAdsRules.SimulateBallisticArc(
			origin,
			launchDir * actor.Projectile.Speed,
			actor.Projectile.GravityScale,
			lifetime,
			stepSeconds: 0.045f,
			maxPoints: ScorchArcMaxPoints,
			trace: ( a, b ) =>
			{
				var hit = Titanfall2CollisionQueries.Run(
					Scene.Trace.Ray( a, b ).IgnoreGameObjectHierarchy( actor.GameObject ).WithoutTags( "trigger", "projectile" ),
					Titanfall2TraceMask.Grenade );
				return (hit.Hit || hit.StartedSolid, hit.EndPosition, hit.Normal);
			},
			_scorchArcSamples,
			out var impactPoint,
			out var impactNormal );

		if ( _scorchArcSamples.Count < 2 )
		{
			SetScorchArcVisible( false );
			return;
		}

		var lastPoint = _scorchArcSamples[^1];
		for ( var i = 0; i < _scorchArcNodes.Count; i++ )
			_scorchArcNodes[i].WorldPosition = i < _scorchArcSamples.Count ? _scorchArcSamples[i] : lastPoint;

		_scorchArcRoot.Enabled = true;

		if ( hasImpact && _scorchRingRoot.IsValid() )
		{
			var basis = Rotation.LookAt( impactNormal );
			var center = impactPoint + impactNormal * 2.5f;
			for ( var i = 0; i < _scorchRingNodes.Count; i++ )
			{
				var angle = i * (MathF.PI * 2f / ScorchRingSegments);
				var radial = (basis.Right * MathF.Cos( angle ) + basis.Up * MathF.Sin( angle )) * ScorchRingRadius;
				_scorchRingNodes[i].WorldPosition = center + radial;
			}
			_scorchRingRoot.Enabled = true;
		}
		else if ( _scorchRingRoot.IsValid() )
		{
			_scorchRingRoot.Enabled = false;
		}
	}

	void EnsureScorchArcVisuals()
	{
		if ( !_scorchArcRoot.IsValid() )
		{
			_scorchArcNodes.Clear();
			_scorchArcRoot = new GameObject( Scene, false, "TF2 Scorch ADS Trajectory Arc" );
			_scorchArcRoot.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked | GameObjectFlags.Absolute;
			for ( var i = 0; i < ScorchArcMaxPoints; i++ )
			{
				var node = new GameObject( _scorchArcRoot, true, "ArcNode" );
				_scorchArcNodes.Add( node );
			}
			_scorchArcLine = _scorchArcRoot.AddComponent<LineRenderer>();
			_scorchArcLine.Points = _scorchArcNodes;
			_scorchArcLine.Width = 2.6f;
			_scorchArcLine.Color = new Color( 1f, 0.48f, 0.08f ) * 2.4f;
			_scorchArcLine.Additive = true;
			_scorchArcLine.Opaque = false;
			_scorchArcLine.CastShadows = false;
		}

		if ( !_scorchRingRoot.IsValid() )
		{
			_scorchRingNodes.Clear();
			_scorchRingRoot = new GameObject( Scene, false, "TF2 Scorch ADS Impact Ring" );
			_scorchRingRoot.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked | GameObjectFlags.Absolute;
			for ( var i = 0; i <= ScorchRingSegments; i++ )
			{
				var node = new GameObject( _scorchRingRoot, true, "RingNode" );
				_scorchRingNodes.Add( node );
			}
			_scorchRingLine = _scorchRingRoot.AddComponent<LineRenderer>();
			_scorchRingLine.Points = _scorchRingNodes;
			_scorchRingLine.Width = 2.2f;
			_scorchRingLine.Color = new Color( 1f, 0.35f, 0.05f ) * 3.0f;
			_scorchRingLine.Additive = true;
			_scorchRingLine.Opaque = false;
			_scorchRingLine.CastShadows = false;
		}
	}

	void SetScorchArcVisible( bool visible )
	{
		if ( _scorchArcRoot.IsValid() ) _scorchArcRoot.Enabled = visible;
		if ( _scorchRingRoot.IsValid() ) _scorchRingRoot.Enabled = visible;
	}

	void UpdatePrimaryAdsAudio( PlayerController pilot, Titanfall2Npc actor, Titanfall2TitanKit kit, Titanfall2TitanCore core )
	{
		if ( _adsAudioChassis != kit.Kit )
		{
			StopAdsAudio();
			_adsAudioChassis = kit.Kit;
		}

		var ident = actor.MountIdent ?? "titanfall2";
		var pos = pilot.EyeTransform.Position;

		if ( kit.Kit == Chassis.Northstar )
		{
			var charge = kit.NorthstarChargeFraction;
			var level = kit.NorthstarChargeLevel;
			var charging = kit.PrimaryZoomFraction >= 0.85f && !actor.Reloading && core is not { BlocksPrimary: true };

			if ( charging && !_wasNorthstarCharging )
			{
				EnsureAdsAudio( pos );
				_adsAudio.Play( ident, "Weapon_Titan_Sniper_WindUp", firstPerson: true, pos );
			}

			if ( level > _lastNorthstarChargeLevel && charging )
			{
				EnsureAdsAudio( pos );
				_adsAudio.Play( ident, Titanfall2TitanPrimaryAdsRules.NorthstarLevelTickSound( level ), firstPerson: true, pos );
			}

			var full = level >= Titanfall2TitanPrimaryAdsRules.NorthstarMaxChargeLevel && charging;
			if ( full && !_wasNorthstarFull )
			{
				EnsureAdsAudio( pos );
				_adsAudio.Play( ident, "Weapon_Titan_Sniper_SustainLoop", firstPerson: true, pos, sustain: true );
			}
			else if ( !full && _wasNorthstarFull )
			{
				_adsAudio?.StopSustain();
			}

			if ( !charging && _wasNorthstarCharging && charge > 0.05f )
			{
				EnsureAdsAudio( pos );
				_adsAudio.StopSustain();
				_adsAudio.Play( ident, "Weapon_Titan_Sniper_WindDown", firstPerson: true, pos,
					seekSeconds: (1f - charge) * Titanfall2TitanPrimaryAdsRules.NorthstarCooldownSeconds );
			}

			_adsAudio?.SetSource( pos );
			_lastNorthstarChargeLevel = level;
			_wasNorthstarCharging = charging;
			_wasNorthstarFull = full;
			return;
		}

		if ( kit.Kit == Chassis.Legion )
		{
			var spin = kit.LegionSpinFraction;
			var spinningUp = spin > _lastLegionSpin + 0.0005f || (spin >= Titanfall2TitanPrimaryAdsRules.LegionSpinReadyThreshold && !actor.Reloading);
			var spinningDown = spin < _lastLegionSpin - 0.0005f;

			if ( spinningUp && !_wasLegionSpinning )
			{
				EnsureAdsAudio( pos );
				_adsAudio.StopAll();
				_adsAudio.Play( ident, "weapon_predator_windup_1p", firstPerson: true, pos, seekSeconds: spin * 1.5f );
				_adsAudio.Play( ident, "Weapon_Predator_MotorLoop_1P", firstPerson: true, pos, sustain: true );
				_wasLegionSpinning = true;
			}
			else if ( spinningDown && _wasLegionSpinning )
			{
				EnsureAdsAudio( pos );
				_adsAudio.StopAll();
				_adsAudio.Play( ident, "weapon_predator_winddown_1p", firstPerson: true, pos, seekSeconds: (1f - spin) * 0.75f );
				_wasLegionSpinning = false;
			}
			else if ( spin <= 0.001f && _wasLegionSpinning )
			{
				_adsAudio?.StopAll();
				_wasLegionSpinning = false;
			}

			_adsAudio?.SetSource( pos );
			_lastLegionSpin = spin;
		}
	}

	void EnsureAdsAudio( Vector3 position )
	{
		if ( _adsAudioRoot.IsValid() && _adsAudio.IsValid() )
		{
			_adsAudioRoot.WorldPosition = position;
			return;
		}
		_adsAudioRoot = new GameObject( Scene, false, "TF2 Titan Primary ADS Audio" );
		_adsAudioRoot.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked | GameObjectFlags.Absolute;
		_adsAudioRoot.WorldPosition = position;
		_adsAudio = _adsAudioRoot.AddComponent<Titanfall2SoundEventEmitter>();
		_adsAudioRoot.Enabled = true;
	}

	void StopAdsAudio()
	{
		_adsAudio?.StopAll();
		_lastNorthstarChargeLevel = 0;
		_wasNorthstarCharging = false;
		_wasNorthstarFull = false;
		_lastLegionSpin = 0f;
		_wasLegionSpinning = false;
	}

	void ClearAdsPreview()
	{
		StopAdsAudio();
		_adsAudioChassis = Chassis.None;
		if ( _adsAudioRoot.IsValid() ) _adsAudioRoot.Destroy();
		_adsAudioRoot = null;
		_adsAudio = null;

		if ( _scorchArcRoot.IsValid() ) _scorchArcRoot.Destroy();
		_scorchArcRoot = null;
		_scorchArcLine = null;
		_scorchArcNodes.Clear();
		_scorchArcSamples.Clear();

		if ( _scorchRingRoot.IsValid() ) _scorchRingRoot.Destroy();
		_scorchRingRoot = null;
		_scorchRingLine = null;
		_scorchRingNodes.Clear();
	}
}
