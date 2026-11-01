using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using OpenUI.Lang.Values;
using Xunit;

namespace OpenUI.Lang.Core.Tests.Documentation;

/// <summary>
/// Every public member's XML doc cites the spec. The compiler already fails on a missing doc (CS1591),
/// so this only reads the docs that exist. A positional record parameter is documented on its record, and
/// an override marked inheritdoc keeps the framework contract.
/// </summary>
public sealed partial class PublicDocsTests
{
    [Fact]
    public void Every_public_member_cites_a_spec_section()
    {
        var assembly = typeof(OuiValue).Assembly;
        var xmlPath = Path.ChangeExtension(assembly.Location, ".xml");
        var types = assembly.GetExportedTypes().ToDictionary(t => t.FullName!.Replace('+', '.'));

        var uncited = XDocument.Load(xmlPath).Descendants("member")
            .Where(member => IsPublic(member.Attribute("name")!.Value, types))
            .Where(member => member.Element("inheritdoc") is null && !Citation().IsMatch(member.ToString()))
            .Select(member => member.Attribute("name")!.Value)
            .ToList();

        Assert.True(uncited.Count == 0, $"{uncited.Count} uncited:\n{string.Join('\n', uncited)}");
    }

    private static bool IsPublic(string id, Dictionary<string, Type> types)
    {
        var name = id[2..].Split('(')[0];
        if (id[0] == 'T')
        {
            return types.ContainsKey(name);
        }

        var dot = name.LastIndexOf('.');
        if (!types.TryGetValue(name[..dot], out var type))
        {
            return false;
        }

        var member = name[(dot + 1)..].Split('~')[0].Split('`')[0];
        if (type.GetConstructors().Any(c => c.GetParameters().Any(p => p.Name == member)))
        {
            return false;
        }

        const BindingFlags Public = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        return member == "#ctor" ? type.GetConstructors().Length > 0 : type.GetMember(member, Public).Length > 0;
    }

    [GeneratedRegex(@"(language|prompt|overview)\.md|(?<!architecture )\bsections? \d")]
    private static partial Regex Citation();
}
