/// <summary>Snapshot-bound retail impact tables shared by weapons, NPCs and projectiles.</summary>
public partial class Titanfall2Mount
{
	readonly Dictionary<string, IReadOnlyList<Titanfall2VmtDefinition.VmtBlock>> _combatTables = new( StringComparer.OrdinalIgnoreCase );

	internal string CombatImpactValue( string table, string section, string material )
	{
		if ( string.IsNullOrWhiteSpace( table ) || table.IndexOfAny( ['/', '\\', '.'] ) >= 0 ) table = "default";
		if ( !_combatTables.TryGetValue( table, out var blocks ) )
		{
			blocks = [];
			if ( _contentSnapshot?.GetSource( $"scripts/impacts/{table}.txt" ) is { } source
				&& source.TryReadAllBytes( out var bytes, out _ ) && bytes.Length < 1024 * 1024
				&& Titanfall2VmtReader.TryReadBlocks( bytes, out var parsed, out _ ) ) blocks = parsed;
			_combatTables[table] = blocks;
		}
		return blocks.FirstOrDefault()?.Children.FirstOrDefault( b => b.Name.Equals( section, StringComparison.OrdinalIgnoreCase ) )
			?.Values.GetValueOrDefault( material );
	}

	internal string CombatSurfaceMaterial( Surface surface )
	{
		var path = surface?.ResourcePath ?? "";
		var marker = path.IndexOf( "surfaceproperties/", StringComparison.OrdinalIgnoreCase );
		if ( marker < 0 ) return null;
		var name = Uri.UnescapeDataString( path[(marker + 18)..] );
		if ( name.EndsWith( ".surface", StringComparison.OrdinalIgnoreCase ) ) name = name[..^8];
		return _surfaceCatalog?.Resolve( name )?.GetValueOrDefault( "gamematerial" )?.ToUpperInvariant();
	}
}

/// <summary>Local presentation only. Damage and hit decisions stay on the host.</summary>
static class Titanfall2CombatEffects
{

	internal static void Impact( Scene scene, in BaseCombatWeapon.ShotEffect shot, string mountIdent = "titanfall2",
		string table = "default", float blastRadius = 0, Color? tint = null )
	{
		if ( Application.IsDedicatedServer || Application.IsHeadless || !scene.IsValid() || !shot.Hit
			|| !shot.HitPosition.IsFinite || !shot.Normal.IsFinite ) return;
		using var scope = scene.Push();
		var normal = shot.Normal.LengthSquared > 0.01f ? shot.Normal.Normal : Vector3.Up;
		var actor = shot.HitObject?.GetComponentInParent<Titanfall2Npc>();
		var player = shot.HitObject?.GetComponentInParent<PlayerController>();
		var mount = Sandbox.Mounting.Directory.Get( mountIdent ?? "titanfall2" ) as Titanfall2Mount;
		var material = blastRadius > 0 && !shot.HitObject.IsValid() ? "airburst"
			: ActorMaterial( actor?.ModelPath, actor?.IsTitan ?? false, player.IsValid() ) ?? mount?.CombatSurfaceMaterial( shot.Surface ) ?? "C";
		var effect = mount?.CombatImpactValue( table, "FX", material );
		var sound = mount?.CombatImpactValue( table, "Sound", material );
		if ( Titanfall2OrdnanceSounds.Prefix( table ) is not null ) Titanfall2OrdnanceSounds.Play( mountIdent, table, shot.HitPosition );
		else if ( !string.IsNullOrWhiteSpace( sound ) && sound != "null_temp" ) Titanfall2GameplayVisuals.PlayFireSound( scene, mountIdent ?? "titanfall2", sound, shot.HitPosition );

		var go = new GameObject( scene, false, blastRadius > 0 ? "TF2 Explosion" : "TF2 Bullet Impact" );
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		go.WorldPosition = shot.HitPosition + normal;
		go.WorldRotation = Rotation.LookAt( normal );
		if ( HasEffect( effect ) )
		{
			var retail = go.AddComponent<Titanfall2PlasmaFlight>();
			retail.MountIdent = mountIdent;
			retail.EffectName = effect;
			retail.CombatImpact = true;
		}
		var cleanup = go.AddComponent<TemporaryEffect>();
		cleanup.DestroyAfterSeconds = blastRadius > 0 ? 6 : 2;
		cleanup.WaitForChildEffects = false;
		go.Enabled = true;
		scene.GetOrAddComponent<Titanfall2CombatEffectBudget>().Track( go, decal: false );
		// Skinning has no stable native decal projection. Keep marks on geometry/props.
		if ( shot.HitObject.IsValid() && material is not ("S" or "N" or "X" or "airburst") && !actor.IsValid() && !player.IsValid()
			&& table is not ("laser_core" or "titan_sword") )
			PlaceMark( scene, shot.HitObject, shot.HitPosition, normal, blastRadius > 0, mount, material, table );
	}

