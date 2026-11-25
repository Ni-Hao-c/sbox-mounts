using Sandbox.Npcs.Layers;

namespace GoldSrc.HalfLife;

public abstract partial class Personnel : Component.ICollisionListener
{
	GameObject _pushingPlayer;
	Vector3 _pushStart;
	TimeUntil _pushExpires, _faceUntil;
	bool _steppingAside;

	void ICollisionListener.OnCollisionStart( Collision collision ) => PlayerTouch( collision );
	void ICollisionListener.OnCollisionUpdate( Collision collision ) => PlayerTouch( collision );

	void PlayerTouch( Collision collision )
	{
		if ( !Networking.IsHost || !CanFollow || Health <= 0 || _talkUntil > 0 || _steppingAside || PhysicsCarrier.IsValid() ) return;
		var player = collision.Other.GameObject?.GetComponentInParent<PlayerController>();
		if ( !player.IsValid() || !AcceptsLeader( player.GameObject ) ) return;
		var velocity = player.Velocity;
		if ( System.MathF.Abs( velocity.x ) + System.MathF.Abs( velocity.y ) <= 50 ) return;

		_pushingPlayer = player.GameObject;
		_pushExpires = 0.5f;
	}

	bool GivePlayerRoom()
	{
		if ( !_pushingPlayer.IsValid() || _pushExpires <= 0 )
		{
			_pushingPlayer = null;
			_steppingAside = false;
			return false;
		}

		if ( !_steppingAside )
		{
			_pushStart = WorldPosition;
			var away = (WorldPosition - _pushingPlayer.WorldPosition).WithZ( 0 ).Normal;
			var destination = Movement.ProjectPoint( WorldPosition + away * 100 );
			if ( destination is null || destination.Value.DistanceSquared( WorldPosition ) < 32 * 32 ||
				!CanReach( destination.Value ) )
				destination = FindCover( WorldPosition, GameObject, false );

			_steppingAside = true;
			if ( destination.HasValue )
			{
				var speed = Definition.Speed * 0.6f;
				Movement.MoveTo( destination.Value, 3, speed );
				_pushExpires = 2 + WorldPosition.Distance( destination.Value ) / speed;
				return true;
			}
			Movement.Stop();
		}

		if ( Movement.Status == MovementLayer.MovementStatus.Moving && (WorldPosition - _pushStart).WithZ( 0 ).LengthSquared < 100 * 100 ) return true;
		if ( _faceUntil <= 0 )
		{
			Movement.Stop();
			_faceUntil = 0.5f;
			_pushExpires = 0.5f;
		}
		Animation.SetLookTarget( _pushingPlayer );
		return true;
	}
}
