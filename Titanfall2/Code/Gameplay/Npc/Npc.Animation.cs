/// <summary>Hysteresis-stabilized locomotion direction and gait selector preventing diagonal/speed animation chatter.</summary>
internal sealed class Titanfall2LocomotionSelector
{
	internal const float MoveStartSpeed = 12f;
	internal const float MoveStopSpeed = 6f;
	const float GaitHysteresisFraction = 0.12f;
	const float DirectionHysteresisRadians = 12f * (MathF.PI / 180f);

	bool _moving;
	bool _running;
	string _direction = "forward";

	internal bool IsMoving => _moving;
	internal bool IsRunning => _running;
	internal string Direction => _direction;

	internal (bool Moving, string Direction, bool Running) Evaluate( Vector3 localVelocity, float speed, float walkSpeed, float runSpeed )
	{
		if ( !float.IsFinite( speed ) || speed < 0 ) speed = 0;
		_moving = _moving ? speed > MoveStopSpeed : speed > MoveStartSpeed;
		if ( !_moving )
		{
			_running = false;
			return (false, _direction, false);
		}

		var midpoint = (MathF.Max( 1f, walkSpeed ) + MathF.Max( 1f, runSpeed )) * 0.5f;
		var margin = midpoint * GaitHysteresisFraction;
		_running = _running ? speed >= midpoint - margin : speed > midpoint + margin;

		var angle = MathF.Atan2( localVelocity.y, localVelocity.x );
		if ( float.IsFinite( angle ) && localVelocity.WithZ( 0 ).LengthSquared > 1f )
			_direction = ChooseDirection( angle, _direction );

		return (true, _direction, _running);
	}

	static string ChooseDirection( float angle, string current )
	{
		var abs = MathF.Abs( angle );
		const float quarter = MathF.PI * 0.25f;
		const float threeQuarter = MathF.PI * 0.75f;
		var h = DirectionHysteresisRadians;

		return current switch
		{
			"forward" when abs <= quarter + h => "forward",
			"backward" when abs >= threeQuarter - h => "backward",
			"left" when angle > 0 && abs >= quarter - h && abs <= threeQuarter + h => "left",
			"right" when angle < 0 && abs >= quarter - h && abs <= threeQuarter + h => "right",
			_ => abs <= quarter ? "forward" : abs >= threeQuarter ? "backward" : angle > 0 ? "left" : "right"
		};
	}
}

/// <summary>Composed NPC/Titan layered locomotion, aim-grid, termination-lock, and gun-bone pose controller.</summary>
internal sealed class Titanfall2NpcAnimationController
{
	HashSet<string> _actorClips;
	string _attackClip;
	TimeSince _sinceAttackCue;
	Vector3 _lastPosition;
	Vector3 _velocity;
	readonly Titanfall2LocomotionSelector _locomotionSelector = new();
	bool _attackPending;
	int _titanActionSerial;

	internal BoneCollection.Bone GunBone { get; private set; }
	internal Transform GunBindInverse { get; private set; }
	internal Titanfall2ActorAnimationData AnimationData { get; private set; }
	internal Titanfall2ActorAnimationPlayer LayeredAnimation;

	internal void SetGunBoneBinding( BoneCollection.Bone bone, Transform bindInverse ) { GunBone = bone; GunBindInverse = bindInverse; }
	internal void ClearGunBoneBinding() => GunBone = null;
	internal bool HasClip( string role ) => _actorClips?.Contains( role ) == true;

	internal float GetClipDuration( string role ) =>
		AnimationData?.Clips?.FirstOrDefault( c => string.Equals( c.Role, role, StringComparison.OrdinalIgnoreCase ) ) is { Duration: > 0.1f } clip
			? clip.Duration
			: 0f;

	internal void BeginTerminationLock( Titanfall2Npc npc, bool isAttacker, string clipRole )
	{
		if ( !Networking.IsHost ) return;
		npc.SetTerminationHost( true, clipRole );
		npc.CancelNpcActions( "termination" );
		npc.GetComponent<Titanfall2WeaponSoundEmitter>()?.StopFiring();
		npc.GetComponent<Titanfall2TitanCore>()?.CancelForTitanLife();
		npc.SetReloadingHost( false );
		npc.SetMovingHost( false );
		npc.Movement?.Stop();
		if ( npc.Movement.IsValid() ) npc.Movement.Enabled = false;
		npc.Agent?.Stop();
		if ( npc.Agent.IsValid() ) npc.Agent.Enabled = false;
		npc.EndCurrentScheduleInternal();
	}

