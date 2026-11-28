/// <summary>View-only duck fraction; uses the committed player stance and never changes the hull.</summary>
internal struct Titanfall2DuckViewState
{
	internal const float DuckSeconds = 0.4f;
	internal const float UnduckSeconds = 0.2f;
	double _transitionStartedAt, _lastSampledAt;
	float _from, _to;
	bool _initialized;
	internal float Fraction { get; private set; }

	internal void Reset() => this = default;

	internal float Sample( double now, bool crouching )
	{
		if ( !double.IsFinite( now ) ) return Fraction;
		var target = crouching ? 1f : 0f;
		if ( !_initialized || now < _lastSampledAt )
		{
			_initialized = true;
			_from = _to = Fraction = target;
			_lastSampledAt = _transitionStartedAt = now;
			return Fraction;
		}
		if ( now == _lastSampledAt ) return Fraction;
		_lastSampledAt = now;
		var duration = (_to > _from ? DuckSeconds : UnduckSeconds) * MathF.Abs( _to - _from );
		var t = duration > 0 ? Math.Clamp( (float)(now - _transitionStartedAt) / duration, 0f, 1f ) : 1f;
		Fraction = float.Lerp( _from, _to, t * t * (3f - 2f * t) );
		if ( target != _to )
		{
			_from = Fraction;
			_to = target;
			_transitionStartedAt = now;
		}
		return Fraction;
	}

	// Retail client.dll 0x1800D21A9: translate along view up, with ADS suppressing the offset.
	internal static float Offset( float authoredOffset, float duckFraction, float adsFraction ) =>
		authoredOffset * MathF.Sin( Math.Clamp( duckFraction, 0f, 1f ) * (MathF.PI * 0.5f) ) * (1f - Math.Clamp( adsFraction, 0f, 1f ));
}

public sealed partial class Titanfall2PilotViewModel
{
	internal const string FirstDraw = "pilot_fp_draw_first";
	internal const string CrouchAdsIn = "pilot_fp_crouch_ads_in";
	internal const string CrouchAdsOut = "pilot_fp_crouch_ads_out";
	internal const string SlideIdle = "pilot_fp_slide_idle";
	internal const string SlideFire = "pilot_fp_slide_fire";
	internal const string SlideRaise = "pilot_fp_slide_raise";

	internal static string StanceRole( string role, bool slide ) => role + (slide ? "_slide" : "_crouch");
	internal static bool IsStanceAction( string role ) => role is FirstDraw or SlideRaise
		|| role?.EndsWith( "_slide", StringComparison.Ordinal ) == true || role?.EndsWith( "_crouch", StringComparison.Ordinal ) == true;

	bool _crouching, _sliding;
	(bool Crouching, bool Sliding) _reloadStance;
	string SelectAction( string role, bool crouching, bool sliding )
	{
		if ( sliding )
		{
			var slide = StanceRole( role, true );
			if ( _clips.Contains( slide ) ) return slide;
		}
		if ( crouching || sliding )
		{
			var crouch = StanceRole( role, false );
			if ( _clips.Contains( crouch ) ) return crouch;
		}
		return role;
	}

	string ReloadAction( string role ) => SelectAction( role, _reloadStance.Crouching, _reloadStance.Sliding );
	string RestingClip => _aiming && _clips.Contains( AdsIdle ) ? AdsIdle : _sprinting ? Sprint
		: _sliding && _clips.Contains( SlideIdle ) ? SlideIdle : _oneHanded ? OneHandedIdle : Idle;
	bool IsRestOrRaise => _action is Idle or AdsIdle or OneHandedIdle or SlideIdle or Sprint or SprintRaise or SlideRaise;

	Titanfall2DuckViewState _duckView;

	float UpdateDuckViewOffset( float zoom )
	{
		if ( _owner.Owner is not { } player || !player.IsValid() ) return 0;
		var fraction = _duckView.Sample( Time.NowDouble, player.IsDucking );
		if ( fraction <= 0 || zoom >= 1 ) return 0;
		var settings = player.GameObject.GetComponent<Titanfall2PilotParkour>() is { IsValid: true, Equipped: true } parkour
			? parkour.Settings : Titanfall2PilotParkour.ResolveSettings( _owner.MountIdent );
		return Titanfall2DuckViewState.Offset( settings.ViewModelDuckOffset, fraction, zoom );
	}
}
