using ActualLab.Plugins;

namespace ActualLab.Tests.Plugins;

public interface ITestPlugin
{
    public string GetName();
}

public interface ITestSingletonPlugin : ITestPlugin, ISingletonPlugin;

public interface ITestPluginEx : ITestPlugin
{
    public string GetVersion();
}

[Plugin]
public abstract class TestPlugin : ITestPlugin
{
    public virtual string GetName() => GetType().Name;
}

[Plugin(IsEnabled = false)]
public class DisabledTestPlugin : TestPlugin, IHasDependencies, ITestSingletonPlugin
{
    public IEnumerable<Type> Dependencies { get; } = Type.EmptyTypes;

    [ServiceConstructor]
    public DisabledTestPlugin() { }
}

public class TestPlugin1 : TestPlugin, IHasDependencies, ITestSingletonPlugin
{
    public IEnumerable<Type> Dependencies { get; } = new [] { typeof(TestPlugin2) };

    [ServiceConstructor]
    public TestPlugin1() { }
}

public class TestPlugin2 : TestPlugin, ITestPluginEx, IHasCapabilities, ITestSingletonPlugin
{
    public virtual string GetVersion() => "1.0";
    public PropertyBag Capabilities { get; } = PropertyBag.Empty
        .Set("Client", true)
        .Set("Server", false);

    public TestPlugin2(IPluginInfoProvider.Query _) { }

    [ServiceConstructor]
    public TestPlugin2(IServiceProvider services)
    {
        services.Should().NotBeNull();
    }
}

[Plugin]
public class WrongPlugin : IHasDependencies
{
    public IEnumerable<Type> Dependencies { get; } = new [] { typeof(TestPlugin2) };

    [ServiceConstructor]
    public WrongPlugin() { }
}
