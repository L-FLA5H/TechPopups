using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;

namespace Celeste.Mod.TechPopups;

public class TechPopupEntity : Entity
{
    public float Delay;
    public float Anim;
    public string TechName;
    private static TechPopupsSettings Settings => TechPopupsModule.Settings;

    private static float HoldTime => Settings.Timing.PopupHoldTime;
    private static float FadeOutTime => Settings.Timing.PopupFadeOutTime;
    private static float BaseAnimInTime => Settings.Timing.PopupFadeInTime;
    private static float BaselineSpeed => Settings.Timing.PopupBaselineSpeed;
    private static float SpeedScale => Settings.Timing.PopupSpeedScale;

    // Now tweakable from the out-of-game panel (Mod 设置 · 技巧框外观).
    private static float LabelOffsetY => Settings.Popup.LabelOffsetY;
    private const float LabelDropHeight = 30f;

    // Entrance: the pill starts at this fraction of its final size and is
    // driven to 1 with a slight overshoot. Kept subtle because the letters do
    // the loud popping (see the per-character constants below).
    private const float PopScaleStart = 0.86f;

    // Exit: the pill drifts upward and shrinks a touch while fading out,
    // instead of evaporating in place.
    private const float ExitRiseDistance = 12f;
    private const float ExitShrink = 0.06f;

    // ---- per-character stagger ------------------------------------------
    // StaggerFraction is how much of the whole entrance/exit is spent
    // spreading the letters out; the remainder is each letter's own duration.
    // Every letter fades in, pops from CharPopStart and settles down from
    // CharRise pixels above its slot.
    private const float StaggerFraction = 0.45f;
    private const float CharPopStart = 0.45f;
    private const float CharRise = 5f;
    private const float CharStroke = 1.2f;

    private const float BackEaseC = 1.70158f;

    private float animInTime;
    private float holdTimer;
    private float exitTimer;
    private float alpha;
    public float StackOffsetY;

    private readonly MTexture _icon;

    public TechPopupEntity(Vector2 worldPosition, string techName, float delay, float speed = 0f) : base(worldPosition)
    {
        TechName = techName;
        Delay = delay;
        Tag = Tags.HUD | Tags.Persistent;
        Depth = -9999;
        animInTime = Math.Max(
            BaseAnimInTime * MathF.Pow((BaselineSpeed / Math.Max(speed, BaselineSpeed)), SpeedScale),
            0.01f);
        _icon = TechIconLoader.TryGet(techName);
    }

    public override void Update()
    {
        base.Update();

        if (Delay > 0f)
        {
            Delay -= Engine.DeltaTime;
            return;
        }

        if (Anim < 1f)
        {
            Anim = Calc.Approach(Anim, 1f, Engine.DeltaTime / animInTime);
            alpha = Ease.SineOut(Anim);
            return;
        }

        holdTimer += Engine.DeltaTime;
        if (holdTimer < HoldTime)
        {
            return;
        }

        exitTimer += Engine.DeltaTime;
        alpha = 1f - Ease.CubeIn(ExitProgress);
        if (ExitProgress >= 1f)
        {
            RemoveSelf();
        }
    }

    private float ExitProgress
    {
        get
        {
            if (FadeOutTime <= 0f) return 1f;
            return Calc.Clamp(exitTimer / FadeOutTime, 0f, 1f);
        }
    }

    // How far along letter `index` is, given the overall 0..1 progress.
    // Letters are spread left-to-right over StaggerFraction of the timeline.
    private static float CharLocal(float overall, int index, int count)
    {
        if (count <= 1) return Calc.Clamp(overall, 0f, 1f);
        float step = StaggerFraction / (count - 1);
        float duration = 1f - StaggerFraction;
        if (duration <= 0f) return Calc.Clamp(overall - index * step, 0f, 1f);
        return Calc.Clamp((overall - index * step) / duration, 0f, 1f);
    }

    // ------------------------------------------------------------------
    // World -> HUD mapping
    //
    // This mirrors exactly what the engine itself does to draw the level:
    //   Camera.Matrix = Translate(floor(-Position)) * RotZ * Scale(Zoom)
    //                   * Translate(floor(Origin))
    // The world viewport is 320x180 while HUD entities draw in 1920x1080,
    // so the final scale factor is 6.
    //
    // IMPORTANT: nothing is clamped to the current room rectangle any more.
    // Level.Bounds IS the current room (it literally is
    // Session.LevelData.Bounds), so the moment a screen transition swaps it,
    // every popup from the previous room got squashed onto the shared edge.
    // Popups are world-anchored and simply ride the camera out instead, which
    // also means a label made at a room border keeps hanging over the border
    // and stays visible after you cross into the next room.
    // ------------------------------------------------------------------
    private static Vector2 WorldToHud(Level level, Vector2 world)
    {
        Camera camera = level.Camera;
        Vector2 relative = world - Calc.Floor(camera.Position);
        if (SaveData.Instance != null && SaveData.Instance.Assists.MirrorMode)
            relative.X = 320f - relative.X;

        float zoom = camera.Zoom;
        Vector2 view = new Vector2(relative.X * zoom, relative.Y * zoom) + Calc.Floor(camera.Origin);
        return view * 6f;
    }

