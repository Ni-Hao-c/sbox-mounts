public sealed partial class Titanfall2Npc
{
	public enum VoiceCue { Spot, Pain, Death, Follow, Unfollow }

	[Property] public string VoiceProfile { get; set; }
	TimeUntil _nextVoice;

	static readonly Dictionary<(string Profile, VoiceCue Cue), string[]> VoiceLines = new()
	{
		[("imc_grunt", VoiceCue.Spot)] = ["diag_imc_grunt1_bc_engagepilotenemy_01", "diag_imc_grunt1_bc_engageenemygrunt_01", "diag_imc_grunt1_bc_enemytitanspotcall_01"],
		[("imc_grunt", VoiceCue.Pain)] = ["diag_StalkerSyncedmelee_grunt_pain_1ch_v1_01", "diag_StalkerSyncedmelee_grunt_pain_1ch_v1_02", "diag_StalkerSyncedmelee_grunt_pain_1ch_v1_03"],
		[("imc_grunt", VoiceCue.Death)] = ["diag_imc_grunt1_bc_death_01"],
		[("imc_grunt", VoiceCue.Follow)] = ["diag_imc_grunt1_bc_detectPlyrReload_01"],
		[("imc_grunt", VoiceCue.Unfollow)] = ["diag_imc_grunt1_bc_detectPlyrReload_02"],

		[("militia_grunt", VoiceCue.Spot)] = ["diag_imc_grunt1_bc_engageenemygrunt_01", "diag_imc_grunt1_bc_enemytitanspotcall_01"],
		[("militia_grunt", VoiceCue.Pain)] = ["diag_StalkerSyncedmelee_grunt_pain_1ch_v1_01", "diag_StalkerSyncedmelee_grunt_pain_1ch_v1_02"],
		[("militia_grunt", VoiceCue.Death)] = ["diag_imc_grunt1_bc_death_01"],
		[("militia_grunt", VoiceCue.Follow)] = ["diag_imc_grunt1_bc_detectPlyrReload_01"],
		[("militia_grunt", VoiceCue.Unfollow)] = ["diag_imc_grunt1_bc_detectPlyrReload_02"],

		[("bt7274", VoiceCue.Spot)] = ["diag_gs_titanBt_autoEngageGrunt_01", "diag_gs_titanBt_autoEngageTitan_01", "diag_gs_titanBt_autoEngageTitans_01"],
		[("bt7274", VoiceCue.Pain)] = ["diag_gs_titanBt_briefCriticalDamage_01", "bt_pain_knockback_backward_01_2ch_v1_01", "bt_pain_knockback_forward_01_2ch_v1_01"],
		[("bt7274", VoiceCue.Death)] = ["diag_gs_titanBt_caution_01"],
		[("bt7274", VoiceCue.Follow)] = ["diag_gs_titanBt_assistedByFriendlyPilot_01"],

		[("titan", VoiceCue.Spot)] = ["diag_gs_titan_def_autoengagepilot_01", "diag_gs_titan_def_autoengagegrunt_01", "diag_gs_titan_def_autoengagetitan_01"],
		[("titan", VoiceCue.Pain)] = ["diag_gs_titan_def_arcchainwarning_10_01", "diag_gs_titan_def_dmgcoreactivated_01_01"],
		[("titan", VoiceCue.Death)] = ["diag_gs_titan_bbalarm_2ch_v1_01"],
		[("titan", VoiceCue.Follow)] = ["diag_gs_titan_def_assistedbyfriendlypilot_18_01"],

		[("spectre", VoiceCue.Death)] = ["spectre_gs_spectredeath_01_1"],
		[("drone", VoiceCue.Death)] = ["AI_Drone_DeathExplo_Close_2ch_v1_01", "AI_Drone_DeathExplo_Close_2ch_v1_02"]
	};

	internal void SayVoice( VoiceCue cue, bool bypassCooldown = false )
	{
		if ( !Networking.IsHost || IsProxy || string.IsNullOrEmpty( VoiceProfile ) ) return;
		if ( !bypassCooldown && _nextVoice > 0 ) return;
		if ( !VoiceLines.TryGetValue( (VoiceProfile, cue), out var lines ) || lines.Length == 0 ) return;
		_nextVoice = cue == VoiceCue.Pain ? 2.5f : 5.5f;
		var pick = lines[Random.Shared.Next( lines.Length )];
		PlayVoiceRpc( pick );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayVoiceRpc( string sound ) => Titanfall2GameplayVisuals.PlayFireSound( MountIdent, sound, Eye );
}
