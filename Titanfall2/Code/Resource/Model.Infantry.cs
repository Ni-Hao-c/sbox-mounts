using Titanfall2.Formats;

partial class ModelLoader
{
	internal const string IdleRole = "tf2_idle";
	internal const string MoveRole = "tf2_move";
	internal const string WalkForwardRole = "tf2_walk_forward";
	internal const string WalkBackwardRole = "tf2_walk_backward";
	internal const string WalkLeftRole = "tf2_walk_left";
	internal const string WalkRightRole = "tf2_walk_right";
	internal const string RunForwardRole = "tf2_run_forward";
	internal const string RunBackwardRole = "tf2_run_backward";
	internal const string RunLeftRole = "tf2_run_left";
	internal const string RunRightRole = "tf2_run_right";
	internal const string FireRole = "tf2_fire";
	internal const string ReloadRole = "tf2_reload";
	internal const string MeleeRole = "tf2_melee";
	internal const string GrenadeRole = "tf2_grenade";
	internal const string DeathRole = "tf2_death";

	// These are full-body retail sequences, not gestures, aim deltas or cinematic poses.
	// The first entries are explicit chassis matches verified from the MDL include graph.
	internal static (string Role, int Score) InfantryAnimationRank( string name, string activity )
	{
		var label = name?.TrimStart( '@' ).ToLowerInvariant() ?? "";
		var act = activity?.ToUpperInvariant();
		if ( label.StartsWith( "add_", StringComparison.Ordinal ) || label.StartsWith( "gest_", StringComparison.Ordinal )
			|| label.StartsWith( "aim_", StringComparison.Ordinal ) || label.Contains( "synced", StringComparison.Ordinal ) ) return default;

		return label switch
		{
			"bt_combat_idle" or "cqb_idle" or "idle_combat" or "pr_idle_alert" or "sspec_idle_combat" => (IdleRole, 120),
			"bt_alert_idle" or "cqb_idle_mp" => (IdleRole, 110),
			// The Locust combat idle can be a held pose. Use its own authored Ronin loop, never Viper flight or doomed poses.
			"titan_light_menu_fd_idle_ronin" or "lt_ronin_reveal_stand_idle" => (IdleRole, 105),
			"bt_combat_walk_forward" or "run_forward_mp" or "pr_combat_run_forward" or "sspec_run_forward" => (MoveRole, 120),
			"bt_casual_walk_forward" or "walk_forward_mp" => (MoveRole, 102),
			"bt_fire_stand" or "mt_stand_fire_auto2" or "legion_attack_auto" or "cqb_fire" or "sspec_speclaunch_fire" => (FireRole, 120),
			"bt_stand_reload_01" or "mt_stand_reload_xo16_vanguard" or "cqb_reload_static" => (ReloadRole, 120),
			"pr_threat_bite_attack" or "sspec_attack_groundslam" or "st_stand_melee_swipe" => (MeleeRole, 120),
			"cqb_throw" => (GrenadeRole, 120),
			"pt_move_grenade_throw" => (GrenadeRole, 110),
			"at_death" or "pr_death_sideflop" => (DeathRole, 120),
			_ => act switch
			{
				"ACT_IDLE" or "ACT_IDLE_COMBAT" or "ACT_IDLE_ANGRY" or "ACT_IDLE_COMBAT_AIM" => (IdleRole, 80),
				"ACT_IDLE_CASUAL" or "ACT_MP_STAND_IDLE" => (IdleRole, 60),
				"ACT_RUN" or "ACT_RUN_AIM" or "ACT_RUN_AIM_FORWARD" or "ACT_MP_RUN_FORWARD" => (MoveRole, 80),
				"ACT_WALK" or "ACT_WALK_AIM" or "ACT_WALK_CASUAL" or "ACT_WALK_AIM_FORWARD" or "ACT_MP_WALK_FORWARD" => (MoveRole, 60),
				"ACT_RANGE_ATTACK1" or "ACT_RANGE_ATTACK1_SINGLE" or "ACT_SPECIAL_ATTACK" => (FireRole, 70),
				"ACT_RELOAD" => (ReloadRole, 70),
				"ACT_MELEE_ATTACK1" or "ACT_MELEE_HIGH" => (MeleeRole, 70),
				"ACT_RANGE_ATTACK2" or "ACT_THROW_GRENADE" => (GrenadeRole, 70),
				"ACT_DIESIMPLE" or "ACT_DIEFORWARD" or "ACT_DIEBACKWARD" => (DeathRole, 70),
				_ => label switch
				{
					"idle" or "stand_idle" or "idle_aim" or "stand_idle_aim" => (IdleRole, 40),
					"run" or "run_forward" or "walk" or "walk_forward" => (MoveRole, 40),
					"death" or "death1" or "death_01" or "die" => (DeathRole, 40),
					_ => default
				}
			}
		};
	}

