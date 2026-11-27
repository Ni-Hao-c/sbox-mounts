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

public sealed partial class Titanfall2Npc
{
	HashSet<string> _actorClips;
	string _attackClip;
	TimeSince _sinceAttackCue;
	BoneCollection.Bone _gunBone;
	Transform _gunBindInverse;
	Titanfall2ActorAnimationData _actorAnimationData;
	Vector3 _lastAnimationPosition;
	Vector3 _animationVelocity;
	Titanfall2ActorAnimationPlayer _layeredAnimation;
	readonly Titanfall2LocomotionSelector _locomotionSelector = new();
	bool _attackPending;
	[Sync( SyncFlags.FromHost )] public Vector2 ActorAimAngles { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool IsInTermination { get; private set; }
	[Sync( SyncFlags.FromHost )] public string TerminationClipRole { get; private set; }

	internal float GetActorClipDuration( string role ) =>
		_actorAnimationData?.Clips?.FirstOrDefault( c => string.Equals( c.Role, role, StringComparison.OrdinalIgnoreCase ) ) is { Duration: > 0.1f } clip
			? clip.Duration
			: 0f;

	internal void BeginTerminationLock( bool isAttacker, string clipRole )
	{
		if ( !Networking.IsHost ) return;
		IsInTermination = true;
		TerminationClipRole = clipRole;
		GetComponent<Titanfall2WeaponSoundEmitter>()?.StopFiring();
		GetComponent<Titanfall2TitanCore>()?.CancelForTitanLife();
		Reloading = false;
		Moving = false;
		Movement?.Stop();
		if ( Movement.IsValid() ) Movement.Enabled = false;
		Agent?.Stop();
		if ( Agent.IsValid() ) Agent.Enabled = false;
		EndCurrentSchedule();
	}

	internal void EndTerminationLock()
	{
		if ( !Networking.IsHost ) return;
		IsInTermination = false;
		TerminationClipRole = null;
		if ( !Dead && Seat is not { IsOccupied: true } && NavigationMode == Navigation.Ground && MoveSpeed > 0 )
		{
			if ( Agent.IsValid() ) Agent.Enabled = true;
			if ( Movement.IsValid() && UsesEngineSchedules ) Movement.Enabled = true;
		}
	}

	internal void StartTerminationAnimation( string clipRole )
	{
		if ( string.IsNullOrWhiteSpace( clipRole ) || !Renderer.IsValid() || !Renderer.SceneModel.IsValid() ) return;
		// Full-body paired execution replaces layered locomotion/aim while active.
		if ( _actorClips?.Contains( clipRole ) != true ) return;
		ReleaseActorAnimation();
		PlaySequence( clipRole, looping: false, restart: true );
	}

	internal void SetAnimationAimDirection( Vector3 direction )
	{
		if ( !Networking.IsHost || IsProxy || direction.LengthSquared < 0.0001f ) return;
		var local = WorldRotation.Inverse * direction.Normal;
		var yaw = MathF.Atan2( local.y, local.x ) * (180f / MathF.PI);
		var pitch = -MathF.Atan2( local.z, MathF.Sqrt( local.x * local.x + local.y * local.y ) ) * (180f / MathF.PI);
		if ( float.IsFinite( yaw ) && float.IsFinite( pitch ) ) ActorAimAngles = new( yaw, pitch );
	}

	void UpdateActorAim()
	{
		if ( Networking.IsHost && !IsProxy && Seat is not { IsOccupied: true } )
		{
			// The native-schedule path owns a last-known contact snapshot. Do not
			// read a hidden enemy's current transform just to drive presentation.
			if ( UsesEngineSchedules && HasCombatContact ) SetAnimationAimDirection( _lastAimPoint - Eye );
			else if ( !UsesEngineSchedules && CanTarget( _target ) ) SetAnimationAimDirection( AimAt( _target ) - Eye );
			else ActorAimAngles = Vector2.Zero;
		}
		_layeredAnimation?.SetAim( ActorAimAngles.x, ActorAimAngles.y, IsTitan ? 280f : 480f );
	}

	void UpdateGunPose()
	{
		if ( _gunBone is not null && _gun.IsValid() && Renderer.IsValid() && Renderer.TryGetBoneTransform( _gunBone, out var pose ) )
			_gun.WorldTransform = pose.ToWorld( _gunBindInverse );
	}

	protected override void OnPreRender()
	{
		if ( !Dead ) UpdateGunPose();
	}

	void InitializeAnimation( Model model )
	{
		_layeredAnimation?.Dispose();
		_layeredAnimation = null;
		_actorClips = new( model.AnimationNames, StringComparer.OrdinalIgnoreCase );
		_actorAnimationData = model.GetData<Titanfall2ActorAnimationData>();
		_lastAnimationPosition = WorldPosition;
		Renderer.UseAnimGraph = false;
		// Keep Agent/seat motion authoritative; the legacy clip supplies the pose only.
		Renderer.Sequence.Blending = true;
		if ( !Application.IsDedicatedServer && !string.IsNullOrEmpty( _actorAnimationData?.Skeleton ) )
		{
			try { _layeredAnimation = new( Renderer, _actorAnimationData ); }
			catch ( Exception error ) { Titanfall2Log.Warning( $"TF2 layered animation unavailable for '{ModelPath}': {error.Message}" ); }
		}
		PlayLocomotion();
	}

	void CueActorAttack( bool melee )
	{
		_attackClip = melee ? ModelLoader.MeleeRole : ModelLoader.FireRole;
		_sinceAttackCue = 0;
		_attackPending = true;
	}

	float EffectiveReloadSeconds => MathF.Max( 0.1f, ReloadSeconds * (GetComponent<Titanfall2TitanCore>()?.ReloadMultiplier ?? 1f) );

	void PlayLocomotion()
	{
		if ( IsInTermination )
		{
			if ( !string.IsNullOrEmpty( TerminationClipRole ) && _actorClips?.Contains( TerminationClipRole ) == true )
				PlaySequence( TerminationClipRole, looping: false );
			return;
		}
		UpdateActorAim();
		if ( _actorClips is null || !Renderer.IsValid() || !Renderer.SceneModel.IsValid() ) return;
		if ( TryPlayMortarAnimation() ) return;
		var velocity = Seat is { IsOccupied: true }
			? Seat.DriveVelocity
			: (WorldPosition - _lastAnimationPosition) / MathF.Max( Time.Delta, 0.001f );
		_lastAnimationPosition = WorldPosition;
		if ( Seat is not { IsOccupied: true } && NavigationMode == Navigation.Ground && Agent.IsValid() && Agent.Velocity.LengthSquared > 1 )
			velocity = Agent.Velocity;
		if ( velocity.Length > MathF.Max( 900, MoveSpeed * 5 ) ) velocity = Vector3.Zero;
		_animationVelocity = Vector3.Lerp( _animationVelocity, velocity.WithZ( 0 ), Math.Clamp( Time.Delta * 14, 0, 1 ) );
		var speed = _animationVelocity.Length;
		var local = WorldRotation.Inverse * _animationVelocity;
		var (moving, direction, running) = _locomotionSelector.Evaluate( local, speed,
			ClipSpeed( "tf2_walk_" + _locomotionSelector.Direction, MoveSpeed * 0.65f ),
			ClipSpeed( "tf2_run_" + _locomotionSelector.Direction, MoveSpeed ) );

		if ( _layeredAnimation is not null ) { PlayLayeredLocomotion( speed, moving, direction, running ); return; }
		if ( ThrowingGrenade && _actorClips.Contains( ModelLoader.GrenadeRole ) )
		{
			if ( PlaySequence( ModelLoader.GrenadeRole, looping: false ) )
				Renderer.Sequence.PlaybackRate = Math.Clamp( Renderer.Sequence.Duration / MathF.Max( 0.2f, GrenadeWindupSeconds ), 0.35f, 8 );
			return;
		}
		if ( _attackClip == ModelLoader.MeleeRole && _actorClips.Contains( _attackClip ) )
		{
			var same = Renderer.Sequence.Name == _attackClip;
			if ( same && !Renderer.Sequence.IsFinished ) return;
			if ( _sinceAttackCue < 0.22f )
			{
				PlaySequence( _attackClip, looping: false, restart: same );
				Renderer.Sequence.PlaybackRate = Renderer.Sequence.Duration / MathF.Max( 0.2f, MeleeInterval * 0.85f );
				return;
			}
			_attackClip = null;
		}
		if ( Reloading && !moving && _actorClips.Contains( ModelLoader.ReloadRole ) )
		{
			if ( PlaySequence( ModelLoader.ReloadRole, looping: false ) )
				Renderer.Sequence.PlaybackRate = Math.Clamp( Renderer.Sequence.Duration / EffectiveReloadSeconds, 0.35f, 3f );
			return;
		}
		if ( !moving && _attackClip is not null && _actorClips.Contains( _attackClip ) )
		{
			var same = Renderer.Sequence.Name == _attackClip;
			if ( same && !Renderer.Sequence.IsFinished ) return;
			if ( _sinceAttackCue < 0.22f )
			{
				PlaySequence( _attackClip, looping: false, restart: same );
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
			var phase = ModelLoader.IsLocomotionRole( Renderer.Sequence.Name ) ? Renderer.Sequence.TimeNormalized : 0;
			if ( PlaySequence( clip, looping: true ) ) Renderer.Sequence.TimeNormalized = (float)Titanfall2ActorPhaseRules.NormalizePhase( phase );
			Renderer.Sequence.PlaybackRate = Math.Clamp( speed / MathF.Max( 1, ClipSpeed( clip, MoveSpeed ) ), 0.2f, 2.2f );
			return;
		}
		PlaySequence( ModelLoader.IdleRole, looping: true );
	}

	float ClipSpeed( string role, float fallback ) => _actorAnimationData?.Clips?.FirstOrDefault( x => x.Role == role ) is { Speed: > 1 } clip ? clip.Speed : fallback;

	void PlayLayeredLocomotion( float speed, bool moving, string direction, bool running )
	{
		var animation = _layeredAnimation;
		if ( ThrowingGrenade && animation.Has( ModelLoader.GrenadeRole ) )
		{
			animation.ClearOverlays();
			animation.Play( ModelLoader.GrenadeRole, animation.Duration( ModelLoader.GrenadeRole ) / MathF.Max( 0.2f, GrenadeWindupSeconds ) );
		}
		else if ( _attackPending && _attackClip == ModelLoader.MeleeRole && animation.Has( _attackClip ) )
		{
			animation.ClearOverlays();
			animation.Play( _attackClip, animation.Duration( _attackClip ) / MathF.Max( 0.2f, MeleeInterval * 0.85f ), restart: true );
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
			if ( !animation.HasUpperMask && Reloading && animation.Has( ModelLoader.ReloadRole ) )
				animation.Play( ModelLoader.ReloadRole, animation.Duration( ModelLoader.ReloadRole ) / EffectiveReloadSeconds );
			else if ( !animation.HasUpperMask && _attackPending && animation.Has( _attackClip ) )
				animation.Play( _attackClip, restart: true );
			else if ( animation.HasUpperMask || animation.Current != ModelLoader.FireRole || animation.Finished )
				animation.Play( role, moving ? Math.Clamp( speed / MathF.Max( 1, ClipSpeed( role, MoveSpeed ) ), 0.2f, 2.2f ) : 1, loop: true );
			if ( _attackPending && _attackClip == ModelLoader.FireRole && !Reloading ) animation.Fire( restart: true );
			animation.Reload( Reloading, EffectiveReloadSeconds );
			_attackPending = false;
		}
		animation.Tick();
	}

	void ReleaseActorAnimation() { _layeredAnimation?.Dispose(); _layeredAnimation = null; }
	protected override void OnDestroy() { ReleaseActorAnimation(); base.OnDestroy(); }

	bool PlaySequence( string name, bool looping, bool restart = false )
	{
		if ( name == ModelLoader.DeathRole ) ReleaseActorAnimation();
		if ( _actorClips?.Contains( name ) != true || !Renderer.IsValid() || !Renderer.SceneModel.IsValid() ) return false;
		var seq = Renderer.Sequence;
		if ( !restart && seq.Name == name ) return false;
		seq.PlaybackRate = 1;
		seq.Name = name;
		seq.Looping = looping;
		seq.Time = 0;
		return true;
	}
}
