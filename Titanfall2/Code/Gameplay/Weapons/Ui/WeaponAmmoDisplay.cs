/// <summary>Places native ammo panels on the retail MDL's actual bone-local RUI quads.</summary>
[Hide]
public sealed class Titanfall2WeaponAmmoDisplay : Component
{
	[Property] public Titanfall2Weapon Weapon { get; set; }
	static readonly Vector2[] PanelCorners = [new( -0.5f, -0.5f ), new( 0.5f, -0.5f ), new( -0.5f, 0.5f ), new( 0.5f, 0.5f )];
	readonly List<Surface> _surfaces = [];
	SkinnedModelRenderer _renderer;
	Model _model;
	Titanfall2WeaponUiDisplay[] _bindings;
	Titanfall2Mount _uiMount;
	Titanfall2WeaponPrewarmRequest _uiRequest;
	bool _pendingSurfaces;
	bool _visible;
	internal int ScreenCount => _surfaces.Count;
	internal bool HasOpticReticleSurface => _visible && _surfaces.Any( surface => surface.IsReticle
		&& surface.Object.IsValid() && surface.Object.Enabled && surface.Counter.HasOpticArtwork );
	internal Titanfall2WeaponPrewarmStatus UiPreparationStatus => _uiRequest?.Status ?? default;
	internal string[] UiPreparationErrors => _uiRequest?.Errors.ToArray() ?? [];
	internal object GetOpticDiagnostics() => new
	{
		Visible = _visible, PendingSurfaces = _pendingSurfaces, ScreenCount, HasOpticReticleSurface,
		Bindings = _bindings, UiPreparationStatus, UiPreparationErrors,
		ModelScreens = _model?.GetData<Titanfall2WeaponScreenData>(),
		Surfaces = _surfaces.Select( surface => new
		{
			surface.MeshName, surface.IsReticle, Enabled = surface.Object.IsValid() && surface.Object.Enabled,
			Layout = surface.Counter.DisplayLayout, surface.Counter.HasOpticArtwork,
			surface.Counter.ArtworkPending, surface.Counter.ArtworkError,
			Images = surface.Counter.Artwork.Select( image => new
			{
				image.Spec.TexturePath, Valid = image.Texture?.IsValid, Error = image.Texture?.IsError,
				Width = image.Texture?.Width, Height = image.Texture?.Height
			} ).ToArray()
		} ).ToArray()
	};
	internal Titanfall2WeaponAmmoObservation[] ObserveScreens() => _surfaces.Where( surface => surface.Object.IsValid() )
		.Select( surface => new Titanfall2WeaponAmmoObservation( surface.Slot, surface.MeshName, surface.Bone.Name,
			surface.Counter.ActualText, Weapon?.DisplayAmmo ?? 0, Weapon?.DisplayAmmoCapacity ?? 0, surface.Object.Enabled && _visible,
			surface.Object.WorldPosition, surface.Object.WorldRotation, surface.Object.WorldScale, surface.CornerError,
			surface.Counter.CanvasSize, surface.Panel.PanelSize, surface.Counter.CanvasOffset, surface.Counter.TextAnchor,
			surface.Counter.NativeFontId, surface.Counter.CounterFontSize, surface.Counter.ActualTextFrame, surface.Counter.ActualInkBounds ) ).ToArray();

	sealed class Surface
	{
		internal GameObject Object;
		internal WorldPanel Panel;
		internal Titanfall2WeaponAmmoPanel Counter;
		internal BoneCollection.Bone Bone;
		internal int[] BoneChain;
		internal Vector3 Origin, Right, Down;
		internal float Width, Height;
		internal Vector2 WindowPixels;
		internal int Slot;
		internal string MeshName;
		internal float CornerError;
		internal bool IsReticle;
	}

	internal void SetVisible( bool visible )
	{
		_visible = visible;
		foreach ( var surface in _surfaces ) if ( surface.Object.IsValid() ) surface.Object.Enabled = visible;
	}

