using OpenUI.Lang.Core.Tests.Conformance;
using OpenUI.Lang.Editing;
using OpenUI.Lang.Library;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests.Editing;

public partial class ReviewedEditingTests
{
    private static OpenUILibrary Library { get; } = ConformanceAdapter.CreateLibrary(FixtureCatalog.LoadLibrary());

    private static IEnumerable<string> Names(string program) => new OpenUIParser(Library).Parse(program).Statements.Select(s => s.Name);

    private static string TextsOf(OuiComponent stack)
    {
        Assert.True(stack["children"].Value.TryGetArray(out var children));
        return string.Join(",", children.Select(child =>
        {
            Assert.True(child.TryGetComponent(out var text));
            return text.Type == "Stack" ? "[" + TextsOf(text) + "]" : OpenUI.Lang.Values.Ecma.ToString(text["text"].Value);
        }));
    }
}
