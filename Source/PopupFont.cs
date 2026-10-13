using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.TechPopups;

// PopupFont - the popup's own heavy typeface.
//
// The font is packed by tools/make_font.py from HarmonyOS Sans SC Bold into a
// BMFont atlas shipped at Dialog/Fonts/techpopups.fnt + techpopups_0.png.
// It is loaded lazily and cached; every entry point falls back to the vanilla
// ActiveFont so that a missing / partially-loading atlas can never break the
// popups - worst case they simply look like they used to.
public static class PopupFont
{
    public const string FaceName = "techpopups";

    // HarmonyOS Sans SC Bold renders ~1.37x taller than vanilla Renogare at the
    // same nominal size. This keeps the drawn size identical to the old look;
    // nudge it up if you want the labels bigger.
    public const float SizeCompensation = 0.73f;

    private static bool tried;
    private static PixelFontSize cachedSize;

    public static PixelFontSize Size {
        get {
            if (!tried) {
                tried = true;
                try {
                    PixelFont font = global::Celeste.Fonts.Load(FaceName);
                    if (font != null && font.Sizes != null && font.Sizes.Count > 0) {
                        cachedSize = font.Sizes[0];
                    }
                } catch {
                    cachedSize = null;
                }
            }
            return cachedSize;
        }
    }

    public static bool Supports(string text) {
        PixelFontSize size = Size;
        if (size == null || size.Characters == null || text == null) {
            return false;
        }
        for (int i = 0; i < text.Length; i++) {
            if (!size.Characters.ContainsKey(text[i])) {
                return false;
            }
        }
        return true;
    }

    public static Vector2 Measure(string text) {
        return Supports(text) ? Size.Measure(text) : ActiveFont.Measure(text);
    }

    // ---- per-character helpers (used by the stagger animation) -----------
    //
    // Width is summed from XAdvance rather than taken from Measure, so the
    // per-character layout and the pill width always agree.

    public static bool CanStagger(string text) {
        return Supports(text);
    }

    public static float CharAdvance(char c) {
        PixelFontSize size = Size;
        if (size == null || size.Characters == null) return 0f;
        PixelFontCharacter glyph;
        if (!size.Characters.TryGetValue(c, out glyph) || glyph == null) return 0f;
        return glyph.XAdvance;
    }

    public static float TextWidth(string text) {
        if (!Supports(text)) return ActiveFont.Measure(text).X;
        float w = 0f;
        for (int i = 0; i < text.Length; i++) w += CharAdvance(text[i]);
        return w;
    }

    // PixelFontSize only exposes Draw(char) - no outlined single-character
    // overload - so the outline is drawn by stamping the glyph around a ring.
    public static void DrawCharOutline(
        char c, Vector2 position, Vector2 justify, float scale,
        Color color, float stroke, Color strokeColor)
    {
        PixelFontSize size = Size;
        if (size == null) return;

        var glyphScale = new Vector2(scale, scale);
        if (stroke > 0f) {
            for (int i = 0; i < 8; i++) {
                float a = i * MathF.PI / 4f;
                var offset = new Vector2(MathF.Cos(a) * stroke, MathF.Sin(a) * stroke);
                size.Draw(c, position + offset, justify, glyphScale, strokeColor);
            }
        }
        size.Draw(c, position, justify, glyphScale, color);
    }

    public static void DrawOutline(
        string text, Vector2 position, Vector2 justify, Vector2 scale,
        Color color, float stroke, Color strokeColor)
    {
        if (Supports(text)) {
            Size.DrawOutline(text, position, justify, scale, color, stroke, strokeColor);
        } else {
            ActiveFont.DrawOutline(text, position, justify, scale, color, stroke, strokeColor);
        }
    }
}
