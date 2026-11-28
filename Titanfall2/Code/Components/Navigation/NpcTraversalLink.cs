using Sandbox.Engine.Resources;
using System.Threading.Tasks;

/// <summary>An authored roof connection. Endpoints must be supported by navigation polygons; the actor validates its whole crossing.</summary>
[Title( "Titanfall 2 Roof Link" ), Category( "Titanfall 2" )]
public sealed class Titanfall2TraversalLink : Component
{
	public enum LinkKinds { Jump, Climb }
	[Property] public LinkKinds Kind { get; set; }
	[Property] public Vector3 LocalStart { get; set; }
	[Property] public Vector3 LocalEnd { get; set; } = new( 192, 0, 192 );
	[Property] public bool Bidirectional { get; set; }
	[Property] public float ConnectionRadius { get; set; } = 32;
	NavMeshLink _link;
	static NavMeshLinkType _jumpType, _climbType;
	static Task<bool> _loadingTypes;
	static DateTime _retryTypesAfter;
	public string LastFailure { get; private set; }
	public static NavMeshLinkType JumpType => _jumpType ??= ResourceLibrary.Get<NavMeshLinkType>( "navigation/titanfall2_rooftop_jump.navlink" );
	public static NavMeshLinkType ClimbType => _climbType ??= ResourceLibrary.Get<NavMeshLinkType>( "navigation/titanfall2_rooftop_climb.navlink" );
	public static int TypesVersion { get; private set; }
	/// <summary>Wait for package GameResources before constructing typed links; compiled .navlink files are registered by the package loader.</summary>
	public static Task<bool> EnsureTypesAsync()
	{
		if ( JumpType is not null && ClimbType is not null && NavMeshLinkType.Jump is not null && NavMeshLinkType.Drop is not null
			&& NavMeshLinkType.Climb is not null && NavMeshLinkType.Ladder is not null ) return System.Threading.Tasks.Task.FromResult( true );
		if ( _loadingTypes is { IsCompleted: false } || _loadingTypes is not null && DateTime.UtcNow < _retryTypesAfter ) return _loadingTypes;
		_retryTypesAfter = DateTime.UtcNow.AddSeconds( 10 );
		return _loadingTypes = LoadTypesAsync();
	}
	static async Task<bool> LoadTypesAsync()
	{
		try
		{
			foreach ( var name in new[] { "jump", "drop", "climb", "ladder" } )
				if ( ResourceLibrary.Get<NavMeshLinkType>( $"navigation/{name}.navlink" ) is null )
					await ResourceLibrary.LoadAsync<NavMeshLinkType>( $"navigation/{name}.navlink" );
			_jumpType ??= await ResourceLibrary.LoadAsync<NavMeshLinkType>( "navigation/titanfall2_rooftop_jump.navlink" );
			_climbType ??= await ResourceLibrary.LoadAsync<NavMeshLinkType>( "navigation/titanfall2_rooftop_climb.navlink" );
			TypesVersion++;
			if ( _jumpType is not null && _climbType is not null && NavMeshLinkType.Jump is not null && NavMeshLinkType.Drop is not null
				&& NavMeshLinkType.Climb is not null && NavMeshLinkType.Ladder is not null ) return true;
			Titanfall2Log.Warning( "Rooftop navigation resources are unavailable; custom crossings remain disabled" );
		}
		catch ( Exception e ) { Titanfall2Log.Warning( $"Rooftop navigation resource load failed: {e.Message}" ); }
		return false;
	}
	protected override void OnStart() => TryCreateLink();
	protected override void OnUpdate() { if ( !_link.IsValid() ) TryCreateLink(); }
	void TryCreateLink()
	{
		_ = EnsureTypesAsync();
		var type = Kind == LinkKinds.Climb ? ClimbType : JumpType;
		if ( type is null ) { LastFailure = "Traversal link resource is unavailable or loading"; return; }
		if ( !LocalStart.IsFinite || !LocalEnd.IsFinite || LocalStart.Distance( LocalEnd ) < 16 ) { LastFailure = "Invalid traversal endpoints"; return; }
		LastFailure = null;
		_link = GameObject.AddComponent<NavMeshLink>();
		_link.Type = type;
		_link.LocalStartPosition = LocalStart;
		_link.LocalEndPosition = LocalEnd;
		_link.ConnectionRadius = Math.Clamp( ConnectionRadius, 8, 128 );
		_link.IsBiDirectional = Bidirectional;
		_link.Occupancy = NavMeshLinkOccupancy.OneAtATime;
		RequestTiles();
	}
	void RequestTiles()
	{
		if ( Scene?.NavMesh is not { IsEnabled: true } nav ) return;
		var a = WorldTransform.PointToWorld( LocalStart );
		var b = WorldTransform.PointToWorld( LocalEnd );
		var bounds = new BBox( Vector3.Min( a, b ) - Vector3.One * 96, Vector3.Max( a, b ) + Vector3.One * 96 );
		nav.RequestTilesGeneration( bounds );
	}
	protected override void OnDisabled() { if ( _link.IsValid() ) _link.Enabled = false; }
	protected override void OnEnabled() { if ( _link.IsValid() ) _link.Enabled = true; }
	protected override void OnDestroy() { if ( _link.IsValid() ) _link.Destroy(); }
}
