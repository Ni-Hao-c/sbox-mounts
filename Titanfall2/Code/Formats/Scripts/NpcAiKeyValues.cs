using System.Collections.ObjectModel;
using System.Globalization;

/// <summary>Bounded, mode-aware KeyValues reader. Keeps the selected value's file instead of flattening away provenance.</summary>
internal static class Titanfall2NpcAiKeyValues
{
	internal sealed record Value( string Text, string Path );
	internal sealed class Block
	{
		internal Dictionary<string, Value> Values { get; } = new( StringComparer.OrdinalIgnoreCase );
		internal Dictionary<string, Block> Children { get; } = new( StringComparer.OrdinalIgnoreCase );
		internal void Merge( Block other )
		{
			foreach ( var (key, value) in other.Values )
				if ( !value.Text.Equals( "<KEEP_DEFAULT>", StringComparison.OrdinalIgnoreCase ) ) Values[key] = value;
			foreach ( var (key, block) in other.Children )
			{
				if ( !Children.TryGetValue( key, out var destination ) ) Children[key] = destination = new();
				destination.Merge( block );
			}
		}
	}

	internal static Block Read( string path, string folder, Func<string, string> read, bool singleplayer,
		ICollection<string> diagnostics, ICollection<string> sources )
		=> ReadFile( Normalize( path, folder ), folder, read, singleplayer, diagnostics, sources, new( StringComparer.OrdinalIgnoreCase ) );

	static Block ReadFile( string path, string folder, Func<string, string> read, bool sp,
		ICollection<string> diagnostics, ICollection<string> sources, HashSet<string> active )
	{
		if ( active.Count >= 16 || !active.Add( path ) ) throw new InvalidDataException( $"Invalid AI #base chain: {path}" );
		try
		{
			var text = read( path ) ?? throw new InvalidDataException( $"Missing AI data: {path}" );
			if ( text.Length > 512 * 1024 ) throw new InvalidDataException( $"AI data exceeds size limit: {path}" );
			if ( !sources.Contains( path ) ) sources.Add( path );
			var reader = new Reader( text.Trim( '\uFEFF', '\0' ), path, sp, diagnostics );
			var inherited = new Block();
			while ( reader.Peek.Text.Equals( "#base", StringComparison.OrdinalIgnoreCase ) )
			{
				reader.Next();
				var include = reader.Next();
				if ( include.Kind != Kind.Word ) throw new InvalidDataException( $"Invalid AI #base in {path}" );
				var enabled = reader.Conditions();
				var slash = path.LastIndexOf( '/' );
				var resolved = Normalize( path[..(slash + 1)] + include.Text, folder );
				if ( enabled ) inherited.Merge( ReadFile( resolved, folder, read, sp, diagnostics, sources, active ) );
			}
			var name = reader.Next();
			if ( name.Kind != Kind.Word ) throw new InvalidDataException( $"Expected AI data root: {path}" );
			var rootEnabled = reader.Conditions();
			if ( reader.Next().Kind != Kind.Open ) throw new InvalidDataException( $"Expected AI data block: {path}" );
			var local = reader.ReadBlock( 0 );
			rootEnabled &= reader.Conditions();
			if ( reader.Peek.Kind != Kind.End ) throw new InvalidDataException( $"Unexpected data after AI root: {path}" );
			if ( rootEnabled ) inherited.Merge( local );
			return inherited;
		}
		finally { active.Remove( path ); }
	}

	internal static string Normalize( string path, string folder )
	{
		if ( string.IsNullOrWhiteSpace( path ) || path.Contains( ':' ) || path.Contains( '\0' ) || path.StartsWith( '/' ) || path.StartsWith( '\\' ) )
			throw new InvalidDataException( $"Invalid AI data path: {path}" );
		var parts = new List<string>();
		foreach ( var part in path.Replace( '\\', '/' ).Split( '/', StringSplitOptions.RemoveEmptyEntries ) )
		{
			if ( part == "." ) continue;
			if ( part == ".." ) { if ( parts.Count == 0 ) throw new InvalidDataException( "AI path escaped root." ); parts.RemoveAt( parts.Count - 1 ); }
			else parts.Add( part );
		}
		var result = string.Join( '/', parts ).ToLowerInvariant();
		if ( !result.StartsWith( folder, StringComparison.Ordinal ) || !result.EndsWith( ".txt", StringComparison.Ordinal ) )
			throw new InvalidDataException( $"AI data escaped {folder}: {path}" );
		return result;
	}

