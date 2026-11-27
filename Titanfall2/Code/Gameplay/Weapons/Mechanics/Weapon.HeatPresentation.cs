public sealed partial class Titanfall2Weapon
{
	readonly List<GameObject> _heatParticles = [];
	GameObject _heatSoundRoot;
	Titanfall2SoundEventEmitter _heatSounds;
	internal int HeatVentVisualSerial { get; private set; }
	internal int HeatBurnoutVisualSerial { get; private set; }
	internal static IEnumerable<string> HeatPresentationSounds => ["lstar_lowammowarning", "LSTAR_VentCooldown", "LSTAR_VentCooldown_3p", "LSTAR_LensBurnout", "LSTAR_LensBurnout_3P"];
	internal static IEnumerable<string> HeatPresentationParticles => ["wpn_mflash_snp_hmn_smokepuff_side_FP", "wpn_mflash_snp_hmn_smokepuff_side", "xo_spark_med", "P_lstar_cooldown_FP", "P_lstar_cooldown_vent_FP", "P_lstar_overheat_smoke_FP"];

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayHeatWarning()
	{
		if ( !HeatProfile.Valid || !StateEffectsFirstPerson ) return;
		HeatSound( "lstar_lowammowarning", true );
	}
	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayHeatVent()
	{
		if ( !HeatProfile.Valid || !IsHeld || !IsActive ) return;
		HeatVentVisualSerial++;
		var first = StateEffectsFirstPerson;
		HeatParticle( first ? "wpn_mflash_snp_hmn_smokepuff_side_FP" : "wpn_mflash_snp_hmn_smokepuff_side", "SWAY_ROTATE", first );
		HeatSound( first ? "LSTAR_VentCooldown" : "LSTAR_VentCooldown_3p", first );
		ViewModel?.GetComponent<Titanfall2PilotViewModel>()?.PlayHeatVent();
	}
	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayHeatBurnout()
	{
		if ( !HeatProfile.Valid || !IsHeld || !IsActive ) return;
		HeatBurnoutVisualSerial++;
		var first = StateEffectsFirstPerson;
		foreach ( var attachment in new[] { "shell", "spinner", "vent_cover_L", "vent_cover_R" } ) HeatParticle( "xo_spark_med", attachment, first );
		HeatSound( first ? "LSTAR_LensBurnout" : "LSTAR_LensBurnout_3P", first );
	}
	void HeatParticle( string effect, string attachment, bool firstPerson )
	{
		_heatParticles.RemoveAll( x => !x.IsValid() );
		var model = (firstPerson ? ViewModel : WorldModel)?.GetComponent<BaseWeaponModel>();
		var particle = Titanfall2WeaponStateEffects.Spawn( model, MountIdent, effect, attachment, firstPerson, 3 );
		if ( particle.IsValid() ) _heatParticles.Add( particle );
	}
	void HeatSound( string name, bool firstPerson )
	{
		if ( Application.IsDedicatedServer || Application.IsHeadless ) return;
		if ( !_heatSoundRoot.IsValid() )
		{
			_heatSoundRoot = new GameObject( GameObject, false, "TF2 heat sounds" );
			_heatSoundRoot.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			_heatSounds = _heatSoundRoot.AddComponent<Titanfall2SoundEventEmitter>(); _heatSoundRoot.Enabled = true;
		}
		_heatSounds.Play( MountIdent, name, firstPerson, WorldPosition );
	}
	void StopHeatPresentation()
	{
		foreach ( var particle in _heatParticles ) if ( particle.IsValid() ) particle.Destroy();
		_heatParticles.Clear(); _heatSounds?.StopAll();
		if ( _heatSoundRoot.IsValid() ) _heatSoundRoot.Destroy();
		_heatSoundRoot = null; _heatSounds = null;
		ViewModel?.GetComponent<Titanfall2PilotViewModel>()?.StopStateParticles();
	}
}
