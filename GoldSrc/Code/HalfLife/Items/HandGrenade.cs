namespace GoldSrc.HalfLife;

[Title( "Hand Grenade" ), Category( "Half-Life" )]
[WeaponPickup( "grenade", "grenades", 10, ClassName = "handgrenade", Bucket = 4, Weight = 5 )]
public sealed class HandGrenade : StudioWeapon
{
	internal override (string Name, float? Delay) IdleAnimation => Game.Random.Float( 0, 1 ) <= 0.75f
		? ("idle", Game.Random.Float( 10, 15 )) : ("fidget", 2.5f);
	bool _holdingPin, _primed, _redrawing;
	TimeSince _pinAge;
	TimeUntil _redrawAt;
	bool _released;

	internal override void Configure()
	{
		HoldType = WeaponHolds.Throwable;
		PrimaryDelay = 0.5f;
		PrimaryAutomatic = false;
		UsesClips = false;
		StartingAmmo = 5;
	}

	public override bool CanPrimaryAttack() => !_holdingPin && !_primed && !_redrawing && base.CanPrimaryAttack();
	protected override bool OnHolstering( BaseInventoryItem next ) => !_holdingPin && !_primed && base.OnHolstering( next );

	public override void PrimaryAttack()
	{
		_holdingPin = true;
		PullPin();
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void PullPin()
	{
		if ( !IsActive || !Owner.IsValid() || _primed || _redrawing || !HasPrimaryAmmo() ) return;
		_primed = true;
		_pinAge = 0;
		ShowAnimation( "pinpull" );
	}

	protected override void Think()
	{
		base.Think();
		if ( !_holdingPin || Input.Down( "attack1" ) ) return;
		_holdingPin = false;
		ReleasePin();
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void ReleasePin()
	{
		if ( IsActive && _primed && Owner.IsValid() ) _released = true;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( !Networking.IsHost || !IsActive || !Owner.IsValid() ) return;
		if ( _released && _pinAge >= 0.5f ) Throw();
		if ( !_redrawing || _redrawAt > 0 ) return;
		_redrawing = false;
		if ( HasPrimaryAmmo() ) ShowAnimation( DeploySequence );
		else Inventory.Remove( this );
	}

	void Throw()
	{
		_primed = false;
		_released = false;
		var angles = Owner.EyeAngles;
		angles.pitch = -10 + angles.pitch * (angles.pitch < 0 ? 80f : 100f) / 90;
		var direction = angles.ToRotation().Forward;
		var speed = System.Math.Clamp( (90 - angles.pitch) * 6.5f, 0, 1000 );
		var origin = BulletTrace( new Ray( Owner.EyePosition, direction ), 16, 2 ).Run().EndPosition;
		TimedGrenade.Throw( Owner.GameObject, origin, direction * speed + Owner.Velocity, System.MathF.Max( 0, 3 - _pinAge ) );
		TakePrimaryAmmo();
		ShowAnimation( speed < 500 ? "throw1" : speed < 1000 ? "throw2" : "throw3" );
		_redrawing = true;
		_redrawAt = 0.5f;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void ShowAnimation( string name )
	{
		if ( name.StartsWith( "throw" ) )
		{
			SetNextPrimaryFire( PrimaryDelay );
			PostWeaponEvent( WeaponEvents.Attack );
		}
		var view = ViewModel?.GetComponent<StudioWeaponView>();
		if ( name == DeploySequence ) view?.Play( name, idleAfter: Game.Random.Float( 10, 15 ) );
		else view?.Play( name, holdLastFrame: true );
	}

	protected override void OnHolstered()
	{
		_holdingPin = _primed = _redrawing = false;
		_released = false;
		base.OnHolstered();
	}
}
