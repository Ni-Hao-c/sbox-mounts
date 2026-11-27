using Sandbox.UI;
using Sandbox.UI.Construct;

/// <summary>A local-only native HUD. Its state is read from the actor, kit and core.</summary>
[Hide]
public sealed partial class Titanfall2TitanCockpitHud : PanelComponent
{
	[Property] public Titanfall2TitanSeat Seat { get; set; }
	Label _name, _link, _tier, _heading, _telemetry, _health, _shield, _ammo, _weapon, _coreName, _coreState, _message, _controls, _target, _dashLabel;
	Panel _healthFill, _shieldFill, _reticle, _hitmarker, _ionBar, _ionFill, _toneLocks, _embarkBar, _embarkFill, _phaseOverlay;
	Panel _healthSegments;
	readonly List<Panel> _tonePips = [];
	readonly List<Panel> _sonarMarkers = [];
	readonly List<GameObject> _sonarTargets = [];
	Titanfall2CoreGauge _coreGauge;
	Image _chassisArt, _weaponArt, _coreArt, _damageArt, _hullBackground, _hullArt, _shieldArt, _dashArt, _reticleArt, _reticleRight, _reticleDot;
	Panel _dashFill;
	readonly List<Image> _abilityArt = [];
	readonly Dictionary<Image, string> _requestedArt = [];
	Titanfall2TitanKit.Chassis _artKit = (Titanfall2TitanKit.Chassis)(-1);
	string _artWeapon;
	int _artHealthSegments;
	float _lastHealth;
	TimeUntil _damageFlash;
	int _lastShotSerial = -1;
	int _lastHitSerial = -1;
	TimeUntil _hitFlash;
	bool _hitKill;
	float _reticleSpread;
	readonly List<(Panel Panel, Label Key, Label Name, Label State, Panel Fill)> _abilities = [];
	TimeUntil _targetCheck;
	GameObject _aimTarget;

	internal static string FormatHeading( float yawDegrees )
	{
		var normalized = ((360f - yawDegrees) % 360f + 360f) % 360f;
		string[] cardinals = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
		var index = (int)MathF.Floor( (normalized + 22.5f) / 45f ) % 8;
		return $"{cardinals[index]}  {(int)MathF.Round( normalized ) % 360:000}°";
	}

