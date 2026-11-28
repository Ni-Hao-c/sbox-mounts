using Sandbox.Npcs.Schedules;
using Sandbox.Npcs.Tasks;

public sealed partial class Titanfall2Npc
{
	protected override BaseSchedule SelectSchedule()
	{
		if ( !UsesEngineSchedules || Dead || Seat is { IsOccupied: true } ) return null;
		RefreshCombatContact( force: true );
		if ( CanFight && HasCombatContact ) return GetSchedule<Titanfall2EngageSchedule>();
		ResetCombatActionIdle();
		return base.SelectSchedule();
	}
}

/// <summary>Native scheduler and movement layer, with concurrent TF2 movement and attack decisions.</summary>
public sealed class Titanfall2EngageSchedule : BaseSchedule
{
	public override int Priority => SchedulePriorities.Combat;
	public override bool Interruptible => false;
	protected override void OnStart() => AddTask( new Titanfall2AttackTask() );
	protected override bool ShouldCancel() => Npc is not Titanfall2Npc actor || actor.Dead || !actor.CanFight
		|| !actor.UsesEngineSchedules || actor.Seat is { IsOccupied: true };
	protected override void OnEnd()
	{
		if ( Npc is Titanfall2Npc actor ) actor.CancelNpcOrdnance();
		if ( Npc is not Titanfall2Npc { AiOwnsMovement: true } ) Npc.Movement?.Stop();
		Npc.Animation.ClearLookTarget();
	}
}

public sealed class Titanfall2AttackTask : BaseTask
{
	protected override TaskResult OnUpdate() => Npc is Titanfall2Npc actor && actor.TickCombatTactics()
		? TaskResult.Running : TaskResult.Success;
}
