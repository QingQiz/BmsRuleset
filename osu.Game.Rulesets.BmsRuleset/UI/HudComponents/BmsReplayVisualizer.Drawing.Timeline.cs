using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Rendering.Vertices;
using osu.Framework.Graphics.Shaders;
using osu.Framework.Graphics.Shaders.Types;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

public sealed partial class BmsReplayVisualizer
{
    internal sealed partial class PlotLayer
    {
        internal TimelineView? Timeline;
        private IShader timelineShader = null!;

        private sealed partial class PlotShaderManager
        {
            private static readonly byte[] timeline_vertex =
                """
                    #include "sh_Utils.h"
                    layout(std140, set = 0, binding = 0) uniform m_TimelineParameters
                    {
                        highp vec4 u_Time;
                        highp vec4 u_Size;
                    };
                    layout(location = 0) in highp vec2 m_Position;
                    layout(location = 1) in lowp vec4 m_Colour;
                    layout(location = 2) in highp vec2 m_TexCoord;
                    layout(location = 3) in highp vec4 m_TexRect;
                    layout(location = 4) in mediump vec2 m_BlendRange;
                    layout(location = 5) in highp float m_BackbufferDrawDepth;
                    layout(location = 0) out highp vec2 v_MaskingPosition;
                    layout(location = 1) out lowp vec4 v_Colour;
                    layout(location = 2) out highp vec2 v_TexCoord;
                    layout(location = 3) out highp vec4 v_TexRect;
                    layout(location = 4) out mediump vec2 v_BlendRange;

                    void main(void)
                    {
                        highp float kind = m_BlendRange.x;
                        highp float top = m_TexRect.z;
                        highp float bottom = m_TexRect.w;
                        bool visible = true;
                        if (kind > 0.0)
                        {
                            top += u_Time.w + ((u_Time.x - m_TexRect.x) * 1000.0 + u_Time.y) * u_Time.z;
                            bottom += u_Time.w + ((u_Time.x - m_TexRect.y) * 1000.0 + u_Time.y) * u_Time.z;
                            if (kind <= 2.0)
                            {
                                visible = bottom >= 0.0 && top <= u_Size.y;
                                bottom = max(bottom, top + 5.0);
                            }
                            else if (kind <= 4.0)
                            {
                                visible = bottom >= u_Time.w + 1.5 && (kind == 4.0 || top + 1.5 <= u_Size.y);
                                top = kind == 4.0 ? u_Time.w - 1.5 : max(top, u_Time.w - 1.5);
                                bottom = max(bottom, top + 3.0);
                            }
                        }

                        highp float clippedTop = max(0.0, top);
                        highp float clippedBottom = min(u_Size.y, bottom);
                        visible = visible && clippedBottom > clippedTop;
                        highp float height = max(0.0, clippedBottom - clippedTop);
                        highp float y = mix(clippedTop, clippedBottom, m_TexCoord.y);
                        highp vec2 position = visible ? vec2(m_Position.x, y) : vec2(-1.0);
                        highp vec3 maskingPos = g_ToMaskingSpace * vec3(position, 1.0);
                        v_MaskingPosition = maskingPos.xy / maskingPos.z;
                        v_Colour = m_Colour;
                        highp float border = kind == 1.0 || kind == 2.0 ? min(1.0, min(m_Position.y, height) / 2.0) : 0.0;
                        v_TexRect = vec4(border, kind >= 5.0 ? 1.0 : 0.0, m_Position.y, height);
                        v_TexCoord = vec2(m_TexCoord.x * m_Position.y,
                            kind == 6.0 ? bottom - y : kind == 5.0 ? y - top : m_TexCoord.y * height);
                        v_BlendRange = vec2(0.0);
                        gl_Position = g_ProjMatrix * vec4(position, 1.0, 1.0);
                        if (g_BackbufferDraw)
                            gl_Position.z = m_BackbufferDrawDepth;
                    }
                    """u8.ToArray();

            private static readonly byte[] timeline_fragment =
                """
                    #define HIGH_PRECISION_VERTEX
                    #include "sh_Utils.h"
                    #include "sh_Masking.h"
                    layout(location = 2) in highp vec2 v_TexCoord;
                    layout(location = 0) out vec4 o_Colour;
                    void main(void)
                    {
                        if (v_TexRect.y > 0.0 && mod(v_TexCoord.y, 7.0) >= 4.0)
                            discard;
                        highp float border = v_TexRect.x;
                        bool interior = border > 0.0
                            && all(greaterThanEqual(v_TexCoord, vec2(border)))
                            && all(lessThan(v_TexCoord, v_TexRect.zw - vec2(border)));
                        o_Colour = getRoundedColor(vec4(1.0, 1.0, 1.0, interior ? 0.08 : 1.0), vec2(0.0));
                    }
                    """u8.ToArray();
        }

        internal sealed partial class PlotDrawNode
        {
            internal readonly record struct TimelineSnapshot(float X, float Width, Vector4 Endpoints, TimelineKind Kind, SRGBColour Colour);

            private sealed class BlockSnapshot
            {
                internal TimelineBlock Source = null!;
                internal long Version = -1;
                internal long Seen;
                internal readonly List<TimelineSnapshot> Rectangles = [];
                internal SRGBColour ParentColour;
            }

            private readonly Dictionary<TimelineBlock, BlockSnapshot> timelineCache = [];
            private readonly Stack<BlockSnapshot> unusedBlocks = [];
            private List<BlockSnapshot> timelineBlocks = [];
            private List<BlockSnapshot> previousBlocks = [];
            private long timelineGeneration;
            private bool hasTimeline;
            private IShader timelineShader = null!;
            private IUniformBuffer<TimelineParameters>? timelineParameters;
            private TimelineParameters timelineState;

