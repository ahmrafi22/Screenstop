using Screendrop.Core.Preview;
using Xunit;

namespace Screendrop.Core.Tests;

public class PreviewStackTests
{
    private static PreviewEntry Entry(string name) => new(
        $@"C:\tmp\{name}.png",
        null,
        "fullscreen",
        DateTimeOffset.Now);

    [Fact]
    public void Newest_item_is_inserted_at_front()
    {
        var stack = new PreviewStack(maxCount: 4);

        stack.Push(Entry("a"));
        stack.Push(Entry("b"));

        Assert.Equal(2, stack.Items.Count);
        Assert.EndsWith("b.png", stack.Items[0].ImagePath);
        Assert.Equal(stack.Items[0], stack.Newest);
    }

    [Fact]
    public void At_capacity_all_items_are_kept()
    {
        var stack = new PreviewStack(maxCount: 3);

        stack.Push(Entry("a"));
        stack.Push(Entry("b"));
        stack.Push(Entry("c"));

        Assert.Equal(3, stack.Items.Count);
    }

    [Fact]
    public void Beyond_capacity_evicts_oldest_keeping_newest()
    {
        var stack = new PreviewStack(maxCount: 3);

        stack.Push(Entry("a"));
        stack.Push(Entry("b"));
        stack.Push(Entry("c"));
        stack.Push(Entry("d"));

        Assert.Equal(3, stack.Items.Count);
        Assert.EndsWith("d.png", stack.Items[0].ImagePath);
        Assert.EndsWith("c.png", stack.Items[1].ImagePath);
        Assert.EndsWith("b.png", stack.Items[2].ImagePath);
        Assert.DoesNotContain(stack.Items, i => i.ImagePath.EndsWith("a.png"));
    }

    [Fact]
    public void Remove_drops_item_and_raises_changed()
    {
        var stack = new PreviewStack(maxCount: 4);
        var a = Entry("a");
        var b = Entry("b");
        stack.Push(a);
        stack.Push(b);

        int changes = 0;
        stack.Changed += () => changes++;

        Assert.True(stack.Remove(a));
        Assert.Equal(1, stack.Items.Count);
        Assert.Equal(1, changes);
        Assert.False(stack.Remove(a));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Clear_empties_stack_once()
    {
        var stack = new PreviewStack(maxCount: 4);
        stack.Push(Entry("a"));
        stack.Push(Entry("b"));

        int changes = 0;
        stack.Changed += () => changes++;

        stack.Clear();
        stack.Clear();

        Assert.Empty(stack.Items);
        Assert.Equal(1, changes);
    }
}