	protected override void OnUpdate()
	{
		if ( !Weapon.IsValid() ) { Destroy(); return; }
		var renderer = GetComponent<BaseWeaponModel>()?.Renderer;
		if ( !renderer.IsValid() || renderer.Model is not { IsError: false } model ) return;
		var mount = Sandbox.Mounting.Directory.Get( Weapon.MountIdent ?? "titanfall2" ) as Titanfall2Mount;
		if ( mount?.IsMounted != true ) { SetVisible( false ); return; }
		if ( _renderer != renderer || _model != model || !ReferenceEquals( _bindings, Weapon.UiDisplays )
			|| _uiMount != mount || _uiRequest is null || _uiRequest.Cancelled )
		{
			ClearSurfaces();
			_renderer = renderer;
			_model = model;
			_bindings = Weapon.UiDisplays;
			_uiMount = mount;
			var supported = model.GetData<Titanfall2WeaponScreenData>()?.Screens ?? [];
			// Retain only resource names in the shared single-worker queue. No
			// scene, renderer or panel is captured by a background preparation job.
			_uiRequest = mount.QueueWeaponPrewarm( (_bindings ?? []).Where( binding => supported.Any( mesh =>
				string.Equals( mesh.MeshName, binding.MeshName, StringComparison.OrdinalIgnoreCase ) ) )
				.Select( binding => new Titanfall2WeaponPrewarmItem( Titanfall2WeaponPrewarmKind.WeaponUi, binding.UiPath ) ) );
			_pendingSurfaces = true;
		}
		// This path also works when general weapon prewarm is disabled. An
		// initially missing layout must not permanently suppress the gun screen.
		if ( !_uiRequest.Finished ) mount.PumpWeaponPrewarm();
		if ( _pendingSurfaces && _uiRequest.Finished )
		{
			BuildSurfaces();
			_pendingSurfaces = false;
		}
		SetVisible( Weapon.HasLocalFirstPersonHud && Weapon.ScopeOpacity < 1 );
	}

	void BuildSurfaces()
	{
		if ( _bindings is not { Length: > 0 } || _model.GetData<Titanfall2WeaponScreenData>()?.Screens is not { } screens ) return;
		foreach ( var binding in _bindings )
		{
			var layout = _uiMount?.GetWeaponUiLayout( binding.UiPath );
			if ( layout is null ) continue;
			var matching = screens.Where( screen => string.Equals( screen.MeshName, binding.MeshName, StringComparison.OrdinalIgnoreCase )
				&& OverlapsRenderedContent( layout, screen.UvStart, screen.UvEnd ) ).Take( 8 ).ToArray();
			for ( var panelIndex = 0; panelIndex < matching.Length; ++panelIndex )
			{
				var mesh = matching[panelIndex];
				if ( !mesh.Origin.IsFinite || !mesh.Right.IsFinite || !mesh.Down.IsFinite
					|| !float.IsFinite( mesh.Width ) || !float.IsFinite( mesh.Height ) || mesh.Width <= 0 || mesh.Height <= 0 ) continue;
				var bone = _model.Bones.GetBone( mesh.BoneName );
				if ( bone is null ) continue;
				var chain = new Stack<int>();
				for ( var current = bone; current is not null && chain.Count < _model.BoneCount; current = current.Parent ) chain.Push( current.Index );
				if ( chain.Count == 0 ) continue;
				var go = new GameObject( _renderer.GameObject, false, $"TF2 ammo screen {binding.Slot}_{panelIndex}" );
				go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
				var panel = go.AddComponent<WorldPanel>();
				panel.LookAtCamera = false;
				panel.Lighting = false;
				panel.InteractionRange = 0;
				// A physical gun screen is a window into the original RUI canvas,
				// not the entire canvas stretched to the surface's aspect ratio.
				panel.PanelSize = layout.CanvasSize * (mesh.UvEnd - mesh.UvStart);
				panel.RenderOptions.Game = false;
				panel.RenderOptions.Overlay = true;
				var counter = go.AddComponent<Titanfall2WeaponAmmoPanel>();
				counter.Weapon = Weapon;
				counter.CanvasSize = layout.CanvasSize;
				counter.CanvasOffset = -mesh.UvStart * layout.CanvasSize;
				counter.CounterFontSize = layout.FontSize;
				counter.NativeFontId = layout.FontId;
				counter.TextAnchor = layout.TextAnchor;
				counter.TextPivot = layout.TextPivot;
				counter.DisplayLayout = layout;
				counter.FadeWithZoom = binding.FadeWithZoom;
				var window = new Rect( mesh.UvStart * layout.CanvasSize, (mesh.UvEnd - mesh.UvStart) * layout.CanvasSize );
				_surfaces.Add( new() { Object = go, Panel = panel, Counter = counter, Bone = bone, BoneChain = chain.ToArray(), Slot = binding.Slot, MeshName = binding.MeshName,
					Origin = mesh.Origin, Right = mesh.Right, Down = mesh.Down, Width = mesh.Width, Height = mesh.Height,
					WindowPixels = panel.PanelSize, IsReticle = layout.Mode == Titanfall2WeaponUiDisplayMode.OpticReticle
						&& layout.Images?.Any( image => image.IsReticle && Intersects( window, image.Rect ) ) == true } );
			}
		}
		UpdatePoses();
	}