	protected override void OnEnabled()
	{
		base.OnEnabled();
		if ( !Panel.IsValid() ) return;
		_abilities.Clear();
		_abilityArt.Clear();
		_requestedArt.Clear();
		_tonePips.Clear();
		_sonarMarkers.Clear();
		_artKit = (Titanfall2TitanKit.Chassis)(-1);
		_artWeapon = "";
		_artHealthSegments = 0;
		_lastHealth = 0;
		_lastShotSerial = -1;
		_lastHitSerial = -1;
		_reticleSpread = 0;

		// Mount assets are not part of the host game's stylesheet filesystem.
		Panel.StyleSheet.AddInline( CockpitStyles, "titanfall2:cockpit" );
		_damageArt = Panel.Add.Image( classname: "damage-overlay" );
		_phaseOverlay = Panel.Add.Panel( "phase-overlay" );
		for ( var i = 0; i < 12; i++ ) _sonarMarkers.Add( Panel.Add.Panel( "sonar-marker" ) );

		var header = Panel.Add.Panel( "link-header" );
		_name = header.Add.Label( "", "chassis" );
		_link = header.Add.Label( "", "link-state" );
		_tier = header.Add.Label( "", "upgrade-tier" );

		var compass = Panel.Add.Panel( "compass-bar" );
		_heading = compass.Add.Label( "", "heading" );
		_telemetry = compass.Add.Label( "", "telemetry" );

		_embarkBar = Panel.Add.Panel( "embark-progress" );
		_embarkFill = AddBar( _embarkBar, "embark-bar" );

		_reticle = Panel.Add.Panel( "reticle" );
		_reticleArt = _reticle.Add.Image( classname: "reticle-art" );
		_reticleRight = _reticle.Add.Image( classname: "reticle-art reticle-right" );
		_reticleDot = _reticle.Add.Image( classname: "reticle-dot" );

		_hitmarker = _reticle.Add.Panel( "hitmarker" );
		_hitmarker.Add.Panel( "hm-arm hm-tl" );
		_hitmarker.Add.Panel( "hm-arm hm-tr" );
		_hitmarker.Add.Panel( "hm-arm hm-bl" );
		_hitmarker.Add.Panel( "hm-arm hm-br" );

		_toneLocks = _reticle.Add.Panel( "tone-locks" );
		for ( var i = 0; i < 3; i++ ) _tonePips.Add( _toneLocks.Add.Panel( "tone-pip" ) );

		_ionBar = _reticle.Add.Panel( "ion-energy-reticle" );
		_ionFill = _ionBar.Add.Panel( "ion-energy-fill" );

		_target = Panel.Add.Label( "", "target-state" );
		_message = Panel.Add.Label( "", "cockpit-message" );

		var integrity = Panel.Add.Panel( "integrity" );
		_chassisArt = integrity.Add.Image( classname: "chassis-art" );
		integrity.Add.Label( "机体完整度 / HULL", "eyebrow" );
		_health = integrity.Add.Label( "", "stat" );
		_healthFill = AddBar( integrity, "hull-bar", retail: true );
		_shield = integrity.Add.Label( "", "shield-stat" );
		_shieldFill = AddBar( integrity, "shield-bar" );
		_shieldArt = _shieldFill.Add.Image( classname: "bar-art" );

		// Retail creates earn_meter, ability_hud and ammo_counter separately from
		// ajax_cockpit_base/lower. Keep those groups independent here as well.
		var systems = Panel.Add.Panel( "systems" );
		var core = systems.Add.Panel( "core" );
		_coreGauge = core.AddChild<Titanfall2CoreGauge>( "core-gauge" );
		_coreArt = core.Add.Image( classname: "core-art" );
		_coreName = core.Add.Label( "", "core-name" );
		_coreState = core.Add.Label( "", "core-state" );

		var ammunition = Panel.Add.Panel( "ammunition" );
		_weaponArt = ammunition.Add.Image( classname: "weapon-art" );
		_weapon = ammunition.Add.Label( "", "eyebrow" );
		_ammo = ammunition.Add.Label( "", "rounds" );

		var row = systems.Add.Panel( "abilities" );
		for ( var i = 0; i < 4; i++ )
		{
			var card = row.Add.Panel( "ability" );
			_abilityArt.Add( card.Add.Image( classname: "ability-art" ) );
			_abilities.Add( (card, card.Add.Label( "", "key" ), card.Add.Label( "", "ability-name" ),
				card.Add.Label( "", "ability-state" ), AddBar( card, "ability-bar" )) );
		}

		var dash = Panel.Add.Panel( "dash" );
		_dashLabel = dash.Add.Label( "", "dash-label" );
		_dashFill = AddBar( dash, "dash-bar" );
		_dashArt = _dashFill.Add.Image( classname: "dash-art" );
		_controls = Panel.Add.Label( "", "controls" );
	}

	Panel AddBar( Panel parent, string name, bool retail = false )
	{
		var bar = parent.Add.Panel( "bar " + name );
		if ( retail ) _hullBackground = bar.Add.Image( classname: "bar-background" );
		var fill = bar.Add.Panel( "fill" );
		if ( retail ) _hullArt = fill.Add.Image( classname: "bar-art" );
		if ( retail ) _healthSegments = bar.Add.Panel( "hull-segments" );
		return fill;
	}
	static string Key( string action ) => string.IsNullOrWhiteSpace( action ) ? "未绑定" : Input.GetButtonOrigin( action )?.ToUpperInvariant() ?? "未绑定";
	static void Fill( Panel panel, float value ) { if ( panel.IsValid() ) panel.Style.Width = Length.Percent( Math.Clamp( value, 0, 1 ) * 100 ); }
	internal static string Remaining( float seconds ) => seconds <= 0 ? "就绪" : $"{MathF.Ceiling( seconds )}s";

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( !Panel.IsValid() ) return;
		var actor = Seat?.Actor;
		var visible = Seat.IsValid() && Seat.LocalPilot.IsValid() && actor is { Dead: false, IsInTermination: false };
		Panel.Style.Display = visible ? DisplayMode.Flex : DisplayMode.None;
		if ( !visible || !_name.IsValid() ) return;

