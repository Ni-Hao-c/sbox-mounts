using System.Diagnostics;
using System.Threading;

internal enum Titanfall2WeaponPrewarmKind { Event, Sound, Particle, Model, Impact, Decal, ParticleSprite, WeaponUi, FontGlyph, ParticleTexture }
internal readonly record struct Titanfall2WeaponPrewarmItem( Titanfall2WeaponPrewarmKind Kind, string Name, bool Loop = false, string Variant = null,
	int Sequence = 0, float FrameRate = 0, int FontId = 0, float FontSize = 0 );

internal sealed class Titanfall2WeaponPrewarmRequest
{
	internal readonly LinkedList<Titanfall2WeaponPrewarmItem> Pending = new();
	readonly HashSet<Titanfall2WeaponPrewarmItem> _seen = [];
	internal bool Cancelled, Busy;
	internal int Requested, Completed, Failed, SoundsReady, ParticleGraphsReady, ModelsReady, FontGlyphsReady;
	internal double CpuMilliseconds, MainMilliseconds, LargestMainStepMilliseconds;
	internal string LargestMainStepKind;
	internal long PcmBytes;
	internal readonly List<string> Errors = [];
	internal bool Finished => !Busy && Pending.Count == 0;
	internal Titanfall2WeaponPrewarmStatus Status => new( true, Finished || Cancelled,
		Finished && !Cancelled && Failed == 0, Requested, Completed, Failed, SoundsReady, ParticleGraphsReady, ModelsReady,
		CpuMilliseconds, MainMilliseconds, LargestMainStepMilliseconds, PcmBytes, FontGlyphsReady: FontGlyphsReady, LargestMainStepKind: LargestMainStepKind );
	internal void Add( Titanfall2WeaponPrewarmItem item )
	{
		if ( string.IsNullOrWhiteSpace( item.Name ) || item.Name is "none" or "null" or "null_temp" || !_seen.Add( item ) ) return;
		if ( Requested >= 1024 ) { Fail( "Weapon prewarm dependency limit reached." ); return; }
		if ( item.Kind == Titanfall2WeaponPrewarmKind.Sound ) Pending.AddFirst( item );
		else Pending.AddLast( item );
		Requested++;
	}
	internal bool TryTake( out Titanfall2WeaponPrewarmItem item )
	{
		item = default;
		if ( Pending.First is not { } first ) return false;
		item = first.Value; Pending.RemoveFirst(); return true;
	}
	internal void Fail( string reason ) { Failed++; if ( Errors.Count < 16 ) Errors.Add( reason ); }
}

public partial class Titanfall2Mount
{
	const double WeaponPrewarmFrameBudgetMs = 2;
	readonly Dictionary<string, Titanfall2WeaponPrewarmRequest> _weaponPrewarmRequests = new( StringComparer.Ordinal );
	readonly Queue<Titanfall2WeaponPrewarmRequest> _weaponPrewarmQueue = new();
	CancellationTokenSource _weaponPrewarmCancel = new();
	Task<WeaponPreparedItem> _weaponPrewarmJob;
	Titanfall2WeaponPrewarmRequest _weaponPrewarmOwner;
	Titanfall2WeaponPrewarmItem _weaponPrewarmItem;
	double _weaponPrewarmFrame = double.NaN;

	internal Titanfall2WeaponPrewarmRequest QueueWeaponPrewarm( IEnumerable<Titanfall2WeaponPrewarmItem> requested )
	{
		var items = requested.Distinct().ToArray();
		var key = string.Join( "\n", items.Select( item => $"{item.Kind}|{item.Loop}|{item.Name}|{item.Variant}|{item.Sequence}|{item.FrameRate}|{item.FontId}|{item.FontSize}" ).Order() );
		if ( _weaponPrewarmRequests.TryGetValue( key, out var previous ) ) return previous;
		// Retain only bounded metadata, never a weapon, scene, emitter or sound handle.
		if ( _weaponPrewarmRequests.Count >= 128 )
			foreach ( var old in _weaponPrewarmRequests.Where( pair => pair.Value.Finished ).Select( pair => pair.Key ).Take( 32 ).ToArray() ) _weaponPrewarmRequests.Remove( old );
		var request = new Titanfall2WeaponPrewarmRequest();
		if ( _weaponPrewarmQueue.Count >= 128 ) { request.Fail( "Weapon prewarm queue is full." ); return request; }
		foreach ( var item in items ) request.Add( item );
		_weaponPrewarmRequests[key] = request;
		_weaponPrewarmQueue.Enqueue( request );
		return request;
	}

