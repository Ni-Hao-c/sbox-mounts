/// <summary>Revision hints for files in an engine-provided filesystem.</summary>
static class Titanfall2SourceIdentity
{
	internal static string Stamp( string path ) => $"{Titanfall2Storage.Normalize( path )}:{Titanfall2Storage.Length( path )}:{Titanfall2Storage.Revision( path )}";
	internal static void RejectReparse( string path ) => Titanfall2Storage.Normalize( path );
}
