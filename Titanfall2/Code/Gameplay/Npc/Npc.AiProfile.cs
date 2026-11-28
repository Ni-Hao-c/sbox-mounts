public sealed partial class Titanfall2Npc
{
	[Property] public string AiSettingsName { get; set; }
	[Property] public string AiWeaponScript { get; set; }
	[Property] public Titanfall2NpcAiMode AiMode { get; set; } = Titanfall2NpcAiMode.EnemyMp;
	[Property] public bool UseRetailAiSettings { get; set; } = true;
	int _burstLimit;

	internal Titanfall2NpcAiProfile AiProfile
	{
		get
		{
			if ( !UseRetailAiSettings || Sandbox.Mounting.Directory.Get( MountIdent ?? "titanfall2" ) is not Titanfall2Mount mount ) return null;
			var kit = GetComponent<Titanfall2TitanKit>();
			if ( kit.IsValid() ) return mount.GetTitanAiProfile( kit.Kit, AiMode, GetComponent<Titanfall2TitanCore>() is { SwordCoreActive: true } );
			return string.IsNullOrWhiteSpace( AiSettingsName ) ? null : mount.GetNpcAiProfile( AiSettingsName, AiMode );
		}
	}

	internal Titanfall2NpcWeaponAiProfile AiPrimaryProfile => !UseRetailAiSettings || string.IsNullOrWhiteSpace( AiWeaponScript ) ? null
		: (Sandbox.Mounting.Directory.Get( MountIdent ?? "titanfall2" ) as Titanfall2Mount)?.GetNpcWeaponAiProfile( AiWeaponScript, AiMode );

	void InitializeAiProfile()
	{
		if ( AiProfile is not { Available: true } profile ) return;
		// Preserve authored scene HP/body/weapon overrides. AISettings owns perception and policy.
		DetectionRange = Math.Clamp( profile.Number( "LookDistDefault_Combat", DetectionRange ), 1, 32000 );
		if ( Senses.IsValid() )
		{
			Senses.SightRange = DetectionRange;
			Senses.FieldOfView = Math.Clamp( profile.Number( "FOV_Combat_Far_Horz", Senses.FieldOfView ), 1, 360 );
		}
	}

	float CommitNpcBurstShot( float interval )
	{
		var data = AiPrimaryProfile;
		if ( _burstLimit <= 0 ) _burstLimit = data is { Available: true, MaxBurst: > 0 }
			? Random.Shared.Next( Math.Max( 1, data.MinBurst ), data.MaxBurst + 1 ) : Math.Max( 1, BurstShots );
		if ( ++_burst < _burstLimit ) return MathF.Max( 0.05f, interval );
		_burst = 0;
		_burstLimit = 0;
		var rest = data is { Available: true, MaxBurst: > 0 }
			? float.Lerp( data.MinRest, data.MaxRest, Random.Shared.NextSingle() ) : BurstPause;
		if ( data is { Available: true } )
		{
			var ordinaryAuto = AiMode is Titanfall2NpcAiMode.AutoTitanFollow or Titanfall2NpcAiMode.AutoTitanGuard;
			rest += MathF.Max( 0, data.Number( ordinaryAuto ? "proficiency_average_additional_rest" : "proficiency_good_additional_rest" ) );
		}
		return MathF.Max( MathF.Max( 0.05f, interval ), rest );
	}
}
