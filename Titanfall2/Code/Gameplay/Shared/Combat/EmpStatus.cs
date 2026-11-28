using Sandbox.UI;
using Sandbox.UI.Construct;

/// <summary>
/// Retail Titanfall 2 Pilot EMP status effect (Arc Grenade self-hit / enemy hit):
/// applies 50% movement slow, 35% look/turn slow, fullscreen electric interference overlay,
/// and crackling loop audio. Cleansed automatically on Phase Shift.
/// </summary>
[Hide]
public sealed class Titanfall2EmpStatus : Component, ICameraModifier
{
	// Retail _weapon_utility.nut constants.
	internal const float MoveSlowSeverity = 0.50f;
	internal const float TurnSlowSeverity = 0.35f;
	internal const float MinDurationSeconds = 1.25f;
	internal const float MaxDurationSeconds = 3.0f;
	internal const float MaxFadeoutSeconds = 1.0f;

	[Sync( SyncFlags.FromHost )] public float Intensity { get; private set; }
	[Sync( SyncFlags.FromHost )] public float HoldRemaining { get; private set; }
	[Sync( SyncFlags.FromHost )] public float FadeRemaining { get; private set; }
	[Sync( SyncFlags.FromHost )] public float FadeDuration { get; private set; } = 0.75f;
	[Sync( SyncFlags.FromHost )] public string MountIdent { get; set; } = "titanfall2";

	PlayerController _controller;
	GameObject _hudObject;
	Titanfall2EmpOverlayHud _hud;
	GameObject _audioRoot;
	Titanfall2SoundEventEmitter _audio;
	bool _audioPlaying;
	bool _audioFailed;
	Angles _lastEyeAngles;
	bool _hasEyeAngles;
	bool _savedVanillaSpeeds;
	float _baseWalkSpeed;
	float _baseRunSpeed;

	public float ActiveIntensity
	{
		get
		{
			if ( HoldRemaining > 0f ) return Math.Clamp( Intensity, 0f, 1f );
			if ( FadeRemaining > 0f && FadeDuration > 0.01f )
				return Math.Clamp( Intensity * (FadeRemaining / FadeDuration), 0f, 1f );
			return 0f;
		}
	}

	public float MoveSpeedScale => ActiveIntensity > 0f ? Math.Clamp( 1f - MoveSlowSeverity * ActiveIntensity, 0.35f, 1f ) : 1f;
	public float TurnSpeedScale => ActiveIntensity > 0f ? Math.Clamp( 1f - TurnSlowSeverity * ActiveIntensity, 0.45f, 1f ) : 1f;

	/// <summary>Host-authoritative application from Arc Grenade blasts (including self-hit).</summary>
	internal static void ApplyFromHost( PlayerController player, float distanceFraction, string mountIdent )
	{
		if ( !Networking.IsHost || !player.IsValid() || !player.GameObject.IsValid() || player.GameObject.IsDestroyed ) return;
		if ( player.GameObject.Tags.Has( Titanfall2PhaseDimension.PhasedTag ) ) return;

		var frac = Math.Clamp( distanceFraction, 0.25f, 1f );
		var total = float.Lerp( MinDurationSeconds, MaxDurationSeconds, frac );
		var fade = Math.Clamp( MaxFadeoutSeconds * frac, 0.35f, total * 0.6f );
		var hold = MathF.Max( 0.25f, total - fade );
		var strength = Math.Clamp( 0.45f + 0.55f * frac, 0.45f, 1f );

		var status = player.GameObject.GetOrAddComponent<Titanfall2EmpStatus>();
		status.MountIdent = mountIdent ?? "titanfall2";
		status.Intensity = MathF.Max( status.ActiveIntensity, strength );
		status.HoldRemaining = MathF.Max( status.HoldRemaining, hold );
		status.FadeDuration = MathF.Max( status.FadeDuration, fade );
		status.FadeRemaining = status.FadeDuration;
	}

	internal void CleanseHost()
	{
		if ( !Networking.IsHost ) return;
		Intensity = 0f;
		HoldRemaining = 0f;
		FadeRemaining = 0f;
	}