	internal void EndTerminationLock( Titanfall2Npc npc )
	{
		if ( !Networking.IsHost ) return;
		npc.SetTerminationHost( false, null );
		if ( !npc.Dead && npc.Seat is not { IsOccupied: true } && npc.NavigationMode == Titanfall2Npc.Navigation.Ground && npc.MoveSpeed > 0 )
		{
			if ( npc.Agent.IsValid() ) npc.Agent.Enabled = true;
			if ( npc.Movement.IsValid() && npc.UsesEngineSchedules ) npc.Movement.Enabled = true;
		}
	}

	internal void StartTerminationAnimation( Titanfall2Npc npc, string clipRole )
	{
		if ( string.IsNullOrWhiteSpace( clipRole ) || !npc.Renderer.IsValid() || !npc.Renderer.SceneModel.IsValid() ) return;
		// Full-body paired execution replaces layered locomotion/aim while active.
		if ( !HasClip( clipRole ) ) return;
		Release();
		PlaySequence( npc, clipRole, looping: false, restart: true );
	}

	internal void SetAimDirection( Titanfall2Npc npc, Vector3 direction )
	{
		if ( !Networking.IsHost || npc.IsProxy || direction.LengthSquared < 0.0001f ) return;
		var local = npc.WorldRotation.Inverse * direction.Normal;
		var yaw = MathF.Atan2( local.y, local.x ) * (180f / MathF.PI);
		var pitch = -MathF.Atan2( local.z, MathF.Sqrt( local.x * local.x + local.y * local.y ) ) * (180f / MathF.PI);
		if ( float.IsFinite( yaw ) && float.IsFinite( pitch ) ) npc.SetActorAimAnglesHost( new( yaw, pitch ) );
	}

	void UpdateAim( Titanfall2Npc npc )
	{
		if ( Networking.IsHost && !npc.IsProxy && npc.Seat is not { IsOccupied: true } )
		{
			// The native-schedule path owns a last-known contact snapshot. Do not
			// read a hidden enemy's current transform just to drive presentation.
			if ( npc.UsesEngineSchedules && npc.HasCombatContact ) SetAimDirection( npc, npc.LastAimPoint - npc.EyePositionInternal );
			else if ( !npc.UsesEngineSchedules && npc.CanTarget( npc.CurrentTarget ) ) SetAimDirection( npc, Titanfall2Npc.AimAtInternal( npc.CurrentTarget ) - npc.EyePositionInternal );
			else npc.SetActorAimAnglesHost( Vector2.Zero );
		}
		LayeredAnimation?.SetAim( npc.ActorAimAngles.x, npc.ActorAimAngles.y, npc.IsTitan ? 280f : 480f );
	}

	internal void UpdateGunPose( Titanfall2Npc npc )
	{
		if ( GunBone is not null && npc.GunObject.IsValid() && npc.Renderer.IsValid() && npc.Renderer.TryGetBoneTransform( GunBone, out var pose ) )
			npc.GunObject.WorldTransform = pose.ToWorld( GunBindInverse );
	}

	internal void Initialize( Titanfall2Npc npc, Model model )
	{
		Release();
		_actorClips = new( model.AnimationNames, StringComparer.OrdinalIgnoreCase );
		AnimationData = model.GetData<Titanfall2ActorAnimationData>();
		_lastPosition = npc.WorldPosition;
		npc.Renderer.UseAnimGraph = false;
		// Keep Agent/seat motion authoritative; the legacy clip supplies the pose only.
		npc.Renderer.Sequence.Blending = true;
		if ( !Application.IsDedicatedServer && !string.IsNullOrEmpty( AnimationData?.Skeleton ) )
		{
			try { LayeredAnimation = new( npc.Renderer, AnimationData ); }
			catch ( Exception error ) { Titanfall2Log.Warning( $"TF2 layered animation unavailable for '{npc.ModelPath}': {error.Message}" ); }
		}
		PlayLocomotion( npc );
	}

	internal void CueAttack( bool melee )
	{
		_attackClip = melee ? ModelLoader.MeleeRole : ModelLoader.FireRole;
		_sinceAttackCue = 0;
		_attackPending = true;
	}

	static float EffectiveReloadSeconds( Titanfall2Npc npc ) =>
		MathF.Max( 0.1f, npc.ReloadSeconds * (npc.GetComponent<Titanfall2TitanCore>()?.ReloadMultiplier ?? 1f) );

