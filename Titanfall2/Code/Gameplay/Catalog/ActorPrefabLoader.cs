/// <summary>Builds spawnable NPC, Frontier Defense, and drivable Titan prefabs.</summary>
internal sealed class Titanfall2ActorPrefabLoader( Titanfall2ActorCatalogEntry definition, Titanfall2WeaponData data ) : ResourceLoader<Titanfall2Mount>
{
	PrefabFile _prefab;

	protected override object Load()
	{
		string Mounted( string name ) => name is null ? null : $"mount://{Host.Ident}/{name}.vmdl";
		var preview = Mounted( definition.Model + ".preview" );
		var builder = new PrefabBuilder().WithName( Path )
			.WithMetadata( "Title", definition.Title )
			.WithMetadata( "PreviewModel", preview )
			.WithMetadata( "PreviewSequence", null );
		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;
			var root = new GameObject( true, definition.Title );
			root.Tags.Add( "npc" );

			var senses = root.AddComponent<Sandbox.Npcs.Layers.SensesLayer>();
			senses.SightRange = definition.Titan ? 4200f : 2000f;
			senses.HearingRange = senses.SightRange * 0.65f;

			if ( definition.Navigation == Titanfall2Npc.Navigation.Ground && definition.Role == Titanfall2Npc.FrontierRole.None )
			{
				var navigation = root.AddComponent<Titanfall2NpcNavigationLayer>();
				navigation.WalkSpeed = definition.Speed * 0.65f;
				navigation.RunSpeed = definition.Speed;
			}

			var npc = root.AddComponent<Titanfall2Npc>();
			ConfigureActor( root, npc, Mounted );
			ConfigureWeapon( npc, Mounted );
			ConfigureNavigationAndBody( root, npc );
			ConfigureTitanComponents( root, npc );
		}
		return _prefab = builder.Create();
	}

	void ConfigureActor( GameObject root, Titanfall2Npc npc, Func<string, string> mounted )
	{
		npc.DisplayName = definition.Title;
		npc.ModelPath = mounted( definition.Model + ".infantry" );
		npc.MountIdent = Host.Ident;
		npc.Faction = definition.Faction;
		npc.VoiceProfile = definition.VoiceProfile ?? (definition.Titan ? "titan" : null);
		npc.MaxHealth = definition.Health;
		npc.MaxShield = definition.Shield;
		npc.IsTitan = definition.Titan;
		// Faction does not imply an owner or an Auto-Titan follow/guard assignment.
		npc.AiMode = Titanfall2NpcAiMode.EnemyMp;
		npc.AiSettingsName = AiSettingsForActor( definition );
		var isReaper = definition.Model.Contains( "/super_spectre/", StringComparison.OrdinalIgnoreCase )
			|| definition.Id.StartsWith( "npc_reaper", StringComparison.OrdinalIgnoreCase );
		var isMarvin = definition.Model.Contains( "/marvin/", StringComparison.OrdinalIgnoreCase );
		var isDroneOrTurret = definition.Model.Contains( "/drone_air_attack/", StringComparison.OrdinalIgnoreCase )
			|| definition.Model.Contains( "/turret_plasma/", StringComparison.OrdinalIgnoreCase )
			|| definition.Id.StartsWith( "npc_attack_drone", StringComparison.OrdinalIgnoreCase )
			|| definition.Id.StartsWith( "npc_plasma_turret", StringComparison.OrdinalIgnoreCase );

		// Native AI settings distinguish lock class from armor. In particular
		// Reapers are large smart-ammo targets even though IsTitan is false.
		npc.SmartAmmoLockType = definition.Titan || isReaper ? Titanfall2SmartAmmoLockType.Large
			: isMarvin ? Titanfall2SmartAmmoLockType.None
			: isDroneOrTurret ? Titanfall2SmartAmmoLockType.Any : Titanfall2SmartAmmoLockType.Small;
		npc.BodyHeight = definition.Height;
		npc.BodyRadius = definition.Radius;
		npc.NavigationMode = definition.Navigation;
		npc.MoveSpeed = definition.Speed;
		npc.MeleeOnly = definition.MeleeDamage > 0 && data is null;
		npc.MeleeDamage = definition.MeleeDamage > 0 ? definition.MeleeDamage
			: definition.Titan ? 100 : isReaper ? 55
			: definition.Id.Contains( "stalker", StringComparison.OrdinalIgnoreCase ) ? 35
			: definition.Id.Contains( "spectre", StringComparison.OrdinalIgnoreCase ) ? 28 : data is not null && definition.Navigation == Titanfall2Npc.Navigation.Ground ? 20 : 0;
		npc.MeleeRange = definition.Titan ? 190 : isReaper ? 125 : definition.Radius + 46;
		npc.MeleeWindup = definition.Height > 100 ? 0.4f : 0.28f;
		npc.MeleeInterval = definition.Height > 100 ? 1.6f : 1.15f;
		npc.Role = definition.Role;
		npc.CombatStyle = definition.Id.Contains( "pilot", StringComparison.OrdinalIgnoreCase ) ? Titanfall2Npc.CombatStyles.Pilot
			: definition.Id.Contains( "stalker", StringComparison.OrdinalIgnoreCase ) || definition.MeleeDamage > 0 ? Titanfall2Npc.CombatStyles.Assault : Titanfall2Npc.CombatStyles.Infantry;
		npc.UseCover = !definition.Titan && definition.Height <= 80 && definition.MeleeDamage == 0;
		npc.ReactionSeconds = npc.CombatStyle == Titanfall2Npc.CombatStyles.Pilot ? 0.12f : 0.25f;
		npc.BurstShots = definition.Titan || npc.CombatStyle == Titanfall2Npc.CombatStyles.Assault ? 6 : npc.CombatStyle == Titanfall2Npc.CombatStyles.Pilot ? 5 : 3;
		npc.BurstPause = npc.CombatStyle == Titanfall2Npc.CombatStyles.Pilot ? 0.2f : 0.45f;
		npc.CanFight = data is not null || definition.MeleeDamage > 0 || definition.Role == Titanfall2Npc.FrontierRole.ArcTitan;
		if ( definition.MeleeDamage > 0 ) npc.AttackRange = definition.Radius + 46;
		npc.AttackPlayers = npc.CanFight
			&& !Titanfall2Npc.IsAllyFaction( definition.Faction )
			&& !Titanfall2Npc.IsNeutralFaction( definition.Faction );
	}

	void ConfigureWeapon( Titanfall2Npc npc, Func<string, string> mounted )
	{
		if ( data is not null )
		{
			npc.AiWeaponScript = data.ScriptName;
			npc.GunModelPath = mounted( data.WorldModel is null ? null : data.WorldModel + ".visual" );
			if ( definition.Titan && data.ViewModel is not null )
				npc.ViewModelPath = mounted( data.ViewModel + ".titanpov" );
			npc.Projectile = data.Projectile with { ModelPath = mounted( data.Projectile.ModelPath ), MountIdent = Host.Ident };
			if ( !string.IsNullOrEmpty( definition.ProjectileTrailOverride ) )
				npc.Projectile = npc.Projectile with { TrailEffect = definition.ProjectileTrailOverride, MountIdent = Host.Ident };
			npc.FireSound = Host.GetGameplayFireSound( data.FireSounds );
			npc.WeaponPresentation = data.Presentation;
			npc.WeaponAudio = Host.GetWeaponAudio( data );
			npc.ShotInterval = 1 / data.ShotsPerSecond;
			npc.ShotDamage = data.Damage;
			npc.TitanShotDamage = data.TitanDamage;
			npc.DamageProfile = data.DamageProfile;
			npc.ShotPellets = data.Pellets;
			npc.Firing = data.Firing;
			npc.SpreadProfile = data.SpreadProfile;
			npc.ShotSpread = data.Spread;
			npc.AdsFov = data.AdsFov;
			npc.AdsMoveSpeedScale = data.AdsMoveSpeedScale;
			npc.MagazineSize = data.Magazine;
			npc.ReloadSeconds = data.ReloadSeconds;
			if ( definition.Titan )
			{
				npc.AttackRange = MathF.Min( data.Range, 2800 );
				npc.DetectionRange = 4200;
			}
		}

		if ( definition.Role == Titanfall2Npc.FrontierRole.ArcTitan )
		{
			npc.GunModelPath = mounted( Titanfall2GameplayCatalogData.ArcCannonWorldModel + ".visual" );
			npc.DetectionRange = 4200;
			npc.AttackRange = 2400;
		}
		if ( definition.Role == Titanfall2Npc.FrontierRole.EliteMonarch )
		{
			npc.ShotSpread *= 0.3f;
			npc.ShotSpreadScale = 0.3f;
		}
		if ( definition.Role == Titanfall2Npc.FrontierRole.MortarTitan && Host.GetMortarWeapon() is { } mortar )
		{
			npc.MortarProjectile = mortar.Projectile with
			{
				ModelPath = mounted( mortar.Projectile.ModelPath is { } projectileModel ? projectileModel + ".visual" : null ),
				MountIdent = Host.Ident
			};
			npc.MortarDamage = mortar.Damage;
			npc.MortarTitanDamage = mortar.TitanDamage;
			npc.FireSound = "Weapon_Titan_Rocket_Launcher.RapidFire_3P";
		}
	}

	void ConfigureNavigationAndBody( GameObject root, Titanfall2Npc npc )
	{
		if ( definition.Navigation == Titanfall2Npc.Navigation.Ground && definition.Speed > 0 )
		{
			npc.Agent = root.AddComponent<NavMeshAgent>();
			npc.Agent.Height = definition.Height;
			npc.Agent.Radius = definition.Radius;
			npc.Agent.MaxSpeed = definition.Speed;
			npc.Agent.UpdateRotation = false;
		}
		npc.Renderer = root.AddComponent<SkinnedModelRenderer>();
		npc.Renderer.UseAnimGraph = false;
		var hull = root.AddComponent<BoxCollider>();
		hull.Scale = new( 2 * definition.Radius - 2, 2 * definition.Radius - 2, definition.Height - 2 );
		hull.Center = Vector3.Up * (hull.Scale.z * 0.5f);
		root.AddComponent<Rigidbody>().MotionEnabled = false;
	}

	static string AiSettingsForActor( Titanfall2ActorCatalogEntry actor )
	{
		if ( actor.Titan ) return Titanfall2NpcAiProfileCatalog.SettingsFor( actor.Chassis, Titanfall2NpcAiMode.EnemyMp );
		var model = actor.Model ?? "";
		if ( model.Contains( "/super_spectre/", StringComparison.OrdinalIgnoreCase ) ) return "npc_super_spectre";
		if ( model.Contains( "/spectre/", StringComparison.OrdinalIgnoreCase ) ) return "npc_spectre";
		if ( model.Contains( "/stalker/", StringComparison.OrdinalIgnoreCase ) ) return "npc_stalker";
		if ( model.Contains( "/grunts/", StringComparison.OrdinalIgnoreCase ) ) return "npc_soldier";
		if ( model.Contains( "/pilots/", StringComparison.OrdinalIgnoreCase ) ) return "npc_pilot_elite";
		return actor.Id;
	}

	void ConfigureTitanComponents( GameObject root, Titanfall2Npc npc )
	{
		if ( !definition.Titan ) return;
		root.Tags.Add( "titan" );
		if ( definition.Role != Titanfall2Npc.FrontierRole.None ) return;

		var kit = root.AddComponent<Titanfall2TitanKit>();
		kit.Actor = npc;
		kit.Kit = definition.Chassis != Titanfall2TitanKit.Chassis.None ? definition.Chassis : Titanfall2TitanKit.Chassis.Monarch;
		root.AddComponent<Titanfall2TitanCore>().Actor = npc;

		npc.Seat = root.AddComponent<Titanfall2TitanSeat>();
		npc.Seat.Actor = npc;
		root.AddComponent<Titanfall2TitanRodeo>().Actor = npc;
		npc.Seat.DriveSpeed = definition.Speed;
		npc.Seat.TooltipTitle = definition.Title;
		npc.Seat.TooltipDescription = "Embark";
		npc.Seat.PitchRange = new Vector2( -65, 65 );
		npc.Seat.YawRange = new Vector2( -115, 115 );
		npc.Seat.SeatPosition = new GameObject( root, true, "Cockpit" ) { LocalPosition = new Vector3( 16, 0, definition.Height * 0.72f ) };
		npc.Seat.EyePosition = new GameObject( npc.Seat.SeatPosition, true, "Pilot View" ) { LocalPosition = new Vector3( 55, 0, 26 ) };
		npc.Seat.ExitPoints =
		[
			new GameObject( root, true, "Left Exit" ) { LocalPosition = new Vector3( 0, definition.Radius + 36, 8 ) },
			new GameObject( root, true, "Right Exit" ) { LocalPosition = new Vector3( 0, -definition.Radius - 36, 8 ) }
		];
	}

	protected override void Shutdown()
	{
		PrefabBuilder.Destroy( _prefab );
		_prefab = null;
	}
}