    // Resting ("fully appeared") anchor: used for layout and stack placement.
    private Vector2 TargetHud(Level level)
    {
        return WorldToHud(level, Position) - new Vector2(0f, LabelOffsetY + StackOffsetY);
    }

    // Where the label is actually drawn this frame (drop-in / rise-out).
    private Vector2 AnchorHud(Level level)
    {
        Vector2 target = TargetHud(level);

        if (Anim < 1f)
        {
            Vector2 start = target - new Vector2(0f, LabelDropHeight * Settings.Popup.Scale);
            return Vector2.Lerp(start, target, Ease.CubeOut(Anim));
        }

        return target - new Vector2(0f, ExitRiseDistance * Settings.Popup.Scale * Ease.CubeOut(ExitProgress));
    }

    // Size multiplier for this frame: pop on entrance, gently shrink on exit.
    private float VisualScale()
    {
        float scale = Settings.Popup.Scale;

        if (Anim < 1f)
        {
            return scale * (PopScaleStart + (1f - PopScaleStart) * BackOut(Anim));
        }

        return scale * (1f - ExitShrink * Ease.CubeIn(ExitProgress));
    }

    private static float BackOut(float t)
    {
        float p = t - 1f;
        return 1f + (BackEaseC + 1f) * p * p * p + BackEaseC * p * p;
    }

    // Bounds in HUD space (1920x1080) of the resting pill. Used by the stacking
    // search, which runs before any entrance animation exists.
    public Rectangle GetScreenBounds(Level level)
    {
        return GetScreenBounds(TargetHud(level));
    }

    // Same thing anchored wherever the caller wants: Render passes the animated
    // position so the edge clamp follows the drop-in / rise-out motion instead
    // of snapping.
    public Rectangle GetScreenBounds(Vector2 anchor)
    {
        float scale = Settings.Popup.Scale;
        var atlasKey = "techpopups/" + TechName.ToLowerInvariant().Replace(" ", "_");
        if (GFX.Gui.Has(atlasKey))
        {
            var tex = GFX.Gui[atlasKey];
            return new Rectangle(
                (int)MathF.Floor(anchor.X - tex.Width * scale * 0.5f),
                (int)MathF.Floor(anchor.Y - tex.Height * scale),
                (int)MathF.Ceiling(tex.Width * scale),
                (int)MathF.Ceiling(tex.Height * scale)
            );
        }

        var textScale = Vector2.One * 0.55f * PopupFont.SizeCompensation * scale;
        var textSize = new Vector2(PopupFont.TextWidth(TechName) * textScale.X,
                                   PopupFont.Measure(TechName).Y * textScale.Y);
        float pad = 5f * scale;
        float iconW = 0f;
        var mode = Settings.Popup.DisplayMode;

        if (mode != PopupDisplayMode.TextOnly)
        {
            if (_icon != null)
            {
                float iconScl = textSize.Y / _icon.Height;
                iconW = _icon.Width * iconScl + pad;
            }
            else
            {
                iconW = textSize.Y + pad;
            }
        }

        if (mode == PopupDisplayMode.IconOnly)
        {
            // The pill collapses to just the icon slot (or the letter badge).
            float slot = iconW - pad;
            return new Rectangle(
                (int)MathF.Floor(anchor.X - slot * 0.5f - pad),
                (int)MathF.Floor(anchor.Y - slot - pad),
                (int)MathF.Ceiling(slot + pad * 2f),
                (int)MathF.Ceiling(slot + pad * 2f)
            );
        }

        return new Rectangle(
            (int)MathF.Floor(anchor.X - (textSize.X + iconW) * 0.5f - pad),
            (int)MathF.Floor(anchor.Y - textSize.Y - pad),
            (int)MathF.Ceiling(textSize.X + iconW + pad * 2f),
            (int)MathF.Ceiling(textSize.Y + pad * 2f)
        );
    }

