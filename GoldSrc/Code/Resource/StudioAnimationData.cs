namespace GoldSrc;

[Sandbox.ModelEditor.GameData( "animation_data" ), Hide]
public sealed class StudioAnimationData
{
	public string Skeleton { get; set; }
	public string ClipFolder { get; set; }
	public string[] Clips { get; set; }
}

[Sandbox.ModelEditor.GameData( "sequence_data", AllowMultiple = true ), Hide]
public sealed class StudioSequenceData
{
	public string Name { get; set; }
	public string Activity { get; set; }
	public int ActivityWeight { get; set; }
}
