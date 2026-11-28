/// <summary>
/// Thin Component shell bridge for composed Titanfall2Npc subsystems:
/// Mortar, FrontierDefense, Ordnance, Tactics, Melee, and Animation.
/// Keeps all s&amp;box [Property], [Sync], and [Rpc] contracts on Titanfall2Npc while encapsulating mutable state in controllers.
/// </summary>
public sealed partial class Titanfall2Npc
{
	public enum FrontierRole { None, ArcTitan, NukeTitan, EliteMonarch, MortarTitan }
	public enum FrontierAbility { None, ArcCharge, NukeArmed, NukeDetonating, ShieldCore, ElectricSmoke }
	public enum CombatStyles { Infantry, Assault, Pilot }
	public enum CombatActions { Idle, Advance, Attack, Reposition, Retreat, TakeCover, Search }

	readonly Titanfall2NpcMortarController _mortar = new();
	readonly Titanfall2NpcFrontierController _frontier = new();
	readonly Titanfall2NpcOrdnanceController _ordnance = new();
	readonly Titanfall2NpcTacticsController _tactics = new();
	readonly Titanfall2NpcMeleeController _melee = new();
	readonly Titanfall2NpcAnimationController _animation = new();

	// --- Mortar ---
	[Property] public Titanfall2ProjectileSettings MortarProjectile { get; set; }
	[Property] public float MortarDamage { get; set; } = 50;
	[Property] public float MortarTitanDamage { get; set; } = 350;
	[Sync( SyncFlags.FromHost )] Titanfall2MortarState MortarState { get; set; }
	[Sync( SyncFlags.FromHost )] int MortarSerial { get; set; }
	[Sync( SyncFlags.FromHost )] double MortarStartedAt { get; set; }
	[Sync( SyncFlags.FromHost )] float MortarDuration { get; set; }
	public Titanfall2MortarSnapshot Mortar => new()
	{
		State = MortarState,
		Serial = MortarSerial,
		StartedAt = MortarStartedAt,
		Duration = MortarDuration
	};
	[Sync( SyncFlags.FromHost )] public int MortarShotsFired { get; private set; }
	bool TickMortar() => _mortar.Tick( this );
	internal bool TryPlayMortarAnimation() => _mortar.TryPlayAnimation( this );
	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayMortarIncoming( Vector3 target ) => Titanfall2GameplayVisuals.PlayFireSound( Scene, MountIdent, "Weapon_FlightCore_Incoming_Projectile", target );
	internal void SetMortarSnapshotHost( Titanfall2MortarSnapshot snapshot )
	{
		MortarState = snapshot.State;
		MortarSerial = snapshot.Serial;
		MortarStartedAt = snapshot.StartedAt;
		MortarDuration = snapshot.Duration;
	}
	internal void IncrementMortarShotsFiredHost() => MortarShotsFired++;
	internal void BroadcastMortarIncoming( Vector3 target ) => PlayMortarIncoming( target );

	// --- Frontier Defense ---
	[Property] public FrontierRole Role { get; set; }
	[Property] public float EmpInnerRadius { get; set; } = 150;
	[Property] public float EmpOuterRadius { get; set; } = 330;
	[Property] public float NukeWindup { get; set; } = 3.3f;
	[Property] public float NukeBlastRadius { get; set; } = 750;
	[Sync( SyncFlags.FromHost )] public FrontierAbility Ability { get; private set; }
	internal const float EmpTickSeconds = Titanfall2NpcFrontierController.EmpTickSeconds;
	internal const float ArcChargeSeconds = Titanfall2NpcFrontierController.ArcChargeSeconds;
	internal const float NukeHarvesterRange = Titanfall2NpcFrontierController.NukeHarvesterRange;
	internal const float EliteShieldThreshold = Titanfall2NpcFrontierController.EliteShieldThreshold;
	internal bool IsNuking => Ability is FrontierAbility.NukeArmed or FrontierAbility.NukeDetonating;
	internal bool IsNukeHazard => IsNuking;
	internal bool IsRushingHarvester => Titanfall2NpcFrontierController.IsRushingHarvester( this );
	internal bool SameFaction( string faction ) => !IsHostileToFaction( faction );
	void StartFrontier() => _frontier.Start( this );
	internal GameObject FindHarvester() => Titanfall2NpcFrontierController.FindHarvester( this );
	bool TickNuke() => _frontier.TickNuke( this );
	void ArmNuke() => _frontier.ArmNuke( this );
	void TickArcAura() => _frontier.TickArcAura( this );
	bool TickArcCannon() => _frontier.TickArcCannon( this );
	bool TickEliteDefense() => _frontier.TickEliteDefense( this );
	void UpdateAbilityLight() => _frontier.UpdateAbilityLight( this );
	internal static float PulseFalloff( float distance, float inner, float outer ) => Titanfall2NpcFrontierController.PulseFalloff( distance, inner, outer );
	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayFrontierSound( string sound ) => Titanfall2GameplayVisuals.PlayFireSound( Scene, MountIdent, sound, WorldPosition );
	internal void SetAbilityHost( FrontierAbility ability ) => Ability = ability;
	internal void BroadcastFrontierSound( string sound ) => PlayFrontierSound( sound );

