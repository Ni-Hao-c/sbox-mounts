/// <summary>A consumable world pickup that replaces the Pilot's one tactical equipment slot.</summary>
[Title( "Titanfall 2 Pilot Tactical Item" ), Group( "Titanfall 2" )]
public sealed class Titanfall2PilotTacticalItem : Component, Component.IPressable
{
	[Property] public string MountIdent { get; set; } = "titanfall2";
	[Property] public Titanfall2PilotTacticalKind Kind { get; set; }
	[Property] public string WorldModelPath { get; set; }
	[Property] public ModelRenderer Visual { get; set; }
	[Sync( SyncFlags.FromHost )] public bool Consumed { get; private set; }

	protected override void OnStart()
	{
		if ( !Visual.IsValid() || Visual.Model is not null || string.IsNullOrWhiteSpace( WorldModelPath ) ) return;
		var model = Titanfall2GameplayVisuals.GetModel( WorldModelPath );
		if ( model is not { IsError: false } ) return;
		Visual.Model = model;
		var bounds = model.Bounds;
		Visual.LocalPosition = new Vector3( -bounds.Center.x, -bounds.Center.y, -bounds.Mins.z );
	}

	Component.IPressable.Tooltip? Component.IPressable.GetTooltip( Component.IPressable.Event e )
	{
		var pilot = e.Source as PlayerController;
		var tactical = pilot.IsValid() ? pilot.GameObject.GetComponent<Titanfall2PilotTactical>() : null;
		var hasKit = pilot.IsValid() && pilot.GameObject.GetComponent<Titanfall2PilotParkour>() is { Equipped: true };
		var equipped = tactical is { HasEquippedTactical: true };
		var description = !hasKit ? "先穿戴铁御跃升背包"
			: equipped && tactical.SelectedKind == Kind ? "已装备此技能"
			: equipped ? "替换当前铁御技能 · T 使用"
			: "拾取铁御技能 · T 使用";
		return new( Titanfall2PilotTacticalRules.ShortLabel( Kind ), "bolt", description );
	}

	bool CanEquip( PlayerController pilot )
	{
		if ( !Active || Consumed || pilot is not { IsValid: true, Active: true } ) return false;
		if ( pilot.GameObject.GetComponent<Titanfall2PilotParkour>() is not { Equipped: true } ) return false;
		var tactical = pilot.GameObject.GetComponent<Titanfall2PilotTactical>();
		return tactical is { IsValid: true, Active: true }
			&& Titanfall2PilotTacticalRules.CanEquip( tactical.HasEquippedTactical, tactical.SelectedKind, Kind );
	}

	bool Component.IPressable.CanPress( Component.IPressable.Event e ) =>
		e.Source is PlayerController pilot && CanEquip( pilot );

	bool Component.IPressable.Press( Component.IPressable.Event e )
	{
		if ( e.Source is not PlayerController pilot || !CanEquip( pilot ) ) return false;
		if ( Networking.IsActive && !Networking.IsHost )
		{
			RequestPickupOnHost( pilot.GameObject );
			return true;
		}
		return ApplyPickupOnHost( pilot );
	}

	[Rpc.Host]
	void RequestPickupOnHost( GameObject pilotObject )
	{
		if ( !Networking.IsHost || !pilotObject.IsValid() ) return;
		var pilot = pilotObject.GetComponent<PlayerController>();
		if ( !pilot.IsValid() || (Rpc.Caller is not null && pilot.Network.Owner != Rpc.Caller) ) return;
		ApplyPickupOnHost( pilot );
	}

	bool ApplyPickupOnHost( PlayerController pilot )
	{
		if ( !Networking.IsHost || !CanEquip( pilot ) || pilot.WorldPosition.Distance( WorldPosition ) > 200f ) return false;
		var trace = Scene.Trace.Ray( pilot.EyeTransform.Position, WorldPosition + Vector3.Up * 8f )
			.IgnoreGameObjectHierarchy( pilot.GameObject ).IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "trigger", "projectile" );
		if ( Titanfall2CollisionQueries.Run( trace, Titanfall2TraceMask.Solid ).Hit ) return false;
		var tactical = pilot.GameObject.GetComponent<Titanfall2PilotTactical>();
		if ( !tactical.TryEquipOnHost( Kind ) ) return false;
		Consumed = true;
		GameObject.Destroy();
		return true;
	}
}