	internal static string InfantryAnimationRole( string name, string activity ) => InfantryAnimationRank( name, activity ).Role;
	internal static bool IsLocomotionRole( string role ) => role == MoveRole || role?.StartsWith( "tf2_walk_", StringComparison.Ordinal ) == true
		|| role?.StartsWith( "tf2_run_", StringComparison.Ordinal ) == true;

	internal static (string Role, int Score) InfantryLocomotionRank( string name, string activity )
	{
		var label = name?.TrimStart( '@' ).ToLowerInvariant() ?? "";
		if ( label.StartsWith( "aim_", StringComparison.Ordinal ) || label.StartsWith( "add_", StringComparison.Ordinal )
			|| label.StartsWith( "gest", StringComparison.Ordinal ) || label.Contains( "damaged", StringComparison.Ordinal )
			|| label.Contains( "doomed", StringComparison.Ordinal ) || label.Contains( "crawl", StringComparison.Ordinal ) ) return default;
		var role = activity?.ToUpperInvariant() switch
		{
			"ACT_WALK" or "ACT_WALK_AIM" or "ACT_WALK_AIM_FORWARD" or "ACT_WALK_FORWARD" or "ACT_MP_WALK_FORWARD" => WalkForwardRole,
			"ACT_WALK_AIM_BACKWARD" or "ACT_WALK_BACKWARD" or "ACT_MP_WALK_BACKWARD" => WalkBackwardRole,
			"ACT_WALK_AIM_LEFT" or "ACT_WALK_LEFT" or "ACT_MP_WALK_LEFT" => WalkLeftRole,
			"ACT_WALK_AIM_RIGHT" or "ACT_WALK_RIGHT" or "ACT_MP_WALK_RIGHT" => WalkRightRole,
			"ACT_RUN" or "ACT_RUN_AIM" or "ACT_RUN_AIM_FORWARD" or "ACT_RUN_FORWARD" or "ACT_MP_RUN_FORWARD" => RunForwardRole,
			"ACT_RUN_AIM_BACKWARD" or "ACT_RUN_BACKWARD" or "ACT_MP_RUN_BACKWARD" => RunBackwardRole,
			"ACT_RUN_AIM_LEFT" or "ACT_RUN_LEFT" or "ACT_MP_RUN_LEFT" => RunLeftRole,
			"ACT_RUN_AIM_RIGHT" or "ACT_RUN_RIGHT" or "ACT_MP_RUN_RIGHT" => RunRightRole,
			_ => label switch { "walk_mp_left" => WalkLeftRole, "walk_mp_right" => WalkRightRole, _ => null }
		};
		if ( role is null ) return default;
		// Full-body NPC rifle loops beat generic MP/player poses; chassis loops beat shared includes.
		var rank = label.StartsWith( "bt_combat_", StringComparison.Ordinal ) ? 150
			: label.StartsWith( "gun_walkrifle_", StringComparison.Ordinal ) || label.StartsWith( "gun_runrifle_", StringComparison.Ordinal ) ? 140
			: label.StartsWith( "st_", StringComparison.Ordinal ) || label.StartsWith( "sspec_", StringComparison.Ordinal ) || label.StartsWith( "pr_combat_", StringComparison.Ordinal ) ? 145 : 80;
		return (role, rank);
	}

	internal static int InfantryNeutralCell( int width, int height, float startX, float endX, float startY, float endY )
	{
		static int Axis( int count, float start, float end ) => count <= 1 || MathF.Abs( end - start ) < 0.001f ? 0
			: (int)MathF.Round( Math.Clamp( -start / (end - start), 0, 1 ) * (count - 1) );
		return Axis( width, startX, endX ) + Math.Max( 1, width ) * Axis( height, startY, endY );
	}

	internal static bool IsInfantryFrameCount( string role, int frames )
		// BT's genuine idle loops have 601/1001 frames; do not replace them with bind poses.
		=> frames > 1 && frames <= (role == IdleRole ? 1200 : 240);

	sealed record InfantryCandidate( AnimationSourceData Source, int[] Remap, int Index, int Flags, string Role, int Score );

