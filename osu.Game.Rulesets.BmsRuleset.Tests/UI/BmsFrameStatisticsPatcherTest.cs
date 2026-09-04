#nullable enable

using System.Collections.Concurrent;
using System.Reflection;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

namespace osu.Game.Rulesets.BmsRuleset.Tests.UI;

[TestFixture]
public class BmsFrameStatisticsPatcherTest
{
    [Test]
    public void DequeueBudgetAllowsOneFramePerDisplayUpdate()
    {
        var patcherType = typeof(BmsFrameStatisticsPatcher);
        var prefix = patcherType.GetMethod("prefix", BindingFlags.Static | BindingFlags.NonPublic)!;
        var dequeue = patcherType.GetMethod("tryDequeueOnce", BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(typeof(object));
        var queue = new ConcurrentQueue<object>([new object(), new object(), new object()]);

        prefix.Invoke(null, null);

        Assert.Multiple(() =>
        {
            Assert.That(invokeDequeue(dequeue, queue), Is.True);
            Assert.That(invokeDequeue(dequeue, queue), Is.False);
            Assert.That(queue.Count, Is.EqualTo(2));
        });

        prefix.Invoke(null, null);

        Assert.Multiple(() =>
        {
            Assert.That(invokeDequeue(dequeue, queue), Is.True);
            Assert.That(queue.Count, Is.EqualTo(1));
        });
    }

    private static bool invokeDequeue(MethodInfo method, ConcurrentQueue<object> queue)
    {
        object?[] arguments = [queue, null];
        return (bool)method.Invoke(null, arguments)!;
    }
}
