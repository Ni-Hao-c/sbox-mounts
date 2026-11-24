/// <summary>Verified raw retail sources. Explosion events never use ambient fuzzy matching.</summary>
static class Titanfall2OrdnanceSounds
{
	internal static string Prefix( string role ) => role?.ToLowerInvariant() switch
	{
		"exp_smr" => "sound/explo_rocket_close_1ch_",
		"exp_frag_grenade" => "sound/explo_frag_close_2ch_",
		"exp_emp" => "sound/explo_proximityemp_close_2ch_",
		"exp_satchel" => "sound/explo_satchel_close_2ch_",
		"exp_gravity_grenade" => "sound/wpn_gravitystar_explo_close_2ch_",
		"gravity_start" => "sound/wpn_gravitystar_starimpact_2ch_",
		"firestar" => "sound/wpn_firestar_impactchink_1ch_",
		"smoke" => "sound/electricsmoke_deploy_2ch_",
		_ => null
	};
	internal static bool Play( string mountIdent, string role, Vector3 position )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || Prefix( role ) is null ) return false;
		if ( Sandbox.Mounting.Directory.Get( mountIdent ?? "titanfall2" ) is not Titanfall2Mount mount ) return false;
		var samples = mount.GetOrdnanceSamples( role );
		if ( samples.Length == 0 || !mount.TryGetExactSound( samples[Random.Shared.Next( samples.Length )], false, out var file ) ) return false;
		var handle = Sound.PlayFile( file, 0.7f, 1, 0, 0 );
		if ( handle.IsValid() ) handle.Position = position;
		return handle.IsValid();
	}
}

public partial class Titanfall2Mount
{
	readonly Dictionary<string, string[]> _ordnanceSoundSamples = new( StringComparer.OrdinalIgnoreCase );
	internal string[] GetOrdnanceSamples( string role )
	{
		if ( _ordnanceSoundSamples.TryGetValue( role, out var samples ) ) return samples;
		var prefix = Titanfall2OrdnanceSounds.Prefix( role );
		return _ordnanceSoundSamples[role] = prefix is null ? [] : _milesSoundAssets.Keys
			.Where( name => name.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) ).OrderBy( name => name, StringComparer.OrdinalIgnoreCase ).ToArray();
	}
}
