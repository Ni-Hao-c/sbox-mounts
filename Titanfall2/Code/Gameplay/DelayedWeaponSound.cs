/// <summary>Retail delayed weapon callbacks survive holstering but stop when the weapon is destroyed.</summary>
[Hide]
public sealed class Titanfall2DelayedWeaponSound : Component
{
	GameObject _owner;
	string _mount, _name;
	TimeUntil _due;
	bool _firstPerson;
	Vector3 _localPosition;

	internal static void Queue( GameObject owner, string mount, string name, float delay, bool firstPerson, Vector3 position )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !owner.IsValid() || string.IsNullOrWhiteSpace( name ) ) return;
		var go = new GameObject( owner.Scene, false, "TF2 Delayed Weapon Sound" );
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		var pending = go.AddComponent<Titanfall2DelayedWeaponSound>();
		pending._owner = owner; pending._mount = mount; pending._name = name;
		pending._due = MathF.Max( 0, delay ); pending._firstPerson = firstPerson;
		pending._localPosition = owner.WorldTransform.PointToLocal( position );
		go.Enabled = true;
	}

	protected override void OnUpdate()
	{
		if ( !_owner.IsValid() ) { GameObject.Destroy(); return; }
		if ( _due > 0 ) return;
		Titanfall2SoundEventEmitter.OneShot( Scene, _mount, _name, _owner.WorldTransform.PointToWorld( _localPosition ), _firstPerson );
		GameObject.Destroy();
	}
}
