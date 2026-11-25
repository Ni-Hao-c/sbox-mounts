namespace GoldSrc.HalfLife;

[Hide]
public sealed class NihilanthTeleport : EnergyBall
{
	internal override string SpriteName => "exit1";
	internal override float SpriteSize => 240;
	internal override float Lifetime => float.PositiveInfinity;
	TimeUntil _nextThink;
	SoundHandle _sound;
	GameObject _destination;

	internal override void PrepareLaunch()
	{
		_destination = Shooter.GetComponent<Nihilanth>().TeleportDestination;
		_nextThink = 0.1f;
	}

	protected override void OnStart()
	{
		base.OnStart();
		if ( Glow.IsValid() ) Glow.PlaybackSpeed = 0;
		var light = GameObject.AddComponent<PointLight>();
		light.Radius = 256;
		light.LightColor = Color.Green;
		light.Shadows = false;
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is HalfLifeMount mount && mount.NpcAudio.Find( "x/x_teleattack1.wav" ) is { } path )
		{
			_sound = NpcAudio.Play( path, WorldPosition, attenuation: 0.2f );
		}
	}

	protected override void OnUpdate()
	{
		if ( _sound.IsValid() ) _sound.Position = WorldPosition;
		if ( Glow.IsValid() ) Glow.CurrentFrameIndex = (int)((float)Age * 10) % 20;
	}

	protected override void OnDisabled() => _sound?.Stop();

	internal override void Guide()
	{
		if ( _nextThink > 0 ) return;
		_nextThink = 0.1f;
		if ( !Shooter.IsValid() || !Sandbox.Npcs.BaseNpc.IsLiveTarget( Target ) )
		{
			GameObject.Destroy();
			return;
		}
		var delta = Npc.TargetHullCenter( Target ) - WorldPosition;
		if ( delta.Length < 128 )
		{
			Arrive();
			GameObject.Destroy();
			return;
		}
		Velocity = Velocity.Normal * System.MathF.Min( Velocity.Length, 300 ) + delta.Normal * 300;
	}

	internal override void OnImpact( IDamageable victim, Vector3 position, Vector3 normal )
	{
		if ( !Shooter.IsValid() ) return;
		if ( victim is Component hit && hit.GameObject.Root == Target?.Root ) Arrive();
		else Shooter.GetComponent<Nihilanth>()?.SummonReinforcement( position, normal );
	}

	void Arrive()
	{
		if ( !Target.IsValid() ) return;
		if ( _destination.IsValid() ) MoveTarget( Target, _destination.WorldTransform );
		Shooter.GetComponent<Nihilanth>()?.OnTeleport?.Invoke( Target );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void MoveTarget( GameObject target, Transform destination )
	{
		if ( !target.IsValid() || target.IsProxy ) return;
		target.WorldPosition = destination.Position;
		target.WorldRotation = destination.Rotation;
		target.Transform.ClearInterpolation();
		if ( target.GetComponent<Rigidbody>() is { } body ) body.Velocity = Vector3.Zero;
		if ( target.GetComponent<PlayerController>() is { } player ) player.EyeAngles = destination.Rotation.Angles();
	}
}
