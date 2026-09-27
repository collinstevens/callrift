using Xunit;

namespace Callrift.RealWorldCases;

public sealed class SerilogRealWorldCaseTests(RealWorldCaseFixture fixture) : IClassFixture<RealWorldCaseFixture>
{
    public static IEnumerable<object[]> Entries => RealWorldCaseData.Entries("serilog");
    public static IEnumerable<object[]> Views => RealWorldCaseData.Views("serilog");

    [Theory]
    [MemberData(nameof(Entries))]
    public Task PinnedHistory(string id) => fixture.VerifyHistoryAsync(id);

    [Theory]
    [MemberData(nameof(Views))]
    public Task ReviewedView(string id, string viewId) => fixture.VerifyViewAsync(id, viewId);
}

public sealed class CleanArchitectureRealWorldCaseTests(RealWorldCaseFixture fixture) : IClassFixture<RealWorldCaseFixture>
{
    public static IEnumerable<object[]> Entries => RealWorldCaseData.Entries("cleanarchitecture");
    public static IEnumerable<object[]> Views => RealWorldCaseData.Views("cleanarchitecture");

    [Theory]
    [MemberData(nameof(Entries))]
    public Task PinnedHistory(string id) => fixture.VerifyHistoryAsync(id);

    [Theory]
    [MemberData(nameof(Views))]
    public Task ReviewedView(string id, string viewId) => fixture.VerifyViewAsync(id, viewId);
}

public sealed class AutofacRealWorldCaseTests(RealWorldCaseFixture fixture) : IClassFixture<RealWorldCaseFixture>
{
    public static IEnumerable<object[]> Views => RealWorldCaseData.Views("autofac");

    [Theory]
    [MemberData(nameof(Views))]
    public Task ReviewedView(string id, string viewId) => fixture.VerifyViewAsync(id, viewId);
}

public sealed class PollyRealWorldCaseTests(RealWorldCaseFixture fixture) : IClassFixture<RealWorldCaseFixture>
{
    public static IEnumerable<object[]> Views => RealWorldCaseData.Views("polly");

    [Theory]
    [MemberData(nameof(Views))]
    public Task ReviewedView(string id, string viewId) => fixture.VerifyViewAsync(id, viewId);
}

public sealed class OcelotRealWorldCaseTests(RealWorldCaseFixture fixture) : IClassFixture<RealWorldCaseFixture>
{
    public static IEnumerable<object[]> Views => RealWorldCaseData.Views("ocelot");

    [Theory]
    [MemberData(nameof(Views))]
    public Task ReviewedView(string id, string viewId) => fixture.VerifyViewAsync(id, viewId);
}

public sealed class OrchardCoreRealWorldCaseTests(RealWorldCaseFixture fixture) : IClassFixture<RealWorldCaseFixture>
{
    public static IEnumerable<object[]> Views => RealWorldCaseData.Views("orchardcore");

    [Theory]
    [MemberData(nameof(Views))]
    public Task ReviewedView(string id, string viewId) => fixture.VerifyViewAsync(id, viewId);
}

public sealed class AspNetCoreRealWorldCaseTests(RealWorldCaseFixture fixture) : IClassFixture<RealWorldCaseFixture>
{
    public static IEnumerable<object[]> Views => RealWorldCaseData.Views("aspnetcore");

    [Theory]
    [MemberData(nameof(Views))]
    public Task ReviewedView(string id, string viewId) => fixture.VerifyViewAsync(id, viewId);
}
