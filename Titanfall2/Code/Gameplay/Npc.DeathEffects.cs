public sealed partial class Titanfall2Npc
{
	internal static string DeathParticle( string model, bool titan )
	{
		if ( titan ) return "xo_exp_death";
		if ( string.IsNullOrEmpty( model ) ) return null;
		if ( model.Contains( "/robots/super_spectre/", StringComparison.OrdinalIgnoreCase ) ) return "P_sup_spectre_death";
		if ( model.Contains( "/robots/spectre/", StringComparison.OrdinalIgnoreCase ) || model.Contains( "/robots/stalker/", StringComparison.OrdinalIgnoreCase ) ) return "P_exp_spectre_death";
		if ( model.Contains( "/robots/turret_plasma/", StringComparison.OrdinalIgnoreCase ) ) return "P_turret_exp_plasma";
		return null;
	}

	// Call once on the host before destroying the actor/ragdoll source. Presentation lives independently of that source.
	internal void OnCombatDeathEffects()
	{
		CancelNpcOrdnance();
		if ( !Networking.IsHost || IsProxy || DeathParticle( ModelPath, IsTitan ) is not { } particle ) return;
		var attachment = Renderer.IsValid() ? Renderer.GetAttachmentObject( IsTitan ? "exp_torso_main" : "CHESTFOCUS" ) : null;
		PlayNpcDeathParticle( particle, attachment.IsValid() ? attachment.WorldPosition : WorldPosition + Vector3.Up * (BodyHeight * 0.55f) );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayNpcDeathParticle( string particle, Vector3 position )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !position.IsFinite || !Scene.IsValid() ) return;
		using var scope = Scene.Push();
		var effect = new GameObject( Scene, false, "TF2 NPC Death" );
		effect.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		effect.WorldPosition = position;
		effect.WorldRotation = WorldRotation;
		var retail = effect.AddComponent<Titanfall2PlasmaFlight>();
		retail.MountIdent = MountIdent ?? "titanfall2";
		retail.EffectName = particle;
		retail.CombatImpact = true;
		var cleanup = effect.AddComponent<TemporaryEffect>();
		cleanup.DestroyAfterSeconds = 6;
		cleanup.WaitForChildEffects = false;
		effect.Enabled = true;
		Scene.GetOrAddComponent<Titanfall2CombatEffectBudget>().Track( effect, decal: false );
	}
}