    // Nudges the pill back inside the viewport, leaving Settings.Popup.EdgeMargin
    // of slack. Popups are world-anchored, so without this a trick performed
    // against a room border put the label half off screen - which is what the
    // Wallbounce against the left wall looked like.
    private static Vector2 ClampToView(Vector2 anchor, Rectangle bounds)
    {
        float margin = Settings.Popup.EdgeMargin;
        float maxX = Engine.ViewWidth - margin;
        float maxY = Engine.ViewHeight - margin;
        float dx = 0f, dy = 0f;

        // A pill wider than the screen cannot be made to fit, so pin it to the
        // leading edge instead of letting the two tests fight over it.
        if (bounds.Width >= Engine.ViewWidth - margin * 2f) dx = margin - bounds.Left;
        else if (bounds.Left < margin)                      dx = margin - bounds.Left;
        else if (bounds.Right > maxX)                       dx = maxX - bounds.Right;

        if (bounds.Height >= Engine.ViewHeight - margin * 2f) dy = margin - bounds.Top;
        else if (bounds.Top < margin)                         dy = margin - bounds.Top;
        else if (bounds.Bottom > maxY)                        dy = maxY - bounds.Bottom;

        return anchor + new Vector2(dx, dy);
    }

    // Edge-clamp offset: worked out on the first rendered frame and then kept
    // for the rest of the popup's life.
    //
    // Clamping every frame was wrong. Popups are world-anchored on purpose so
    // they ride the camera out of a room, but a per-frame clamp dragged every
    // surviving popup back against the screen border the moment the level
    // scrolled, so during a screen transition they piled up in a stack down one
    // edge. The clamp is meant to rescue a label *created* near a border, not to
    // pin one that is legitimately leaving.
    private Vector2 edgeOffset;
    private bool edgeOffsetDone;

    public override void Render()
    {
        base.Render();

        if (alpha <= 0f) return;

        var level = Scene as Level;
        if (level == null) return;

        // The pause menu, its Options submenu and Everest's mod list are all
        // drawn inside Level.Render as well, and this entity sits at
        // Depth = -9999 - lower than any of them - so it lands on top of the
        // menu. A transient "nice trick" label has no business covering the
        // pause UI, so skip the frame outright. Skipping before Want() also
        // means the blur passes do not run while the game is paused.
        if (level.Paused) return;

        var drawPos = AnchorHud(level);
        float scale = VisualScale();

        if (!edgeOffsetDone)
        {
            edgeOffsetDone = true;
            // Measured off the resting anchor with the resting scale, so the
            // slack is sized for the largest the pill ever gets and does not
            // drift with the entrance / exit animation.
            Vector2 resting = TargetHud(level);
            edgeOffset = ClampToView(resting, GetScreenBounds(resting)) - resting;
        }
        drawPos += edgeOffset;

        var atlasKey = "techpopups/" + TechName.ToLowerInvariant().Replace(" ", "_");
        if (GFX.Gui.Has(atlasKey))
        {
            DrawIconOnly(drawPos, atlasKey, scale);
        }
        else
        {
            DrawLabel(drawPos, scale);
        }
    }

    private void DrawIconOnly(Vector2 drawPos, string atlasKey, float scale)
    {
        var tex = GFX.Gui[atlasKey];
        tex.DrawJustified(drawPos, new Vector2(0.5f, 1f), Color.White * alpha, new Vector2(scale, scale));
    }

    // Rounded pill. Deliberately NOT using Draw.Circle: decompiling it shows
    // nothing but Draw.Line calls, i.e. it is an OUTLINE, not a filled disc -
    // the caps came out as rings.
    //
    // The caps are drawn as 1px vertical columns (one per pixel of radius), each
    // as tall as the circle's chord at that offset. Columns never overlap, so a
    // translucent fill colour is never drawn twice over the same pixel (that
    // used to make the middle of the pill darker than its ends).
    //
    // Not a const on purpose: a const would make the square fallback dead code
    // and trip CS0162. Flip this to false if you prefer plain rectangles.
    // Reused across frames: the two-layer frost lookup runs per label per
    // frame and must not allocate. See BackdropBlur.GetLayers.
    private static readonly BackdropBlur.Layer[] LayerBuf = new BackdropBlur.Layer[2];

    private static readonly bool RoundedPill = true;

    // Colour of the stand-in icon badge (see DrawInitialBadge).
    private static readonly Color BadgeFill = new Color(0.99f, 0.83f, 0.25f);

    // ---- backdrop -------------------------------------------------------
    //
    // The pill used to be flat opaque black, which reads as a hole punched into
    // the level. Glass mode layers instead:
    //
    //   drop shadow -> dark rim -> blurred level (or a translucent cool body
    //   when the blur is off) -> fine grain -> bright top edge, faint bottom edge
    //
    // It used to say "there is deliberately no real backdrop blur" here, on the
    // grounds that sampling the render target you are drawing into is undefined
    // behaviour and Celeste's own FxGaussianBlur belongs to the screen-wipe path.
    // Both of those are still true. What changed is that Level.Render blits a
    // finished copy of the level onto the backbuffer and only afterwards starts
    // drawing the HUD - so there is a brief window, just before HudRenderer
    // renders the Tags.HUD entities, where a clean level image exists and can be
    // grabbed from the CPU side. See BackdropBlur for the whole story.