	internal void PlayLocomotion( Titanfall2Npc npc )
	{
		// The paired interaction graph owns the renderer until commit/cancellation.
		if ( npc.Seat is { IsTransitioning: true } ) return;
		if ( npc.IsInTermination )
		{
			if ( !string.IsNullOrEmpty( npc.TerminationClipRole ) && HasClip( npc.TerminationClipRole ) )
				PlaySequence( npc, npc.TerminationClipRole, looping: false );
			return;
		}
		UpdateAim( npc );
		if ( _actorClips is null || !npc.Renderer.IsValid() || !npc.Renderer.SceneModel.IsValid() ) return;
		if ( npc.TryPlayMortarAnimation() ) return;
		if ( npc.TryPlayTraversalAnimation() ) return;
		if ( TryPlayTitanAction( npc ) ) return;
		var velocity = npc.Seat is { IsOccupied: true }
			? npc.Seat.DriveVelocity
			: (npc.WorldPosition - _lastPosition) / MathF.Max( Time.Delta, 0.001f );
		_lastPosition = npc.WorldPosition;
		if ( npc.Seat is not { IsOccupied: true } && npc.NavigationMode == Titanfall2Npc.Navigation.Ground && npc.Agent.IsValid() && npc.Agent.Velocity.LengthSquared > 1 )
			velocity = npc.Agent.Velocity;
		if ( velocity.Length > MathF.Max( 900, npc.MoveSpeed * 5 ) ) velocity = Vector3.Zero;
		_velocity = Vector3.Lerp( _velocity, velocity.WithZ( 0 ), Math.Clamp( Time.Delta * 14, 0, 1 ) );
		var speed = _velocity.Length;
		var local = npc.WorldRotation.Inverse * _velocity;
		var (moving, direction, running) = _locomotionSelector.Evaluate( local, speed,
			ClipSpeed( "tf2_walk_" + _locomotionSelector.Direction, npc.MoveSpeed * 0.65f ),
			ClipSpeed( "tf2_run_" + _locomotionSelector.Direction, npc.MoveSpeed ) );

		if ( LayeredAnimation is not null ) { PlayLayeredLocomotion( npc, speed, moving, direction, running ); return; }
		if ( npc.ThrowingGrenade && _actorClips.Contains( ModelLoader.GrenadeRole ) )
		{
			if ( PlaySequence( npc, ModelLoader.GrenadeRole, looping: false ) )
				npc.Renderer.Sequence.PlaybackRate = Math.Clamp( npc.Renderer.Sequence.Duration / MathF.Max( 0.2f, npc.GrenadeWindupSeconds ), 0.35f, 8 );
			return;
		}
		if ( _attackClip == ModelLoader.MeleeRole && _actorClips.Contains( _attackClip ) )
		{
			var same = npc.Renderer.Sequence.Name == _attackClip;
			if ( same && !npc.Renderer.Sequence.IsFinished ) return;
			if ( _sinceAttackCue < 0.22f )
			{
				PlaySequence( npc, _attackClip, looping: false, restart: same );
				npc.Renderer.Sequence.PlaybackRate = npc.Renderer.Sequence.Duration / MathF.Max( 0.2f, npc.MeleeInterval * 0.85f );
				return;
			}
			_attackClip = null;
		}
		if ( npc.Reloading && !moving && _actorClips.Contains( ModelLoader.ReloadRole ) )
		{
			if ( PlaySequence( npc, ModelLoader.ReloadRole, looping: false ) )
				npc.Renderer.Sequence.PlaybackRate = Math.Clamp( npc.Renderer.Sequence.Duration / EffectiveReloadSeconds( npc ), 0.35f, 3f );
			return;
		}
		if ( !moving && _attackClip is not null && _actorClips.Contains( _attackClip ) )
		{
			var same = npc.Renderer.Sequence.Name == _attackClip;
			if ( same && !npc.Renderer.Sequence.IsFinished ) return;
			if ( _sinceAttackCue < 0.22f )
			{
				PlaySequence( npc, _attackClip, looping: false, restart: same );
				return;
			}
			_attackClip = null;
		}
		if ( moving )
		{
			var walk = "tf2_walk_" + direction;
			var run = "tf2_run_" + direction;
			var clip = running ? run : walk;
			if ( !_actorClips.Contains( clip ) ) clip = _actorClips.Contains( walk ) ? walk : _actorClips.Contains( run ) ? run : ModelLoader.MoveRole;
			var phase = ModelLoader.IsLocomotionRole( npc.Renderer.Sequence.Name ) ? npc.Renderer.Sequence.TimeNormalized : 0;
			if ( PlaySequence( npc, clip, looping: true ) ) npc.Renderer.Sequence.TimeNormalized = (float)Titanfall2ActorPhaseRules.NormalizePhase( phase );
			npc.Renderer.Sequence.PlaybackRate = Math.Clamp( speed / MathF.Max( 1, ClipSpeed( clip, npc.MoveSpeed ) ), 0.2f, 2.2f );
			return;
		}
		PlaySequence( npc, ModelLoader.IdleRole, looping: true );
	}

	float ClipSpeed( string role, float fallback ) => AnimationData?.Clips?.FirstOrDefault( x => x.Role == role ) is { Speed: > 1 } clip ? clip.Speed : fallback;