	protected override void OnUpdate()
	{
		_controller ??= GetComponent<PlayerController>();

		if ( Networking.IsHost )
		{
			if ( GameObject.Tags.Has( Titanfall2PhaseDimension.PhasedTag ) )
			{
				CleanseHost();
			}
			else if ( HoldRemaining > 0f )
			{
				HoldRemaining = MathF.Max( 0f, HoldRemaining - Time.Delta );
			}
			else if ( FadeRemaining > 0f )
			{
				FadeRemaining = MathF.Max( 0f, FadeRemaining - Time.Delta );
				if ( FadeRemaining <= 0f ) Intensity = 0f;
			}
		}

		var active = ActiveIntensity;
		ApplyVanillaMoveSlow( active );

		if ( !IsProxy && !Application.IsDedicatedServer && !Application.IsHeadless )
		{
			ApplyLocalTurnSlow( active );
			UpdateOverlayHud( active );
			UpdateAudio( active );
		}
		else
		{
			StopAudio();
			DestroyHud();
		}
	}

	void ApplyVanillaMoveSlow( float active )
	{
		if ( !_controller.IsValid() ) return;
		// PilotMoveMode multiplies speeds directly via MoveSpeedScale.
		var hasPilotParkour = GetComponent<Titanfall2PilotParkour>() is { IsValid: true, Equipped: true };
		if ( hasPilotParkour )
		{
			RestoreVanillaSpeeds();
			return;
		}

		if ( active > 0.001f )
		{
			if ( !_savedVanillaSpeeds )
			{
				_baseWalkSpeed = _controller.WalkSpeed;
				_baseRunSpeed = _controller.RunSpeed;
				_savedVanillaSpeeds = true;
			}
			var scale = MoveSpeedScale;
			_controller.WalkSpeed = _baseWalkSpeed * scale;
			_controller.RunSpeed = _baseRunSpeed * scale;
		}
		else
		{
			RestoreVanillaSpeeds();
		}
	}

	void RestoreVanillaSpeeds()
	{
		if ( !_savedVanillaSpeeds ) return;
		if ( _controller.IsValid() )
		{
			_controller.WalkSpeed = _baseWalkSpeed;
			_controller.RunSpeed = _baseRunSpeed;
		}
		_savedVanillaSpeeds = false;
	}

	void ApplyLocalTurnSlow( float active )
	{
		if ( !_controller.IsValid() || !_controller.UseInputControls ) { _hasEyeAngles = false; return; }
		var current = _controller.EyeAngles;
		if ( !_hasEyeAngles || active <= 0.001f )
		{
			_lastEyeAngles = current;
			_hasEyeAngles = true;
			return;
		}
		var dYaw = ShortestAngleDelta( _lastEyeAngles.yaw, current.yaw );
		var dPitch = ShortestAngleDelta( _lastEyeAngles.pitch, current.pitch );
		var scale = TurnSpeedScale;
		var damped = new Angles(
			Math.Clamp( _lastEyeAngles.pitch + dPitch * scale, -89f, 89f ),
			_lastEyeAngles.yaw + dYaw * scale,
			current.roll );
		_controller.EyeAngles = damped;
		_lastEyeAngles = damped;
	}

	internal static float ShortestAngleDelta( float from, float to )
	{
		var diff = (to - from) % 360f;
		if ( diff > 180f ) diff -= 360f;
		else if ( diff < -180f ) diff += 360f;
		return diff;
	}

	public void ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		if ( IsProxy ) return;
		var active = ActiveIntensity;
		if ( active <= 0.001f ) return;
		var t = (float)Time.NowDouble;
		var jitterPitch = MathF.Sin( t * 53f ) * 0.55f * active;
		var jitterYaw = MathF.Cos( t * 47f ) * 0.55f * active;
		var jitterRoll = MathF.Sin( t * 39f ) * 1.15f * active;
		view.Rotation *= new Angles( jitterPitch, jitterYaw, jitterRoll ).ToRotation();
	}

	void UpdateOverlayHud( float active )
	{
		if ( active <= 0.001f )
		{
			if ( _hud.IsValid() ) _hud.SetIntensity( 0f );
			return;
		}
		if ( !_hud.IsValid() )
		{
			_hudObject = new GameObject( GameObject, false, "TF2 EMP Screen Overlay" );
			_hudObject.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			_hudObject.AddComponent<ScreenPanel>();
			_hud = _hudObject.AddComponent<Titanfall2EmpOverlayHud>();
			_hudObject.Enabled = true;
		}
		_hud.SetIntensity( active );
	}

	void UpdateAudio( float active )
	{
		if ( active > 0.05f )
		{
			if ( !_audioPlaying && !_audioFailed )
			{
				StopAudio();
				_audioRoot = new GameObject( GameObject, false, "TF2 EMP Audio" );
				_audioRoot.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
				_audioRoot.LocalTransform = global::Transform.Zero;
				_audio = _audioRoot.AddComponent<Titanfall2SoundEventEmitter>();
				_audioPlaying = _audio.Play( MountIdent ?? "titanfall2", "sound/emp_titan_aura_close_2ch_v1_01_lp", firstPerson: true, WorldPosition, sustain: true );
				if ( _audioPlaying ) _audioRoot.Enabled = true;
				else { _audioFailed = true; DestroyAudioRoot(); }
			}
			else if ( _audioPlaying && _audio.IsValid() )
			{
				_audio.SetSource( WorldPosition );
			}
		}
		else
		{
			StopAudio();
		}
	}

	void StopAudio()
	{
		_audioPlaying = false;
		_audioFailed = false;
		if ( _audio.IsValid() ) _audio.StopSustain();
		DestroyAudioRoot();
	}

	void DestroyAudioRoot()
	{
		if ( _audioRoot.IsValid() ) _audioRoot.Destroy();
		_audioRoot = null;
		_audio = null;
	}

	void DestroyHud()
	{
		if ( _hudObject.IsValid() ) _hudObject.Destroy();
		_hudObject = null;
		_hud = null;
	}

	protected override void OnDisabled()
	{
		RestoreVanillaSpeeds();
		StopAudio();
		DestroyHud();
		_hasEyeAngles = false;
	}

	protected override void OnDestroy()
	{
		RestoreVanillaSpeeds();
		StopAudio();
		DestroyHud();
	}
}