    // How dark the glass goes at 背景透明度 = 0, as a plain black wash over the
    // frosted body. 0.35 was picked so that the finished pill lands on the same
    // brightness as the old "37% opaque" body (197 vs 198 on the test backdrop):
    // the frost is what changed, not how light or dark the label looks.

    // 0..100 -> 0..1 with clamping, for the panel's alpha sliders. The stored
    // value has already been range-checked by TechVisual, but a hand-edited
    // settings file should not be able to paint an all-black screen.
    private static float Pct(int v)
    {
        if (v < 0) v = 0;
        if (v > 100) v = 100;
        return v * 0.01f;
    }

    private static byte ByteC(int v)
    {
        if (v < 0) v = 0;
        if (v > 255) v = 255;
        return (byte)v;
    }

    // Panel-tunable colours. Both default to black, which is what the layers
    // used to be hard-coded with, so an untouched install looks the same.
    private static Color OutlineColor()
    {
        var p = Settings.Popup;
        return new Color(ByteC(p.PillOutlineR), ByteC(p.PillOutlineG), ByteC(p.PillOutlineB));
    }

    private static Color ShadowColor()
    {
        var p = Settings.Popup;
        return new Color(ByteC(p.PillShadowR), ByteC(p.PillShadowG), ByteC(p.PillShadowB));
    }

    private static Color BgColor()
    {
        var p = Settings.Popup;
        return new Color(ByteC(p.PillBgR), ByteC(p.PillBgG), ByteC(p.PillBgB));
    }

    private void DrawPillBackdrop(float x, float y, float w, float h)
    {
        var sp = Settings.Popup;

        // The pill is a translucent tinted plate - THAT is the glass: a colour
        // and an alpha, nothing more. Whether the level behind it is blurred
        // is a separate, experimental switch further down. The outline and the
        // shadow belong to the frame, not the plate: they draw from their own
        // settings no matter what the body is doing.
        float bodyA = sp.GlassBodyAlpha;
        if (bodyA < 0f) bodyA = 0f;

        // 1. shadow. Masked to the crescent that lies OUTSIDE the pill: the
        //    body is translucent, and a whole shadow behind it would shine
        //    through as a second, solid-dark pill - most obviously at
        //    transparency 100, where nothing sits between the shadow and the
        //    level at all.
        if (sp.PillShadow && sp.PillShadowAlphaPct > 0)
        {
            Color sc = ShadowColor() * alpha * Pct(sp.PillShadowAlphaPct);
            DrawShadowCapsule(x, y, w, h, sp.PillShadowX, sp.PillShadowY,
                              sp.PillShadowBlur, sc);
        }

        // 2. outline: the ring between the body capsule and one grown by the
        //    outline width. It has to be a HOLLOW ring, not a filled grown
        //    capsule - with a see-through body the latter showed its solid
        //    middle through the pill, so the whole plate read as filled even
        //    though the slider said transparent. Matches the panel preview,
        //    where a CSS box-shadow ring is by definition hollow.
        if (sp.PillOutline && sp.PillOutlineAlphaPct > 0 && sp.PillOutlineWidth > 0)
        {
            float ow = Math.Clamp(sp.PillOutlineWidth, 0, 8);
            DrawRingBand(x, y, w, h, 0f, 0f, ow, 0f,
                         OutlineColor() * alpha * Pct(sp.PillOutlineAlphaPct));
        }

        // 3. experimental frost: a real blurred copy of the level, drawn at
        //    FULL alpha - it is its own layer, not a share of the tint.
        //    (Scaling it by the transparency was tried: at transparency 100
        //    the frost vanished along with everything else, which made the
        //    whole 毛玻璃 switch look broken.) What 背景透明度 governs is the
        //    colour plate in step 4, which sits on top of this.
        //
        //    Want() doubles as "a label is on screen this frame": when no pill
        //    is built, captures stop entirely, so the feature still costs
        //    nothing while the label is off screen.
        if (sp.GlassBlur) BackdropBlur.Want(Scene, x, y, w, h);

        int layers = sp.GlassBlur ? BackdropBlur.GetLayers(Scene as Level, LayerBuf) : 0;
        if (layers > 0)
        {
            // Two layers once two grabs have landed: the older frost is drawn
            // at (1 - mix) and the newer at mix, so the backdrop keeps changing
            // between grabs instead of snapping. Each layer carries its own
            // sample offset, so they agree on WHERE things are.
            for (int i = 0; i < layers; i++)
            {
                BackdropBlur.Layer layer = LayerBuf[i];
                if (layer.Texture == null || layer.Alpha <= 0.004f) continue;
                DrawBlurredCapsule(x, y, w, h, alpha * layer.Alpha, layer.Texture, layer.Shift);
            }
        }

        // 4. the plate itself.
        if (bodyA > 0f)
            DrawCapsule(x, y, w, h, BgColor() * alpha * bodyA);
        if (bodyA <= 0f) return;

        // Everything below only makes sense inside the straight middle band; the
        // rounded caps are a couple of pixels wide, so skipping them is invisible.
        float r = Math.Min(h * 0.5f, w * 0.5f);
        float bandX = x + r;
        float bandW = w - r * 2f;
        if (bandW <= 1f || h <= 4f) return;

        // 5. fine grain, scaled with the plate so it vanishes with it.
        //    Positions come from a hash rather than Random so the pattern is
        //    identical every frame - otherwise the label sparkles.
        int gy0 = (int)MathF.Ceiling(y + 2f);
        int gy1 = (int)MathF.Floor(y + h - 2f);
        int gx0 = (int)MathF.Ceiling(bandX);
        int gx1 = (int)MathF.Floor(bandX + bandW);
        for (int gy = gy0; gy < gy1; gy += 3)
        {
            for (int gx = gx0; gx < gx1; gx += 3)
            {
                int hash = gx * 73856093 ^ gy * 19349663;
                if ((hash & 3) != 0) continue;
                Draw.Rect(gx + ((hash >> 3) & 1), gy, 1f, 1f, Color.White * alpha * 0.055f * bodyA);
            }
        }

        // 6. rim light: a bright top edge, a whisper of one along the bottom
        Draw.Rect(bandX, y + 1f, bandW, 1f, Color.White * alpha * 0.22f * bodyA);
        Draw.Rect(bandX, y + 2f, bandW, 1f, Color.White * alpha * 0.07f * bodyA);
        Draw.Rect(bandX, y + h - 2f, bandW, 1f, Color.White * alpha * 0.06f * bodyA);
    }

