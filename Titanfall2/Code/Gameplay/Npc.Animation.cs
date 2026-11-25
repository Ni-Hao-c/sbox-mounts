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
	bool _attackPending;

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

	void PlayLocomotion()
	{
		if ( _actorClips is null || !Renderer.IsValid() || !Renderer.SceneModel.IsValid() ) return;
		var velocity = (WorldPosition - _lastAnimationPosition) / MathF.Max( Time.Delta, 0.001f );
		_lastAnimationPosition = WorldPosition;
		if ( NavigationMode == Navigation.Ground && Agent.IsValid() && Agent.Velocity.LengthSquared > 1 ) velocity = Agent.Velocity;
		if ( velocity.Length > MathF.Max( 500, MoveSpeed * 4 ) ) velocity = Vector3.Zero;
		_animationVelocity = Vector3.Lerp( _animationVelocity, velocity.WithZ( 0 ), Math.Clamp( Time.Delta * 16, 0, 1 ) );
		var speed = _animationVelocity.Length;
		var moving = speed > 8;
		if ( _layeredAnimation is not null ) { PlayLayeredLocomotion( speed, moving ); return; }
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
				Renderer.Sequence.PlaybackRate = Math.Clamp( Renderer.Sequence.Duration / MathF.Max( 0.1f, ReloadSeconds ), 0.35f, 3f );
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
			var local = WorldRotation.Inverse * _animationVelocity;
			var direction = MathF.Abs( local.y ) > MathF.Abs( local.x ) ? (local.y > 0 ? "left" : "right") : local.x < 0 ? "backward" : "forward";
			var walk = "tf2_walk_" + direction;
			var run = "tf2_run_" + direction;
			var walkSpeed = ClipSpeed( walk, MoveSpeed * 0.65f );
			var runSpeed = ClipSpeed( run, MoveSpeed );
			var clip = speed > (walkSpeed + runSpeed) * 0.5f ? run : walk;
			if ( !_actorClips.Contains( clip ) ) clip = _actorClips.Contains( walk ) ? walk : _actorClips.Contains( run ) ? run : ModelLoader.MoveRole;
			var phase = ModelLoader.IsLocomotionRole( Renderer.Sequence.Name ) ? Renderer.Sequence.TimeNormalized : 0;
			if ( PlaySequence( clip, looping: true ) ) Renderer.Sequence.TimeNormalized = phase % 1;
			Renderer.Sequence.PlaybackRate = Math.Clamp( speed / MathF.Max( 1, ClipSpeed( clip, MoveSpeed ) ), 0.2f, 2.2f );
			return;
		}
		PlaySequence( ModelLoader.IdleRole, looping: true );
	}

	float ClipSpeed( string role, float fallback ) => _actorAnimationData?.Clips?.FirstOrDefault( x => x.Role == role ) is { Speed: > 1 } clip ? clip.Speed : fallback;

	void PlayLayeredLocomotion( float speed, bool moving )
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
				var local = WorldRotation.Inverse * _animationVelocity;
				var direction = MathF.Abs( local.y ) > MathF.Abs( local.x ) ? local.y > 0 ? "left" : "right" : local.x < 0 ? "backward" : "forward";
				var walk = "tf2_walk_" + direction;
				var run = "tf2_run_" + direction;
				role = speed > (ClipSpeed( walk, MoveSpeed * 0.65f ) + ClipSpeed( run, MoveSpeed )) * 0.5f ? run : walk;
				if ( !animation.Has( role ) ) role = animation.Has( walk ) ? walk : animation.Has( run ) ? run : ModelLoader.MoveRole;
			}
			if ( !animation.HasUpperMask && Reloading && animation.Has( ModelLoader.ReloadRole ) )
				animation.Play( ModelLoader.ReloadRole, animation.Duration( ModelLoader.ReloadRole ) / MathF.Max( 0.1f, ReloadSeconds ) );
			else if ( !animation.HasUpperMask && _attackPending && animation.Has( _attackClip ) )
				animation.Play( _attackClip, restart: true );
			else if ( animation.HasUpperMask || animation.Current != ModelLoader.FireRole || animation.Finished )
				animation.Play( role, moving ? Math.Clamp( speed / MathF.Max( 1, ClipSpeed( role, MoveSpeed ) ), 0.2f, 2.2f ) : 1, loop: true );
			if ( _attackPending && _attackClip == ModelLoader.FireRole && !Reloading ) animation.Fire( restart: true );
			animation.Reload( Reloading, ReloadSeconds );
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
