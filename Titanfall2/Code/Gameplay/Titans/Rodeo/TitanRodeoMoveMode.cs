using Sandbox.Movement;

/// <summary>External rider attachment, deliberately independent of BaseChair and cockpit occupancy.</summary>
[Hide]
public sealed class Titanfall2TitanRodeoMoveMode : MoveMode
{
	public Titanfall2TitanRodeo Rodeo { get; internal set; }
	public bool IsAttached => Rodeo is { IsValid: true, IsRiding: true } && Rodeo.Rider == Controller;
	bool _captured, _bodyEnabled;
	Vector3 _releaseVelocity;
	bool _hasReleaseVelocity;
	public override int Score( PlayerController controller ) => Rodeo is { IsValid: true, IsRiding: true } && Rodeo.Rider == controller ? 20000 : -100;
	public override Vector3 UpdateMove( Rotation eyes, Vector3 input ) => Vector3.Zero;
	public override void AddVelocity() { }
	public override void OnModeBegin()
	{
		if ( !Controller.Body.IsValid() ) return;
		_bodyEnabled = Controller.Body.Enabled;
		_captured = true;
		Controller.Body.Enabled = false;
		// Keep the rider collider/renderer available to weapon traces and smoke damage.
	}
	public override void PrePhysicsStep() => Rodeo?.UpdateRiderPlacement();
	public override void OnModeEnd( MoveMode next )
	{
		RestoreBody();
		if ( Rodeo is { IsValid: true, IsRiding: true } ) Rodeo.ReleaseRider();
	}
	internal void Release( Vector3 velocity )
	{
		Rodeo = null;
		_releaseVelocity = velocity;
		_hasReleaseVelocity = true;
	}
	void RestoreBody()
	{
		if ( !_captured ) return;
		_captured = false;
		if ( Controller.IsValid() && Controller.Body.IsValid() )
		{
			Controller.Body.Enabled = _bodyEnabled;
			if ( _hasReleaseVelocity && _bodyEnabled ) Controller.Body.Velocity = _releaseVelocity;
		}
		_hasReleaseVelocity = false;
	}
	protected override void OnDisabled() { Rodeo?.ReleaseRider(); RestoreBody(); }
	protected override void OnDestroy() { Rodeo?.ReleaseRider(); RestoreBody(); }
}
