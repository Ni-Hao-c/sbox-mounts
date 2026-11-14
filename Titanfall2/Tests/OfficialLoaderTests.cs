using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Mounting;
[TestClass]
public class OfficialLoaderTests
{
	sealed class Loader : ResourceLoader<Titanfall2Mount>
	{
		public int Loads;
		public TaskCompletionSource<object> Pending;
		protected override Task<object> LoadAsync() { Loads++; return Pending?.Task ?? Task.FromResult<object>( new object() ); }
	}
	[TestMethod]
	public async Task OfficialLoaderSharesCachedResult()
	{
		var loader = new Loader(); var first = await loader.GetOrCreate();
		Assert.AreSame( first, await loader.GetOrCreate() ); Assert.AreEqual( 1, loader.Loads );
	}
	[TestMethod]
	public async Task ConcurrentRequestsShareInFlightLoad()
	{
		var loader = new Loader { Pending = new() };
		var first = loader.GetOrCreate(); var second = loader.GetOrCreate();
		Assert.AreEqual( 1, loader.Loads ); var value = new object(); loader.Pending.SetResult( value );
		Assert.AreSame( value, await first ); Assert.AreSame( value, await second );
		Assert.AreSame( value, await loader.GetOrCreate() );
	}
}
