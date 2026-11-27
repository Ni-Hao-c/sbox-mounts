public sealed partial class Titanfall2Npc
{
	double _actorFlinchUntil;
	[Sync( SyncFlags.FromHost )] public int ActorFlinchSerial { get; private set; }

	// Called only after native damage listeners have accepted health damage.
	// This is a presentation gesture: it neither stops navigation nor cancels
	// the independently timed fire/reload channels or changes damage gameplay.
	void CueActorFlinch( Component.IDamageable.DamageEvent damage )
	{
		if ( !Networking.IsHost || IsProxy || Dead || Health <= 0 || damage.Cancelled
			|| !float.IsFinite( damage.AcceptedDamage ) || damage.AcceptedDamage <= 0
			|| Time.NowDouble < _actorFlinchUntil ) return;
		var clip = _actorAnimationData?.Clips?.FirstOrDefault( clip => clip.Role == ModelLoader.FlinchRole );
		if ( clip is not { Additive: true, Duration: > 0 } || string.IsNullOrEmpty( clip.BoneMask ) ) return;
		// One authored gesture is allowed to finish before another starts; repeated
		// impacts must not pin the pose at its first frame. Retail's broader flinch
		// schedule/weighted activity selection is outside this presentation adapter.
		_actorFlinchUntil = Time.NowDouble + clip.Duration;
		ActorFlinchSerial = ActorFlinchSerial == int.MaxValue ? 1 : ActorFlinchSerial + 1;
		PlayActorFlinch();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayActorFlinch()
	{
		if ( Dead || Health <= 0 ) return;
		_layeredAnimation?.Flinch();
	}
}
