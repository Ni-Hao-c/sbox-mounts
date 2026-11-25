public record struct Titanfall2ChargePresentation
{
	public string Windup1P { get; set; }
	public string Windup3P { get; set; }
	public string Winddown1P { get; set; }
	public string Winddown3P { get; set; }
	public bool SeekWindup { get; set; }
	public bool SeekWinddown { get; set; }
	public bool StopWhenFull { get; set; }
	public bool StopWhenEmpty { get; set; }
	public bool EffectWhileDraining { get; set; }
	public float DrainSoundSeconds { get; set; }
	public string Effect1P { get; set; }
	public string Effect3P { get; set; }
	public string Attachment { get; set; }
	public string Effect2_1P { get; set; }
	public string Effect2_3P { get; set; }
	public string Attachment2 { get; set; }
	internal readonly IEnumerable<string> Sounds => [Windup1P, Windup3P, Winddown1P, Winddown3P];
	internal readonly IEnumerable<string> Particles => [Effect1P, Effect3P, Effect2_1P, Effect2_3P];
}

internal sealed partial record Titanfall2WeaponData
{
	internal Titanfall2ChargePresentation ChargePresentation { get; init; }
	static Titanfall2ChargePresentation ReadChargePresentation( Func<string, string> get, Func<string, float, float> number ) => new()
	{
		Windup1P = get( "charge_sound_1p" ), Windup3P = get( "charge_sound_3p" ),
		Winddown1P = get( "charge_drain_sound_1p" ), Winddown3P = get( "charge_drain_sound_3p" ),
		SeekWindup = get( "charge_sound_seek_to_charge_fraction" ) == "1", SeekWinddown = get( "charge_drain_sound_seek_to_charge_fraction" ) == "1",
		StopWhenFull = get( "charge_sound_stop_when_full" ) == "1", StopWhenEmpty = get( "charge_drain_sound_stop_when_empty" ) == "1",
		EffectWhileDraining = get( "charge_effect_show_during_drain" ) != "0", DrainSoundSeconds = MathF.Max( 0, number( "cooldown_time", 0 ) ),
		Effect1P = get( "charge_effect_1p" ), Effect3P = get( "charge_effect_3p" ), Attachment = get( "charge_effect_attachment" ),
		Effect2_1P = get( "charge_effect2_1p" ), Effect2_3P = get( "charge_effect2_3p" ), Attachment2 = get( "charge_effect2_attachment" )
	};
}

public sealed partial class Titanfall2Weapon
{
	[Property] public Titanfall2ChargePresentation ChargePresentation { get; set; }
	Titanfall2SoundEventEmitter _chargeSound;
	GameObject _chargeVisualRoot;
	readonly List<GameObject> _chargeParticles = [];
	int _chargeVisualState = -1;
	bool _chargeVisualFirstPerson;
	BaseWeaponModel _chargeVisualModel;
	internal int ChargeVisualState => _chargeVisualState;
	internal float ChargeSoundSeek { get; private set; }
	internal int ChargeSoundStarts { get; private set; }
	internal int ChargeParticleCount => _chargeParticles.Count( x => x.IsValid() );
	bool StateEffectsFirstPerson => !IsProxy && ViewModel.IsValid() && WeaponModel == ViewModel.GetComponent<BaseWeaponModel>();

