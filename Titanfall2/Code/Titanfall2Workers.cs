/// <summary>CPU work scheduled by the engine, with a completion signal safe to join during synchronous imports.</summary>
static class Titanfall2Workers
{
	internal static Task Run( Action work ) => Run( () => { work(); return true; } );
	internal static Task<T> Run<T>( Func<T> work )
	{
		var completion = new TaskCompletionSource<T>();
		_ = Schedule();
		return completion.Task;

		async Task Schedule()
		{
			try
			{
				await GameTask.RunInThreadAsync( () =>
				{
					try { completion.TrySetResult( work() ); }
					catch ( Exception error ) { completion.TrySetException( error ); }
				} );
			}
			catch ( Exception error ) { completion.TrySetException( error ); }
		}
	}
}
