using System;
using System.Collections.Generic;
using System.Text;

namespace Celeste.Mod.TechPopups;

// Everything on this page is about how the popup LOOKS and how long it stays,
// not about which techs are detected. The panel renders whatever this class
// describes, so adding a new knob means touching one list - no HTML edit.
public static class TechVisual
{
    private sealed class Knob
    {
        public string Key;
        public string Label;
        public string Group;   // "外观" / "时长"
        public string Kind;    // "range" | "bool" | "enum"
        public int Min, Max;
        public string Unit;
        public string[] Options;

        public int Value()
        {
            switch (Key) {
                case "popup.scale":        return TechPopupsModule.Settings.Popup.ScaleInt;
                case "popup.offset":       return TechPopupsModule.Settings.Popup.LabelOffsetY;
                case "popup.mode":         return (int)TechPopupsModule.Settings.Popup.DisplayMode;
                case "popup.perchar":      return TechPopupsModule.Settings.Popup.PerCharEnabled ? 1 : 0;
                case "popup.edgemargin":   return TechPopupsModule.Settings.Popup.EdgeMargin;
                case "popup.glass":        return TechPopupsModule.Settings.Popup.GlassBackdrop ? 1 : 0;
                case "popup.glasstrans":   return TechPopupsModule.Settings.Popup.GlassTransparencyPct;
                case "popup.bgr":          return TechPopupsModule.Settings.Popup.PillBgR;
                case "popup.bgg":          return TechPopupsModule.Settings.Popup.PillBgG;
                case "popup.bgb":          return TechPopupsModule.Settings.Popup.PillBgB;
                case "popup.shadowblur":   return TechPopupsModule.Settings.Popup.PillShadowBlur;
                case "popup.bluron":       return TechPopupsModule.Settings.Popup.GlassBlur ? 1 : 0;
                case "popup.blur":         return TechPopupsModule.Settings.Popup.GlassBlurStrengthPct;
                case "popup.outline":      return TechPopupsModule.Settings.Popup.PillOutline ? 1 : 0;
                case "popup.outlinew":     return TechPopupsModule.Settings.Popup.PillOutlineWidth;
                case "popup.outlinea":     return TechPopupsModule.Settings.Popup.PillOutlineAlphaPct;
                case "popup.outliner":     return TechPopupsModule.Settings.Popup.PillOutlineR;
                case "popup.outlineg":     return TechPopupsModule.Settings.Popup.PillOutlineG;
                case "popup.outlineb":     return TechPopupsModule.Settings.Popup.PillOutlineB;
                case "popup.shadowr":      return TechPopupsModule.Settings.Popup.PillShadowR;
                case "popup.shadowg":      return TechPopupsModule.Settings.Popup.PillShadowG;
                case "popup.shadowb":      return TechPopupsModule.Settings.Popup.PillShadowB;
                case "popup.shadow":       return TechPopupsModule.Settings.Popup.PillShadow ? 1 : 0;
                case "popup.shadowx":      return TechPopupsModule.Settings.Popup.PillShadowX;
                case "popup.shadowy":      return TechPopupsModule.Settings.Popup.PillShadowY;
                case "popup.shadowa":      return TechPopupsModule.Settings.Popup.PillShadowAlphaPct;
                case "timing.hold":        return TechPopupsModule.Settings.Timing.PopupHoldTimeCs;
                case "timing.fadein":      return TechPopupsModule.Settings.Timing.PopupFadeInTimeCs;
                case "timing.fadeout":     return TechPopupsModule.Settings.Timing.PopupFadeOutTimeCs;
                default:                   return 0;
            }
        }