	/// <summary>Mode blocks override the root only after inheritance. Mods and unrelated nested blocks stay separate.</summary>
	internal static IReadOnlyDictionary<string, Value> EffectiveValues( Block root, bool singleplayer, IEnumerable<string> mods = null )
	{
		var values = new Dictionary<string, Value>( root.Values, StringComparer.OrdinalIgnoreCase );
		void Overlay( Block block ) { foreach ( var (key, value) in block.Values ) if ( !value.Text.Equals( "<KEEP_DEFAULT>", StringComparison.OrdinalIgnoreCase ) ) values[key] = value; }
		if ( root.Children.TryGetValue( singleplayer ? "sp" : "mp", out var mode ) ) Overlay( mode );
		if ( root.Children.TryGetValue( singleplayer ? "SP_BASE" : "MP_BASE", out mode ) ) Overlay( mode );
		if ( mods is not null && root.Children.TryGetValue( "Mods", out var modRoot ) )
			foreach ( var mod in mods ) if ( modRoot.Children.TryGetValue( mod, out var block ) ) Overlay( block );
		return new ReadOnlyDictionary<string, Value>( values );
	}

	enum Kind { End, Word, Open, Close, Condition }
	readonly record struct Token( Kind Kind, string Text );
	sealed class Reader( string text, string path, bool sp, ICollection<string> diagnostics )
	{
		int _position;
		Token? _pending;
		internal Token Peek => _pending ??= Lex();
		internal Token Next() { var token = Peek; _pending = null; return token; }
		internal Block ReadBlock( int depth )
		{
			if ( depth >= 24 ) throw new InvalidDataException( $"AI data nesting limit exceeded: {path}" );
			var block = new Block();
			while ( Peek.Kind != Kind.Close )
			{
				var key = Next();
				if ( key.Kind != Kind.Word ) throw new InvalidDataException( $"Expected AI key: {path}" );
				var enabled = Conditions(); // AISettings permits "mp" [$mp] { ... }.
				var value = Next();
				if ( value.Kind is not (Kind.Open or Kind.Word) ) throw new InvalidDataException( $"Expected AI value: {path}/{key.Text}" );
				var child = value.Kind == Kind.Open ? ReadBlock( depth + 1 ) : null;
				enabled &= Conditions();
				if ( !enabled ) continue;
				if ( child is null ) block.Values[key.Text] = new( value.Text, path );
				else if ( block.Children.TryGetValue( key.Text, out var previous ) ) previous.Merge( child );
				else block.Children[key.Text] = child;
			}
			Next();
			return block;
		}
		internal bool Conditions()
		{
			var enabled = true;
			while ( Peek.Kind == Kind.Condition )
			{
				var expression = Next().Text;
				var unknown = false;
				var any = false;
				foreach ( var term in expression.Split( "||", StringSplitOptions.TrimEntries ) )
				{
					var all = true;
					foreach ( var atom in term.Split( "&&", StringSplitOptions.TrimEntries ) )
					{
						var negate = atom.StartsWith( '!' );
						var name = (negate ? atom[1..].Trim() : atom).TrimStart( '$' ).ToLowerInvariant();
						if ( name.Length == 0 || name.Any( c => !char.IsAsciiLetterOrDigit( c ) && c != '_' ) )
							throw new InvalidDataException( $"Unsupported AI condition: [{expression}] in {path}" );
						bool selected;
						switch ( name )
						{
							case "sp": selected = sp; break;
							case "mp": selected = !sp; break;
							case "pc": case "windows": case "win32": selected = true; break;
							case "x360": case "xbox": case "xboxone": case "ps3": case "ps4": case "console": selected = false; break;
							default: selected = false; unknown = true; break;
						}
						all &= negate != selected;
					}
					any |= all;
				}
				if ( unknown ) diagnostics.Add( $"Unknown condition [{expression}] in {path}; entry disabled." );
				enabled &= any && !unknown;
			}
			return enabled;
		}
		Token Lex()
		{
			while ( _position < text.Length )
			{
				if ( char.IsWhiteSpace( text[_position] ) ) { _position++; continue; }
				if ( text[_position] != '/' || _position + 1 >= text.Length ) break;
				if ( text[_position + 1] == '/' ) { while ( _position < text.Length && text[_position] is not ('\r' or '\n') ) _position++; }
				else if ( text[_position + 1] == '*' )
				{
					var end = text.IndexOf( "*/", _position + 2, StringComparison.Ordinal );
					if ( end < 0 ) throw new InvalidDataException( $"Unclosed AI comment: {path}" );
					_position = end + 2;
				}
				else break;
			}
			if ( _position >= text.Length ) return new( Kind.End, "" );
			var c = text[_position++];
			if ( c == '{' ) return new( Kind.Open, "{" );
			if ( c == '}' ) return new( Kind.Close, "}" );
			if ( c is '"' or '[' )
			{
				var end = text.IndexOf( c == '[' ? ']' : '"', _position );
				if ( end < 0 ) throw new InvalidDataException( $"Unclosed AI token: {path}" );
				var value = text[_position..end]; _position = end + 1;
				return new( c == '[' ? Kind.Condition : Kind.Word, value );
			}
			var start = _position - 1;
			while ( _position < text.Length && !char.IsWhiteSpace( text[_position] ) && text[_position] is not ('{' or '}' or '[' or '"') ) _position++;
			return new( Kind.Word, text[start.._position] );
		}
	}
}
