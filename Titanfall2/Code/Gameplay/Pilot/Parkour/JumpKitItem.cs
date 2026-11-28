/// <summary>
/// Spawnable Titanfall 2 Pilot Jump Kit world item (Entities -> Titanfall 2).
/// Press E to equip Pilot parkour (persists across death/respawn until unequipped).
/// </summary>
[Title( "Titanfall 2 Pilot Jump Kit" ), Group( "Titanfall 2" )]
public sealed class Titanfall2JumpKitItem : Component, Component.IPressable
{
	internal const string DefaultWorldModel = "models/humans/heroes/mlt_hero_jack_helmet_static.mdl";
	internal const string FallbackWorldModel = "models/weapons/ammoboxes/backpack_single.mdl";

	[Property] public string MountIdent { get; set; } = "titanfall2";
	[Property] public string WorldModelPath { get; set; }
	[Property] public string PackModelPath { get; set; }
	[Property] public Titanfall2PilotSettings Settings { get; set; } = Titanfall2PilotSettings.RetailDefault;
	[Property] public ModelRenderer Visual { get; set; }

	protected override void OnStart()
	{
		if ( Visual.IsValid() && Visual.Model is null && !string.IsNullOrWhiteSpace( WorldModelPath ) )
		{
			var model = Titanfall2GameplayVisuals.GetModel( WorldModelPath );
			if ( model is { IsError: false } )
			{
				Visual.Model = model;
				// Seat the mesh flush on the collider base in case the retail static model keeps a foot-origin pivot.
				var bounds = model.Bounds;
				Visual.LocalPosition = new Vector3( -bounds.Center.x, -bounds.Center.y, -bounds.Mins.z );
			}
		}
	}

	Component.IPressable.Tooltip? Component.IPressable.GetTooltip( Component.IPressable.Event e )
	{
		var equipped = e.Source is PlayerController pilot
			&& pilot.GameObject.GetComponent<Titanfall2PilotParkour>() is { Equipped: true };
		return new Component.IPressable.Tooltip(
			"Pilot Jump Kit",
			"bolt",
			equipped ? "卸下铁驭跃升背包 (退出铁驭战斗态，恢复普通移动与按键)" : "穿戴铁驭跃升背包 (进入铁驭战斗态：跑酷 / F近战 / G投掷物)" );
	}

	bool Component.IPressable.CanPress( Component.IPressable.Event e ) =>
		Active && e.Source is PlayerController { IsValid: true, Active: true };

	bool Component.IPressable.Press( Component.IPressable.Event e )
	{
		if ( e.Source is not PlayerController { IsValid: true, Active: true } pilot ) return false;
		if ( Networking.IsActive && !Networking.IsHost )
		{
			RequestToggleOnHost( pilot.GameObject );
			return true;
		}
		return ApplyToggleOnHost( pilot );
	}

	[Rpc.Host]
	void RequestToggleOnHost( GameObject pilotObject )
	{
		if ( !Networking.IsHost || !pilotObject.IsValid() ) return;
		var pilot = pilotObject.GetComponent<PlayerController>();
		if ( !pilot.IsValid() || !pilot.Active ) return;
		if ( Rpc.Caller is not null && pilot.Network.Owner != Rpc.Caller ) return;
		if ( pilot.WorldPosition.Distance( WorldPosition ) > 200f ) return;
		ApplyToggleOnHost( pilot );
	}

	bool ApplyToggleOnHost( PlayerController pilot )
	{
		if ( !Active || !pilot.IsValid() ) return false;
		if ( pilot.GameObject.GetComponent<Titanfall2PilotParkour>() is { Equipped: true } )
		{
			Titanfall2PilotParkour.Unequip( pilot );
			Titanfall2GameplayVisuals.PlayFireSound( Scene, MountIdent, Settings.SoundSlideStop1P, WorldPosition );
			return true;
		}

		var parkour = Titanfall2PilotParkour.Equip( pilot, MountIdent, Settings, PackModelPath );
		if ( !parkour.IsValid() ) return false;
		parkour.PlayJumpJetBurst();
		GameObject.Destroy();
		return true;
	}
}