	void UpdateChargePresentation()
	{
		if ( !ChargeProfile.Valid || !IsHeld || !IsActive || !Enabled || Application.IsDedicatedServer || Application.IsHeadless )
		{ StopChargePresentation(); return; }
		var firstPerson = StateEffectsFirstPerson;
		var model = (firstPerson ? ViewModel : WorldModel)?.GetComponent<BaseWeaponModel>();
		if ( !model.IsValid() ) { StopChargePresentation(); return; }
		var fraction = ChargeFraction;
		var state = fraction <= 0 ? 0 : fraction >= 1 ? 3 : ChargePhase == Titanfall2ChargePhase.Charging ? 1 : 2;
		if ( model != _chargeVisualModel || firstPerson != _chargeVisualFirstPerson ) StopChargePresentation();
		_chargeVisualModel = model; _chargeVisualFirstPerson = firstPerson;
		if ( _chargeVisualRoot.IsValid() ) _chargeSound?.SetSource( model.WorldPosition );
		if ( state == _chargeVisualState ) return;
		_chargeVisualState = state;
		_chargeSound?.StopAll();
		if ( !_chargeVisualRoot.IsValid() )
		{
			_chargeVisualRoot = new GameObject( GameObject, false, "TF2 charge sound" );
			_chargeVisualRoot.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			_chargeSound = _chargeVisualRoot.AddComponent<Titanfall2SoundEventEmitter>(); _chargeVisualRoot.Enabled = true;
		}
		var sound = state == 1 || state == 3 && !ChargePresentation.StopWhenFull
			? firstPerson ? ChargePresentation.Windup1P : ChargePresentation.Windup3P
			: state == 2 ? firstPerson ? ChargePresentation.Winddown1P : ChargePresentation.Winddown3P : null;
		ChargeSoundSeek = state == 2 ? ChargePresentation.SeekWinddown ? (1 - fraction) * ChargePresentation.DrainSoundSeconds : 0
			: ChargePresentation.SeekWindup ? fraction * ChargeProfile.ChargeSeconds : 0;
		if ( !string.IsNullOrEmpty( sound ) && _chargeSound.Play( MountIdent, sound, firstPerson, model.WorldPosition, seekSeconds: ChargeSoundSeek ) ) ChargeSoundStarts++;
		var effects = state is 1 or 3 || state == 2 && ChargePresentation.EffectWhileDraining;
		if ( !effects ) { DestroyChargeParticles(); return; }
		if ( _chargeParticles.Count == 0 )
		{
			Add( firstPerson ? ChargePresentation.Effect1P : ChargePresentation.Effect3P, ChargePresentation.Attachment );
			Add( firstPerson ? ChargePresentation.Effect2_1P : ChargePresentation.Effect2_3P, ChargePresentation.Attachment2 );
		}
		void Add( string name, string attachment )
		{
			var particle = Titanfall2WeaponStateEffects.Spawn( model, MountIdent, name, attachment, firstPerson );
			if ( particle.IsValid() ) _chargeParticles.Add( particle );
		}
	}
	void DestroyChargeParticles() { foreach ( var particle in _chargeParticles ) if ( particle.IsValid() ) particle.Destroy(); _chargeParticles.Clear(); }
	void StopChargePresentation()
	{
		_chargeSound?.StopAll();
		if ( _chargeVisualRoot.IsValid() ) _chargeVisualRoot.Destroy();
		_chargeVisualRoot = null; _chargeSound = null; _chargeVisualModel = null; _chargeVisualState = -1;
		DestroyChargeParticles();
	}
}

static class Titanfall2WeaponStateEffects
{
	internal static GameObject Spawn( BaseWeaponModel model, string ident, string name, string attachment, bool firstPerson, float lifetime = 0 )
	{
		if ( Application.IsDedicatedServer || Application.IsHeadless || !model.IsValid() || !model.Renderer.IsValid() || string.IsNullOrWhiteSpace( name )
			|| Sandbox.Mounting.Directory.Get( ident ?? "titanfall2" ) is not Titanfall2Mount mount
			|| !mount.TryPrepareWeaponParticle( name, out var definition ) ) return null;
		var anchor = model.Renderer.GetAttachmentObject( attachment ?? "muzzle_flash" );
		if ( !anchor.IsValid() ) return null;
		var go = new GameObject( anchor, false, name );
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked; go.LocalTransform = global::Transform.Zero;
		go.AddComponent<Titanfall2ParticleControlPoints>();
		if ( !Titanfall2ParticleFactory.TryCreate( go, mount, definition, out _, out _ ) ) { go.Destroy(); return null; }
		foreach ( var renderer in go.GetComponentsInChildren<ParticleSpriteRenderer>( true ) )
		{
			renderer.RenderOptions.Game = !firstPerson; renderer.RenderOptions.Overlay = firstPerson;
			if ( firstPerson ) { renderer.CameraFadeNear = 0; renderer.CameraFadeFar = 0; renderer.DepthFeather = 0; }
		}
		foreach ( var renderer in go.GetComponentsInChildren<ModelRenderer>( true ) )
		{ renderer.RenderOptions.Game = !firstPerson; renderer.RenderOptions.Overlay = firstPerson; renderer.RenderType = ModelRenderer.ShadowRenderType.Off; }
		foreach ( var renderer in go.GetComponentsInChildren<ParticleModelRenderer>( true ) )
		{ renderer.RenderOptions.Game = !firstPerson; renderer.RenderOptions.Overlay = firstPerson; }
		foreach ( var renderer in go.GetComponentsInChildren<Titanfall2ParticleRefractionRenderer>( true ) )
		{ renderer.RenderOptions.Game = !firstPerson; renderer.RenderOptions.Overlay = firstPerson; }
		if ( lifetime > 0 ) { var cleanup = go.AddComponent<TemporaryEffect>(); cleanup.DestroyAfterSeconds = lifetime; cleanup.WaitForChildEffects = false; }
		go.Enabled = true; return go;
	}
}

[Hide]
public sealed class Titanfall2WeaponParticleFollow : Component
{
	readonly Dictionary<int, GameObject> _anchors = [];
	internal void Set( int index, GameObject anchor ) { _anchors[index] = anchor; UpdatePoints(); }
	protected override void OnUpdate() => UpdatePoints();
	void UpdatePoints()
	{
		var points = GetComponent<Titanfall2ParticleControlPoints>();
		if ( !points.IsValid() ) return;
		foreach ( var entry in _anchors ) if ( entry.Value.IsValid() ) points.SetControlPoint( entry.Key, entry.Value.WorldPosition );
	}
}
