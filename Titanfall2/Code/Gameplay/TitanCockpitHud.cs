using Sandbox.UI;
using Sandbox.UI.Construct;

/// <summary>A local-only native HUD. Its state is read from the actor, kit and core.</summary>
[Hide]
public sealed partial class Titanfall2TitanCockpitHud : PanelComponent
{
	[Property] public Titanfall2TitanSeat Seat { get; set; }
	Label _name, _link, _health, _shield, _ammo, _weapon, _coreName, _coreState, _message, _controls, _target;
	Panel _healthFill, _shieldFill, _reticle;
	Panel _healthSegments;
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
	readonly List<(Panel Panel, Label Key, Label Name, Label State, Panel Fill)> _abilities = [];
	TimeUntil _targetCheck;
	GameObject _aimTarget;

	protected override void OnEnabled()
	{
		base.OnEnabled();
		if ( !Panel.IsValid() ) return;
		_abilities.Clear();
		_abilityArt.Clear();
		_requestedArt.Clear();
		_artKit = (Titanfall2TitanKit.Chassis)(-1);
		_artWeapon = "";
		_artHealthSegments = 0;
		_lastHealth = 0;
		// Mount assets are not part of the host game's stylesheet filesystem.
		Panel.StyleSheet.AddInline( CockpitStyles, "titanfall2:cockpit" );
		_damageArt = Panel.Add.Image( classname: "damage-overlay" );
		var header = Panel.Add.Panel( "link-header" );
		_name = header.Add.Label( "", "chassis" );
		_link = header.Add.Label( "", "link-state" );
		_reticle = Panel.Add.Panel( "reticle" );
		_reticleArt = _reticle.Add.Image( classname: "reticle-art" );
		_reticleRight = _reticle.Add.Image( classname: "reticle-art reticle-right" );
		_reticleDot = _reticle.Add.Image( classname: "reticle-dot" );
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
		for ( var i = 0; i < 2; i++ )
		{
			var card = row.Add.Panel( "ability" );
			_abilityArt.Add( card.Add.Image( classname: "ability-art" ) );
			_abilities.Add( (card, card.Add.Label( "", "key" ), card.Add.Label( "", "ability-name" ),
				card.Add.Label( "", "ability-state" ), AddBar( card, "ability-bar" )) );
		}
		var dash = Panel.Add.Panel( "dash" );
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
		var visible = Seat.IsValid() && Seat.LocalPilot.IsValid() && actor is { Dead: false };
		Panel.Style.Display = visible ? DisplayMode.Flex : DisplayMode.None;
		if ( !visible || !_name.IsValid() ) return;
		var kit = actor.GetComponent<Titanfall2TitanKit>();
		var core = actor.GetComponent<Titanfall2TitanCore>();
		UpdateRetailArtwork( actor, kit );
		_name.Text = actor.DisplayName ?? "TITAN";
		_link.Text = Seat.IsEmbarking ? $"正在登乘 · {Seat.EmbarkRemaining:0.0}s" : "LINK ESTABLISHED · 手动驾驶";
		Panel.SetClass( "boarding", Seat.IsEmbarking );
		Panel.SetClass( "piloting", !Seat.IsEmbarking );
		Panel.SetClass( "critical", actor.IsDoomed || actor.Health <= actor.MaxHealth * 0.25f );
		var healthMaximum = actor.IsDoomed ? actor.MaxDoomedHealth : actor.MaxHealth;
		_health.Text = $"{(actor.IsDoomed ? "危急 · DOOMED  " : "")}{MathF.Ceiling( actor.Health ):N0} / {healthMaximum:N0}";
		_shield.Text = $"护盾  {MathF.Ceiling( actor.Shield ):N0} / {actor.MaxShield:N0}";
		Fill( _healthFill, actor.Health / MathF.Max( 1, healthMaximum ) );
		Fill( _shieldFill, actor.Shield / MathF.Max( 1, actor.MaxShield ) );
		_weapon.Text = WeaponName( actor.GunModelPath );
		_ammo.Text = actor.Reloading ? "装填中" : core is { UnlimitedAmmo: true } ? "∞" : $"{actor.Rounds:00}";
		_ammo.SetClass( "reloading", actor.Reloading );
		if ( kit.IsValid() )
		{
			if ( kit.Kit == Titanfall2TitanKit.Chassis.Ion )
			{
				SetAbility( 0, Seat.IonLaserAction, AbilityName( kit.OffenseName ), kit.OffenseRemaining, 1,
					$"能量 {kit.SharedEnergy}/{kit.SharedEnergyMaximum}", kit.CanIonLaser );
				SetAbility( 1, "duck", AbilityName( kit.DefenseName ), 0, 1,
					kit.IonVortexActive ? $"按住吸收 · 松开反射 · {kit.CapturedAmmo} 发" : "按住使用 · 共享能量", kit.SharedEnergy >= 100 );
				foreach ( var card in _abilities ) Fill( card.Fill, (float)kit.SharedEnergy / kit.SharedEnergyMaximum );
			}
			else
			{
			SetAbility( 0, "attack2", AbilityName( kit.OffenseName ), kit.OffenseRemaining,
				kit.OffenseCooldown, kit.Kit == Titanfall2TitanKit.Chassis.Tone ? $"标记 {kit.TrackerMarks}/3" : null,
				kit.Kit != Titanfall2TitanKit.Chassis.Tone || kit.HasTrackerLock );
			SetAbility( 1, "duck", AbilityName( kit.DefenseName ), kit.DefenseRemaining,
				kit.Kit == Titanfall2TitanKit.Chassis.Northstar ? Titanfall2TetherRules.RecoverySeconds : kit.DefenseCooldown,
				kit.GuardRemaining > 0 ? $"生效 {kit.GuardRemaining:0.0}s" : null );
			}
			Fill( _dashFill, 1 - kit.DashRemaining / MathF.Max( 0.1f, kit.DashRechargeSeconds ) );
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
		_controls.Text = $"[{Key( "attack1" )}] 开火   [{Key( "reload" )}] 装填   [{Key( "run" )}] 冲刺   [{Key( "use" )}] 离舱   [{Key( Seat.EjectAction )}] 弹射";
		if ( kit?.Kit == Titanfall2TitanKit.Chassis.Ion ) _controls.Text += $"   [{Key( "attack2" )}] 按住分裂射击";
		UpdateTarget( actor, kit );
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
		"Gun Shield" => "枪盾", "Particle Guard" => "粒子护盾", "Thermal Shield" => "热能护盾", "Electric Smoke" => "电烟雾",
		"Sword Block" => "剑格挡", "Tether Trap" => "绊索陷阱", "Energy Siphon" => "能量虹吸", "Upgrade Core" => "升级核心", "Laser Core" => "激光核心",
		"Smart Core" => "智能核心", "Sword Core" => "剑核心", "Salvo Core" => "齐射核心", "Flame Core" => "火焰核心", "Flight Core" => "飞行核心", _ => name
	};
}
