namespace PocketStation.Core;

public interface IGameModule : IDisposable
{
    string Name { get; }
    void Initialize();
}
