using WarMusic.Services;

namespace WarMusic.UnitTests;

internal sealed class TestWorkspace : IDisposable
{
    public TestWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "WarMusic.UnitTests", Guid.NewGuid().ToString("N"));
        Store.Initialize(Root);
    }

    public string Root { get; }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    public static float[] Filled(float value) => Enumerable.Repeat(value, 960).ToArray();
}
