using System.Reflection;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.SharedKernel;
using NetArchTest.Rules;

namespace MonitorAgent.Tests.Architecture;

/// <summary>
/// Layering rules from ADR-001. Module rules apply to every assembly named MonitorAgent.Modules.*, so each new
/// module is covered as soon as the host references it.
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly SharedKernel = typeof(Entity).Assembly;
    private static readonly Assembly BuildingBlocks = typeof(IDispatcher).Assembly;
    private static readonly Assembly Host = typeof(Program).Assembly;

    private static IReadOnlyList<Assembly> Modules => Host.GetReferencedAssemblies()
        .Where(a => a.Name!.StartsWith("MonitorAgent.Modules.", StringComparison.Ordinal))
        .Select(Assembly.Load)
        .ToList();

    private static void AssertPasses(TestResult result) =>
        Assert.True(result.IsSuccessful, "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));

    [Fact]
    public void SharedKernel_depends_on_nothing_else_in_the_solution() =>
        AssertPasses(Types.InAssembly(SharedKernel).ShouldNot()
            .HaveDependencyOnAny("MonitorAgent.BuildingBlocks", "MonitorAgent.Modules", "MonitorAgent.Api", "Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore")
            .GetResult());

    [Fact]
    public void BuildingBlocks_do_not_depend_on_modules_or_the_host() =>
        AssertPasses(Types.InAssembly(BuildingBlocks).ShouldNot()
            .HaveDependencyOnAny("MonitorAgent.Modules", "MonitorAgent.Api", "Microsoft.AspNetCore")
            .GetResult());

    [Fact]
    public void Modules_do_not_depend_on_the_host()
    {
        foreach (var module in Modules)
            AssertPasses(Types.InAssembly(module).ShouldNot().HaveDependencyOn("MonitorAgent.Api").GetResult());
    }

    [Fact]
    public void Modules_only_reach_each_other_through_their_contracts()
    {
        foreach (var module in Modules)
        {
            var own = string.Join('.', module.GetName().Name!.Split('.').Take(3));
            var others = Modules.Select(m => string.Join('.', m.GetName().Name!.Split('.').Take(3)))
                .Where(name => name != own).Distinct()
                .SelectMany(name => new[] { name + ".Domain", name + ".Application", name + ".Infrastructure" })
                .ToArray();
            if (others.Length == 0) continue;
            AssertPasses(Types.InAssembly(module).ShouldNot().HaveDependencyOnAny(others).GetResult());
        }
    }

    [Fact]
    public void Handlers_are_internal_so_callers_go_through_the_dispatcher()
    {
        foreach (var module in Modules)
        {
            var publicHandlers = module.GetTypes()
                .Where(t => t.IsPublic && t.GetInterfaces().Any(i => i.IsGenericType && DependencyInjection.IsHandlerInterface(i.GetGenericTypeDefinition())))
                .Select(t => t.FullName);
            Assert.Empty(publicHandlers);
        }
    }

    [Fact]
    public void Every_command_and_query_in_a_module_has_exactly_one_handler()
    {
        foreach (var module in Modules)
        {
            var handled = DependencyInjection.FindHandlers(module).Select(h => h.Service.GenericTypeArguments[0]).ToList();
            var requests = module.GetTypes().Where(t => !t.IsAbstract && t.GetInterfaces().Any(i => i.IsGenericType &&
                (i.GetGenericTypeDefinition() == typeof(ICommand<>) || i.GetGenericTypeDefinition() == typeof(IQuery<>))));
            foreach (var request in requests)
                Assert.Equal(1, handled.Count(h => h == request));
        }
    }
}