	List<InfantryCandidate> GetInfantryCandidates( SkeletonData skeleton, List<AnimationSourceData> sources, Dictionary<string, int> targetBones, bool idleOnly = false )
	{
		var candidates = new List<InfantryCandidate>();
		foreach ( var source in sources )
		{
			var remap = source.Model.Bones.Select( bone => string.IsNullOrEmpty( bone.Name ) ? -1 : targetBones.GetValueOrDefault( bone.Name, -1 ) ).ToArray();
			var matched = remap.Count( index => index >= 0 );
			if ( matched < Math.Min( skeleton.Names.Length, 6 ) ) continue;
			if ( source.Model.Sequences.Length == 0 )
			{
				foreach ( var a in source.Model.Animations ) Add( a.Index, a.Name, null, a.Animation.Flags, 0 );
			}
			else
			{
				foreach ( var seq in source.Model.Sequences )
				{
					var cell = 0;
					var center = InfantryNeutralCell( seq.Sequence.GroupSize[0], seq.Sequence.GroupSize[1],
						seq.Sequence.ParameterStart[0], seq.Sequence.ParameterEnd[0], seq.Sequence.ParameterStart[1], seq.Sequence.ParameterEnd[1] );
					foreach ( var index in seq.AnimationIndices )
					{
						if ( index >= 0 && index < source.Model.Animations.Length )
							Add( index, seq.Label, seq.ActivityName, seq.Sequence.Flags, Math.Abs( cell - center ) );
						cell++;
					}
				}
			}

			void Add( int index, string label, string activity, int flags, int cell )
			{
				var (role, rank) = InfantryAnimationRank( label, activity );
				var locomotion = InfantryLocomotionRank( label, activity );
				if ( role is not null ) AddRole( role, rank );
				if ( locomotion.Role is not null ) AddRole( locomotion.Role, locomotion.Score );

				void AddRole( string role, int rank )
				{
					if ( idleOnly && role != IdleRole ) return;
					var desc = source.Model.Animations[index].Animation;
					var combined = desc.Flags | flags;
					if ( (combined & 4) != 0 || !IsInfantryFrameCount( role, desc.FrameCount ) ) return;
					var loop = (combined & 1) != 0;
					if ( (role == IdleRole || IsLocomotionRole( role )) ? !loop : loop && role != ReloadRole ) return;
					candidates.Add( new( source, remap, index, flags, role, rank * 1000 + Math.Min( matched, 500 ) - Math.Min( cell, 100 ) ) );
				}
			}
		}
		return candidates;
	}

	int AddInfantryAnimations( ModelBuilder builder, SkeletonData skeleton, List<AnimationSourceData> sources, Dictionary<string, int> targetBones )
	{
		var candidates = GetInfantryCandidates( skeleton, sources, targetBones );
		var names = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var count = 0;
		var failures = 0;
		var clips = new List<Titanfall2ActorAnimationData.Clip>();
		foreach ( var role in candidates.GroupBy( x => x.Role ) )
		{
			foreach ( var c in role.OrderByDescending( x => x.Score ).DistinctBy( x => (x.Source, x.Index) ).Take( 12 ) )
			{
				TryAddAnimation( builder, skeleton, c.Source, c.Remap, c.Index, c.Role, c.Flags, names, ref count, ref failures,
					decoded => clips.Add( new() { Role = c.Role, Source = c.Source.Description, Animation = c.Source.Model.Animations[c.Index].Name,
						Speed = IsLocomotionRole( c.Role ) ? InfantryRootSpeed( decoded ) : 0 } ) );
				if ( names.Contains( role.Key ) ) break;
			}
		}
		if ( !names.Contains( IdleRole ) ) Titanfall2Log.Warning( $"TF2 actor model '{ImportPath}' has no verified full-body idle clip." );
		builder.WithData( new Titanfall2ActorAnimationData { Clips = clips.ToArray() } );
		return count;
	}

	internal static float InfantryRootSpeed( Titanfall2Mdl53AnimationReader.DecodedAnimation decoded )
	{
		if ( decoded.Frames.Length < 2 || decoded.Frames[0].Length == 0 ) return 0;
		var travel = decoded.Frames[^1][0].Position - decoded.Frames[0][0].Position;
		var speed = MathF.Sqrt( travel.X * travel.X + travel.Y * travel.Y ) * decoded.FramesPerSecond / (decoded.Frames.Length - 1);
		return float.IsFinite( speed ) && speed > 1 ? speed : 0;
	}

	static bool HasPoseMotion( Titanfall2Mdl53AnimationReader.DecodedAnimation decoded, int[] map )
	{
		var first = decoded.Frames[0];
		foreach ( var frame in decoded.Frames.Skip( 1 ) )
			for ( var i = 0; i < map.Length; i++ )
			{
				if ( map[i] < 0 ) continue;
				if ( System.Numerics.Vector3.DistanceSquared( frame[i].Position, first[i].Position ) > 0.001f
					|| MathF.Abs( System.Numerics.Quaternion.Dot( frame[i].Rotation, first[i].Rotation ) ) < 0.99999f ) return true;
			}
		return false;
	}
}

[Sandbox.ModelEditor.GameData( "titanfall2_actor_animation" )]
public sealed class Titanfall2ActorAnimationData
{
	public sealed class Clip
	{
		public string Role { get; set; }
		public string Source { get; set; }
		public string Animation { get; set; }
		public float Speed { get; set; }
	}
	public Clip[] Clips { get; set; } = [];
}
