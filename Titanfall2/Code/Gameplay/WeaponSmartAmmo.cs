/// <summary>Retail smart-ammo lock timing and burst limits after multiplayer overrides.</summary>
public record struct Titanfall2SmartAmmoProfile
{
	public bool Enabled { get; set; }
	public float SearchDistance { get; set; }
	public float SearchAngle { get; set; }
	public int MaxTargets { get; set; }
	public int MaxBurst { get; set; }
	public float TimeMin { get; set; }
	public float TimeMax { get; set; }
	public float NpcTimeMin { get; set; }
	public float NpcTimeMax { get; set; }
	public float NpcLockFactor { get; set; }
	public float MaxLocksNormal { get; set; }
	public float MaxLocksHeavy { get; set; }
	public float NewTargetDelay { get; set; }
	public float UnlockDebounce { get; set; }
	public bool AllowAds { get; set; }
	public bool AllowHip { get; set; }
	public bool SearchNpcs { get; set; }
	public bool StickToLocked { get; set; }
	public bool DelayFirstTarget { get; set; }
	public string AcquiringSound { get; set; }
	public string ConfirmedSound { get; set; }

	// client!5747E0 acquires faster at close range. 573A60 reverses that
	// interpolation for decay; neither function uses a random lock duration.
	internal readonly float LockSeconds( float distance, bool npc, bool decay = false )
	{
		var minimum = npc && NpcTimeMin >= 0 ? NpcTimeMin : TimeMin;
		var maximum = npc && NpcTimeMax >= 0 ? NpcTimeMax : TimeMax;
		minimum = MathF.Min( minimum, maximum );
		var fraction = SearchDistance > 0 ? Math.Clamp( distance / SearchDistance, 0, 1 ) : 1;
		return decay ? float.Lerp( maximum, minimum, fraction ) : float.Lerp( minimum, maximum, fraction );
	}

	internal readonly int RequiredLocks( float health, float nearDamage, bool npc, bool heavy )
	{
		var configured = heavy ? MaxLocksHeavy : MaxLocksNormal;
		var factor = npc ? MathF.Max( 0.001f, NpcLockFactor ) : 1;
		var value = configured >= 0 ? configured : MathF.Ceiling( health / MathF.Max( 0.001f, nearDamage * factor ) );
		return (int)Math.Clamp( value, 1, Math.Clamp( MaxBurst, 1, 12 ) );
	}

	// client!573520/573DB0: angular bounds in a fixed 70-degree projection,
	// then distance of their nearest point to the acquisition region's center.
	internal readonly float SearchMetric( Ray aim, BBox bounds, Vector3 aimPoint )
	{
		var toAim = aimPoint - aim.Position;
		if ( toAim.LengthSquared < 0.001f || Vector3.Dot( aim.Forward, toAim.Normal ) < MathF.Cos( SearchAngle * MathF.PI / 180 ) ) return 1;
		var frame = Rotation.LookAt( aim.Forward );
		var minimum = new Vector2( float.MaxValue );
		var maximum = new Vector2( float.MinValue );
		var any = false;
		for ( var i = 0; i < 8; ++i )
		{
			var point = new Vector3( (i & 1) == 0 ? bounds.Mins.x : bounds.Maxs.x,
				(i & 2) == 0 ? bounds.Mins.y : bounds.Maxs.y, (i & 4) == 0 ? bounds.Mins.z : bounds.Maxs.z ) - aim.Position;
			var forward = Vector3.Dot( point, frame.Forward );
			if ( forward <= 0 ) continue;
			var projected = new Vector2( MathF.Atan2( Vector3.Dot( point, frame.Right ), forward ),
				MathF.Atan2( -Vector3.Dot( point, frame.Up ), forward ) ) / (35 * MathF.PI / 180);
			minimum = new( MathF.Min( minimum.x, projected.x ), MathF.Min( minimum.y, projected.y ) );
			maximum = new( MathF.Max( maximum.x, projected.x ), MathF.Max( maximum.y, projected.y ) );
			any = true;
		}
		if ( !any ) return 1;
		var closest = new Vector2( Math.Clamp( 0, minimum.x, maximum.x ), Math.Clamp( 0, minimum.y, maximum.y ) );
		return Math.Clamp( closest.Length / MathF.Max( 0.0001f, MathF.Tan( SearchAngle * MathF.PI / 360 ) / MathF.Tan( 35 * MathF.PI / 180 ) ), 0, 1 );
	}
}

internal sealed partial record Titanfall2WeaponData
{
	internal Titanfall2SmartAmmoProfile SmartAmmo { get; init; }
	static Titanfall2SmartAmmoProfile ReadSmartAmmo( Func<string, string> get, Func<string, float, float> number )
	{
		if ( get( "is_smart_pistol" ) != "1" ) return default;
		return new()
		{
			Enabled = true, SearchDistance = Math.Clamp( number( "smart_ammo_search_distance", 0 ), 0, 16384 ),
			SearchAngle = Math.Clamp( number( "smart_ammo_search_angle", 1 ), 1, 120 ),
			MaxTargets = (int)Math.Clamp( number( "smart_ammo_max_targets", 5 ), 1, 8 ),
			MaxBurst = (int)Math.Clamp( number( "smart_ammo_max_targeted_burst", 5 ), 1, 12 ),
			TimeMin = Math.Clamp( number( "smart_ammo_targeting_time_min", 0.5f ), 0, 30 ),
			TimeMax = Math.Clamp( number( "smart_ammo_targeting_time_max", 2 ), 0, 30 ),
			NpcTimeMin = Math.Clamp( number( "smart_ammo_targeting_time_min_npc", -1 ), -1, 30 ),
			NpcTimeMax = Math.Clamp( number( "smart_ammo_targeting_time_max_npc", -1 ), -1, 30 ),
			NpcLockFactor = Math.Clamp( number( "smart_ammo_target_npc_lock_factor", 1 ), 0.001f, 100 ),
			MaxLocksNormal = number( "smart_ammo_target_max_locks_normal", -1 ), MaxLocksHeavy = number( "smart_ammo_target_max_locks_heavy", -1 ),
			NewTargetDelay = Math.Clamp( number( "smart_ammo_new_target_delay", 0.3f ), 0, 30 ),
			UnlockDebounce = Math.Clamp( number( "smart_ammo_unlock_debounce_time", 0.15f ), 0, 30 ),
			AllowAds = get( "smart_ammo_allow_ads_lock" ) == "1", AllowHip = get( "smart_ammo_allow_hip_fire_lock" ) == "1",
			SearchNpcs = get( "smart_ammo_search_npcs" ) != "0", StickToLocked = get( "smart_ammo_stick_to_fully_locked_targets" ) != "0",
			DelayFirstTarget = get( "smart_ammo_apply_new_target_delay_to_first_target" ) == "1",
			AcquiringSound = get( "smart_ammo_looping_sound_acquiring" ),
			ConfirmedSound = get( "smart_ammo_target_confirmed_sound" )
		};
	}
}
