namespace GoldSrc.HalfLife;

[Hide]
public abstract class ScientistBase : Personnel
{
	protected ScientistBase() => Body = -1;

	// A spawned object's id is shared by peers, so a random head survives late joins.
	internal static int ResolveBody( int body, GameObject owner ) => body == -1 ? owner.Id.GetHashCode() & 3 : body;
	internal override int EffectiveBody => ResolveBody( Body, GameObject );

	protected override int PainVariants => 5;
	protected override string PreDisasterGreeting => "PHELLO";
	protected override string PreDisasterIdle => "PIDLE";
	internal override void DeathSound() => PainSound();

	internal override string SentencePrefix => "SC";

	internal override int EffectiveSkin => Skin == 0 && EffectiveBody == 2 ? 1 : Skin;

	internal override float VoicePitch => (EffectiveBody % 3) switch { 0 => 1.05f, 2 => 0.95f, _ => 1 };

	internal override NpcSounds Sounds { get; } = new( "!SC_IDLE", "!SC_FEAR", "scientist/sci_pain", null,
		Follow: "!SC_OK", Wait: "!SC_WAIT" );
}
