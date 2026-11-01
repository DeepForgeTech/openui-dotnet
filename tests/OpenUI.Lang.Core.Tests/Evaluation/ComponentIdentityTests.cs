using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests.Evaluation;

public class ComponentIdentityTests
{
    private static readonly OpenUI.Lang.Library.OpenUILibrary Library = Conformance.ConformanceAdapter.CreateLibrary(Conformance.FixtureCatalog.LoadLibrary());

    private static OuiComponent Root(string program, params (string Name, OuiValue Value)[] state)
    {
        var document = new OpenUIParser(Library).Parse(program);
        var inputs = new EvaluationInputs(state.ToDictionary(pair => pair.Name, pair => pair.Value));
        return OpenUIEvaluator.Evaluate(document, inputs).Root!;
    }

    private static List<OuiComponent> Children(OuiComponent parent)
    {
        Assert.True(parent["children"].Value.TryGetArray(out var items));
        return [.. items.Select(item => item.TryGetComponent(out var component) ? component : null).OfType<OuiComponent>()];
    }

    [Fact]
    public void A_component_id_is_the_path_to_it_inside_its_statement()
    {
        var root = Root("root = Stack([Text(\"a\"), Stack([Text(\"b\")])])\n");

        Assert.Equal("", root.Id.Value);
        Assert.Equal(["children/0", "children/1"], Children(root).Select(child => child.Id.Value));
        Assert.Equal("children/1/children/0", Children(Children(root)[1])[0].Id.Value);
    }

    [Fact]
    public void Adding_content_after_a_component_does_not_change_its_id_or_its_parents()
    {
        var one = Root("root = Stack([Stack([Text(\"a\")])])\n");
        var more = Root("root = Stack([Stack([Text(\"a\"), Text(\"b\")]), Text(\"c\")])\n");

        Assert.Equal(one.Id, more.Id);
        Assert.Equal(Children(one)[0].Id, Children(more)[0].Id);
        Assert.Equal(Children(Children(one)[0])[0].Id, Children(Children(more)[0])[0].Id);
    }

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 1)]
    public void A_conditional_changing_shape_does_not_change_the_id_or_key_of_what_follows(bool shown, int count)
    {
        var root = Root("$c = false\nroot = Stack([$c ? Text(\"x\") : null, Text(\"y\")])\n", ("$c", shown));

        var children = Children(root);
        Assert.Equal(count, children.Count);
        Assert.Equal("children/1", children[^1].Id.Value);
        Assert.Equal(new RenderKey("root", new NodeId("children/1"), 0), RenderKey.ForSiblings(children)[^1]);
    }

    [Fact]
    public void An_id_does_not_copy_the_statement_name()
    {
        var root = Root("root = Stack([card])\ncard = Stack([Text(\"a\")])\n");

        var card = Children(root)[0];
        Assert.Equal("", card.Id.Value);
        Assert.Equal("children/0", Children(card)[0].Id.Value);
        Assert.Equal("card", Children(card)[0].StatementId);
    }

    [Fact]
    public void Each_iteration_of_a_loop_has_its_own_id()
    {
        var root = Root("root = Stack(@Each([1, 2, 3], \"i\", Text(\"t\")))\n");

        Assert.Equal(["children/0", "children/1", "children/2"], Children(root).Select(child => child.Id.Value));
    }

    [Fact]
    public void Siblings_get_unique_keys_when_one_statement_is_referenced_twice()
    {
        var root = Root("root = Stack([a, a, b, Text(\"x\"), Text(\"y\"), a])\na = Text(\"shared\")\nb = Text(\"other\")\n");

        var children = Children(root);
        Assert.Same(children[0], children[1]);
        Assert.Equal(children[0].Id, children[2].Id);

        var keys = RenderKey.ForSiblings(children);
        Assert.Equal(keys.Length, keys.Distinct().Count());
        Assert.Equal([0, 1, 2], new[] { keys[0], keys[1], keys[5] }.Select(key => key.Repeat));
    }

    [Fact]
    public void Component_ids_stay_put_while_a_stream_adds_to_them()
    {
        var first = Children(Root("root = Stack([Text(\"a\"), "))[0];
        var second = Children(Root("root = Stack([Text(\"a\"), Text(\"b\"), Text(\"c\")])\n"))[0];
        Assert.Equal(first.Id, second.Id);
    }
}
