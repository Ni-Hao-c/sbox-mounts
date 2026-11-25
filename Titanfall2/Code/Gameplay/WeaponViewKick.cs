/// <summary>Retail pitch/yaw use half-width random ranges; roll uses its explicit min/max.</summary>
public record struct Titanfall2ViewKickAxis
{
	public float Base { get; set; }
	public float RandomMin { get; set; }
	public float RandomMax { get; set; }
	public float SoftScale { get; set; }
	public float HardScale { get; set; }
}

public record struct Titanfall2ViewKickSpring
{
	public Vector3 Constant { get; set; }
	public Vector3 Damping { get; set; }
}

/// <summary>WeaponData viewkick fields plus the named WeaponSprings resource.</summary>
public record struct Titanfall2ViewKickProfile
{
	public bool Enabled { get; set; }
	public string SpringName { get; set; }
	public Titanfall2ViewKickSpring HipSpring { get; set; }
	public Titanfall2ViewKickSpring AdsSpring { get; set; }
	public Titanfall2ViewKickAxis Pitch { get; set; }
	public Titanfall2ViewKickAxis Yaw { get; set; }
	public Titanfall2ViewKickAxis Roll { get; set; }
	public Titanfall2ViewKickAxis PermanentPitch { get; set; }
	public Titanfall2ViewKickAxis PermanentYaw { get; set; }
	public float HipWeaponFraction { get; set; }
	public float AdsWeaponFraction { get; set; }
	public float HipWeaponScale { get; set; }
	public float AdsWeaponScale { get; set; }
	public float FirstShotHip { get; set; }
	public float FirstShotAds { get; set; }
	public float MinHip { get; set; }
	public float MaxHip { get; set; }
	public float MinAds { get; set; }
	public float MaxAds { get; set; }
	public float ValuePerShot { get; set; }
	public float ValueLerpStart { get; set; }
	public float ValueLerpEnd { get; set; }
	public float ValueDecayDelay { get; set; }
	public float ValueDecayRate { get; set; }
	public float DuckScale { get; set; }
	public float HoverScale { get; set; }
	public float MoveScale { get; set; }
	public float AirScaleAds { get; set; }
}

/// <summary>
/// One weapon-punch spring, separate from movement/fall punch. Fire once per accepted
/// local shot, Advance once per update; rendering and aiming only sample the state.
/// </summary>
internal sealed class Titanfall2ViewKickState
{
	internal Vector3 Punch { get; private set; }
	internal Vector3 Velocity { get; private set; }
	internal int ShotsInBurst { get; private set; }
	float _valueAtShot;
	float _elapsed;

	internal void Reset()
	{
		Punch = Velocity = default;
		_valueAtShot = _elapsed = 0;
		ShotsInBurst = 0;
	}

	// Release ends first-shot accounting but does not erase the spring or its ramp.
	internal void EndBurst() => ShotsInBurst = 0;

	internal float AccumulatedValue( in Titanfall2ViewKickProfile profile ) => MathF.Max( 0,
		_valueAtShot - MathF.Max( 0, _elapsed - profile.ValueDecayDelay ) * profile.ValueDecayRate );

	/// <returns>Permanent pitch/yaw delta, to apply to owner input angles exactly once.</returns>
	internal Angles Fire( in Titanfall2ViewKickProfile profile, int seed, float zoom,
		float crouchFraction = 0, bool grounded = true, bool hovering = false, bool movingFast = false,
		float impulseScale = 1, float rollScale = 1 )
	{
		if ( !profile.Enabled ) return default;
		zoom = Math.Clamp( zoom, 0, 1 );
		var value = AccumulatedValue( profile );
		var fraction = profile.ValueLerpStart == profile.ValueLerpEnd ? 1
			: Math.Clamp( (value - profile.ValueLerpStart) / (profile.ValueLerpEnd - profile.ValueLerpStart), 0, 1 );
		var hip = ShotsInBurst == 0 && profile.FirstShotHip != 0 ? profile.FirstShotHip : float.Lerp( profile.MinHip, profile.MaxHip, fraction );
		var ads = ShotsInBurst == 0 && profile.FirstShotAds != 0 ? profile.FirstShotAds : float.Lerp( profile.MinAds, profile.MaxAds, fraction );
		var scale = float.Lerp( hip, ads, zoom ) * float.Lerp( 1, profile.DuckScale, Math.Clamp( crouchFraction, 0, 1 ) )
			* (hovering ? profile.HoverScale : grounded ? 1 : float.Lerp( 1, profile.AirScaleAds, zoom ))
			* (movingFast ? profile.MoveScale : 1) * impulseScale;
		// Deterministic adaptation: retail named prediction RNG's exact sequence is not reproduced.
		var random = new ShotRandom( seed );
		var kick = new Vector3( Sample( profile.Pitch, ref random ) * scale,
			Sample( profile.Yaw, ref random ) * scale, Sample( profile.Roll, ref random ) * rollScale );
		// client.dll 0x1802CFC50: hard is spring displacement, never permanent aim.
		Punch += new Vector3( kick.x * profile.Pitch.HardScale, kick.y * profile.Yaw.HardScale, kick.z * profile.Roll.HardScale );
		Velocity += new Vector3( kick.x * profile.Pitch.SoftScale, kick.y * profile.Yaw.SoftScale, kick.z * profile.Roll.SoftScale ) * 20;
		var permanent = new Angles( Sample( profile.PermanentPitch, ref random ) * scale, Sample( profile.PermanentYaw, ref random ) * scale, 0 );
		// 0x1805B4DBA: impulse uses the decayed pre-shot value; accumulation follows it.
		_valueAtShot = Math.Clamp( value + profile.ValuePerShot, 0, MathF.Max( 0, profile.ValueLerpEnd ) );
		_elapsed = 0;
		ShotsInBurst++;
		return permanent;
	}