	// --- Ordnance ---
	[Property] public bool UseGrenades { get; set; } = true;
	[Property] public int GrenadeCount { get; set; } = 2;
	[Property] public float GrenadeCooldown { get; set; } = 14;
	[Sync( SyncFlags.FromHost )] public int GrenadesThrown { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool ThrowingGrenade { get; private set; }
	[Sync( SyncFlags.FromHost )] public float GrenadeWindupSeconds { get; private set; } = 0.68f;
	internal static bool SupportsGrenades( string model ) => Titanfall2NpcOrdnanceController.SupportsGrenades( model );
	internal Vector3 GrenadeLanding => _ordnance.Landing;
	internal bool TickNpcOrdnance() => _ordnance.Tick( this );
	internal bool TryStartGrenade( Vector3 landing, Titanfall2NpcGrenadeData profile ) => _ordnance.TryStart( this, landing, profile );
	internal void CancelNpcOrdnance() => _ordnance.Cancel( this );
	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayGrenadeThrowSound() => Titanfall2GameplayVisuals.PlayFireSound( Scene, MountIdent ?? "titanfall2", "Weapon_FragGrenade_Throw_3P", _ordnance.GrenadeHand( this ) );
	internal void SetThrowingGrenadeHost( bool throwing ) => ThrowingGrenade = throwing;
	internal void SetGrenadeWindupHost( float windup ) => GrenadeWindupSeconds = windup;
	internal void IncrementGrenadesThrownHost() => GrenadesThrown++;
	internal void BroadcastGrenadeThrowSound() => PlayGrenadeThrowSound();
	protected override void OnDisabled()
	{
		CancelNpcActions( "disabled" );
		_ordnance.OnDisabled( this );
		base.OnDisabled();
	}

	// --- Tactics ---
	[Property] public CombatStyles CombatStyle { get; set; }
	[Property] public bool UseCover { get; set; } = true;
	[Property] public float ContactMemorySeconds { get; set; } = 6;
	[Property] public float ReactionSeconds { get; set; } = 0.25f;
	[Property] public int BurstShots { get; set; } = 3;
	[Property] public float BurstPause { get; set; } = 0.45f;
	public CombatActions CombatAction => _tactics.CombatAction;
	public Vector3? CombatDestination => _tactics.CombatDestination;
	public Vector3? LastSeenPosition => _tactics.LastContact;
	internal bool IsUnderFire => _tactics.IsUnderFire;
	internal bool HasCombatContact => _tactics.HasCombatContact( this );
	internal bool SeesContact => _tactics.SeesContact;
	internal Vector3 LastAimPoint => _tactics.LastAimPoint;
	internal Vector3? LastContactPoint => _tactics.LastContact;
	internal float ContactAgeSeconds => _tactics.ContactAge;
	internal float VisualContactAgeSeconds => _tactics.VisualContactAge;
	internal bool ReactionReady => _tactics.ReactionReady;
	internal void RefreshCombatContact( bool force = false ) => _tactics.RefreshContact( this, force );
	internal float EvaluateTargetScore( GameObject candidate ) => _tactics.EvaluateTargetScore( this, candidate );
	void RememberCombatContact( GameObject target, Vector3 position, Vector3 aimPoint, bool seen ) => _tactics.RememberContact( this, target, position, aimPoint, seen );
	internal void RememberCombatContactInternal( GameObject target, Vector3 position, Vector3 aimPoint, bool seen ) => _tactics.RememberContact( this, target, position, aimPoint, seen );
	void ReactToCombatDamage( GameObject attacker ) => _tactics.ReactToDamage( this, attacker );
	internal bool TryFindNearbyHazard( out Vector3 hazardOrigin, out float escapeDistance ) => _tactics.TryFindNearbyHazard( this, out hazardOrigin, out escapeDistance );
	internal bool TickCombatTactics() => _tactics.Tick( this );
	internal void StopCombatMove( CombatActions action = CombatActions.Attack ) => _tactics.StopMove( this, action );
	internal void ResetCombatActionIdle() => _tactics.ResetActionIdle();

	// --- Melee ---
	internal bool MeleePending => _melee.Pending;
	internal bool TryStartMelee( Ray aim, GameObject attacker ) => _melee.TryStart( this, aim, attacker );
	void TickMelee() => _melee.Tick( this );

	// --- Animation ---
	[Sync( SyncFlags.FromHost )] public Vector2 ActorAimAngles { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool IsInTermination { get; private set; }
	[Sync( SyncFlags.FromHost )] public string TerminationClipRole { get; private set; }
	internal Titanfall2ActorAnimationData AnimationData => _animation.AnimationData;
	internal Titanfall2ActorAnimationPlayer LayeredAnimation => _animation.LayeredAnimation;
	internal BoneCollection.Bone GunBone => _animation.GunBone;
	internal Transform GunBindInverse => _animation.GunBindInverse;
	internal void SetGunBoneBinding( BoneCollection.Bone bone, Transform bindInverse ) => _animation.SetGunBoneBinding( bone, bindInverse );
	internal void ClearGunBoneBinding() => _animation.ClearGunBoneBinding();
	internal bool HasActorClip( string role ) => _animation.HasClip( role );
	internal float GetActorClipDuration( string role ) => _animation.GetClipDuration( role );
	internal void BeginTerminationLock( bool isAttacker, string clipRole ) => _animation.BeginTerminationLock( this, isAttacker, clipRole );
	internal void EndTerminationLock() => _animation.EndTerminationLock( this );
	internal void StartTerminationAnimation( string clipRole ) => _animation.StartTerminationAnimation( this, clipRole );
	internal void SetAnimationAimDirection( Vector3 direction ) => _animation.SetAimDirection( this, direction );
	internal void SetActorAimAnglesHost( Vector2 angles ) => ActorAimAngles = angles;
	internal void SetTerminationHost( bool active, string clipRole ) { IsInTermination = active; TerminationClipRole = clipRole; }
	void UpdateGunPose() => _animation.UpdateGunPose( this );
	void InitializeAnimation( Model model ) => _animation.Initialize( this, model );
	void CueActorAttack( bool melee ) => _animation.CueAttack( melee );
	void PlayLocomotion() => _animation.PlayLocomotion( this );
	internal bool PlaySequence( string name, bool looping, bool restart = false ) => _animation.PlaySequence( this, name, looping, restart );
	protected override void OnPreRender() { if ( !Dead ) _animation.UpdateGunPose( this ); }
	protected override void OnDestroy() { CancelNpcActions( "destroyed" ); _animation.Release(); base.OnDestroy(); }

	// --- Controlled internal state bridges for composed subsystems ---
	internal GameObject CurrentTarget { get => _target; set => _target = value; }
	internal GameObject GunObject => _gun;
	internal Vector3 EyePositionInternal => Eye;
	internal bool NextShotReady => _nextShot <= 0;
	internal void SetNextShotDelay( float seconds ) => _nextShot = seconds;
	internal void ResetBurstCount() { _burst = 0; _burstLimit = 0; }
	internal void ResetScanTimer() => _scan = 0;
	internal void SetShieldHost( float value ) => Shield = value;
	internal void SetRoundsHost( int value ) => Rounds = value;
	internal void SetReloadingHost( bool value ) => Reloading = value;
	internal void SetMovingHost( bool value ) => Moving = value;
	internal GameObject LastVerifiedAttacker => LastVerifiedTitanAttacker;
	internal GameObject LastVerifiedWeapon => LastVerifiedTitanWeapon;
	internal void ForceKillTitanInternal( GameObject attacker, GameObject weapon ) => ForceKillTitan( attacker, weapon );
	internal GameObject FindTargetInternal() => FindTarget();
	internal static Vector3 AimAtInternal( GameObject target ) => AimAt( target );
	internal void PlayShotSoundInternal( Vector3 position, bool melee = false, bool npc = true ) => PlayShotSound( position, melee, npc );
	internal void SayVoiceInternal( VoiceCue cue, bool bypassCooldown = false ) => SayVoice( cue, bypassCooldown );
	internal void EndCurrentScheduleInternal() => EndCurrentSchedule();
	internal void StopForAbility()
	{
		Agent?.Stop();
		_chasing = false;
		Moving = false;
	}

	internal bool AiOwnsMovement => IsTraversing || GetComponent<Titanfall2TitanKit>() is { AiControlsMovement: true };
	internal bool AiIsMoving => Moving;
	internal void CancelNpcActions( string reason )
	{
		if ( !Networking.IsHost || IsProxy ) return;
		CancelTraversal( reason, immediate: true );
		CancelNpcOrdnance();
		_melee.Cancel();
		GetComponent<Titanfall2TitanKit>()?.CancelAiActions( reason );
	}
}
