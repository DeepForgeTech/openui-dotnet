using System.ComponentModel;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace OpenUI.Lang.Core.Tests.Conformance;

/// <summary>Which check a fixture test method runs; it also decides which cases the method applies to.</summary>
public enum FixtureSuite
{
    /// <summary>Level 1, every case folder.</summary>
    Program,

    /// <summary>Level 2, the cases with an interactions.json.</summary>
    Interactions,

    /// <summary>Level 1 streaming equals parsing, for every program case; only validation cases have no program.</summary>
    CharByChar,
}

/// <summary>
/// Marks a method taking a case id (<c>&lt;area&gt;/&lt;case&gt;</c>) that runs once per matching fixture
/// folder. A plain Theory cannot give each row its own display name and traits in xunit 2.
/// </summary>
[XunitTestCaseDiscoverer("OpenUI.Lang.Core.Tests.Conformance.FixtureTestDiscoverer", "OpenUI.Lang.Core.Tests")]
[AttributeUsage(AttributeTargets.Method)]
public sealed class FixtureTestAttribute(FixtureSuite suite) : FactAttribute
{
    public FixtureSuite Suite { get; } = suite;
}

public sealed class FixtureTestDiscoverer(IMessageSink diagnosticMessageSink) : IXunitTestCaseDiscoverer
{
    public IEnumerable<IXunitTestCase> Discover(
        ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo factAttribute)
    {
        var suite = FixtureTestCase.SuiteOf(factAttribute);
        return CasesOf(suite)
            .Select(fixture => new FixtureTestCase(
                diagnosticMessageSink,
                discoveryOptions.MethodDisplayOrDefault(),
                discoveryOptions.MethodDisplayOptionsOrDefault(),
                testMethod,
                [fixture.Id]));
    }

    /// <summary>The cases a suite runs. The discovery tests check this list against the source tree.</summary>
    internal static IEnumerable<FixtureCase> CasesOf(FixtureSuite suite) =>
        FixtureCatalog.Cases.Where(fixture => Applies(suite, fixture));

    private static bool Applies(FixtureSuite suite, FixtureCase fixture) => suite switch
    {
        FixtureSuite.Program => true,
        FixtureSuite.Interactions => fixture.HasInteractions,
        FixtureSuite.CharByChar => !fixture.IsValidation,
        _ => throw new ArgumentOutOfRangeException(nameof(suite), suite, null),
    };
}

/// <summary>A test case shown as its fixture id, carrying Area and Level traits.</summary>
public sealed class FixtureTestCase : XunitTestCase
{
    // Required by xunit to rebuild the case when it crosses a process boundary.
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
    public FixtureTestCase()
    {
    }

    public FixtureTestCase(
        IMessageSink diagnosticMessageSink,
        TestMethodDisplay defaultMethodDisplay,
        TestMethodDisplayOptions defaultMethodDisplayOptions,
        ITestMethod testMethod,
        object[] testMethodArguments)
        : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod, testMethodArguments)
    {
    }

    internal static FixtureSuite SuiteOf(IAttributeInfo factAttribute) =>
        (FixtureSuite)Enum.ToObject(typeof(FixtureSuite), factAttribute.GetConstructorArguments().Single()!);

    protected override void Initialize()
    {
        base.Initialize();

        var id = (string)TestMethodArguments[0];
        var suite = SuiteOf(TestMethod.Method.GetCustomAttributes(typeof(FixtureTestAttribute)).Single());

        // Level 2 and the character-by-character run are extra checks on a case that already has
        // a Level 1 test, so they get a suffix to keep every display name unique.
        DisplayName = suite switch
        {
            FixtureSuite.Interactions => $"{id}#interactions",
            FixtureSuite.CharByChar => $"{id}#char-by-char",
            _ => id,
        };
        Traits["Area"] = [id[..id.IndexOf('/')]];
        Traits["Level"] = [suite == FixtureSuite.Interactions ? "2" : "1"];
    }
}