        public void Set(int v)
        {
            var p = TechPopupsModule.Settings.Popup;
            var t = TechPopupsModule.Settings.Timing;
            switch (Key) {
                case "popup.scale":   p.ScaleInt        = v; break;
                case "popup.offset":  p.LabelOffsetY    = v; break;
                case "popup.mode":    p.DisplayMode     = (PopupDisplayMode)v; break;
                case "popup.perchar": p.PerCharEnabled  = v != 0; break;
                case "popup.edgemargin": p.EdgeMargin   = v; break;
                case "popup.glass":    p.GlassBackdrop  = v != 0; break;
                case "popup.glasstrans": p.GlassTransparencyPct = v; break;
                case "popup.bgr":        p.PillBgR        = v; break;
                case "popup.bgg":        p.PillBgG        = v; break;
                case "popup.bgb":        p.PillBgB        = v; break;
                case "popup.shadowblur": p.PillShadowBlur = v; break;
                case "popup.bluron":   p.GlassBlur            = v != 0; break;
                case "popup.blur":     p.GlassBlurStrengthPct = v; break;
                case "popup.outline":  p.PillOutline         = v != 0; break;
                case "popup.outlinew": p.PillOutlineWidth    = v; break;
                case "popup.outlinea": p.PillOutlineAlphaPct = v; break;
                case "popup.shadow":   p.PillShadow          = v != 0; break;
                case "popup.shadowx":  p.PillShadowX         = v; break;
                case "popup.shadowy":  p.PillShadowY         = v; break;
                case "popup.shadowa":  p.PillShadowAlphaPct  = v; break;
                case "popup.outliner": p.PillOutlineR = v; break;
                case "popup.outlineg": p.PillOutlineG = v; break;
                case "popup.outlineb": p.PillOutlineB = v; break;
                case "popup.shadowr":  p.PillShadowR  = v; break;
                case "popup.shadowg":  p.PillShadowG  = v; break;
                case "popup.shadowb":  p.PillShadowB  = v; break;
                case "timing.hold":    t.PopupHoldTimeCs    = v; break;
                case "timing.fadein":  t.PopupFadeInTimeCs  = v; break;
                case "timing.fadeout": t.PopupFadeOutTimeCs = v; break;
            }
        }
    }

    private static readonly Knob[] Knobs = {
        new Knob { Key = "popup.scale",   Label = "技巧框大小",   Group = "外观",
                   Kind = "range", Min = 25,  Max = 200, Unit = "%" },
        new Knob { Key = "popup.offset",  Label = "头顶高度",     Group = "外观",
                   Kind = "range", Min = 0,   Max = 120, Unit = "px" },
        // Order MUST match Celeste.Mod.TechPopups.PopupDisplayMode:
        // 0 = Both, 1 = TextOnly, 2 = IconOnly.
        new Knob { Key = "popup.mode",    Label = "显示内容",     Group = "外观",
                   Kind = "enum",
                   Options = new[] { "图标 + 文字", "只显示文字", "只显示图标" } },
        new Knob { Key = "popup.perchar", Label = "逐字动画",     Group = "外观",
                   Kind = "bool" },
        new Knob { Key = "popup.edgemargin", Label = "贴边留白",  Group = "外观",
                   Kind = "range", Min = 0,   Max = 300, Unit = "px" },
        new Knob { Key = "popup.glasstrans", Label = "背景透明度", Group = "外观",
                   Kind = "range", Min = 0,   Max = 100, Unit = "%" },
        new Knob { Key = "popup.bgr",     Label = "背景红",       Group = "外观",
                   Kind = "range", Min = 0,   Max = 255 },
        new Knob { Key = "popup.bgg",     Label = "背景绿",       Group = "外观",
                   Kind = "range", Min = 0,   Max = 255 },
        new Knob { Key = "popup.bgb",     Label = "背景蓝",       Group = "外观",
                   Kind = "range", Min = 0,   Max = 255 },

        new Knob { Key = "popup.outline",  Label = "显示边框",   Group = "边框",
                   Kind = "bool" },
        new Knob { Key = "popup.outlinew", Label = "边框宽度",   Group = "边框",
                   Kind = "range", Min = 0,   Max = 8,   Unit = "px" },
        new Knob { Key = "popup.outlinea", Label = "边框浓度",   Group = "边框",
                   Kind = "range", Min = 0,   Max = 100, Unit = "%" },
        new Knob { Key = "popup.outliner", Label = "边框红",     Group = "边框",
                   Kind = "range", Min = 0,   Max = 255 },
        new Knob { Key = "popup.outlineg", Label = "边框绿",     Group = "边框",
                   Kind = "range", Min = 0,   Max = 255 },
        new Knob { Key = "popup.outlineb", Label = "边框蓝",     Group = "边框",
                   Kind = "range", Min = 0,   Max = 255 },

        new Knob { Key = "popup.shadow",   Label = "显示阴影",   Group = "阴影",
                   Kind = "bool" },
        new Knob { Key = "popup.shadowx",  Label = "水平偏移",   Group = "阴影",
                   Kind = "range", Min = -12, Max = 12,  Unit = "px" },
        new Knob { Key = "popup.shadowy",  Label = "垂直偏移",   Group = "阴影",
                   Kind = "range", Min = -12, Max = 12,  Unit = "px" },
        new Knob { Key = "popup.shadowa",  Label = "阴影浓度",   Group = "阴影",
                   Kind = "range", Min = 0,   Max = 100, Unit = "%" },
        new Knob { Key = "popup.shadowblur", Label = "阴影模糊", Group = "阴影",
                   Kind = "range", Min = 0,   Max = 20,  Unit = "px" },
        new Knob { Key = "popup.shadowr",  Label = "阴影红",     Group = "阴影",
                   Kind = "range", Min = 0,   Max = 255 },
        new Knob { Key = "popup.shadowg",  Label = "阴影绿",     Group = "阴影",
                   Kind = "range", Min = 0,   Max = 255 },
        new Knob { Key = "popup.shadowb",  Label = "阴影蓝",     Group = "阴影",
                   Kind = "range", Min = 0,   Max = 255 },

        new Knob { Key = "timing.hold",   Label = "停留时间",     Group = "时长",
                   Kind = "range", Min = 1,   Max = 300, Unit = "厘秒" },
        new Knob { Key = "timing.fadein", Label = "渐入时间",     Group = "时长",
                   Kind = "range", Min = 1,   Max = 100, Unit = "厘秒" },
        new Knob { Key = "timing.fadeout",Label = "渐出时间",     Group = "时长",
                   Kind = "range", Min = 1,   Max = 250, Unit = "厘秒" },

        // Experimental, deliberately LAST in the panel: the frosted backdrop is
        // the one feature here that can cost frames, and how good it looks
        // depends on the strength slider being high.
        new Knob { Key = "popup.bluron", Label = "毛玻璃效果",   Group = "毛玻璃（实验性）",
                   Kind = "bool" },
        new Knob { Key = "popup.blur",   Label = "模糊程度",     Group = "毛玻璃（实验性）",
                   Kind = "range", Min = 0,   Max = 100, Unit = "%" },
    };