	internal void ResetWeaponPrewarm()
	{
		_weaponPrewarmCancel.Cancel();
		_weaponPrewarmCancel.Dispose();
		_weaponPrewarmCancel = new();
		foreach ( var request in _weaponPrewarmRequests.Values ) { request.Cancelled = true; request.Pending.Clear(); }
		_weaponPrewarmRequests.Clear(); _weaponPrewarmQueue.Clear();
		_weaponPrewarmedParticles.Clear();
		_weaponPrewarmOwner = null; _weaponPrewarmJob = null;
		_weaponPrewarmFrame = double.NaN;
	}

	internal void PumpWeaponPrewarm()
	{
		if ( !IsMounted || Application.IsHeadless || Application.IsDedicatedServer || _weaponPrewarmFrame == Time.NowDouble ) return;
		_weaponPrewarmFrame = Time.NowDouble;
		var frame = Stopwatch.StartNew();
		for ( var operations = 0; operations < 8 && frame.Elapsed.TotalMilliseconds < WeaponPrewarmFrameBudgetMs; operations++ )
		{
			if ( _weaponPrewarmJob is not null )
			{
				if ( !_weaponPrewarmJob.IsCompleted ) return;
				var owner = _weaponPrewarmOwner;
				var started = Stopwatch.GetTimestamp();
				try
				{
					var prepared = _weaponPrewarmJob.GetAwaiter().GetResult(); // completed; never blocks the engine thread
					owner.CpuMilliseconds += prepared.CpuMilliseconds;
					if ( !PublishWeaponPreparedItem( _weaponPrewarmItem, prepared, owner, out var error ) ) owner.Fail( error );
				}
				catch ( Exception e ) { owner.Fail( e.Message ); }
				finally
				{
					RecordWeaponPrewarmStep( owner, started, _weaponPrewarmItem.Kind ); owner.Completed++; owner.Busy = false;
					_weaponPrewarmJob = null; _weaponPrewarmOwner = null;
					if ( owner.Pending.Count > 0 && !owner.Cancelled ) _weaponPrewarmQueue.Enqueue( owner );
				}
				// Native graph/material construction is indivisible. Do not immediately
				// start another expensive graph on the same frame after publishing one.
				if ( _weaponPrewarmItem.Kind is Titanfall2WeaponPrewarmKind.Particle or Titanfall2WeaponPrewarmKind.FontGlyph ) return;
				continue;
			}
			if ( !_weaponPrewarmQueue.TryDequeue( out var request ) ) return;
			if ( request.Cancelled || !request.TryTake( out var item ) ) continue;
			var begin = Stopwatch.GetTimestamp();
			try
			{
				if ( item.Kind is Titanfall2WeaponPrewarmKind.Sound or Titanfall2WeaponPrewarmKind.Particle
					or Titanfall2WeaponPrewarmKind.WeaponUi or Titanfall2WeaponPrewarmKind.FontGlyph )
				{
					_weaponPrewarmJob = BeginWeaponPreparedItem( item, _weaponPrewarmCancel.Token );
					_weaponPrewarmOwner = request; _weaponPrewarmItem = item; request.Busy = true;
				}
				else { ExpandWeaponPrewarmItem( item, request ); request.Completed++; }
			}
			catch ( Exception e ) { request.Fail( e.Message ); request.Completed++; }
			finally { RecordWeaponPrewarmStep( request, begin, item.Kind ); }
			if ( !request.Busy && request.Pending.Count > 0 ) _weaponPrewarmQueue.Enqueue( request );
			if ( item.Kind is Titanfall2WeaponPrewarmKind.Model or Titanfall2WeaponPrewarmKind.Decal or Titanfall2WeaponPrewarmKind.ParticleSprite
				or Titanfall2WeaponPrewarmKind.WeaponUi or Titanfall2WeaponPrewarmKind.FontGlyph or Titanfall2WeaponPrewarmKind.ParticleTexture ) return;
		}
	}

