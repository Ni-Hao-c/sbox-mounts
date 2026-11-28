/// <summary>One authored movement/turn contribution in view-local Source coordinates.</summary>
public record struct Titanfall2ViewMotionDirection
{
	public Vector3 Translation { get; set; }
	/// <summary>Pitch, yaw, roll in degrees.</summary>
	public Vector3 Rotation { get; set; }
}

public record struct Titanfall2ViewBobProfile
{
	public float CycleTime { get; set; }
	public float MinimumSpeed { get; set; }
	public float MaximumSpeed { get; set; }
	public float VerticalDistance { get; set; }
	public float HorizontalDistance { get; set; }
	public Vector3 Rotation { get; set; }
	public float Gain { get; set; }
}

public record struct Titanfall2ViewSwayProfile
{
	public string RotateAttachment { get; set; }
	public float AttachmentBlendTime { get; set; }
	public Vector3 TranslationMinimum { get; set; }
	public Vector3 TranslationMaximum { get; set; }
	public Vector3 RotationMinimum { get; set; }
	public Vector3 RotationMaximum { get; set; }
	public float TranslationGain { get; set; }
	public float RotationGain { get; set; }
	public Titanfall2ViewBobProfile Bob { get; set; }
	/// <summary>Forward/back/left/right/up/down movement, then left/right/up/down turns.</summary>
	public Titanfall2ViewMotionDirection[] Directions { get; set; }
}

public record struct Titanfall2ViewMotionProfile
{
	public bool Enabled { get; set; }
	public bool ZoomedEnabled { get; set; }
	public Titanfall2ViewSwayProfile Hip { get; set; }
	public Titanfall2ViewSwayProfile Ads { get; set; }
}

internal sealed partial record Titanfall2WeaponData
{
	internal Titanfall2ViewMotionProfile ViewMotion { get; init; }

	static Titanfall2ViewMotionProfile ReadViewMotionProfile( Func<string, string> get, Func<string, float, float> number )
	{
		// client.dll 0x180592810: the zoomed block has its own defaults. It does
		// not inherit omitted values from hipfire; signed directions stay signed.
		Titanfall2ViewSwayProfile Read( bool zoomed )
		{
			var suffix = zoomed ? "_zoomed" : "";
			float Value( string name, float fallback = 0 ) => number( name + suffix, fallback );
			Vector3 Vector( string prefix, bool angles = false ) => angles
				? new( Value( prefix + "pitch" ), Value( prefix + "yaw" ), Value( prefix + "roll" ) )
				: new( Value( prefix + "x" ), Value( prefix + "y" ), Value( prefix + "z" ) );
			var directions = new[] { "move_forward", "move_back", "move_left", "move_right", "move_up", "move_down",
				"turn_left", "turn_right", "turn_up", "turn_down" };
			return new()
			{
				RotateAttachment = get( "sway_rotate_attach" + suffix ),
				AttachmentBlendTime = Value( "sway_rotate_attach_blend_time" ),
				TranslationMinimum = Vector( "sway_min_" ), TranslationMaximum = Vector( "sway_max_" ),
				RotationMinimum = Vector( "sway_min_", true ), RotationMaximum = Vector( "sway_max_", true ),
				TranslationGain = Value( "sway_translate_gain", zoomed ? 24 : 12 ),
				RotationGain = Value( "sway_rotate_gain", zoomed ? 24 : 12 ),
				Bob = new()
				{
					CycleTime = Value( "bob_cycle_time" ), MinimumSpeed = Value( "bob_min_speed", 0.1f ),
					MaximumSpeed = Value( "bob_max_speed", 0.2f ), VerticalDistance = Value( "bob_vert_dist" ),
					HorizontalDistance = Value( "bob_horz_dist" ), Rotation = Vector( "bob_", true ), Gain = Value( "bob_gain", 12 )
				},
				Directions = directions.Select( direction => new Titanfall2ViewMotionDirection
				{
					Translation = Vector( "sway_" + direction + "_translate_" ),
					Rotation = Vector( "sway_" + direction + "_rotate_", true )
				} ).ToArray()
			};
		}
		return new() { Enabled = true, ZoomedEnabled = number( "sway_enable_zoomed", 1 ) != 0, Hip = Read( false ), Ads = Read( true ) };
	}
}