    // Vertical extent of the capsule (x, y, w, h) in one screen column.
    // top > bot means "this column is outside the capsule".
    private static void CapsuleSpan(float x, float y, float w, float h, float px,
                                    out float top, out float bot)
    {
        float u = px + 0.5f - x;
        if (u < 0f || u >= w) { top = 0f; bot = -1f; return; }
        float r = Math.Min(h * 0.5f, w * 0.5f);
        float chord = CapsuleChord(u, r, w) * h;
        top = y + (h - chord) * 0.5f;
        bot = top + chord;
    }

    // Draw [top, bot] in column px, skipping whatever the body covers.
    //
    // An empty interval is marked by bot < top (CapsuleSpan's "this column
    // misses the shape"), NOT by a magic pair of numbers: the first version of
    // this compared bBot against the band like any other coordinate, so a
    // column where the body does not exist at all (bTop = 0, bBot = -1) got
    // "the body covers nothing" and the full band was drawn - from y = -1 down
    // past the pill, i.e. a column a thousand pixels tall. That is what turned
    // the pill into a huge frame.
    private static void EmitVisible(float px, float top, float bot,
                                    float bTop, float bBot, Color c)
    {
        if (bot <= top) return;
        if (bBot < bTop)
        {
            Draw.Rect(px, MathF.Round(top), 1f, MathF.Round(bot) - MathF.Round(top), c);
            return;
        }
        if (bBot <= top || bTop >= bot)
        {
            Draw.Rect(px, MathF.Round(top), 1f, MathF.Round(bot) - MathF.Round(top), c);
            return;
        }
        if (top < bTop)
            Draw.Rect(px, MathF.Round(top), 1f, MathF.Round(bTop) - MathF.Round(top), c);
        if (bot > bBot)
            Draw.Rect(px, MathF.Round(bBot), 1f, MathF.Round(bot) - MathF.Round(bBot), c);
    }

