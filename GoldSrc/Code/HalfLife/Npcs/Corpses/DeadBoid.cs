namespace GoldSrc.HalfLife;

[Hide]
public sealed class DeadBoid : NpcCorpse
{
	[Property] public float FlightFrame { get; set; }
	internal override string PoseName => DeathSequence;
	internal override float PoseTime => FlightFrame;
}
