using Sandbox.Movement;

/// <summary>Snapshot of Pilot parkour kinematics consumed by weapons and first-person viewmodels.</summary>
public readonly record struct Titanfall2PilotViewPoseState(
	float WallrunSide,
	float SlideBlend,
	float MantleProgress,
	float AirborneBlend,
	float HorizontalSpeed,
	float LandingDip,
	bool IsWallrunning,
	bool IsSliding,
	bool IsMantling,
	bool IsSprinting,
	float OffhandDip = 0f,
	float MeleeProgress = 0f,
	Titanfall2PilotMeleeStrikeKind MeleeStrikeKind = Titanfall2PilotMeleeStrikeKind.None,
	bool IsWallhanging = false );

/// <summary>Typed state surface exposed by the Pilot Jump Kit controller for weapons and HUDs.</summary>
public interface ITitanfall2PilotParkourState
{
	bool Equipped { get; }
	bool CombatBusy { get; }
	float WallrunSide { get; }
	float SlideBlend { get; }
	float MantleProgress { get; }
	bool IsWallrunning { get; }
	bool IsWallhanging { get; }
	bool IsSliding { get; }
	bool IsMantling { get; }
	bool IsSprinting { get; }
	bool IsAirborne { get; }
	float HorizontalSpeed { get; }
	Vector3 WallNormal { get; }
	int DoubleJumpsRemaining { get; }
	Titanfall2PilotViewPoseState ViewPoseState { get; }
}

/// <summary>
/// Persists Jump Kit ownership across Sandbox death and respawn within the active scene.
/// Runs zero work when no player has a Jump Kit equipped.
/// </summary>
public sealed class Titanfall2JumpKitPersistence : GameObjectSystem<Titanfall2JumpKitPersistence>
{
	sealed record EquippedEntry( string MountIdent, Titanfall2PilotSettings Settings, string PackModelPath,
		Titanfall2OffhandOrdnanceConfig? Ordnance, Titanfall2PilotTacticalKind? Tactical );

	readonly Dictionary<Guid, EquippedEntry> _equipped = [];

	public Titanfall2JumpKitPersistence( Scene scene ) : base( scene )
	{
		Listen( Stage.StartFixedUpdate, 5, ReapplyToRespawnedPlayers, nameof( ReapplyToRespawnedPlayers ) );
	}

	internal static Guid OwnerKey( PlayerController player ) =>
		player?.Network?.Owner?.Id ?? Guid.Empty;

	internal void Remember( PlayerController player, string mountIdent, Titanfall2PilotSettings settings, string packModelPath )
	{
		if ( !player.IsValid() ) return;
		var key = OwnerKey( player );
		var existing = _equipped.TryGetValue( key, out var prev ) ? prev : null;
		_equipped[key] = new( mountIdent ?? "titanfall2", settings, packModelPath, existing?.Ordnance, existing?.Tactical );
	}

	internal void RememberOrdnance( PlayerController player, in Titanfall2OffhandOrdnanceConfig config )
	{
		if ( !player.IsValid() || !config.Valid ) return;
		var key = OwnerKey( player );
		if ( _equipped.TryGetValue( key, out var existing ) )
			_equipped[key] = existing with { Ordnance = config };
	}

	internal void RememberTactical( PlayerController player, Titanfall2PilotTacticalKind tactical )
	{
		if ( !player.IsValid() ) return;
		var key = OwnerKey( player );
		if ( _equipped.TryGetValue( key, out var existing ) )
			_equipped[key] = existing with { Tactical = tactical };
	}

	internal void Forget( PlayerController player )
	{
		if ( !player.IsValid() ) return;
		_equipped.Remove( OwnerKey( player ) );
	}

	internal bool IsRemembered( PlayerController player ) =>
		player.IsValid() && _equipped.ContainsKey( OwnerKey( player ) );

	void ReapplyToRespawnedPlayers()
	{
		if ( _equipped.Count == 0 || !Scene.IsValid() ) return;
		if ( Networking.IsActive && !Networking.IsHost ) return;

		foreach ( var player in Scene.GetAllComponents<PlayerController>() )
		{
			if ( !player.IsValid() || !player.Active || player.GameObject.IsDestroyed ) continue;
			var key = OwnerKey( player );
			if ( !_equipped.TryGetValue( key, out var entry ) ) continue;
			var parkour = player.GameObject.GetComponent<Titanfall2PilotParkour>();
			if ( parkour is { Equipped: true } ) continue;
			Titanfall2PilotParkour.AttachEquipped( player, entry.MountIdent, entry.Settings, entry.PackModelPath, entry.Ordnance, entry.Tactical );
		}
	}
}

