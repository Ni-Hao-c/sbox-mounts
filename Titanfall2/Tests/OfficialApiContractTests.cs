using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;
[TestClass]
public class OfficialApiContractTests
{
	[TestMethod]
	public void NativeBatchBackendCannotEnableInternalGpuCommands() => Assert.IsFalse( Titanfall2StreamingSettings.GpuStaticScene );
	[TestMethod]
	public void SkyboxUsesPublicComponentAndSceneLoaderUnloadHook()
	{
		Assert.IsTrue( typeof( Skybox3D ).IsPublic );
		Assert.IsNotNull( typeof( Skybox3D ).GetProperty( nameof( Skybox3D.SkyboxScene ) ) );
		var method = typeof( MapLoader ).GetMethod( "OnSceneUnloaded", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic );
		Assert.AreEqual( typeof( MapLoader ), method?.DeclaringType );
	}
}