	protected override void OnPreRender() => UpdatePoses();
	void UpdatePoses()
	{
		if ( !_visible || !_renderer.IsValid() || !Weapon.IsValid() || !Weapon.HasLocalFirstPersonHud ) return;
		foreach ( var surface in _surfaces )
		{
			if ( !surface.Object.IsValid() || !TryReadScreenPose( _renderer, surface.BoneChain, out var pose ) ) continue;
			var center = pose.PointToWorld( surface.Origin );
			var right = pose.PointToWorld( surface.Origin + surface.Right * surface.Width ) - center;
			var down = pose.PointToWorld( surface.Origin + surface.Down * surface.Height ) - center;
			var width = right.Length;
			var height = down.Length;
			if ( width <= 0.001f || height <= 0.001f ) continue;
			var normal = Vector3.Cross( right, down ).Normal;
			if ( normal.LengthSquared < 0.5f ) continue;
			// Keep the authored UV axes; PanelRotation accounts for the native
			// shader's pixel-Y reflection. Their cross product points into the gun,
			// so the depth bias must go in the opposite direction.
			surface.Object.LocalPosition = center - normal * 0.01f;
			surface.Object.LocalRotation = PanelRotation( right.Normal, down.Normal );
			// Native panel pixels map to object axes before the panel's rotation.
			// Preserve the source canvas units using independent U/V scale factors.
			surface.Object.LocalScale = PanelScale( surface.WindowPixels, width, height );
			surface.CornerError = PanelCornerError( surface.Object.LocalTransform, surface.Panel.PanelSize,
				center - normal * 0.01f, right, down );
		}
	}

	static bool TryReadScreenPose( SkinnedModelRenderer renderer, int[] chain, out Transform pose )
	{
		pose = global::Transform.Zero;
		if ( !renderer.SceneModel.IsValid() ) return false;
		// Read only parent-space bones. Cached world bones and SceneModel's root
		// may belong to different camera placements, even within the same frame.
		foreach ( var index in chain )
		{
			var parentSpace = renderer.SceneModel.GetParentSpaceBone( index );
			if ( !parentSpace.IsValid ) return false;
			pose = pose.ToWorld( parentSpace );
		}
		return pose.IsValid;
	}

