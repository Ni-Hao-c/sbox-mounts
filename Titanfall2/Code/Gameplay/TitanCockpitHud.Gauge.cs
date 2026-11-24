using Sandbox.UI;

/// <summary>Native earn-meter display; this does not execute the compiled retail RUI.</summary>
[Hide]
public sealed class Titanfall2CoreGauge : Panel
{
	float _value, _target, _from, _elapsed;
	bool _warm, _supported = true, _initialized;

	public void SetValue( float value, bool warm, bool supported )
	{
		value = float.IsFinite( value ) ? Math.Clamp( value, 0, 1 ) : 0;
		if ( value != _target ) { _from = _value; _elapsed = 0; _target = value; }
		if ( !_initialized ) { _value = _from = _target; _initialized = true; }
		_warm = warm;
		_supported = supported;
	}

	public override void Tick()
	{
		base.Tick();
		// Fixed-duration transition, matching the retail earn meter's interval.
		_elapsed = MathF.Min( 0.7f, _elapsed + MathF.Max( 0, Time.Delta ) );
		_value = _from + (_target - _from) * (_elapsed / 0.7f);
	}

	public override void OnDraw( Painter painter )
	{
		var size = Box.Rect.Size;
		var center = size * 0.5f;
		var radius = MathF.Min( size.x, size.y ) * 0.44f;
		if ( radius <= 0 ) return;
		var width = radius * 0.09f;
		painter.Stroke = new Stroke( new Color( 0.08f, 0.16f, 0.23f, 0.85f ), width + 3 );
		painter.Arc( center, radius, 140, 260 );
		painter.Stroke = new Stroke( new Color( 0.36f, 0.63f, 0.79f, 0.22f ), width );
		painter.Arc( center, radius, 140, 260 );
		if ( _supported && _value > 0 )
		{
			painter.Stroke = new Stroke( _warm ? new Color( 1, 0.73f, 0.26f ) : new Color( 0.45f, 0.78f, 1 ), width );
			painter.Arc( center, radius, 140, 260 * _value );
		}
	}
}