	static void RecordWeaponPrewarmStep( Titanfall2WeaponPrewarmRequest request, long started, Titanfall2WeaponPrewarmKind kind )
	{
		var elapsed = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
		request.MainMilliseconds += elapsed;
		if ( elapsed > request.LargestMainStepMilliseconds ) { request.LargestMainStepMilliseconds = elapsed; request.LargestMainStepKind = kind.ToString(); }
	}

	void ExpandWeaponPrewarmItem( Titanfall2WeaponPrewarmItem item, Titanfall2WeaponPrewarmRequest request )
	{
		switch ( item.Kind )
		{
			case Titanfall2WeaponPrewarmKind.Event:
				if ( TryGetSoundEvent( item.Name, out var soundEvent ) )
					foreach ( var source in soundEvent.Layers.SelectMany( layer => layer.Choice.Sources ).Distinct( StringComparer.OrdinalIgnoreCase ) )
						request.Add( new( Titanfall2WeaponPrewarmKind.Sound, source, item.Loop ) );
				else request.Add( new( Titanfall2WeaponPrewarmKind.Sound, item.Name, item.Loop ) );
				break;
			case Titanfall2WeaponPrewarmKind.Model:
				var path = item.Name.StartsWith( "mount://", StringComparison.OrdinalIgnoreCase ) ? item.Name : $"mount://{Ident}/{item.Name}";
				if ( path.EndsWith( ".mdl", StringComparison.OrdinalIgnoreCase ) || path.EndsWith( ".rmdl", StringComparison.OrdinalIgnoreCase ) ) path += ".vmdl";
				var model = Model.Load( path );
				if ( model is null || model.IsError ) { request.Fail( $"Model unavailable: {path}" ); break; }
				request.ModelsReady++;
				if ( model.GetData<Titanfall2PilotViewData>() is { Events: { } events } )
					foreach ( var evt in events.Where( evt => evt.Kind == Titanfall2PilotViewData.EventKind.Sound ) )
						request.Add( new( Titanfall2WeaponPrewarmKind.Event, evt.Options ) );
				break;
			case Titanfall2WeaponPrewarmKind.Impact:
				if ( GetCombatImpactBlocks( item.Name ).FirstOrDefault() is { } table )
					foreach ( var section in table.Children.Where( block => block.Name is "FX" or "Sound" ) )
						foreach ( var value in section.Values.Values ) request.Add( new( section.Name == "FX" ? Titanfall2WeaponPrewarmKind.Particle : Titanfall2WeaponPrewarmKind.Event, value ) );
				foreach ( var sound in GetOrdnanceSamples( item.Name ) ) request.Add( new( Titanfall2WeaponPrewarmKind.Sound, sound ) );
				foreach ( var surface in new[] { "C", "M", "W", "Y" } ) request.Add( new( Titanfall2WeaponPrewarmKind.Decal, item.Name, Variant: surface ) );
				break;
			case Titanfall2WeaponPrewarmKind.Decal:
				GetCombatDecal( item.Variant, item.Name.StartsWith( "exp_", StringComparison.OrdinalIgnoreCase ), item.Name );
				break;
			case Titanfall2WeaponPrewarmKind.ParticleSprite:
				if ( !TryGetLegacyParticleSprite( item.Name, item.FrameRate, out _, out var error, item.Sequence ) ) request.Fail( error );
				break;
			case Titanfall2WeaponPrewarmKind.ParticleTexture:
				var texture = Texture.Load( $"mount://{Ident}/{Titanfall2LegacyPath.NormalizeTextureName( item.Name )}.vtex", false );
				if ( texture is not { IsValid: true, IsError: false } ) request.Fail( $"Particle data texture unavailable: {item.Name}" );
				break;
		}
	}
}
