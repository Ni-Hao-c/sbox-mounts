using Titanfall2.Formats;
using Sequence = Titanfall2.Formats.Titanfall2Mdl53Reader.SequenceInfo;

partial class ModelLoader
{
	internal readonly record struct PilotOrdnanceSequenceSet( Sequence Pullout, Sequence Prep, Sequence Hold, Sequence Toss, Sequence Detonate )
	{
		internal IEnumerable<(string Role, Sequence Sequence)> Roles()
		{
			// The hybrid needs separate attack and toss timelines. Ordinary grenades
			// already mount their toss as Fire, so keep that path and avoid duplicate clips.
			if ( Detonate is null ) yield break;
			yield return (Titanfall2PilotViewModel.TossPullout, Pullout);
			yield return (Titanfall2PilotViewModel.TossPrep, Prep);
			yield return (Titanfall2PilotViewModel.TossHold, Hold);
			yield return (Titanfall2PilotViewModel.Toss, Toss);
			yield return (Titanfall2PilotViewModel.Detonate, Detonate);
		}
	}

	internal static PilotOrdnanceSequenceSet ResolvePilotOrdnanceSequences( Titanfall2Mdl53Reader.ParsedModel model )
	{
		var toss = FindSequence( model, [], "ACT_VM_TOSS" );
		if ( toss is null ) return default;
		// An offhand hybrid has distinct toss and primary activities. The clacker's primary
		// activity is a detonation only when it has the corresponding attack event.
		var detonate = FindSequence( model, [], "ACT_VM_PRIMARYATTACK" );
		if ( detonate?.Events.Any( item => item.Name == "AE_WPN_PRIMARYATTACK" ) != true ) detonate = null;
		return new(
			FindSequence( model, [], "ACT_VM_TOSS_PREP_PULLOUT" ),
			FindSequence( model, [], "ACT_VM_TOSS_PREP" ),
			FindSequence( model, [], "ACT_VM_TOSS_HOLD" ),
			toss, detonate );
	}
}
