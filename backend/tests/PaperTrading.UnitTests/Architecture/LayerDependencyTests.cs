using System.Reflection;
using PaperTrading.Application.Trading;
using PaperTrading.Domain;
using PaperTrading.Domain.Matching;
using PaperTrading.Infrastructure.Instruments;

namespace PaperTrading.UnitTests.Architecture;

/// <summary>
/// Clean Architecture rules, enforced: dependencies point inwards (Api -> Infrastructure -> Application -> Domain)
/// and the domain stays free of frameworks. A violation fails the build instead of eroding quietly.
/// </summary>
public class LayerDependencyTests
{
    private static readonly Assembly Domain = typeof(Order).Assembly;
    private static readonly Assembly Application = typeof(ITradingService).Assembly;
    private static readonly Assembly Infrastructure = typeof(StaticInstrumentCatalog).Assembly;
    private static readonly Assembly Api = typeof(Program).Assembly;

    private static IEnumerable<string> References(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!);

    [Fact]
    public void Domain_depends_only_on_the_base_class_library()
    {
        var offending = References(Domain).Where(name =>
            name.StartsWith("PaperTrading", StringComparison.Ordinal) ||
            name.StartsWith("Microsoft", StringComparison.Ordinal) ||
            name.StartsWith("System.Text.Json", StringComparison.Ordinal) ||
            name.StartsWith("System.Net", StringComparison.Ordinal));

        Assert.Empty(offending);
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_web()
    {
        var offending = References(Application).Where(name =>
            name is "PaperTrading.Infrastructure" or "PaperTrading.Api" ||
            name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));

        Assert.Empty(offending);
        Assert.Contains("PaperTrading.Domain", References(Application));
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_the_web_layer()
    {
        var offending = References(Infrastructure).Where(name =>
            name == "PaperTrading.Api" || name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));

        Assert.Empty(offending);
    }

    [Fact]
    public void Api_reaches_the_engine_only_through_the_trading_service()
    {
        Type[] forbidden = [typeof(MatchingEngine), typeof(Portfolio), typeof(Order)];

        var violations =
            from type in Api.GetTypes()
            from member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            from used in UsedTypes(member)
            where forbidden.Contains(used)
            select $"{type.Name}.{member.Name} uses {used.Name}";

        Assert.Empty(violations);
    }

    [Fact]
    public void Engine_sequencer_is_an_application_implementation_detail()
    {
        Assert.False(Application.GetTypes().Single(t => t.Name == "EngineSequencer").IsPublic);
        Assert.False(Application.GetTypes().Single(t => t.Name == "TradingService").IsPublic);
    }

    private static IEnumerable<Type> UsedTypes(MemberInfo member) => member switch
    {
        FieldInfo f => [f.FieldType],
        PropertyInfo p => [p.PropertyType],
        MethodBase m => m.GetParameters().Select(p => p.ParameterType)
            .Concat(m is MethodInfo mi ? [mi.ReturnType] : []),
        _ => [],
    };
}
