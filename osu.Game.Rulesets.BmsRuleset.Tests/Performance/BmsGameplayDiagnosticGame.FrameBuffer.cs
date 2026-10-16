#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Performance;

internal partial class BmsGameplayDiagnosticGame
{
    // Recording millions of unlimited-FPS frames must not trigger managed array
    // growth and collections in the gameplay being measured.
    internal sealed class FrameBuffer : IReadOnlyCollection<Frame>, IDisposable
    {
        private const int chunk_size = 16384;
        private readonly List<nint> chunks = new(128);
        private bool disposed;

        public int Count { get; private set; }

        public unsafe void Add(Frame frame)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var offset = Count % chunk_size;
            if (offset == 0)
                chunks.Add((nint)NativeMemory.Alloc((nuint)chunk_size, (nuint)sizeof(Frame)));
            ((Frame*)chunks[^1])[offset] = frame;
            Count++;
        }

        public IEnumerator<Frame> GetEnumerator()
        {
            for (var i = 0; i < Count; i++)
                yield return read(i);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private unsafe Frame read(int index)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return ((Frame*)chunks[index / chunk_size])[index % chunk_size];
        }

        public unsafe void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var chunk in chunks) NativeMemory.Free((void*)chunk);
            chunks.Clear();
            Count = 0;
        }
    }
}
