/// <summary>Resource preparation status; Ready does not certify first-draw GPU pipeline compilation.</summary>
public readonly record struct Titanfall2WeaponPrewarmStatus( bool Started, bool Finished, bool Ready,
	int Requested, int Completed, int Failed, int SoundsReady, int ParticleGraphsReady, int ModelsReady,
	double CpuMilliseconds, double MainMilliseconds, double LargestMainStepMilliseconds, long PcmBytes,
	bool GpuPipelineWarmed = false, int FontGlyphsReady = 0, string LargestMainStepKind = null );

public sealed partial class Titanfall2Weapon
{
	[Property] public bool PrewarmResources { get; set; } = true;
	Titanfall2WeaponPrewarmRequest _weaponPrewarm;
	Titanfall2Mount _weaponPrewarmMount;
	public Titanfall2WeaponPrewarmStatus PrewarmStatus => _weaponPrewarm?.Status ?? default;
	public string[] PrewarmErrors => _weaponPrewarm?.Errors.ToArray() ?? [];

	// Called at spawn and again when equipping/reconfiguring an existing instance.
	void BeginWeaponPrewarm()
	{
		if ( !PrewarmResources || Application.IsHeadless || Application.IsDedicatedServer
			|| Sandbox.Mounting.Directory.Get( MountIdent ?? "titanfall2" ) is not Titanfall2Mount mount || !mount.IsMounted ) return;
		var items = new List<Titanfall2WeaponPrewarmItem>();
		void Sound( string name, bool loop = false ) { if ( Useful( name ) ) items.Add( new( Titanfall2WeaponPrewarmKind.Event, name, loop ) ); }
		void Particle( string name ) { if ( Useful( name ) ) items.Add( new( Titanfall2WeaponPrewarmKind.Particle, name ) ); }
		void AudioSet( Titanfall2WeaponSoundEvents set )
		{
			Sound( set.Fire1 ); Sound( set.Fire2 ); Sound( set.Start ); Sound( set.Loop, Audio.Looping ); Sound( set.End );
		}
		AudioSet( Audio.Player1P ); AudioSet( Audio.Player3P );
		if ( UsesOrdnance )
		{
			Sound( OrdnanceProfile.PrepSound1P ); Sound( OrdnanceProfile.PrepSound3P );
			Sound( OrdnanceProfile.ThrowSound1P ); Sound( OrdnanceProfile.ThrowSound3P );
		}
		if ( ChargeProfile.Valid )
		{
			foreach ( var name in ChargePresentation.Sounds ) Sound( name );
			foreach ( var name in ChargePresentation.Particles ) Particle( name );
		}
		if ( HeatProfile.Valid )
		{
			foreach ( var name in HeatPresentationSounds ) Sound( name );
			foreach ( var name in HeatPresentationParticles ) Particle( name );
		}
		Sound( FireSound1P ); Sound( FireSound ); Sound( Presentation.ShellSound );
		foreach ( var name in new[] { Presentation.MuzzleView, Presentation.MuzzleWorld, Presentation.ShellView,
			Presentation.ShellWorld, Presentation.Shell2View, Presentation.Shell2World, Presentation.TracerView,
			Presentation.TracerWorld, Projectile.TrailEffect, Projectile.ScriptTrailEffect, Projectile.HazardEffect } ) Particle( name );
		foreach ( var layer in Presentation.MuzzleLayers ?? [] )
		{
			if ( Presentation.Silenced && layer.SkipWhenSilenced ) continue;
			Particle( layer.View ); Particle( layer.World );
		}
		if ( Useful( ViewModelPath ) ) items.Add( new( Titanfall2WeaponPrewarmKind.Model, ViewModelPath ) );
		if ( Useful( Projectile.ModelPath ) ) items.Add( new( Titanfall2WeaponPrewarmKind.Model, Projectile.ModelPath ) );
		foreach ( var display in UiDisplays ?? [] )
			if ( Useful( display.UiPath ) ) items.Add( new( Titanfall2WeaponPrewarmKind.WeaponUi, display.UiPath ) );
		foreach ( var table in new[] { Presentation.ImpactTable, Projectile.ImpactTable }.Where( Useful ).Distinct( StringComparer.OrdinalIgnoreCase ) )
			items.Add( new( Titanfall2WeaponPrewarmKind.Impact, table ) );
		_weaponPrewarmMount = mount;
		_weaponPrewarm = mount.QueueWeaponPrewarm( items );
	}

	void TickWeaponPrewarm()
	{
		if ( !PrewarmResources ) return;
		if ( _weaponPrewarm is null || _weaponPrewarm.Cancelled || _weaponPrewarmMount?.IsMounted != true ) BeginWeaponPrewarm();
		_weaponPrewarmMount?.PumpWeaponPrewarm();
	}

	static bool Useful( string value ) => !string.IsNullOrWhiteSpace( value ) && value is not ("none" or "null" or "null_temp");
}
