using Sandbox.Npcs.Layers;

public sealed partial class Titanfall2Npc
{
	[Property] public Titanfall2TraversalFamily TraversalFamily { get; set; } = Titanfall2TraversalFamily.Auto;
	[Property] public bool AllowTraversal { get; set; } = true;
	[Sync( SyncFlags.FromHost )] public Titanfall2TraversalPhase TraversalPhase { get; private set; }
	[Sync( SyncFlags.FromHost )] public Titanfall2TraversalKind TraversalKind { get; private set; }
	[Sync( SyncFlags.FromHost )] public float TraversalCycle { get; private set; }
	[Sync( SyncFlags.FromHost )] public int TraversalSerial { get; private set; }
	[Sync( SyncFlags.FromHost )] public string TraversalClip { get; private set; }
	public string LastTraversalFailure { get; private set; }
	public bool IsTraversing => TraversalPhase != Titanfall2TraversalPhase.None
		|| Movement is NavigationLayer { IsTraversingLink: true }
		|| Agent.IsValid() && Agent.IsTraversingLink;
	internal Titanfall2TraversalProfile TraversalProfile => AllowTraversal
		? Titanfall2NpcTraversalRules.Profile( Titanfall2NpcTraversalRules.ResolveFamily( TraversalFamily, ModelPath, IsTitan ) ) : default;

	internal void SetTraversalHost( Titanfall2TraversalPhase phase, Titanfall2TraversalKind kind, float cycle, string clip = null, bool begin = false )
	{
		if ( !Networking.IsHost || IsProxy ) return;
		TraversalPhase = phase;
		TraversalKind = kind;
		TraversalCycle = Math.Clamp( cycle, 0, 1 );
		if ( begin ) { TraversalSerial++; LastTraversalFailure = null; }
		if ( clip is not null || phase == Titanfall2TraversalPhase.None ) TraversalClip = clip;
	}
	internal void SetTraversalFailureHost( string reason ) { if ( Networking.IsHost && !IsProxy ) LastTraversalFailure = reason; }
	internal void CancelTraversal( string reason = "Cancelled", bool immediate = false )
	{
		if ( !Networking.IsHost || IsProxy ) return;
		GetComponent<Titanfall2NpcNavigationLayer>( true )?.CancelTraversal( reason, immediate );
	}
	internal bool TryPlayTraversalAnimation()
	{
		if ( TraversalPhase == Titanfall2TraversalPhase.None ) return false;
		if ( !Renderer.IsValid() ) return true;
		var role = TraversalClip;
		if ( string.IsNullOrEmpty( role ) || !HasActorClip( role ) ) return true;
		if ( LayeredAnimation is { } layered )
		{
			layered.ClearOverlays();
			layered.Play( role, loop: false );
			layered.SetCycle( TraversalCycle );
			layered.Tick();
		}
		else
		{
			PlaySequence( role, looping: false );
			Renderer.Sequence.PlaybackRate = 0;
			Renderer.Sequence.TimeNormalized = TraversalCycle;
		}
		return true;
	}
}