    // The band between two concentric versions of the same capsule: one grown
    // by gIn, one by gOut, both centred on (x + ox, y + oy), with the body
    // capsule punched out of it.
    //
    // Everything the pill draws outside its own silhouette goes through here,
    // because the body can be see-through: a filled grown capsule would show
    // its solid middle through a translucent plate, which reads as "the pill
    // is a solid block".
    private static void DrawRingBand(float x, float y, float w, float h,
                                     float ox, float oy, float gOut, float gIn, Color c)
    {
        if (w <= 0f || h <= 0f || c.A <= 1) return;
        if (gOut <= 0f) gOut = 0f;

        float ox0 = x + ox - gOut,     oy0 = y + oy - gOut;
        float ow = w + gOut * 2f,      oh = h + gOut * 2f;
        float ix0 = x + ox - gIn,      iy0 = y + oy - gIn;
        float iw = w + gIn * 2f,       ih = h + gIn * 2f;

        int px0 = (int)MathF.Floor(ox0) - 1;
        int px1 = (int)MathF.Ceiling(ox0 + ow) + 1;

        // gIn == gOut means "no inner shape": the caller wants the whole outer
        // region (the shadow's solid crescent), so skip the subtraction.
        bool noInner = gIn >= gOut;

        for (int px = px0; px <= px1; px++)
        {
            CapsuleSpan(ox0, oy0, ow, oh, px, out float oTop, out float oBot);
            if (oBot <= oTop) continue;

            CapsuleSpan(x, y, w, h, px, out float bTop, out float bBot);

            if (noInner)
            {
                EmitVisible(px, oTop, oBot, bTop, bBot, c);
                continue;
            }

            CapsuleSpan(ix0, iy0, iw, ih, px, out float iTop, out float iBot);

            // The inner shape does not reach this column (it is a shorter-wider
            // or offset capsule, and this is off one of its rounded ends), so
            // there is nothing to subtract here: the whole outer span is ring.
            // Emitting the bottom strip from iBot = -1 would draw the column
            // from -1 all the way down - the "giant frame" bug.
            if (iBot < iTop)
            {
                EmitVisible(px, oTop, oBot, bTop, bBot, c);
                continue;
            }

            // ring = outer minus inner: a strip above the inner shape and one
            // below it, each with the body punched out
            EmitVisible(px, oTop, iTop, bTop, bBot, c);
            EmitVisible(px, iBot, oBot, bTop, bBot, c);
        }
    }

    // The shadow: its crescent outside the pill, plus a soft edge when `blur`
    // is set.
    //
    // Both halves are drawn with DrawRingBand so the body is never covered:
    // with a see-through plate a whole shadow behind it would read as a
    // second, solid-dark pill - most obviously at transparency 100, where
    // nothing sits between the shadow and the level.
    //
    // `blur` adds concentric rings OUTSIDE the shadow itself, each a little
    // fainter than the one inside it. That is what a blurred edge looks like:
    // the darkest part is the shadow, and it fades outward. (The first attempt
    // dilated the shadow and stacked the layers, so every ring piled onto the
    // ones inside it and a high 阴影浓度 turned the whole thing into one
    // black blob - rings must be disjoint, not nested.)
    private static void DrawShadowCapsule(float x, float y, float w, float h,
                                          float dx, float dy, float blur, Color color)
    {
        if (w <= 0f || h <= 0f) return;
        float a = color.A / 255f;
        if (a <= 0.008f) return;

        float b = Math.Max(0f, blur);
        int steps = b > 0.5f ? Math.Min(5, 1 + (int)MathF.Ceiling(b / 4f)) : 0;

        // the shadow itself: the whole capsule at the offset, minus the body
        DrawRingBand(x, y, w, h, dx, dy, 0f, 0f, color);

        // and the rings that soften its edge, faintest on the outside
        for (int k = steps; k >= 1; k--)
        {
            float gOut = b * k / steps;
            float gIn  = b * (k - 1) / steps;
            float ringA = a * (float)(steps - k + 1) / (steps + 1);
            Color c = new Color(color.R, color.G, color.B,
                                (byte)Math.Clamp((int)MathF.Round(ringA * 255f), 0, 255));
            DrawRingBand(x, y, w, h, dx, dy, gOut, gIn, c);
        }
    }

    private static void DrawCapsule(float x, float y, float w, float h, Color color)
    {
        if (w <= 0f || h <= 0f) return;

        if (!RoundedPill)
        {
            Draw.Rect(x, y, w, h, color);
            return;
        }

        // True rounded ends. Every 1px column of the pill is drawn exactly once,
        // by exactly one Draw.Rect, and its height is the circle's chord at that
        // column's centre.
        //
        // The previous version drew the middle band as one wide rect and then
        // the caps as columns running from x+0 to x+r-1. When r came out
        // fractional (it does: the pill height is a text height plus padding)
        // the last cap column and the middle band either overlapped by a
        // fraction of a pixel or left a sliver of a gap between them - with the
        // new translucent glass you could plainly see that seam as a vertical
        // stripe where the cap met the band, because the overlap got blended
        // twice. One column, one draw call: no seam is possible.
        float r = Math.Min(h * 0.5f, w * 0.5f);
        int cols = (int)MathF.Ceiling(w);

        for (int i = 0; i < cols; i++)
        {
            float chord = CapsuleChord(i + 0.5f, r, w);
            float colH = MathF.Round(h * chord);
            if (colH <= 0f) continue;

            Draw.Rect(x + i, MathF.Round(y + (h - colH) * 0.5f), 1f, colH, color);
        }
    }

