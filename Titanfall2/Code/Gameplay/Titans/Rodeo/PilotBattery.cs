/// <summary>One carried battery per pilot. It is never client writable and is lost on death.</summary>
[Title( "Titanfall 2 Pilot Battery" ), Group( "Titanfall 2" )]
public sealed class Titanfall2PilotBattery : Component
{
	[Sync( SyncFlags.FromHost )] public bool HasBattery { get; private set; }
	internal bool Give()
	{
		if ( !Networking.IsHost || HasBattery ) return false;
		HasBattery = true;
		return true;
	}
	internal bool Consume()
	{
		if ( !Networking.IsHost || !HasBattery ) return false;
		HasBattery = false;
		return true;
	}
	protected override void OnUpdate()
	{
		if ( Networking.IsHost && HasBattery && !IsPilotAlive( GetComponent<PlayerController>() ) ) HasBattery = false;
	}
	internal static bool IsPilotAlive( PlayerController pilot )
	{
		if ( !pilot.IsValid() || !pilot.Active || pilot.GameObject.IsDestroyed ) return false;
		// Sandbox game players own health outside PlayerController. Use the public TypeLibrary,
		// as the existing Stim adapter does, rather than reflecting into a particular game's player.
		foreach ( var component in pilot.GameObject.Components.GetAll() )
		{
			if ( !component.IsValid() ) continue;
			var health = Game.TypeLibrary?.GetType( component.GetType() )?.GetProperty( "Health" );
			if ( health is { CanRead: true } && health.PropertyType == typeof( float ) && health.GetValue( component ) is float value && value <= 0 ) return false;
		}
		return true;
	}
}