	// The official UI vertex shader reflects pixel Y before applying
	// ScenePanelObject.BuildPanelToObjectMatrix. Invert the complete pipeline,
	// not only its rotation; omitting the reflection renders glyphs upside down.
	internal static Rotation PanelRotation( Vector3 right, Vector3 down )
	{
		var native = Rotation.From( 0, 90, 90 );
		var nativeRight = native * Vector3.Forward;
		var nativeDown = native * -Vector3.Left;
		var nativeFrame = Rotation.LookAt( Vector3.Cross( nativeRight, nativeDown ).Normal, -nativeDown );
		return Rotation.LookAt( Vector3.Cross( right, down ).Normal, -down ) * nativeFrame.Inverse;
	}

	internal static Vector3 PanelScale( Vector2 pixels, float width, float height )
	{
		var native = Rotation.From( 0, 90, 90 );
		static Vector3 Absolute( Vector3 value ) => new( MathF.Abs( value.x ), MathF.Abs( value.y ), MathF.Abs( value.z ) );
		var horizontal = width / (pixels.x * Sandbox.UI.WorldPanel.ScreenToWorldScale);
		var vertical = height / (pixels.y * Sandbox.UI.WorldPanel.ScreenToWorldScale);
		return Absolute( native * Vector3.Forward ) * horizontal + Absolute( native * Vector3.Left ) * vertical
			+ Absolute( native * Vector3.Up ) * horizontal;
	}

	internal static float PanelCornerError( Transform transform, Vector2 pixels, Vector3 center, Vector3 right, Vector3 down )
	{
		var native = Rotation.From( 0, 90, 90 );
		var error = 0f;
		foreach ( var corner in PanelCorners )
		{
			var local = native * new Vector3( corner.x * pixels.x, -corner.y * pixels.y, 0 ) * Sandbox.UI.WorldPanel.ScreenToWorldScale;
			var expected = center + right * corner.x + down * corner.y;
			error = MathF.Max( error, transform.PointToWorld( local ).Distance( expected ) );
		}
		return error;
	}

	void ClearSurfaces()
	{
		foreach ( var surface in _surfaces ) if ( surface.Object.IsValid() ) surface.Object.Destroy();
		_surfaces.Clear();
	}

	internal static bool OverlapsRenderedContent( Titanfall2WeaponUiLayout layout, Vector2 uvStart, Vector2 uvEnd )
	{
		var window = new Rect( uvStart * layout.CanvasSize, (uvEnd - uvStart) * layout.CanvasSize );
		if ( layout.Mode == Titanfall2WeaponUiDisplayMode.OpticFrame ) return true;
		// Reticle artwork can span several MDL UV windows. Its centre need not
		// lie on every visible piece (unlike a numeric text anchor).
		if ( layout.Images?.Any( image => Intersects( window, image.Rect ) ) == true ) return true;
		if ( layout.PrimaryBar is { } primary && Intersects( window, primary.Rect ) ) return true;
		if ( layout.SecondaryBar is { } secondary && Intersects( window, secondary.Rect ) ) return true;
		if ( layout.FontSize > 0 )
		{
			if ( window.IsInside( layout.TextAnchor ) ) return true;
			if ( layout.SecondaryTextAnchor is { } second && window.IsInside( second ) ) return true;
		}
		return false;
	}
	static bool Intersects( Rect a, Rect b ) => a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;
	protected override void OnDisabled() { ClearSurfaces(); _uiRequest = null; _uiMount = null; _pendingSurfaces = false; }
	protected override void OnEnabled() { _model = null; _bindings = null; _uiRequest = null; }
	protected override void OnDestroy() => ClearSurfaces();
}

internal readonly record struct Titanfall2WeaponAmmoObservation( int Slot, string MeshName, string BoneName, string Text,
	int Rounds, int Capacity, bool Visible, Vector3 Position, Rotation Rotation, Vector3 Scale, float CornerError,
	Vector2 CanvasSize, Vector2 WindowPixels, Vector2 CanvasOffset, Vector2 TextAnchor, int FontId, float FontSize, Rect TextFrame, Rect InkBounds );