		var kit = actor.GetComponent<Titanfall2TitanKit>();
		var core = actor.GetComponent<Titanfall2TitanCore>();
		UpdateRetailArtwork( actor, kit );

		_name.Text = actor.DisplayName ?? "TITAN";
		_link.Text = Seat.IsEmbarking ? $"正在登乘 · {Seat.EmbarkRemaining:0.0}s" : "LINK ESTABLISHED · 手动驾驶";
		_tier.Text = core is { Kit: Titanfall2TitanKit.Chassis.Monarch, UpgradeLevel: > 0 }
			? $"UPGRADE CORE · TIER {core.UpgradeLevel}"
			: "";

		if ( Seat.IsEmbarking )
		{
			var progress = 1f - Seat.EmbarkRemaining / MathF.Max( 0.1f, Seat.EmbarkSeconds );
			Fill( _embarkFill, progress );
		}

		var yaw = Seat.LocalPilot.EyeTransform.Rotation.Angles().yaw;
		var speedKmh = MathF.Round( Seat.DriveVelocity.WithZ( 0 ).Length * 0.09144f );
		_heading.Text = FormatHeading( yaw );
		_telemetry.Text = $"{speedKmh:00} KM/H  //  {(kit is { DashRemaining: <= 0 } ? "DASH READY" : "THRUST RECHARGE")}";

		Panel.SetClass( "boarding", Seat.IsEmbarking );
		Panel.SetClass( "piloting", !Seat.IsEmbarking );
		Panel.SetClass( "critical", actor.IsDoomed || actor.Health <= actor.MaxHealth * 0.25f );

		var healthMaximum = actor.IsDoomed ? actor.MaxDoomedHealth : actor.MaxHealth;
		_health.Text = $"{(actor.IsDoomed ? "危急 · DOOMED  " : "")}{MathF.Ceiling( actor.Health ):N0} / {healthMaximum:N0}";
		_shield.Text = $"护盾  {MathF.Ceiling( actor.Shield ):N0} / {actor.MaxShield:N0}";
		Fill( _healthFill, actor.Health / MathF.Max( 1, healthMaximum ) );
		Fill( _shieldFill, actor.Shield / MathF.Max( 1, actor.MaxShield ) );

		_weapon.Text = core is { SwordCoreActive: true } ? "浪人 · 宽刃剑核心" : WeaponName( actor.GunModelPath );
		_ammo.Text = actor.Reloading ? "装填中" : core is { UnlimitedAmmo: true } ? "∞" : $"{actor.Rounds:00}";
		_ammo.SetClass( "reloading", actor.Reloading );

		UpdateReticleFeedback( actor, kit );