    // Height of the capsule at `px` (a column centre in local coordinates), as
    // a fraction of the full height: 1 through the straight middle, falling off
    // as the chord of the end discs. Shared by the flat body and the blurred one
    // so the two silhouettes are guaranteed identical.
    private static float CapsuleChord(float px, float r, float w)
    {
        if (r <= 0f) return 1f;
        if (px < r)
        {
            float t = (r - px) / r;              // 1 at the outer edge, 0 at the band
            return MathF.Sqrt(Math.Max(0f, 1f - t * t));
        }
        if (px > w - r)
        {
            float t = (px - (w - r)) / r;
            return MathF.Sqrt(Math.Max(0f, 1f - t * t));
        }
        return 1f;
    }

    // Same silhouette, but filled with the blurred level instead of a colour.
    //
    // BackdropBlur hands us a 320x180 image that covers the whole screen, while
    // the pill lives in the 1920x1080 HUD space, so the source rectangle is the
    // destination rectangle scaled down by that ratio - one screen pixel per
    // column, as before. A few hundred 1x40 quads per label is nothing, and it
    // keeps the rounded ends exact (a single stretched quad would show square
    // corners). The source is already blurred, so the 6x magnification has no
    // detail left to lose; LinearClamp smooths the step between columns.
    //
    // srcShift is the camera travel since the grab (see BackdropBlur.GetLayers)
    // and is added to the SOURCE side only: the quads still land on the pill,
    // but they read the part of the texture that currently holds the world
    // behind the pill. Without it the backdrop inside the glass sat a whole
    // camera pan away from the level around it.
    private static void DrawBlurredCapsule(float x, float y, float w, float h,
                                           float alpha, Texture2D tex, Vector2 srcShift)
    {
        if (w <= 0f || h <= 0f) return;

        float r = Math.Min(h * 0.5f, w * 0.5f);
        int cols = (int)MathF.Ceiling(w);

        float kx = (float)tex.Width / BackdropBlur.HudW;
        float ky = (float)tex.Height / BackdropBlur.HudH;
        int srcW = Math.Max(1, (int)MathF.Ceiling(kx));
        var color = Color.White * alpha;

        for (int i = 0; i < cols; i++)
        {
            float chord = CapsuleChord(i + 0.5f, r, w);
            float colH = MathF.Round(h * chord);
            if (colH <= 0f) continue;

            float colX = MathF.Floor(x + i);
            float colY = MathF.Floor(y + (h - colH) * 0.5f);

            int sx = (int)MathF.Floor((colX + srcShift.X) * kx);
            int sy = (int)MathF.Floor((colY + srcShift.Y) * ky);
            int sh = Math.Max(1, (int)MathF.Ceiling(colH * ky));
            if (sh > tex.Height) sh = tex.Height;

            // A label near the edge of the screen, or a sample window pushed
            // there by the camera offset, can point outside the texture. Clamp
            // to the rim so the pill stays frosted all the way out instead of
            // losing a strip of columns.
            sx = Math.Clamp(sx, 0, tex.Width - srcW);
            sy = Math.Clamp(sy, 0, tex.Height - sh);

            Draw.SpriteBatch.Draw(tex, new Vector2(colX, colY),
                                  new Rectangle(sx, sy, srcW, sh), color,
                                  0f, Vector2.Zero,
                                  new Vector2(1f, colH / sh),
                                  SpriteEffects.None, 0f);
        }
    }