	bool TryPlayTitanAction( Titanfall2Npc npc )
	{
		var kit = npc.GetComponent<Titanfall2TitanKit>();
		var rearm = Titanfall2RearmPresentation.Sample( kit?.RearmVisualElapsed ?? float.PositiveInfinity, firstPerson: false );
		var role = rearm.Clip ?? (kit is { AiPhase: not Titanfall2TitanAiPhase.Idle } ? kit.AiAnimationRole : null);
		if ( string.IsNullOrEmpty( role ) || !HasClip( role ) )
		{
			LayeredAnimation?.ClearActionOverlay();
			return false;
		}
		var restart = _titanActionSerial != kit.AiActionSerial;
		_titanActionSerial = kit.AiActionSerial;
		var cycle = rearm.Clip is null ? Math.Clamp( kit.AiActionProgress, 0, 1 ) : rearm.Cycle;
		if ( LayeredAnimation is { } layered && layered.Has( role ) )
		{
			if ( layered.SetActionOverlay( role, cycle, restart ) ) return false;
			layered.ClearActionOverlay();
			if ( kit.AiBlocksPrimary ) layered.ClearOverlays();
			else
			{
				if ( _attackPending && _attackClip == ModelLoader.FireRole && !npc.Reloading ) layered.Fire( restart: true );
				layered.Reload( npc.Reloading, EffectiveReloadSeconds( npc ) );
			}
			layered.Play( role, restart: restart );
			layered.SetCycle( cycle );
			layered.Tick();
			_attackPending = false;
		}
		else
		{
			PlaySequence( npc, role, looping: false, restart: restart );
			npc.Renderer.Sequence.PlaybackRate = 0;
			npc.Renderer.Sequence.TimeNormalized = cycle;
		}
		return true;
	}

	void PlayLayeredLocomotion( Titanfall2Npc npc, float speed, bool moving, string direction, bool running )
	{
		var animation = LayeredAnimation;
		var reloadSeconds = EffectiveReloadSeconds( npc );
		if ( npc.ThrowingGrenade && animation.Has( ModelLoader.GrenadeRole ) )
		{
			animation.ClearOverlays();
			animation.Play( ModelLoader.GrenadeRole, animation.Duration( ModelLoader.GrenadeRole ) / MathF.Max( 0.2f, npc.GrenadeWindupSeconds ) );
		}
		else if ( _attackPending && _attackClip == ModelLoader.MeleeRole && animation.Has( _attackClip ) )
		{
			animation.ClearOverlays();
			animation.Play( _attackClip, animation.Duration( _attackClip ) / MathF.Max( 0.2f, npc.MeleeInterval * 0.85f ), restart: true );
			_attackPending = false;
		}
		else if ( animation.Current != ModelLoader.MeleeRole || animation.Finished )
		{
			var role = ModelLoader.IdleRole;
			if ( moving )
			{
				var walk = "tf2_walk_" + direction;
				var run = "tf2_run_" + direction;
				role = running ? run : walk;
				if ( !animation.Has( role ) ) role = animation.Has( walk ) ? walk : animation.Has( run ) ? run : ModelLoader.MoveRole;
			}
			if ( !animation.HasUpperMask && npc.Reloading && animation.Has( ModelLoader.ReloadRole ) )
				animation.Play( ModelLoader.ReloadRole, animation.Duration( ModelLoader.ReloadRole ) / reloadSeconds );
			else if ( !animation.HasUpperMask && _attackPending && animation.Has( _attackClip ) )
				animation.Play( _attackClip, restart: true );
			else if ( animation.HasUpperMask || animation.Current != ModelLoader.FireRole || animation.Finished )
				animation.Play( role, moving ? Math.Clamp( speed / MathF.Max( 1, ClipSpeed( role, npc.MoveSpeed ) ), 0.2f, 2.2f ) : 1, loop: true );
			if ( _attackPending && _attackClip == ModelLoader.FireRole && !npc.Reloading ) animation.Fire( restart: true );
			animation.Reload( npc.Reloading, reloadSeconds );
			_attackPending = false;
		}
		animation.Tick();
	}

	internal void Release() { LayeredAnimation?.Dispose(); LayeredAnimation = null; }

	internal bool PlaySequence( Titanfall2Npc npc, string name, bool looping, bool restart = false )
	{
		if ( name == ModelLoader.DeathRole ) Release();
		if ( _actorClips?.Contains( name ) != true || !npc.Renderer.IsValid() || !npc.Renderer.SceneModel.IsValid() ) return false;
		var seq = npc.Renderer.Sequence;
		if ( !restart && seq.Name == name ) return false;
		seq.PlaybackRate = 1;
		seq.Name = name;
		seq.Looping = looping;
		seq.Time = 0;
		return true;
	}
}