/// <summary>Fullscreen electric-arc EMP screen interference filter for Pilot/Player self-hit and enemy Arc hits.</summary>
[Hide]
public sealed class Titanfall2EmpOverlayHud : Titanfall2MountPanelComponent
{
	Panel _root;
	Panel _vignette;
	Panel _scanBandA;
	Panel _scanBandB;
	Panel _flashLayer;
	float _intensity;

	const string Styles = """
titanfall2empoverlayhud {
	position: absolute;
	left: 0;
	top: 0;
	width: 100%;
	height: 100%;
	pointer-events: none;

	.emp-root {
		position: absolute;
		left: 0;
		top: 0;
		width: 100%;
		height: 100%;
		background-color: #1a588c38;
		backdrop-filter: saturate(35%) contrast(1.22) blur(1.4px);
		opacity: 0;
	}
	.emp-vignette {
		position: absolute;
		left: 0;
		top: 0;
		width: 100%;
		height: 100%;
		border: 26px solid #4cc8ff9e;
		box-shadow: inset 0 0 110px #2b8fffb8;
	}
	.emp-flash {
		position: absolute;
		left: 0;
		top: 0;
		width: 100%;
		height: 100%;
		background-color: #9beeff52;
	}
	.emp-band {
		position: absolute;
		left: 0;
		width: 100%;
		height: 6%;
		background-color: #7ee4ff55;
		border-top: 2px solid #d4f8ffaa;
		border-bottom: 2px solid #2b8fff88;
	}
}
""";

	protected override void OnEnabled()
	{
		base.OnEnabled();
		if ( !Panel.IsValid() ) return;
		Panel.StyleSheet.AddInline( Styles, "titanfall2:emp-overlay" );
		_root = Panel.Add.Panel( "emp-root" );
		_vignette = _root.Add.Panel( "emp-vignette" );
		_flashLayer = _root.Add.Panel( "emp-flash" );
		_scanBandA = _root.Add.Panel( "emp-band" );
		_scanBandB = _root.Add.Panel( "emp-band" );
	}

	internal void SetIntensity( float intensity ) => _intensity = Math.Clamp( intensity, 0f, 1f );

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( !_root.IsValid() ) return;
		if ( _intensity <= 0.001f )
		{
			_root.Style.Opacity = 0f;
			return;
		}
		var t = (float)Time.NowDouble;
		var flicker = 0.78f + 0.22f * MathF.Sin( t * 44f ) * MathF.Cos( t * 19f );
		_root.Style.Opacity = Math.Clamp( _intensity * flicker, 0f, 1f );

		if ( _flashLayer.IsValid() )
		{
			var burst = MathF.Max( 0f, MathF.Sin( t * 31f ) ) * _intensity * _intensity;
			_flashLayer.Style.Opacity = burst * 0.65f;
		}
		if ( _scanBandA.IsValid() )
		{
			var yA = ((t * 68f) % 110f) - 5f;
			_scanBandA.Style.Top = Length.Percent( yA );
			_scanBandA.Style.Opacity = _intensity * 0.72f;
		}
		if ( _scanBandB.IsValid() )
		{
			var yB = 100f - ((t * 95f + 37f) % 110f);
			_scanBandB.Style.Top = Length.Percent( yB );
			_scanBandB.Style.Opacity = _intensity * 0.55f;
		}
	}
}
