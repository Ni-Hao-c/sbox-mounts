public sealed partial class Titanfall2Weapon
{
	[Property] public Titanfall2ViewKickProfile ViewKick { get; set; }
	readonly Titanfall2ViewKickState _viewKick = new();
	PlayerController _viewKickOwner;
	int _viewKickShotSeed;

	bool CanUseViewKick => ViewKick.Enabled && !IsProxy && IsHeld && IsActive && Owner.IsValid();

	void TickViewKick()
	{
		if ( !CanUseViewKick ) { ResetViewKick(); return; }
		EnsureViewKickOwner();
		// A queued burst continues after trigger release. Do not mark its later rounds first-shot.
		if ( Owner.UseInputControls && _burstRemaining == 0 && !Input.Down( "attack1" ) ) _viewKick.EndBurst();
		_viewKick.Advance( ViewKick, Time.Delta, _adsBlend );
	}

	// Call once after the OnShootEffects NoEvents guard, never once per pellet/impact.
	void FireViewKick()
	{
		if ( !CanUseViewKick ) return;
		EnsureViewKickOwner();
		var owner = Owner;
		// The controller exposes a binary duck state; native fractional duck and special
		// hover movement states are not synthesized here.
		var permanent = _viewKick.Fire( ViewKick, unchecked( ++_viewKickShotSeed ), _adsBlend,
			crouchFraction: owner.IsDucking ? 1 : 0, grounded: owner.IsOnGround,
			movingFast: owner.Velocity.WithZ( 0 ).Length > MathF.Max( owner.RunSpeed, 1 ) * 0.5f );
		if ( permanent.pitch == 0 && permanent.yaw == 0 ) return;
		var angles = owner.EyeAngles + permanent;
		if ( owner.PitchClamp > 0 ) angles.pitch = Math.Clamp( angles.pitch, -owner.PitchClamp, owner.PitchClamp );
		owner.EyeAngles = angles;
	}

	void EnsureViewKickOwner()
	{
		var owner = Owner;
		if ( _viewKickOwner == owner ) return;
		_viewKick.Reset();
		_viewKickOwner = owner;
	}

	void ResetViewKick()
	{
		_viewKick.Reset();
		_viewKickOwner = null;
	}

	void ApplyViewKickCamera( ref CameraView view )
	{
		if ( CanUseViewKick && _viewKickOwner == Owner )
			view.Rotation *= _viewKick.CameraAngles( ViewKick, _adsBlend ).ToRotation();
	}

	Rotation ViewKickWeaponRotation => CanUseViewKick && _viewKickOwner == Owner
		? _viewKick.WeaponAngles( ViewKick, _adsBlend ).ToRotation() : Rotation.Identity;

	Ray ViewKickAimRay
	{
		get
		{
			var ray = base.AimRay;
			if ( !CanUseViewKick || _viewKickOwner != Owner ) return ray;
			var owner = Owner;
			// Base AimRay reads Camera.WorldTransform in third person. ComposeView writes
			// Camera.View, not WorldTransform: this ray has no camera punch to subtract.
			// Keep the base ray's origin and rotate its original orientation by full punch.
			var rotation = owner.ThirdPerson && Scene.Camera.IsValid()
				? Scene.Camera.WorldRotation : owner.EyeTransform.Rotation;
			return new Ray( ray.Position, (rotation * _viewKick.AimAngles.ToRotation()).Forward );
		}
	}
}
