using Sandbox.Rendering;

namespace Source1;

[Title( "Source Underwater" ), Category( "Source Engine" )]
public sealed class SourceUnderwater : BasePostProcess<SourceUnderwater>
{
	[Property] public Material Overlay { get; set; }

	public override void Render()
	{
		if ( !Overlay.IsValid() ) return;
		Blit( BlitMode.WithBackbuffer( Overlay, Stage.BeforePostProcess, 3000 ), "Source underwater" );
	}
}
