using System;

namespace GoldSrc.HalfLife;

[Hide]
public abstract class TalkingNpc : VoicedNpc
{
	protected virtual float HeadYaw
	{
		get
		{
			if ( !Animation.IsLooking || PhysicsCarrier.IsValid() ) return 0;
			var towards = WorldRotation.Inverse * (Animation.CalculateLookPosition() - WorldPosition);
			var yaw = towards.EulerAngles.Normal.yaw;
			// Directly behind us either turn is equally short. Keep the current side.
			if ( MathF.Abs( yaw ) > 150f && MathF.Abs( _headAngle ) > 1f )
				return MathF.CopySign( MathF.Abs( yaw ), _headAngle );
			return yaw;
		}
	}

	StudioController _head;
	float _headAngle;
	static readonly string[] Controls = [StudioAnimationGraphs.HeadParameter, null, null, null, StudioAnimationGraphs.MouthParameter];
	internal override string[] ControllerParameters => Controls;

	internal override void OnModelLoaded( ModelLoader loader )
	{
		_head = loader.Controllers.FirstOrDefault();
	}

	internal virtual float GetHeadAngle( StudioController head )
	{
		var target = Math.Clamp( HeadYaw, Math.Min( head.Start, head.End ), Math.Max( head.Start, head.End ) );
		var step = 240f * Time.Delta;
		_headAngle += Math.Clamp( target - _headAngle, -step, step );
		return _headAngle;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( Health > 0 && Renderer.IsValid() && Renderer.Animation.IsValid )
		{
			if ( _head is not null )
				Renderer.Animation.Parameters.TrySet( StudioAnimationGraphs.HeadParameter, _head.Fraction( GetHeadAngle( _head ) ) );
			Renderer.Animation.Parameters.TrySet( StudioAnimationGraphs.MouthParameter, VoiceAmplitude );
		}
	}
}
