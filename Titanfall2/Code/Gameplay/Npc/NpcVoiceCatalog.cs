/// <summary>Declarative NPC voice cue tables with optional JSON overlay (voices.catalog.json).</summary>
internal static class Titanfall2NpcVoiceCatalog
{
	const string OverlayFile = "voices.catalog.json";

	static readonly Dictionary<(string Profile, Titanfall2Npc.VoiceCue Cue), string[]> Defaults = new()
	{
		[("imc_grunt", Titanfall2Npc.VoiceCue.Spot)] = ["diag_imc_grunt1_bc_engagepilotenemy_01", "diag_imc_grunt1_bc_engageenemygrunt_01", "diag_imc_grunt1_bc_enemytitanspotcall_01"],
		[("imc_grunt", Titanfall2Npc.VoiceCue.Pain)] = ["diag_StalkerSyncedmelee_grunt_pain_1ch_v1_01", "diag_StalkerSyncedmelee_grunt_pain_1ch_v1_02", "diag_StalkerSyncedmelee_grunt_pain_1ch_v1_03"],
		[("imc_grunt", Titanfall2Npc.VoiceCue.Death)] = ["diag_imc_grunt1_bc_death_01"],
		[("imc_grunt", Titanfall2Npc.VoiceCue.Follow)] = ["diag_imc_grunt1_bc_detectPlyrReload_01"],
		[("imc_grunt", Titanfall2Npc.VoiceCue.Unfollow)] = ["diag_imc_grunt1_bc_detectPlyrReload_02"],

		[("militia_grunt", Titanfall2Npc.VoiceCue.Spot)] = ["diag_imc_grunt1_bc_engageenemygrunt_01", "diag_imc_grunt1_bc_enemytitanspotcall_01"],
		[("militia_grunt", Titanfall2Npc.VoiceCue.Pain)] = ["diag_StalkerSyncedmelee_grunt_pain_1ch_v1_01", "diag_StalkerSyncedmelee_grunt_pain_1ch_v1_02"],
		[("militia_grunt", Titanfall2Npc.VoiceCue.Death)] = ["diag_imc_grunt1_bc_death_01"],
		[("militia_grunt", Titanfall2Npc.VoiceCue.Follow)] = ["diag_imc_grunt1_bc_detectPlyrReload_01"],
		[("militia_grunt", Titanfall2Npc.VoiceCue.Unfollow)] = ["diag_imc_grunt1_bc_detectPlyrReload_02"],

		[("bt7274", Titanfall2Npc.VoiceCue.Spot)] = ["diag_gs_titanBt_autoEngageGrunt_01", "diag_gs_titanBt_autoEngageTitan_01", "diag_gs_titanBt_autoEngageTitans_01"],
		[("bt7274", Titanfall2Npc.VoiceCue.Pain)] = ["diag_gs_titanBt_briefCriticalDamage_01", "bt_pain_knockback_backward_01_2ch_v1_01", "bt_pain_knockback_forward_01_2ch_v1_01"],
		[("bt7274", Titanfall2Npc.VoiceCue.Death)] = ["diag_gs_titanBt_caution_01"],
		[("bt7274", Titanfall2Npc.VoiceCue.Follow)] = ["diag_gs_titanBt_assistedByFriendlyPilot_01"],

		[("titan", Titanfall2Npc.VoiceCue.Spot)] = ["diag_gs_titan_def_autoengagepilot_01", "diag_gs_titan_def_autoengagegrunt_01", "diag_gs_titan_def_autoengagetitan_01"],
		[("titan", Titanfall2Npc.VoiceCue.Pain)] = ["diag_gs_titan_def_arcchainwarning_10_01", "diag_gs_titan_def_dmgcoreactivated_01_01"],
		[("titan", Titanfall2Npc.VoiceCue.Death)] = ["diag_gs_titan_bbalarm_2ch_v1_01"],
		[("titan", Titanfall2Npc.VoiceCue.Follow)] = ["diag_gs_titan_def_assistedbyfriendlypilot_18_01"],

		[("spectre", Titanfall2Npc.VoiceCue.Death)] = ["spectre_gs_spectredeath_01_1"],
		[("drone", Titanfall2Npc.VoiceCue.Death)] = ["AI_Drone_DeathExplo_Close_2ch_v1_01", "AI_Drone_DeathExplo_Close_2ch_v1_02"]
	};

	static Dictionary<(string Profile, Titanfall2Npc.VoiceCue Cue), string[]> _active = Defaults;

	internal static void Reload( Titanfall2ContentSnapshot snapshot )
	{
		if ( !Titanfall2RetailScriptReader.TryReadJsonOverlay<Dictionary<string, Dictionary<Titanfall2Npc.VoiceCue, string[]>>>( snapshot, OverlayFile, out var overlay )
			|| overlay.Count == 0 )
		{
			_active = Defaults;
			return;
		}
		var merged = new Dictionary<(string Profile, Titanfall2Npc.VoiceCue Cue), string[]>( Defaults );
		foreach ( var (profile, cues) in overlay )
		{
			if ( string.IsNullOrWhiteSpace( profile ) || cues is null ) continue;
			foreach ( var (cue, lines) in cues )
			{
				if ( lines is { Length: > 0 } ) merged[(profile, cue)] = lines;
			}
		}
		_active = merged;
	}

	internal static void Reset() => _active = Defaults;

	internal static bool TryGetLines( string profile, Titanfall2Npc.VoiceCue cue, out string[] lines ) =>
		_active.TryGetValue( (profile, cue), out lines ) && lines is { Length: > 0 };
}
