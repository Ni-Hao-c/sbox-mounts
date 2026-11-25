namespace GoldSrc.HalfLife;

[Library]
internal static partial class BloodEffects
{
	internal static void Wound( GameObject victim, Npc.BloodColor color, DamageInfo info, float damage )
	{
		if ( color == Npc.BloodColor.None || !Npc.HitStudioModel( info ) || damage < 1 ) return;

		var direction = (info.Position - info.Origin).Normal;
		Spray( color, info.Position, (int)damage );
		var tags = info.Tags;
		if ( !(tags.Has( "bullet" ) || tags.Has( "slash" ) || tags.Has( "melee" ) || tags.Has( "club" )
			|| tags.Has( "crush" ) || tags.Has( "blast" ) || tags.Has( "explosion" ) || tags.Has( "mortar" )) ) return;

		var (traces, noise) = damage < 10 ? (1, 0.1f) : damage < 25 ? (2, 0.2f) : (4, 0.3f);
		for ( int i = 0; i < traces; i++ )
		{
			var scatter = new Vector3( Game.Random.Float( -noise, noise ), Game.Random.Float( -noise, noise ), Game.Random.Float( -noise, noise ) );
			var surface = victim.Scene.Trace.Ray( info.Position, info.Position + (direction + scatter) * 172 )
				.IgnoreGameObjectHierarchy( victim ).WithoutTags( "playercontroller", "npc", "player", "trigger", "ragdoll" ).Run();
			if ( surface.Hit ) Decal( color, surface.EndPosition, surface.Normal, Game.Random.Int( 1, 6 ) );
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	internal static void Decal( Npc.BloodColor color, Vector3 point, Vector3 normal, int variant ) =>
		PlaceDecal( color, point, normal, variant );

	internal static void PlaceDecal( Npc.BloodColor color, Vector3 point, Vector3 normal, int variant )
	{
		var prefix = color == Npc.BloodColor.Red ? "blood" : "yblood";
		ImpactDecal.Place( point, normal, $"{prefix}{variant}", 0 );
	}

}
