/// <summary>The same sequence names feed playback and spawn-time preparation.</summary>
internal static class Titanfall2TitanInteractionAssets
{
	internal static string[] GroundSides => ["front", "behind", "left", "right"];
	internal static string[] RodeoSides => ["front", "back", "front_lower", "back_lower", "left", "right"];
	internal static string PilotModel( string ident ) => $"mount://{ident}/models/humans/pilots/pilot_medium_reaper_m.mdl.vmdl";
	internal static string ArmsModel( string ident ) => $"mount://{ident}/models/weapons/arms/pov_pilot_medium_reaper_m.mdl.vmdl";
	internal static (string Third, string First) RodeoClips( string family, Titanfall2RodeoState state, string side = "back_lower" )
	{
		if ( state == Titanfall2RodeoState.Mounting ) return ($"pt_rodeo_move_{family}_{side}_entrance", $"ptpov_rodeo_move_{family}_{side}_entrance");
		if ( state == Titanfall2RodeoState.ThrowingGrenade )
		{
			var size = Size( family );
			return ($"pt_rodeo_{size}_grenade_1st", $"ptpov_rodeo_{size}_grenade_1st");
		}
		if ( state == Titanfall2RodeoState.FriendlyIdle ) return family switch
		{
			"ogre" => ("pt_rodeo_ogre_panel_aim_idle_move", "ptpov_ogre_rodeo_panel_aim_idle"),
			"stryder" => ("pt_rodeo_stryder_panel_aim_idle_move", "ptpov_stryder_rodeo_panel_aim_idle"),
			_ => ("pt_rodeo_ride_R_idle", "ptpov_rodeo_ride_R_idle")
		};
		var action = state == Titanfall2RodeoState.InsertingBattery ? "return_battery" : "hijack_battery";
		return family switch
		{
			"ogre" => ($"pt_rodeo_ogre_{action}", $"ptpov_rodeo_ogre_R_{action}"),
			"stryder" => ($"pt_rodeo_stryder_ride_R_{action}", $"ptpov_rodeo_stryder_R_{action}"),
			_ => ($"pt_rodeo_ride_R_{action}", $"ptpov_rodeo_ride_R_{action}")
		};
	}
	static string Size( string family ) => family == "ogre" ? "heavy" : family == "stryder" ? "light" : "medium";

	internal static Titanfall2WeaponPrewarmItem[] Dependencies( string ident, string titanModel, Titanfall2TitanKit.Chassis chassis )
	{
		ident = string.IsNullOrWhiteSpace( ident ) ? "titanfall2" : ident;
		var family = Titanfall2TitanBoardingRules.Subclass( chassis, titanModel );
		var body = PilotModel( ident );
		var arms = ArmsModel( ident );
		var items = new List<Titanfall2WeaponPrewarmItem>();
		void Add( Titanfall2WeaponPrewarmKind kind, string name, string variant = null ) => items.Add( new( kind, name, Variant: variant ) );
		void Skeleton( string model ) => Add( Titanfall2WeaponPrewarmKind.AnimationSkeleton, model + "/skeleton.skel" );
		void Clip( string model, string sequence ) => Add( Titanfall2WeaponPrewarmKind.AnimationClip, model + "/animations/" + Uri.EscapeDataString( sequence ) + ".anim", sequence );
		foreach ( var model in new[] { body, arms } ) { Add( Titanfall2WeaponPrewarmKind.InteractionModel, model ); Skeleton( model ); }
		if ( !string.IsNullOrWhiteSpace( titanModel ) ) Skeleton( titanModel );
		foreach ( var side in GroundSides )
		{
			Clip( body, $"pt_mount_{family}_stand_{side}" );
			if ( !string.IsNullOrWhiteSpace( titanModel ) ) Clip( titanModel, $"at_mount_stand_{side}" );
		}
		Clip( body, $"pt_dismount_{family}_stand" );
		Clip( arms, $"ptpov_dismount_{family}_stand" );
		if ( !string.IsNullOrWhiteSpace( titanModel ) ) Clip( titanModel, "at_dismount_stand" );
		var startup = chassis switch
		{
			Titanfall2TitanKit.Chassis.Ion => "ion_power_up", Titanfall2TitanKit.Chassis.Tone => "tone_power_up",
			Titanfall2TitanKit.Chassis.Scorch => "scorch_power_up", Titanfall2TitanKit.Chassis.Legion => "legion_power_up",
			Titanfall2TitanKit.Chassis.Ronin => "ronin_power_up", Titanfall2TitanKit.Chassis.Northstar => "northstar_power_up",
			_ => "bt_power_up"
		};
		Add( Titanfall2WeaponPrewarmKind.Event, startup );
		// Retail buddy settings explicitly disable rodeo; do not prepare unsupported aliases.
		if ( family != "buddy" )
		{
			foreach ( var prop in new[] { Titanfall2TitanInteractionPresentation.BatteryHandPropModel, Titanfall2TitanInteractionPresentation.GrenadeHandPropModel } )
				Add( Titanfall2WeaponPrewarmKind.InteractionModel, $"mount://{ident}/{prop}.vmdl" );
			foreach ( var side in RodeoSides )
			{
				var clips = RodeoClips( family, Titanfall2RodeoState.Mounting, side ); Clip( body, clips.Third ); Clip( arms, clips.First );
			}
			foreach ( var state in new[] { Titanfall2RodeoState.StealingBattery, Titanfall2RodeoState.ThrowingGrenade, Titanfall2RodeoState.InsertingBattery, Titanfall2RodeoState.FriendlyIdle } )
			{
				var clips = RodeoClips( family, state ); Clip( body, clips.Third );
				if ( state != Titanfall2RodeoState.FriendlyIdle ) Clip( arms, clips.First );
			}
			foreach ( var action in new[] { "battery_steal", "grenade", "battery_return" } )
				foreach ( var view in new[] { "int", "ext" } ) Add( Titanfall2WeaponPrewarmKind.Event, $"rodeo_{Size( family )}_{action}_{view}" );
			foreach ( var side in new[] { "ClimbOn_Front", "LandOn_Above", "ClimbOn_Back", "LandOn_Back", "ClimbOn_Left", "ClimbOn_Right" } )
				foreach ( var view in new[] { "Interior", "Exterior" } ) Add( Titanfall2WeaponPrewarmKind.Event, $"Rodeo_{family}_Rodeo_{side}_{view}" );
			foreach ( var sound in new[] { "Rodeo_Jump_Off_Interior", "Rodeo_Jump_Off", "UI_TitanBattery_Pilot_PickUp", "UI_TitanBattery_Pilot_Give_TitanBattery", "UI_TitanBattery_Pilot_Take_TitanBattery" } )
				Add( Titanfall2WeaponPrewarmKind.Event, sound );
		}
		return items.Distinct().ToArray();
	}
	internal static Titanfall2Mount MountFor( string path )
	{
		if ( path?.StartsWith( "mount://", StringComparison.OrdinalIgnoreCase ) != true ) return null;
		var slash = path.IndexOf( '/', 8 );
		return slash > 8 ? Sandbox.Mounting.Directory.Get( path[8..slash] ) as Titanfall2Mount : null;
	}
}