	// Retail surface letters distinguish organic targets, Spectres and Titan armour.
	internal static string ActorMaterial( string model, bool titan, bool player ) => titan ? "E" : player ? "F"
		: model?.Contains( "/humans/", StringComparison.OrdinalIgnoreCase ) == true || model?.Contains( "/creatures/", StringComparison.OrdinalIgnoreCase ) == true ? "F"
		: model?.Contains( "/robots/", StringComparison.OrdinalIgnoreCase ) == true ? "P" : null;
	internal static bool HasEffect( string effect ) => !string.IsNullOrWhiteSpace( effect )
		&& !effect.Equals( "none", StringComparison.OrdinalIgnoreCase ) && !effect.Equals( "null", StringComparison.OrdinalIgnoreCase )
		&& !effect.Equals( "null_temp", StringComparison.OrdinalIgnoreCase );

	internal static void Burst( GameObject parent, Color color, int count, float speed, float size, float lifetime, bool smoke = false )
	{
		var child = new GameObject( parent, false, smoke ? "Impact smoke" : "Impact sparks" );
		child.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		var particles = child.AddComponent<ParticleEffect>();
		particles.MaxParticles = count;
		particles.Lifetime = lifetime;
		particles.Tint = color;
		particles.InitialScale = size;
		particles.Alpha = new ParticleFloat { Type = ParticleFloat.ValueType.Curve, CurveA = new Curve( new Curve.Frame( 0, 1 ), new Curve.Frame( 1, 0 ) ) };
		particles.Force = !smoke;
		particles.ForceDirection = Vector3.Down;
		particles.ForceScale = 240;
		var emitter = child.AddComponent<ParticleSphereEmitter>();
		emitter.Radius = smoke ? size : 1;
		emitter.Burst = count;
		emitter.Rate = 0;
		emitter.Loop = false;
		particles.StartVelocity = speed;
		var sprites = child.AddComponent<ParticleSpriteRenderer>();
		// A static smoke texture is required here: FromTexture does not split flipbook sheets.
		sprites.Sprite = Sprite.FromTexture( Texture.Load( smoke ? "textures/particles/smoke/smoke_wispy_a.vtex" : "textures/particles/glow_simple_01.vtex" ) );
		sprites.Additive = !smoke;
		sprites.Shadows = false;
		child.Enabled = true;
	}

	internal static void PlaceMark( Scene scene, GameObject target, Vector3 position, Vector3 normal, bool scorch,
		Titanfall2Mount mount = null, string surface = "C", string table = "default" )
	{
		var definition = mount?.GetCombatDecal( surface, scorch, table );
		if ( definition is null ) return;
		var go = new GameObject( scene, false, scorch ? "TF2 Scorch Mark" : "TF2 Bullet Hole" );
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		if ( target.IsValid() ) go.Parent = target;
		go.WorldScale = 1;
		go.WorldPosition = position + normal * 0.5f;
		go.WorldRotation = Titanfall2DecalFactory.RotationForNormal( normal );
		var decal = go.AddComponent<Decal>();
		decal.Decals = [definition];
		// Native Decal multiplies Size by the definition dimensions. Authored world
		// dimensions are already in the definition; applying them twice squares them.
		decal.Size = Vector2.One;
		decal.Depth = 3;
		decal.Rotation = 0;
		decal.Transient = true;
		var cleanup = go.AddComponent<TemporaryEffect>();
		cleanup.DestroyAfterSeconds = 20;
		cleanup.WaitForChildEffects = false;
		go.Enabled = true;
		scene.GetOrAddComponent<Titanfall2CombatEffectBudget>().Track( go, decal: true );
	}

}

/// <summary>Per-scene presentation limits keep sustained automatic fire bounded.</summary>
[Hide]
public sealed class Titanfall2CombatEffectBudget : Component
{
	readonly Queue<GameObject> _decals = new();
	readonly Queue<GameObject> _bursts = new();
	internal void Track( GameObject effect, bool decal )
	{
		var queue = decal ? _decals : _bursts;
		queue.Enqueue( effect );
		while ( queue.Count > (decal ? 128 : 64) )
		{
			var oldest = queue.Dequeue();
			if ( oldest.IsValid() ) oldest.Destroy();
		}
	}
}