		if ( kit.IsValid() )
		{
			if ( kit.Kit == Titanfall2TitanKit.Chassis.Ion )
			{
				SetAbility( 0, Seat.OffenseAction, AbilityName( kit.OffenseName ), kit.OffenseRemaining, 1,
					$"能量 {kit.SharedEnergy}/{kit.SharedEnergyMaximum}", kit.CanIonLaser );
				SetAbility( 1, Seat.DefenseAction, AbilityName( kit.DefenseName ), 0, 1,
					kit.IonVortexActive ? $"反射 · {kit.CapturedAmmo} 发" : "按住吸收", kit.SharedEnergy >= 100 );
				SetAbility( 2, Seat.UtilityAction, AbilityName( kit.UtilityName ), kit.UtilityRemaining, kit.UtilityCooldownSeconds,
					kit.SharedEnergy < 350 ? "能量不足" : null, kit.CanUseUtility );
				Fill( _abilities[0].Fill, (float)kit.SharedEnergy / kit.SharedEnergyMaximum );
				Fill( _abilities[1].Fill, (float)kit.SharedEnergy / kit.SharedEnergyMaximum );
			}
			else
			{
				var offenseExtra = kit.Kit == Titanfall2TitanKit.Chassis.Tone ? $"标记 {kit.TrackerMarks}/3"
					: kit.Kit == Titanfall2TitanKit.Chassis.Legion ? (kit.LegionLongRangeMode ? "远距·穿甲" : "近距·霰弹")
					: null;
				SetAbility( 0, Seat.OffenseAction, AbilityName( kit.OffenseName ), kit.OffenseRemaining,
					kit.OffenseCooldown, offenseExtra,
					kit.Kit != Titanfall2TitanKit.Chassis.Tone || kit.HasTrackerLock );

				if ( kit.Kit == Titanfall2TitanKit.Chassis.Scorch )
				{
					var pct = (int)MathF.Round( kit.ThermalShieldMeter * 100f );
					SetAbility( 1, "attack2", AbilityName( kit.DefenseName ), 0, 1,
						kit.GuardRemaining > 0 ? $"灼烧 · {pct}%" : $"按住 · {pct}%", kit.ThermalShieldMeter > 0.05f );
					Fill( _abilities[1].Fill, kit.ThermalShieldMeter );
				}
				else if ( kit.Kit == Titanfall2TitanKit.Chassis.Ronin )
				{
					SetAbility( 1, "attack2", AbilityName( kit.DefenseName ), 0, 1,
						kit.RoninBlocking ? "格挡中" : "按住格挡", !kit.IsPhasing );
					Fill( _abilities[1].Fill, kit.RoninBlocking ? 1f : 0.7f );
				}
				else
				{
					SetAbility( 1, "attack2", AbilityName( kit.DefenseName ), kit.DefenseRemaining,
						kit.Kit == Titanfall2TitanKit.Chassis.Northstar ? Titanfall2TetherRules.RecoverySeconds
							: kit.Kit == Titanfall2TitanKit.Chassis.Tone ? 14f : kit.DefenseCooldown,
						kit.GuardRemaining > 0 ? $"生效 {kit.GuardRemaining:0.0}s" : null );
				}

				var utilityExtra = kit.IsHovering ? $"悬停 {kit.HoverRemaining:0.0}s"
					: kit.IsPhasing ? $"相位 {kit.PhaseRemaining:0.0}s"
					: kit.Kit == Titanfall2TitanKit.Chassis.Legion ? (kit.LegionLongRangeMode ? "远距" : "近距")
					: null;
				SetAbility( 2, Seat.UtilityAction, AbilityName( kit.UtilityName ), kit.UtilityRemaining,
					kit.UtilityCooldownSeconds, utilityExtra, kit.CanUseUtility );
			}

			SetAbility( 3, Seat.SmokeAction, AbilityName( kit.SmokeName ), kit.SmokeRemaining, kit.SmokeCooldown );

			var maxDashes = Math.Max( 1, kit.MaxDashCharges );
			var dashReady = kit.DashCharges > 0;
			_dashLabel.Text = maxDashes > 1
				? (dashReady ? $"[{Key( "run" )}] 推进 {kit.DashCharges}/{maxDashes}" : $"推进充能 0/{maxDashes} · {kit.DashRemaining:0.0}s")
				: (dashReady ? $"[{Key( "run" )}] 推进就绪" : $"推进充能 {kit.DashRemaining:0.0}s");
			_dashLabel.SetClass( "ready", dashReady );
			var dashFill = (kit.DashCharges + (kit.DashCharges < maxDashes ? kit.DashRechargeProgress : 0f)) / maxDashes;
			Fill( _dashFill, dashFill );
			_phaseOverlay?.SetClass( "active", kit.IsPhasing );
		}
		else
		{
			_phaseOverlay?.SetClass( "active", false );
		}