            internal long TimelineCopiedRectangles { get; private set; }

            private int timelineQuadCount;

            internal int QuadCount => hasTimeline ? timelineQuadCount : Rectangles.Count;

            internal IReadOnlyList<TimelineSnapshot> GetTimelineBlock(int index) => timelineBlocks[index].Rectangles;

            private void applyTimeline(PlotLayer layer)
            {
                hasTimeline = layer.Timeline != null;
                timelineQuadCount = 0;
                timelineGeneration++;
                (timelineBlocks, previousBlocks) = (previousBlocks, timelineBlocks);
                timelineBlocks.Clear();
                if (layer.Timeline is { } view)
                {
                    timelineShader = layer.timelineShader;
                    var seconds = Math.Floor(view.Time / 1000);
                    var scale = view.Size.Y / (view.Range * 1000);
                    timelineState = new TimelineParameters
                    {
                        Time = new Vector4((float)seconds, (float)(view.Time - seconds * 1000), scale, view.Size.Y * judgement_line),
                        Size = new Vector4(view.Size.X, view.Size.Y, 0, 0),
                    };
                    var parent = DrawColourInfo.Colour.TopLeft;
                    foreach (var block in view.Blocks)
                    {
                        if (!timelineCache.TryGetValue(block, out var cached))
                        {
                            cached = unusedBlocks.TryPop(out var reused) ? reused : new BlockSnapshot();
                            cached.Source = block;
                            cached.Version = -1;
                            timelineCache.Add(block, cached);
                        }

                        cached.Seen = timelineGeneration;
                        if (cached.Version != block.Version || !cached.ParentColour.Equals(parent))
                        {
                            cached.Version = block.Version;
                            cached.ParentColour = parent;
                            cached.Rectangles.Clear();
                            cached.Rectangles.EnsureCapacity(block.Rectangles.Count);
                            foreach (var rectangle in block.Rectangles)
                            {
                                // Split seconds and milliseconds before converting to float;
                                // long charts must retain sub-millisecond scrolling precision.
                                var topSeconds = Math.Floor(rectangle.TopTime / 1000);
                                var bottomSeconds = Math.Floor(rectangle.BottomTime / 1000);
                                var fixedRectangle = rectangle.Kind == TimelineKind.Fixed;
                                var endpoints = new Vector4((float)topSeconds, (float)bottomSeconds,
                                    rectangle.TopOffset - (fixedRectangle ? 0 : (float)(rectangle.TopTime - topSeconds * 1000) * scale),
                                    rectangle.BottomOffset - (fixedRectangle ? 0 : (float)(rectangle.BottomTime - bottomSeconds * 1000) * scale));
                                cached.Rectangles.Add(new TimelineSnapshot(rectangle.X, rectangle.Width, endpoints, rectangle.Kind, parent * (SRGBColour)rectangle.Colour));
                            }

                            TimelineCopiedRectangles += block.Rectangles.Count;
                        }

                        timelineBlocks.Add(cached);
                        timelineQuadCount += cached.Rectangles.Count;
                    }
                }

                // Draw snapshots own their buffers. Recycle only blocks absent from this
                // snapshot; another buffered draw node can still be rendering its own copy.
                foreach (var block in previousBlocks)
                {
                    if (block.Seen == timelineGeneration)
                        continue;

                    timelineCache.Remove(block.Source);
                    unusedBlocks.Push(block);
                }

                previousBlocks.Clear();
            }

            private void drawTimeline(IRenderer renderer)
            {
                if (QuadCount == 0)
                    return;

                quadBatch ??= renderer.CreateQuadBatch<TexturedVertex2D>(2048, 2);
                timelineParameters ??= renderer.CreateUniformBuffer<TimelineParameters>();
                timelineParameters.Data = timelineState;
                renderer.PushLocalMatrix(DrawInfo.Matrix);
                timelineShader.Bind();
                timelineShader.BindUniformBlock("m_TimelineParameters", timelineParameters);
                AddTimelineVertices(renderer, quadBatch.AddAction);
                timelineShader.Unbind();
                renderer.PopLocalMatrix();
            }

            internal void AddTimelineVertices(IRenderer renderer, Action<TexturedVertex2D> addVertex)
            {
                var vertex = new TexturedVertex2D(renderer);
                foreach (var block in timelineBlocks)
                {
                    foreach (ref readonly var rectangle in CollectionsMarshal.AsSpan(block.Rectangles))
                    {
                        vertex.Colour = rectangle.Colour.SRGB;
                        vertex.TextureRect = rectangle.Endpoints;
                        vertex.BlendRange = new Vector2((float)rectangle.Kind, 0);
                        vertex.Position = new Vector2(rectangle.X, rectangle.Width);
                        vertex.TexturePosition = Vector2.UnitY;
                        addVertex(vertex);
                        vertex.Position.X += rectangle.Width;
                        vertex.TexturePosition = Vector2.One;
                        addVertex(vertex);
                        vertex.TexturePosition = Vector2.UnitX;
                        addVertex(vertex);
                        vertex.Position.X = rectangle.X;
                        vertex.TexturePosition = Vector2.Zero;
                        addVertex(vertex);
                    }
                }
            }

            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            private record struct TimelineParameters
            {
                public UniformVector4 Time;
                public UniformVector4 Size;
            }
        }
    }
}
