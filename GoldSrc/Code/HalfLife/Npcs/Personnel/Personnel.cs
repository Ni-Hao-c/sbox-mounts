using Sandbox.Npcs;

namespace GoldSrc.HalfLife;

[Hide]
public abstract partial class Personnel : TalkingNpc, Component.IPressable
{
	internal override float ViewDot => -0.7f;

	/// <summary>Use Black Mesa's pre-disaster dialogue and decline follow requests.</summary>
	[Property] public bool PreDisaster { get; set; }

	[Sync( SyncFlags.FromHost )] protected GameObject Leader { get; set; }
	bool _runningToLeader;

	protected void ApproachLeader( float stop, bool scared = false )
	{
		var distance = WorldPosition.Distance( Leader.WorldPosition );
		if ( distance >= 270 ) _runningToLeader = true;
		else if ( distance < 190 ) _runningToLeader = false;
		var gait = Activity( scared ? (_runningToLeader ? 64 : 63) : (_runningToLeader ? Run : Walk) );
		MoveToward( Leader.WorldPosition, stop, gait is { GroundSpeed: > 0 } ? gait.GroundSpeed / Definition.Speed : 1 );
	}

	internal override void IdleMovement()
	{
		if ( Movement?.MoveTarget.HasValue == true ) Movement.Stop();
	}

	internal virtual bool AssistLeader() => false;

	internal override bool FollowLeader()
	{
		if ( AssistLeader() ) return true;
		if ( GivePlayerRoom() ) return true;
		if ( !IsLiveTarget( Leader ) ) return false;
		if ( WorldPosition.DistanceSquared( Leader.WorldPosition ) > 128 * 128 )
			ApproachLeader( 128 );
		else
		{
			if ( Movement?.MoveTarget.HasValue == true ) Movement.Stop();
			Animation.SetLookTarget( Leader );
		}
		return true;
	}

	internal abstract string SentencePrefix { get; }

	protected void StopFollowing( bool speak )
	{
		Leader = null;
		Movement?.Stop();
		if ( speak ) Say( "WAIT" );
	}

	bool CanSeeColleague( Personnel colleague, Vector3 start )
	{
		var end = colleague.WorldPosition + Vector3.Up * colleague.Definition.Height;
		return !Scene.Trace.Ray( start, end ).IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "npc", "player", "playercontroller", "trigger", "ragdoll", "debris" ).Run().Hit;
	}

	internal Personnel NearestColleague()
	{
		var start = WorldPosition + Vector3.Up * Definition.Height;
		return Scene.GetAllComponents<Personnel>()
			.Where( friend => friend != this && friend.Health > 0 && !friend.PhysicsCarrier.IsValid() )
			.Select( friend => (Friend: friend, Distance: start.DistanceSquared( friend.WorldPosition + Vector3.Up * friend.Definition.Height )) )
			.Where( candidate => candidate.Distance < 500 * 500 )
			.OrderBy( candidate => candidate.Distance )
			.FirstOrDefault( candidate => CanSeeColleague( candidate.Friend, start ) ).Friend;
	}

	protected override void OnDamaged( Component.IDamageable.DamageEvent damage )
	{
		base.OnDamaged( damage );
		_speaker = null;
		if ( IsBarnacleVictim || damage.Instigator?.GetComponentInParent<PlayerController>() is null ) return;
		NearestColleague()?.Say( "SCARED", attenuation: GoldSrcSoundEvents.Normal );
	}

	protected override void OnDeath( Component.IDamageable.DeathEvent death )
	{
		if ( !IsBarnacleVictim && death.Instigator?.GetComponentInParent<PlayerController>() is { } killer )
		{
			foreach ( var friend in Scene.GetAllComponents<Personnel>() )
			{
				if ( friend == this || friend.Health <= 0 ) continue;
				var witnessed = CanSeeColleague( friend, WorldPosition + Vector3.Up );
				if ( witnessed ) friend.SetRelationship( killer.GameObject, friend.Definition.Attack == NpcAttack.None ? Disposition.Fearful : Disposition.Hostile );
				if ( friend.Leader == killer.GameObject ) friend.StopFollowing( !witnessed );
			}
		}
		base.OnDeath( death );
	}

	bool CanFollow => Definition.Movement == NpcMovement.Ground;
	bool AcceptsLeader( GameObject player ) => CanFollow && GetDisposition( player ) is not (Disposition.Hostile or Disposition.Fearful);
	IPressable.Tooltip? IPressable.GetTooltip( IPressable.Event e )
	{
		var player = e.Source.GameObject;
		if ( PreDisaster || !AcceptsLeader( player ) ) return null;
		return Leader == player
			? new IPressable.Tooltip( "#npc.action.stop_following", "person_off", null )
			: new IPressable.Tooltip( "#npc.action.follow", "follow_the_signs", null );
	}

	bool IPressable.CanPress( IPressable.Event e ) => AcceptsLeader( e.Source.GameObject );
	bool IPressable.Press( IPressable.Event e )
	{
		ToggleFollow( e.Source.GameObject );
		return true;
	}

	[Rpc.Host]
	void ToggleFollow( GameObject source )
	{
		var player = source?.GetComponentInParent<PlayerController>();
		if ( !player.IsValid() || !AcceptsLeader( player.GameObject ) || player.WorldPosition.Distance( WorldPosition ) > 160 ||
			(Rpc.Caller is not null && player.Network.Owner != Rpc.Caller) )
			return;
		if ( PreDisaster )
		{
			Say( "POK", player.GameObject, 2, attenuation: GoldSrcSoundEvents.Normal );
			return;
		}
		if ( Leader == player.GameObject )
		{
			StopFollowing( true );
			return;
		}
		// The SDK leaves one existing follower when a second colleague joins.
		foreach ( var surplus in Scene.GetAllComponents<Personnel>().Where( friend => friend != this && friend.Leader == player.GameObject ).Skip( 1 ) )
			surplus.StopFollowing( true );
		Leader = player.GameObject;
		Movement?.Stop();
		Say( "OK" );
	}
}
