/// <summary>Authored presentation from the retail multiplayer WeaponData, not generic engine effects.</summary>
public record struct Titanfall2WeaponPresentation
{
	public string MuzzleView { get; set; }
	public string MuzzleWorld { get; set; }
	public string MuzzleAttachment { get; set; }
	public string ShellView { get; set; }
	public string ShellWorld { get; set; }
	public string ShellAttachment { get; set; }
	public string TracerView { get; set; }
	public string TracerWorld { get; set; }
	public string ImpactTable { get; set; }
	public Titanfall2WeaponBodyGroup[] BodyGroups { get; set; }
	public readonly void ApplyBodyGroups( SkinnedModelRenderer renderer )
	{
		if ( !renderer.IsValid() || BodyGroups is null ) return;
		foreach ( var group in BodyGroups ) renderer.SetBodyGroup( group.Name, group.Choice );
	}
}

public record struct Titanfall2WeaponBodyGroup( string Name, int Choice );

static class Titanfall2RetailWeaponEffects
{
	internal static bool Spawn( GameObject anchor, string mountIdent, string effectName, bool firstPerson,
		float lifetime = 2, Vector3? end = null )
	{
		if ( Application.IsDedicatedServer || Application.IsHeadless || !anchor.IsValid() || string.IsNullOrEmpty( effectName )
			|| Sandbox.Mounting.Directory.Get( mountIdent ?? "titanfall2" ) is not Titanfall2Mount mount ) return false;
		// Never start an old shot seconds later when the catalog finally finishes indexing.
		if ( !mount.TryPrepareWeaponParticle( effectName, out var definition ) ) return false;
		var go = new GameObject( anchor.Scene, false, effectName );
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		if ( !end.HasValue ) { go.Parent = anchor; go.LocalTransform = global::Transform.Zero; }
		else { go.WorldPosition = anchor.WorldPosition; go.WorldRotation = anchor.WorldRotation; }
		var points = go.AddComponent<Titanfall2ParticleControlPoints>();
		if ( end is { } target ) { points.SetControlPoint( 0, anchor.WorldPosition ); points.SetControlPoint( 1, target ); }
		if ( !Titanfall2ParticleFactory.TryCreate( go, mount, definition, out _, out _ ) ) { go.Destroy(); return false; }
		foreach ( var renderer in go.GetComponentsInChildren<ParticleSpriteRenderer>( true ) )
		{
			renderer.RenderOptions.Game = !firstPerson;
			renderer.RenderOptions.Overlay = firstPerson;
			if ( firstPerson )
			{
				renderer.CameraFadeNear = 0; renderer.CameraFadeFar = 0; renderer.DepthFeather = 0;
				// The overlay uses the viewmodel projection. Large energy cards which are
				// sensible in world space can cover the whole sight at arm's length.
				if ( !renderer.FaceVelocity && anchor.Scene.Camera is { } camera && camera.IsValid() &&
					renderer.GetComponent<ParticleEffect>( true ) is { } effect &&
					mount.TryGetParticleDefinition( effect.GameObject.Name, out var layer ) )
				{
					var range = MathF.Max( 1f, Vector3.Dot( camera.WorldRotation.Forward, anchor.WorldPosition - camera.WorldPosition ) );
					var radius = MathF.Max( layer.RadiusMinimum, layer.RadiusMaximum ) * MathF.Abs( renderer.WorldScale.x );
					if ( layer.RadiusScale is { } scale ) radius *= MathF.Max( 1f, MathF.Max( scale.StartScale, scale.EndScale ) );
					renderer.Scale *= Math.Clamp( 0.20f * range / MathF.Max( 0.01f, radius ), 0f, 1f );
				}
			}
		}
		foreach ( var renderer in go.GetComponentsInChildren<ModelRenderer>( true ) )
		{
			renderer.RenderOptions.Game = !firstPerson;
			renderer.RenderOptions.Overlay = firstPerson;
			renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
		}
		foreach ( var renderer in go.GetComponentsInChildren<ParticleModelRenderer>( true ) )
		{
			renderer.RenderOptions.Game = !firstPerson;
			renderer.RenderOptions.Overlay = firstPerson;
		}
		var cleanup = go.AddComponent<TemporaryEffect>();
		cleanup.DestroyAfterSeconds = lifetime;
		cleanup.WaitForChildEffects = false;
		go.Enabled = true;
		return true;
	}
}
