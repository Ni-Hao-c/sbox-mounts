public sealed partial class Titanfall2Npc
{
	public enum VoiceCue { Spot, Pain, Death, Follow, Unfollow }

	[Property] public string VoiceProfile { get; set; }
	TimeUntil _nextVoice;

	internal void SayVoice( VoiceCue cue, bool bypassCooldown = false )
	{
		if ( !Networking.IsHost || IsProxy || string.IsNullOrEmpty( VoiceProfile ) ) return;
		if ( !bypassCooldown && _nextVoice > 0 ) return;
		if ( !Titanfall2NpcVoiceCatalog.TryGetLines( VoiceProfile, cue, out var lines ) ) return;
		_nextVoice = cue == VoiceCue.Pain ? 2.5f : 5.5f;
		var pick = lines[Random.Shared.Next( lines.Length )];
		PlayVoiceRpc( pick );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayVoiceRpc( string sound ) => Titanfall2GameplayVisuals.PlayFireSound( Scene, MountIdent, sound, Eye );
}
