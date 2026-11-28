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
	internal const string FlinchRole = "tf2_small_flinch";
	internal const string MeleeRole = "tf2_melee";
	internal const string GrenadeRole = "tf2_grenade";
	internal const string DeathRole = "tf2_death";
	internal const string JumpRole = "tf2_traverse_jump";
	internal const string DropRole = "tf2_traverse_drop";
	internal const string ClimbRole = "tf2_traverse_climb";
	internal const string LandRole = "tf2_traverse_land";
	internal const string MortarDeployRole = "tf2_mortar_deploy";
	internal const string MortarAttackRole = "tf2_mortar_attack";
	internal const string MortarRecoverRole = "tf2_mortar_recover";
	internal const string ExecAttackerRole = "tf2_exec_attacker";
	internal const string ExecVictimMonarchRole = "tf2_exec_victim_monarch";
	internal const string ExecVictimIonRole = "tf2_exec_victim_ion";
	internal const string ExecVictimToneRole = "tf2_exec_victim_tone";
	internal const string ExecVictimLegionRole = "tf2_exec_victim_legion";
	internal const string ExecVictimScorchRole = "tf2_exec_victim_scorch";
	internal const string ExecVictimNorthstarRole = "tf2_exec_victim_northstar";
	internal const string ExecVictimRoninRole = "tf2_exec_victim_ronin";

	internal static bool IsExecutionRole( string role ) =>
		role?.StartsWith( "tf2_exec_", StringComparison.Ordinal ) == true;

	// These are full-body retail sequences, not gestures, aim deltas or cinematic poses.
	// The first entries are explicit chassis matches verified from the MDL include graph.
	internal static (string Role, int Score) InfantryAnimationRank( string name, string activity, string modelPath = null )
	{
		var label = name?.TrimStart( '@' ).ToLowerInvariant() ?? "";
		var act = activity?.ToUpperInvariant();
		// A separate additive role, never a substitute for a full-body action.
		if ( act == "ACT_GESTURE_SMALL_FLINCH" ) return (FlinchRole, 80);

		var execRank = TitanExecutionAnimationRank( label, modelPath );
		if ( execRank.Role is not null ) return execRank;
		var titanAction = TitanActionAnimationRank( label, modelPath );
		if ( titanAction.Role is not null ) return titanAction;

		if ( label.StartsWith( "add_", StringComparison.Ordinal ) || label.StartsWith( "gest_", StringComparison.Ordinal )
			|| label.StartsWith( "aim_", StringComparison.Ordinal ) || label.Contains( "synced", StringComparison.Ordinal ) ) return default;
		var traversal = label.Contains( "flight_core", StringComparison.Ordinal ) ? default : TraversalAnimationRank( act );
		if ( traversal.Role is not null ) return traversal;

		return label switch
		{
			"at_mortar_stand2knee" => (MortarDeployRole, 150),
			"at_mortar_knee" => (MortarAttackRole, 150),
			"at_mortar_knee2stand" => (MortarRecoverRole, 150),
			"bt_combat_idle" or "cqb_idle" or "idle_combat" or "pr_idle_alert" or "sspec_idle_combat" => (IdleRole, 120),
			"bt_alert_idle" or "cqb_idle_mp" => (IdleRole, 110),
			// The Locust combat idle can be a held pose. Use its own authored Ronin loop, never Viper flight or doomed poses.
			"titan_light_menu_fd_idle_ronin" or "lt_ronin_reveal_stand_idle" => (IdleRole, 105),
			"bt_combat_walk_forward" or "run_forward_mp" or "pr_combat_run_forward" or "sspec_run_forward" => (MoveRole, 120),
			"bt_casual_walk_forward" or "walk_forward_mp" => (MoveRole, 102),
			"bt_fire_stand" or "mt_stand_fire_auto2" or "legion_attack_auto" or "at_attack_auto" or "cqb_fire" or "sspec_speclaunch_fire" => (FireRole, 120),
			"at_fire_combat_kickup" => (FireRole, 115),
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
				"ACT_RANGE_ATTACK1" or "ACT_RANGE_ATTACK1_SINGLE" or "ACT_SPECIAL_ATTACK" when !label.Contains( "arcwave", StringComparison.Ordinal ) && !label.Contains( "sword", StringComparison.Ordinal ) => (FireRole, 70),
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

	internal static (string Role, int Score) TitanExecutionAnimationRank( string label, string modelPath )
	{
		if ( string.IsNullOrEmpty( label ) ) return default;
		var path = modelPath?.ToLowerInvariant() ?? "";
		if ( !string.IsNullOrEmpty( path ) && !path.Contains( "/titans/", StringComparison.Ordinal ) )
			return default;

		// Victim roles apply across all 3 Titan weight classes (medium / heavy / light).
		switch ( label )
		{
			case "mt_execution_battery_victim_vanguard" or "ht_execution_battery_victim_vanguard" or "lt_execution_battery_victim_vanguard":
				return (ExecVictimMonarchRole, 180);
			case "mt_execution_victim_vanguard" or "ht_execution_victim_vanguard" or "lt_execution_victim_vanguard":
				return (ExecVictimMonarchRole, 150);
			case "mt_execution_victim_laser" or "ht_execution_victim_laser" or "lt_execution_victim_laser":
				return (ExecVictimIonRole, 180);
			case "mt_execution_victim_tone" or "ht_execution_victim_tone" or "lt_execution_victim_tone":
				return (ExecVictimToneRole, 180);
			case "t_meleeexecuted_by_htpred":
				return (ExecVictimLegionRole, 180);
			case "t_meleeexecuted_by_htthermite":
				return (ExecVictimScorchRole, 180);
			case "mt_execution_victim_sweep_01" or "ht_execution_victim_sweep_01" or "lt_execution_victim_sweep_01":
				return (ExecVictimNorthstarRole, 180);
			case "mt_execution_victim_sword_01" or "ht_execution_victim_sword_01" or "lt_execution_victim_sword_01":
				return (ExecVictimRoninRole, 180);
		}

		// Attacker role is matched specifically to each Titan chassis model so Monarch/Ion/Tone/Legion/Scorch/Northstar/Ronin play their signature termination.
		var expectedAttacker = path switch
		{
			_ when path.Contains( "vanguard", StringComparison.Ordinal ) || path.Contains( "buddy", StringComparison.Ordinal ) => "mt_execution_battery_attacker_vanguard",
			_ when path.Contains( "wraith", StringComparison.Ordinal ) => "mt_execution_attacker_laser",
			_ when path.Contains( "ajax", StringComparison.Ordinal ) => "mt_execution_attacker_tone",
			_ when path.Contains( "deadbolt", StringComparison.Ordinal ) => "htpred_mp_sync_execution_attacker",
			_ when path.Contains( "ogre", StringComparison.Ordinal ) => "htthermite_mp_sync_execution_attacker",
			_ when path.Contains( "raptor", StringComparison.Ordinal ) => "lt_execution_attacker_sweep_01",
			_ when path.Contains( "locust", StringComparison.Ordinal ) => "lt_execution_attacker_sword_01",
			_ => null
		};
		if ( expectedAttacker is not null && label == expectedAttacker )
			return (ExecAttackerRole, 200);
		if ( (path.Contains( "vanguard", StringComparison.Ordinal ) || path.Contains( "buddy", StringComparison.Ordinal ))
			&& label == "mt_execution_attacker_vanguard" )
			return (ExecAttackerRole, 160);

		return default;
	}

	internal static string InfantryAnimationRole( string name, string activity ) => InfantryAnimationRank( name, activity ).Role;
	internal static bool IsLocomotionRole( string role ) => role == MoveRole || role?.StartsWith( "tf2_walk_", StringComparison.Ordinal ) == true
		|| role?.StartsWith( "tf2_run_", StringComparison.Ordinal ) == true;
	internal static bool IsTraversalRole( string role ) => role?.StartsWith( "tf2_traverse_", StringComparison.Ordinal ) == true;
	internal static bool HasExternalRootMotion( string role ) => IsTraversalRole( role ) || role is "ai_northstar_core" or "ai_northstar_utility";
	internal static bool IsTitanActionRole( string role ) => role?.StartsWith( "ai_", StringComparison.Ordinal ) == true;
	internal static bool IsUpperTitanActionRole( string role ) => IsTitanActionRole( role )
		&& !role.Contains( "_core", StringComparison.Ordinal ) && !role.StartsWith( "ai_ronin_offense", StringComparison.Ordinal )
		&& !role.StartsWith( "ai_legion_offense", StringComparison.Ordinal ) && role != "ai_northstar_utility";
	// Exact labels verified against retail MDL include sequences; no ACT_OFFHAND catch-all across incompatible kits.
	internal static (string Role, int Score) TitanActionAnimationRank( string label, string modelPath )
	{
		var path = modelPath?.ToLowerInvariant() ?? "";
		var kit = path.Contains( "wraith", StringComparison.Ordinal ) ? "ion"
			: path.Contains( "locust", StringComparison.Ordinal ) ? "ronin"
			: path.Contains( "deadbolt", StringComparison.Ordinal ) ? "legion"
			: path.Contains( "raptor", StringComparison.Ordinal ) ? "northstar"
			: path.Contains( "ajax", StringComparison.Ordinal ) ? "tone"
			: path.Contains( "vanguard", StringComparison.Ordinal ) || path.Contains( "buddy", StringComparison.Ordinal ) ? "monarch" : null;
		var role = (kit, label) switch
		{
			("ion", "mt_laserlite_out_alt_") => "ai_ion_offense_prepare",
			("ion", "mt_laserlite_fire") => "ai_ion_offense",
			("ion", "mt_laserlite_in") => "ai_ion_offense_recover",
			("ion", "mp_vortex_ion_add") => "ai_ion_defense",
			("ion", "mt_chest_laser_forward_loop") => "ai_ion_core",
			("ion", "mt_chest_laser_start") => "ai_ion_core_prepare",
			("ion", "mt_chest_laser_end") => "ai_ion_core_recover",
			("ronin", "lt_ai_arcwave_swing_anim") => "ai_ronin_offense",
			("ronin", "lt_mp_arcwave_pullout") => "ai_ronin_offense_prepare",
			("ronin", "lt_mp_player_walk_sword_block_00") => "ai_ronin_defense",
			("legion", "htlegion_stand_powershot_ai") => "ai_legion_offense",
			("legion", "htgat_vortex_pose_mp") => "ai_legion_defense",
			("legion", "mp_htlegion_gunmode_stand") => "ai_legion_utility",
			("northstar", "lt_npc_flight_core") => "ai_northstar_core",
			("northstar", "lt_npc_jump_hover") => "ai_northstar_utility",
			("tone", "mt_sonarpulse_out_alt") => "ai_tone_utility_prepare",
			("tone", "mt_sonarpulse_fire") => "ai_tone_utility",
			("tone", "mt_sonarpulse_in") => "ai_tone_utility_recover",
			("tone", "mt_salvo_core") => "ai_tone_core",
			("tone", "mt_salvo_core_start") => "ai_tone_core_prepare",
			("tone", "mt_salvo_core_end") => "ai_tone_core_recover",
			("monarch", "mt_laserstun_fire") => "ai_monarch_defense",
			("monarch", "mt_rearm_charge") => "ai_monarch_utility",
			("monarch", "mt_rearm_out") => "ai_monarch_utility_prepare",
			("monarch", "mt_rearm_in") => "ai_monarch_utility_recover",
			_ => null
		};
		return role is null ? default : (role, 180);
	}
	internal static (string Role, int Score) TraversalAnimationRank( string activity )
	{
		var act = activity?.ToUpperInvariant() ?? "";
		if ( act.StartsWith( "ACT_TRAVERSE_", StringComparison.Ordinal ) )
		{
			if ( act.Contains( "CLIMB", StringComparison.Ordinal ) ) return (ClimbRole, act.Contains( "256", StringComparison.Ordinal ) ? 120 : 95);
			if ( act.Contains( "DOWN", StringComparison.Ordinal ) && !act.Contains( "UP", StringComparison.Ordinal ) ) return (DropRole, 110);
			return (JumpRole, act.Contains( "128", StringComparison.Ordinal ) ? 120 : 100);
		}
		return act switch
		{
			"ACT_JUMP" => (JumpRole, 90),
			"ACT_MP_JUMP_START" or "ACT_MP_JUMP" => (JumpRole, 60),
			"ACT_CLIMB_UP" or "ACT_CLIMB_DOWN" => (ClimbRole, 80),
			"ACT_LAND" or "ACT_MP_JUMP_LAND" => (LandRole, 90),
			"ACT_GLIDE" or "ACT_MP_JUMP_FLOAT" => (DropRole, 60),
			_ => default
		};
	}

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
		=> frames > 1 && frames <= (role == IdleRole || IsTitanActionRole( role ) ? 1200 : 240);

	internal static bool IsSmallFlinchPose( int sequenceFlags, int animationFlags, int cells, int autoLayers ) =>
		(sequenceFlags & 4) != 0 && (animationFlags & 4) != 0 && ((sequenceFlags | animationFlags) & 1) == 0
		&& cells == 1 && autoLayers == 0;

	sealed record InfantryCandidate( AnimationSourceData Source, int[] Remap, int Index, int SequenceIndex, int Flags, string Role, int Score );

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
				foreach ( var a in source.Model.Animations ) Add( a.Index, -1, a.Name, null, a.Animation.Flags, 0 );
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
							Add( index, seq.Index, seq.Label, seq.ActivityName, seq.Sequence.Flags, Math.Abs( cell - center ) );
						cell++;
					}
				}
			}

			void Add( int index, int sequenceIndex, string label, string activity, int flags, int cell )
			{
				var (role, rank) = InfantryAnimationRank( label, activity, ImportPath );
				var locomotion = InfantryLocomotionRank( label, activity );
				if ( role is not null ) AddRole( role, rank );
				if ( locomotion.Role is not null ) AddRole( locomotion.Role, locomotion.Score );

				void AddRole( string role, int rank )
				{
					if ( idleOnly && role != IdleRole ) return;
					var desc = source.Model.Animations[index].Animation;
					var combined = desc.Flags | flags;
					if ( !IsInfantryFrameCount( role, desc.FrameCount ) ) return;
					if ( role == FlinchRole )
					{
						if ( sequenceIndex < 0 ) return;
						var sequence = source.Model.Sequences[sequenceIndex];
						if ( !IsSmallFlinchPose( flags, desc.Flags, sequence.AnimationIndices.Length, sequence.Sequence.AutoLayerCount ) ) return;
					}
					else if ( (combined & 4) != 0 ) return;
					var loop = (combined & 1) != 0;
					if ( (role == IdleRole || IsLocomotionRole( role )) ? !loop : loop && role != ReloadRole && role != MortarAttackRole && !IsTraversalRole( role ) && !IsTitanActionRole( role ) ) return;
					candidates.Add( new( source, remap, index, sequenceIndex, flags, role, rank * 1000 + Math.Min( matched, 500 ) - Math.Min( cell, 100 ) ) );
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
		_actorAnimationEntries.Clear();
		_actorBoneMasks.Clear();
		foreach ( var role in candidates.GroupBy( x => x.Role ) )
		{
			foreach ( var c in role.OrderByDescending( x => x.Score ).DistinctBy( x => (x.Source, x.Index) ).Take( 12 ) )
			{
				TryAddAnimation( builder, skeleton, c.Source, c.Remap, c.Index, c.Role, c.Flags, names, ref count, ref failures,
					decoded =>
					{
						var desc = c.Source.Model.Animations[c.Index].Animation;
						_actorAnimationEntries[c.Role] = new( c.Role, c.Source == sources[0] ? null : c.Source.Description,
							c.Index, c.Flags, desc.Flags, desc.FrameCount, desc.FramesPerSecond );
						var clip = new Titanfall2ActorAnimationData.Clip { Role = c.Role, Source = c.Source.Description, Animation = c.Source.Model.Animations[c.Index].Name,
							Path = $"{Path}/actor/{c.Role}.anim", Speed = IsLocomotionRole( c.Role ) ? InfantryRootSpeed( decoded ) : 0,
							Additive = decoded.Delta, Looping = decoded.Looping,
							Duration = Math.Max( 0, desc.FrameCount - 1 ) / MathF.Max( 0.001f, decoded.FramesPerSecond ) };
						AddActorSequenceData( clip, c, skeleton, sources, decoded );
						if ( HasExternalRootMotion( c.Role ) )
						{
							var root = Array.FindIndex( c.Source.Model.Bones, bone => bone.Bone.Parent < 0 );
							if ( root >= 0 )
							{
								var first = decoded.Frames[0][root];
								var firstYaw = ToSandbox( first.Rotation ).Angles().yaw;
								clip.RootMotion = decoded.Frames.Select( f => ToSandbox( f[root].Position - first.Position ) ).ToArray();
								clip.RootYaw = decoded.Frames.Select( f => MathX.DeltaDegrees( ToSandbox( f[root].Rotation ).Angles().yaw, firstYaw ) ).ToArray();
							}
						}
						clips.Add( clip );
					} );
				if ( names.Contains( role.Key ) ) break;
			}
		}
		if ( !names.Contains( IdleRole ) ) Titanfall2Log.Warning( $"TF2 actor model '{ImportPath}' has no verified full-body idle clip." );
		builder.WithData( new Titanfall2ActorAnimationData { Skeleton = $"{Path}/actor.skel", Clips = clips.ToArray() } );
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
		public string Path { get; set; }
		public float Speed { get; set; }
		public float Duration { get; set; }
		public bool Additive { get; set; }
		public bool Looping { get; set; }
		public string BoneMask { get; set; }
		public Titanfall2SequenceRuntimeData Sequence { get; set; }
		public AimLayer[] AimLayers { get; set; } = [];
		public EventBonePose[] EventBonePoses { get; set; } = [];
		/// <summary>Authored root travel before actor clips are made in-place. Used to time fitted link motion.</summary>
		public Vector3[] RootMotion { get; set; } = [];
		public float[] RootYaw { get; set; } = [];
	}
	public sealed class EventBonePose
	{
		public int EventIndex { get; set; }
		public float Cycle { get; set; }
		public string Bone { get; set; }
		public Transform Transform { get; set; }
	}
	public sealed class AimLayer
	{
		public string Id { get; set; }
		public string Source { get; set; }
		public string BoneMask { get; set; }
		public Titanfall2SequenceRuntimeData Sequence { get; set; }
		public string[] Cells { get; set; } = [];
	}
	public Clip[] Clips { get; set; } = [];
	public string Skeleton { get; set; }

	// Explicit TF2 rig families; do not overlay arbitrary creature limbs by guessing.
	internal static Dictionary<string, float> UpperBodyWeights( string[] names, int[] parents )
	{
		var weights = new Dictionary<string, float>( StringComparer.OrdinalIgnoreCase );
		for ( var i = 0; i < names.Length; i++ )
		{
			var name = names[i].ToLowerInvariant();
			var upper = name.Contains( "spine" ) || name.Contains( "chest" ) || name.Contains( "propgun" ) || name.Contains( "prophand" );
			var parent = parents[i];
			weights[names[i]] = upper || parent >= 0 && parent < i && weights.GetValueOrDefault( names[parent] ) > 0 ? 1 : 0;
		}
		return weights;
	}
}