    private void DrawLabel(Vector2 drawPos, float scale)
    {
        var textScale = Vector2.One * 0.55f * PopupFont.SizeCompensation * scale;
        var textSize = new Vector2(PopupFont.TextWidth(TechName) * textScale.X,
                                   PopupFont.Measure(TechName).Y * textScale.Y);
        float pad = 5f * scale;
        var mode = Settings.Popup.DisplayMode;

        var icon = (mode != PopupDisplayMode.TextOnly) ? _icon : null;

        // Every technique gets an icon slot now: the older ones use their PNG
        // from the atlas, the newer ones get a generated letter badge. Without
        // this the newer labels had a hole on the left and sat off-centre.
        float iconW = 0f;
        float iconScl = 1f;
        if (mode != PopupDisplayMode.TextOnly)
        {
            if (icon != null)
            {
                iconScl = textSize.Y / icon.Height;
                iconW = icon.Width * iconScl + pad;
            }
            else
            {
                iconW = textSize.Y + pad;
            }
        }

        bool showText = mode != PopupDisplayMode.IconOnly;
        float contentW = showText ? textSize.X + iconW : iconW - pad;
        float contentH = textSize.Y;

        float pillLeft = drawPos.X - contentW * 0.5f - pad;
        float pillTop = drawPos.Y - contentH - pad;
        float pillWidth = contentW + pad * 2f;
        float pillHeight = contentH + pad * 2f;

        if (showText)
        {
            DrawPillBackdrop(pillLeft, pillTop, pillWidth, pillHeight);
        }

        if (iconW > 0f)
        {
            float iconX = pillLeft + pad * 0.5f;

            if (icon != null)
            {
                float iconY = pillTop + (pillHeight - icon.Height * iconScl) * 0.5f;
                icon.Draw(
                    new Vector2(iconX, iconY),
                    Vector2.Zero,
                    Color.White * alpha,
                    new Vector2(iconScl, iconScl)
                );
            }
            else
            {
                float iconY = pillTop + (pillHeight - textSize.Y) * 0.5f;
                DrawInitialBadge(iconX, iconY, textSize.Y);
            }
        }

        if (showText)
        {
            DrawTextStaggered(drawPos, contentW, textSize.X, iconW, textScale.X);
        }
    }

    // Stand-in icon for a technique that has no PNG in the atlas.
    //
    // Shipping one drawing per technique does not scale (49 techniques and
    // counting), so instead of leaving a gap we draw a small rounded badge with
    // the technique's initial. It uses the popup's own heavy face, so it looks
    // deliberate rather than like a missing-asset placeholder.
    private void DrawInitialBadge(float x, float y, float size)
    {
        if (size <= 1f || string.IsNullOrEmpty(TechName)) return;

        DrawCapsule(x, y, size, size, BadgeFill * alpha);

        char letter = char.ToUpperInvariant(TechName[0]);
        var fontScale = Vector2.One * (size / Math.Max(1f, PopupFont.Measure("M").Y)) * 0.95f;
        PopupFont.DrawOutline(
            letter.ToString(),
            new Vector2(x + size * 0.5f, y + size * 0.5f),
            new Vector2(0.5f, 0.5f), fontScale,
            Color.Black * alpha, 1f, Color.Black * alpha * 0.35f
        );
    }

    // Letters are laid out one by one so each can run its own little
    // entrance/exit. Falls back to one-shot drawing whenever per-character
    // animation is switched off, or the atlas cannot cover the string
    // (missing glyph, atlas not loaded, ...).
    private void DrawTextStaggered(Vector2 drawPos, float contentW, float textW, float iconW, float scaleX)
    {
        if (!Settings.Popup.PerCharEnabled || !PopupFont.CanStagger(TechName))
        {
            var textScale = Vector2.One * scaleX;
            var textAnchor = new Vector2(drawPos.X + (contentW * 0.5f - textW * 0.5f), drawPos.Y);
            PopupFont.DrawOutline(
                TechName, textAnchor,
                new Vector2(0.5f, 1f), textScale,
                Color.White * alpha, 2f,
                Color.Black * alpha * 0.8f
            );
            return;
        }

        // 0 = letters still waiting to appear, 1 = fully settled.
        //
        // Entrance and exit must share the same endpoint. Feeding the raw exit
        // progress into the entrance timeline made every letter snap back to
        // local = 0 on the first exit frame (half size, lifted by CharRise,
        // and the trailing letters skipped outright) - that was the visible
        // "jump" before the popup faded out. The exit therefore runs the
        // entrance timeline backwards: it starts at local = 1, which is
        // exactly where the hold phase left each letter, and eases down to 0.
        bool exiting = Anim >= 1f && exitTimer > 0f;
        int count = TechName.Length;
        float x = drawPos.X - contentW * 0.5f + iconW;

        for (int i = 0; i < count; i++)
        {
            char c = TechName[i];
            float advance = PopupFont.CharAdvance(c) * scaleX;

            float local;
            if (Anim < 1f)
            {
                local = CharLocal(Anim, i, count);
            }
            else if (exiting)
            {
                local = 1f - CharLocal(ExitProgress, i, count);
            }
            else
            {
                local = 1f;
            }

            if (local <= 0f)
            {
                x += advance;
                continue;
            }

            float charAlpha = alpha * Ease.SineOut(local);
            float charScale = scaleX * (CharPopStart + (1f - CharPopStart) * BackOut(local));
            float y = drawPos.Y - (1f - local) * CharRise * Settings.Popup.Scale;

            PopupFont.DrawCharOutline(
                c,
                new Vector2(x + advance * 0.5f, y),
                new Vector2(0.5f, 1f),
                charScale,
                Color.White * charAlpha,
                CharStroke,
                Color.Black * charAlpha * 0.85f
            );

            x += advance;
        }
    }
}