	internal void Advance( in Titanfall2ViewKickProfile profile, float delta, float zoom )
	{
		if ( !profile.Enabled ) { Reset(); return; }
		if ( !float.IsFinite( delta ) || delta <= 0 ) return;
		_elapsed += delta;
		// Retail checks the resting threshold before integrating the next frame.
		if ( Punch.LengthSquared <= 0.001f && Velocity.LengthSquared <= 0.001f )
		{
			Punch = Velocity = default;
			return;
		}
		zoom = Math.Clamp( zoom, 0, 1 );
		var constant = profile.HipSpring.Constant + (profile.AdsSpring.Constant - profile.HipSpring.Constant) * zoom;
		var damping = profile.HipSpring.Damping + (profile.AdsSpring.Damping - profile.HipSpring.Damping) * zoom;
		var px = Punch.x; var py = Punch.y; var pz = Punch.z;
		var vx = Velocity.x; var vy = Velocity.y; var vz = Velocity.z;
		Integrate( ref px, ref vx, constant.x, damping.x, delta );
		Integrate( ref py, ref vy, constant.y, damping.y, delta );
		Integrate( ref pz, ref vz, constant.z, damping.z, delta );
		Punch = new( px, py, pz );
		Velocity = new( vx, vy, vz );
	}

	internal Angles CameraAngles( in Titanfall2ViewKickProfile profile, float zoom )
	{
		var fraction = 1 - WeaponFraction( profile, zoom );
		return new( Punch.x * fraction, Punch.y * fraction, Punch.z );
	}

	/// <summary>Relative to a camera that already includes CameraAngles.</summary>
	internal Angles WeaponAngles( in Titanfall2ViewKickProfile profile, float zoom )
	{
		var fraction = WeaponFraction( profile, zoom ) * float.Lerp( profile.HipWeaponScale, profile.AdsWeaponScale, Math.Clamp( zoom, 0, 1 ) );
		return new( Punch.x * fraction, Punch.y * fraction, -Punch.z );
	}

	// 0x1802C53C0 adds weapon fraction back to camera punch: full pitch/yaw.
	// Apply relative to unrecoiled owner angles, not an already-recoiled camera.
	internal Angles AimAngles => new( Punch.x, Punch.y, Punch.z );

	static float WeaponFraction( in Titanfall2ViewKickProfile profile, float zoom ) =>
		float.Lerp( profile.HipWeaponFraction, profile.AdsWeaponFraction, Math.Clamp( zoom, 0, 1 ) );

	static float Sample( Titanfall2ViewKickAxis axis, ref ShotRandom random )
	{
		var sign = random.Next() < 0.5f ? 1 : -1;
		return axis.Base + float.Lerp( axis.RandomMin, axis.RandomMax, random.Next() ) * sign;
	}

	/// <summary>
	/// Analytic spring branch from 0x180625BB0/0x180625ED0/0x180626240.
	/// Retail also has a fixed-.0166 legacy branch; its runtime selector is unresolved.
	/// </summary>
	internal static void Integrate( ref float position, ref float velocity, float constant, float damping, float delta )
	{
		var discriminant = damping * damping - 4 * constant;
		var half = damping * 0.5f;
		if ( MathF.Abs( discriminant ) <= 0.000001f )
		{
			var c = velocity + half * position;
			var decay = MathF.Exp( -half * delta );
			var value = position + c * delta;
			position = value * decay;
			velocity = (c - half * value) * decay;
		}
		else if ( discriminant > 0 )
		{
			var root = MathF.Sqrt( discriminant );
			var a = ((damping + root) * 0.5f * position + velocity) / root;
			var b = position - a;
			// Equivalent separated exponents avoid exp(+large)*exp(-large) overflow.
			var r1 = (-damping + root) * 0.5f;
			var r2 = (-damping - root) * 0.5f;
			var p = a * MathF.Exp( r1 * delta );
			var q = b * MathF.Exp( r2 * delta );
			position = p + q;
			velocity = r1 * p + r2 * q;
		}
		else
		{
			var root = MathF.Sqrt( -discriminant );
			var b = (position * damping + 2 * velocity) / root;
			var decay = MathF.Exp( -half * delta );
			var angle = root * 0.5f * delta;
			var cos = MathF.Cos( angle );
			var sin = MathF.Sin( angle );
			var value = cos * position + sin * b;
			velocity = (value * damping - (cos * b - sin * position) * root) * (decay * -0.5f);
			position = value * decay;
		}
	}

	struct ShotRandom
	{
		uint _state;
		internal ShotRandom( int seed ) => _state = unchecked( (uint)seed ) ^ 0x9e3779b9u;
		internal float Next()
		{
			if ( _state == 0 ) _state = 0x6d2b79f5u;
			_state ^= _state << 13;
			_state ^= _state >> 17;
			_state ^= _state << 5;
			return (_state >> 8) * (1.0f / 16777216.0f);
		}
	}
}
