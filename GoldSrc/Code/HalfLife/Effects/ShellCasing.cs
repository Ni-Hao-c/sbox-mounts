namespace GoldSrc.HalfLife;

[Hide]
public sealed class ShellCasing : Component, Component.ICollisionListener
{
	bool _shotgun;

	internal static void Eject( Vector3 origin, Rotation facing, bool shotgun, Vector3 velocity )
	{
		if ( Application.IsDedicatedServer ) return;
		var shell = new GameObject( false, shotgun ? "Shotgun shell" : "Brass casing" );
		shell.NetworkMode = NetworkMode.Never;
		shell.Tags.Add( "debris" );
		shell.Tags.Add( "nocollide_props" );
		shell.WorldPosition = origin;
		shell.WorldRotation = Rotation.FromYaw( facing.Angles().yaw );
		var model = Model.Load( shotgun ? "mount://hl1/valve/models/shotgunshell.mdl.vmdl" : "mount://hl1/valve/models/shell.mdl.vmdl" );
		shell.AddComponent<ModelRenderer>().Model = model;
		var collider = shell.AddComponent<ModelCollider>();
		collider.Model = model;
		collider.ColliderFlags |= ColliderFlags.IgnoreTraces;
		collider.Elasticity = 0.35f;
		var body = shell.AddComponent<Rigidbody>();
		body.RigidbodyFlags |= RigidbodyFlags.DisableCollisionSounds;
		body.EnhancedCcd = true;
		shell.AddComponent<TemporaryEffect>().DestroyAfterSeconds = 2.5f;
		shell.AddComponent<ShellCasing>()._shotgun = shotgun;
		shell.Enabled = true;
		body.AngularVelocity = facing.Forward * Game.Random.Float( -14, 14 );
		body.Velocity = velocity;
	}

	void ICollisionListener.OnCollisionStart( Collision collision )
	{
		if ( collision.Other.IsTrigger || Sandbox.Mounting.Directory.Get( "hl1" ) is not HalfLifeMount mount ) return;
		var speed = System.MathF.Abs( collision.Contact.Speed.z );
		if ( speed <= 0 || (speed < 200 && Game.Random.Int( 0, 3 ) != 0) ) return;
		var sample = _shotgun ? "weapons/sshell" : "player/pl_shell";
		var volume = (_shotgun ? 0.5f : 0.8f) * System.MathF.Min( speed / 350, 1 );
		NpcAudio.Play( mount.NpcAudio.Find( sample ), collision.Contact.Point, volume );
	}
}
