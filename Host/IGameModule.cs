namespace PocketStation.Host;

public interface IGameModule : IDisposable
{
    string Name { get; }
    void Initialize();
}