    public static string ToJson()
    {
        var sb = new StringBuilder("{\"visual\":[");
        bool first = true;
        foreach (var k in Knobs) {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"key\":").Append(Json(k.Key))
              .Append(",\"label\":").Append(Json(k.Label))
              .Append(",\"group\":").Append(Json(k.Group))
              .Append(",\"kind\":").Append(Json(k.Kind))
              .Append(",\"min\":").Append(k.Min)
              .Append(",\"max\":").Append(k.Max)
              .Append(",\"unit\":").Append(Json(k.Unit ?? ""))
              .Append(",\"value\":").Append(k.Value());
            if (k.Options != null) {
                sb.Append(",\"options\":[");
                for (int i = 0; i < k.Options.Length; i++) {
                    if (i > 0) sb.Append(',');
                    sb.Append(Json(k.Options[i]));
                }
                sb.Append(']');
            }
            sb.Append('}');
        }
        return sb.Append("]}").ToString();
    }

    // Thread-safe key check: used by the HTTP thread to reject bogus ids before
    // queueing anything. The Knobs array is never mutated, so reading it from
    // another thread is fine.
    public static bool HasKey(string key)
    {
        foreach (var k in Knobs) {
            if (k.Key == key) return true;
        }
        return false;
    }

    // Only called from the main thread. Returns false for unknown keys so the
    // panel stops buzzing the endpoint with stale ids.
    public static bool SafeSet(string key, int value)
    {
        foreach (var k in Knobs) {
            if (k.Key != key) continue;
            try {
                if (k.Kind == "range") value = Math.Max(k.Min, Math.Min(k.Max, value));
                k.Set(value);
                return true;
            } catch { return false; }
        }
        return false;
    }

    private static string Json(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in s ?? "") {
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c == '\n') sb.Append("\\n");
            else if (c < 0x20) sb.Append(' ');
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }
}
