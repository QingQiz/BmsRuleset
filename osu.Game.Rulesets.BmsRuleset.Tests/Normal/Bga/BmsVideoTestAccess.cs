#nullable enable

using System;
using System.Reflection;
using System.Threading.Tasks;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets.BmsRuleset.Media.Video.Supplemental;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Bga;

internal static class BmsVideoTestAccess
{
    private const BindingFlags instance_flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static bool IsWorkerCompleted(BmsSupplementalVideoFrameSource source) =>
        getOptionalFieldValue<Task>(source, "worker")?.IsCompleted ?? true;

    internal static int GetQueuedFrameCount(BmsSupplementalVideoFrameSource source)
    {
        var queue = getRequiredFieldValue<object>(source, "queuedFrames");
        return getRequiredPropertyValue<int>(queue, "Count");
    }

    internal static BmsSupplementalVideoFrameSource? GetFrameSource(BmsSupplementalVideoDrawable drawable) =>
        getOptionalFieldValue<BmsSupplementalVideoFrameSource>(drawable, "frameSource");

    internal static bool HasTexture(BmsSupplementalVideoDrawable drawable) =>
        getOptionalFieldValue<Texture>(drawable, "texture") != null;

    private static T getRequiredFieldValue<T>(object instance, string name)
    {
        var field = instance.GetType().GetField(name, instance_flags)
                    ?? throw new InvalidOperationException($"Required field '{instance.GetType().FullName}.{name}' was not found.");
        return castRequiredValue<T>(field.GetValue(instance), $"field '{field.DeclaringType?.FullName}.{name}'");
    }

    private static T? getOptionalFieldValue<T>(object instance, string name)
        where T : class
    {
        var field = instance.GetType().GetField(name, instance_flags)
                    ?? throw new InvalidOperationException($"Required field '{instance.GetType().FullName}.{name}' was not found.");

        if (field.GetValue(instance) is not { } value)
            return null;

        return value as T
               ?? throw new InvalidOperationException($"Required field '{field.DeclaringType?.FullName}.{name}' has value type '{value.GetType().FullName}', expected '{typeof(T).FullName}'.");
    }

    private static T getRequiredPropertyValue<T>(object instance, string name)
    {
        var property = instance.GetType().GetProperty(name, instance_flags)
                       ?? throw new InvalidOperationException($"Required property '{instance.GetType().FullName}.{name}' was not found.");
        return castRequiredValue<T>(property.GetValue(instance), $"property '{property.DeclaringType?.FullName}.{name}'");
    }

    private static T castRequiredValue<T>(object? value, string member)
    {
        if (value is T typed)
            return typed;

        throw new InvalidOperationException($"Required {member} has value type '{value?.GetType().FullName ?? "null"}', expected '{typeof(T).FullName}'.");
    }
}