		var supported = core is { Supported: true };
		_coreName.Text = supported ? $"[{Key( Seat.CoreAction )}]  {AbilityName( core.Name )}"
			: "核心 · 此机体尚未接入";
		_coreState.Text = !supported ? "—" : core.IsCharging ? $"蓄力 {core.ChargeupRemaining:0.0}s"
			: core.IsActive ? $"生效 {core.ActiveRemaining:0.0}s" : core.CanActivate ? "核心就绪"
			: $"{MathF.Floor( core.Charge * 100 )}%";
		_coreGauge.SetValue( !supported ? 0 : core.IsActive ? core.ActiveRemaining / MathF.Max( 0.1f, core.Duration ) : core.Charge,
			core is { CanActivate: true } or { IsActive: true }, supported );
		Panel.SetClass( "core-ready", core is { CanActivate: true } );
		Panel.SetClass( "core-active", core is { IsActive: true } );
		_message.Text = Seat.StatusMessage ?? (actor.IsDoomed ? $"警告 · 泰坦已进入危急状态   [{Key( Seat.EjectAction )}] 弹射" : actor.Health <= actor.MaxHealth * 0.25f ? "警告 · 机体受损严重" : "");
		_controls.Text = $"[{Key( "attack1" )}] 开火   [{Key( "reload" )}] 装填   [{Key( Seat.MeleeAction )}] 近战   [{Key( "run" )}] 冲刺   [{Key( "use" )}] 离舱   [{Key( Seat.EjectAction )}] 弹射   [{Key( "spawnmenu" )}] 菜单";
		if ( kit?.Kit == Titanfall2TitanKit.Chassis.Ion ) _controls.Text += $"   [{Key( "attack2" )}] 分裂射击";
		UpdateTarget( actor, kit );
		UpdateCockpitSonarMarkers();
	}

	void UpdateCockpitSonarMarkers()
	{
		var cam = Scene?.Camera;
		var registry = Scene?.GetSystem<Titanfall2SonarRegistry>();
		if ( !cam.IsValid() || registry is null )
		{
			foreach ( var m in _sonarMarkers ) m?.SetClass( "visible", false );
			return;
		}
		registry.FillActiveTargets( _sonarTargets );
		var used = 0;
		for ( var i = 0; i < _sonarTargets.Count && used < _sonarMarkers.Count; i++ )
		{
			var target = _sonarTargets[i];
			if ( !target.IsValid() ) continue;
			var screen = cam.PointToScreenNormal( target.GetBounds().Center, out var isBehind );
			if ( isBehind || screen.x < 0.02f || screen.x > 0.98f || screen.y < 0.02f || screen.y > 0.98f ) continue;
			var marker = _sonarMarkers[used++];
			marker.SetClass( "visible", true );
			marker.Style.Left = Length.Percent( screen.x * 100f );
			marker.Style.Top = Length.Percent( screen.y * 100f );
		}
		for ( var i = used; i < _sonarMarkers.Count; i++ )
			_sonarMarkers[i].SetClass( "visible", false );
	}

	void UpdateReticleFeedback( Titanfall2Npc actor, Titanfall2TitanKit kit )
	{
		if ( _lastShotSerial >= 0 && actor.ShotSerial != _lastShotSerial )
			_reticleSpread = MathF.Min( 18f, _reticleSpread + 6f );
		_lastShotSerial = actor.ShotSerial;

		if ( _lastHitSerial >= 0 && actor.HitMarkerSerial != _lastHitSerial )
		{
			_hitFlash = 0.22f;
			_hitKill = actor.HitMarkerKill;
		}
		_lastHitSerial = actor.HitMarkerSerial;

		var moveSpread = Math.Clamp( Seat.DriveVelocity.WithZ( 0 ).Length / 260f, 0f, 1f ) * 4f;
		var ionTighten = kit is { Kit: Titanfall2TitanKit.Chassis.Ion } ? kit.IonZoomFraction * 8f : 0f;
		_reticleSpread = MathX.Lerp( _reticleSpread, MathF.Max( 0f, moveSpread - ionTighten ), Math.Clamp( Time.Delta * 12f, 0f, 1f ) );

		var offset = 26f + _reticleSpread;
		if ( _reticleArt.IsValid() ) _reticleArt.Style.Left = Length.Pixels( -offset );
		if ( _reticleRight.IsValid() ) _reticleRight.Style.Left = Length.Pixels( offset - 24f );

		if ( _hitmarker.IsValid() )
		{
			_hitmarker.SetClass( "active", _hitFlash > 0 );
			_hitmarker.SetClass( "kill", _hitFlash > 0 && _hitKill );
		}

		var isTone = kit is { Kit: Titanfall2TitanKit.Chassis.Tone };
		if ( _toneLocks.IsValid() )
		{
			_toneLocks.Style.Display = isTone ? DisplayMode.Flex : DisplayMode.None;
			if ( isTone )
			{
				for ( var i = 0; i < _tonePips.Count; i++ )
				{
					_tonePips[i].SetClass( "filled", i < kit.TrackerMarks );
					_tonePips[i].SetClass( "locked", kit.HasTrackerLock );
				}
			}
		}

		var isIon = kit is { Kit: Titanfall2TitanKit.Chassis.Ion };
		if ( _ionBar.IsValid() )
		{
			_ionBar.Style.Display = isIon ? DisplayMode.Flex : DisplayMode.None;
			if ( isIon )
			{
				Fill( _ionFill, (float)kit.SharedEnergy / MathF.Max( 1, kit.SharedEnergyMaximum ) );
				_ionBar.SetClass( "low", kit.SharedEnergy < 250 );
				_ionBar.SetClass( "ads", kit.IonZoomFraction > 0.5f );
			}
		}
	}

	void SetAbility( int index, string action, string name, float remaining, float recharge, string extra = null, bool canUse = true )
	{
		if ( _abilities.Count <= index ) return;
		var card = _abilities[index];
		card.Key.Text = Key( action );
		card.Name.Text = name;
		card.State.Text = extra is null ? Remaining( remaining ) : remaining > 0 ? $"{extra} · {Remaining( remaining )}" : extra;
		if ( !canUse && remaining <= 0 ) card.State.Text = $"{extra} · 未就绪";
		card.Panel.SetClass( "ready", remaining <= 0 && canUse );
		Fill( card.Fill, 1 - remaining / MathF.Max( 0.1f, recharge ) );
	}

	void UpdateTarget( Titanfall2Npc actor, Titanfall2TitanKit kit )
	{
		if ( _targetCheck <= 0 )
		{
			_targetCheck = 0.1f;
			var aim = Seat.LocalPilot.EyeTransform.ForwardRay;
			var hit = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( aim, 6000 ).UseHitboxes()
				.IgnoreGameObjectHierarchy( actor.GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Shot );
			_aimTarget = hit.GameObject?.GetComponentInParent<Titanfall2Npc>()?.GameObject;
		}
		var target = _aimTarget?.GetComponent<Titanfall2Npc>();
		var hostile = target is { Dead: false } && actor.IsHostileToFaction( target.Faction );
		_reticle.SetClass( "hostile", hostile );
		_target.Text = target is { Dead: false } ? $"{(hostile ? "敌方" : "友方")} // {target.DisplayName}"
			+ (kit?.TrackedTarget == target.GameObject ? $"  [{kit.TrackerMarks}/3]" : "") : "";
	}

	static string WeaponName( string path ) => path?.ToLowerInvariant() switch
	{
		{ } p when p.Contains( "xo16" ) => "XO-16 · 链炮", { } p when p.Contains( "predator" ) => "掠食者 · 加农炮",
		{ } p when p.Contains( "particle_accelerator" ) => "分裂枪", { } p when p.Contains( "leadwall" ) || p.Contains( "triple_threat" ) => "浪人 · 铅墙",
		{ } p when p.Contains( "sniper" ) => "等离子轨道炮", { } p when p.Contains( "40mm" ) => "40mm 追踪炮",
		{ } p when p.Contains( "meteor" ) => "T-203 · 铝热炮", _ => "泰坦武装"
	};
	static string AbilityName( string name ) => name switch
	{
		"Rocket Salvo" => "火箭齐射", "Laser Shot" => "激光肩炮", "Power Shot" => "强力射击", "Tracker Rockets" => "追踪火箭",
		"Firewall" => "火墙", "Cluster Missile" => "集束导弹", "Arc Wave" => "电弧波", "Vortex Guard" => "涡旋护盾",
		"Gun Shield" => "枪盾", "Particle Guard" => "粒子护盾", "Thermal Shield" => "热能护盾", "Electric Smoke" => "电子烟幕",
		"Sword Block" => "剑格挡", "Tether Trap" => "绊索陷阱", "Energy Siphon" => "能量虹吸",
		"Rearm" => "重新装填", "Laser Tripwire" => "激光绊雷", "Close-Range" => "近距模式", "Long-Range" => "远距模式",
		"Sonar Lock" => "声呐脉冲", "Incendiary Trap" => "燃烧罐", "VTOL Hover" => "垂直悬停", "Phase Dash" => "相位冲刺",
		"Broad Sword" => "宽刃剑", "Titan Melee" => "泰坦近战",
		"Upgrade Core" => "升级核心", "Laser Core" => "激光核心",
		"Smart Core" => "智能核心", "Sword Core" => "剑核心", "Salvo Core" => "齐射核心", "Flame Core" => "火焰核心", "Flight Core" => "飞行核心", _ => name
	};
}
