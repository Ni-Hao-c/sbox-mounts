namespace GoldSrc.HalfLife;

[Hide]
public abstract class ProjectileWeapon : StudioWeapon
{
	TimeUntil _nextLaunch, _nextSecondary;

	public override void PrimaryAttack() => Launch( AimRay );

	[Rpc.Host( NetFlags.OwnerOnly )]
	void Launch( Ray ray ) => TryLaunch( ray, Release );

	protected void TryLaunch( Ray ray, System.Func<Ray, bool> release, bool secondary = false, float extraDelay = 0, bool keepIdleDeadline = false )
	{
		if ( !IsActive || !Owner.IsValid() || (secondary ? _nextSecondary : _nextLaunch) > 0 || !ray.Position.IsFinite || !ray.Forward.IsFinite ||
			ray.Position.Distance( Owner.EyePosition ) > 96 || !HasPrimaryAmmo() ) return;
		if ( !release( ray ) ) return;
		TakePrimaryAmmo();
		_nextLaunch = (secondary ? SecondaryDelay : PrimaryDelay) + extraDelay;
		if ( secondary ) _nextSecondary = _nextLaunch;
		ShowLaunch( ShotSequence, IdleAfterShot, keepIdleDeadline );
	}

	internal abstract bool Release( Ray ray );

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void ShowLaunch( string sequence, float? idleAfter, bool keepIdleDeadline )
	{
		if ( ViewModel?.GetComponent<StudioWeaponView>() is { } view )
		{
			if ( keepIdleDeadline ) idleAfter = view.RemainingIdleTime;
			if ( sequence is not null ) view.Play( sequence, idleAfter: idleAfter );
			else if ( idleAfter.HasValue ) view.RemainingIdleTime = idleAfter.Value;
		}
		PlayLaunchSound();
		PostWeaponEvent( WeaponEvents.Attack );
	}

	internal virtual void PlayLaunchSound()
	{
		if ( ShotSound is { } sound ) PlaySample( sound );
	}
}
