using System;
using System.Collections.Generic;
using System.Text;

namespace Celeste.Mod.TechPopups;

public enum TechCategory {
    Dash,
    Dashless,
    Entity,
    Obscure,
}

// One row per technique. The out-of-game picker reads this list; the hooks in
// TechTracker read the matching settings flag.
public class TechEntry {
    public string Id;
    public string Name;
    // Chinese shorthand for the technique. The picker shows it as the card title
    // and the mod options show "alias (Name)", so a Chinese player can find a
    // technique by the name they actually use it under. "" = none was given.
    public string Alias;
    public TechCategory Category;
    // Which of the three sections the card belongs to.
    public string Section;
    // Where the card sits on the picker board, in pixels. The board is laid out
    // by hand, so columns and rows carry meaning: a card sits where it sits
    // because of what is above, below and beside it. The page only places them.
    public int X;
    public int Y;
    // Name of the settings flag this row toggles. Two rows can share one flag
    // (Wavedash / Hyper / Demohyper are all just HyperEnabled), and the page
    // needs to know that: toggling one has to move the others too, or the row
    // you click next is showing a stale state and you end up flipping it the
    // wrong way.
    public string Key;
    public Func<bool> Get;
    public Action<bool> Set;
}

public static class TechRegistry
{
    private static List<TechEntry> entries;
    private static TechPopupsSettings Settings => TechPopupsModule.Settings;

    // Section names, also the section headings on the picker board.
    public const string SecDash     = "冲刺技巧";
    public const string SecDashless = "无冲技巧";
    public const string SecEntity   = "实体技巧";

    public static List<TechEntry> Entries {
        get {
            if (entries == null) entries = Build();
            return entries;
        }
    }

    public static TechEntry Find(string id) {
        foreach (var e in Entries) {
            if (e.Id == id) return e;
        }
        return null;
    }

    // Hand-rolled JSON: no third-party dependency, and the payload is tiny.
    public static string ToJson() {
        var sb = new StringBuilder();
        sb.Append("{\"techs\":[");
        bool first = true;
        foreach (var e in Entries) {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"id\":").Append(Json(e.Id));
            sb.Append(",\"name\":").Append(Json(e.Name));
            sb.Append(",\"alias\":").Append(Json(e.Alias));
            sb.Append(",\"cat\":").Append(Json(e.Category.ToString()));
            sb.Append(",\"sec\":").Append(Json(e.Section));
            sb.Append(",\"x\":").Append(e.X);
            sb.Append(",\"y\":").Append(e.Y);
            sb.Append(",\"key\":").Append(Json(e.Key));
            sb.Append(",\"on\":").Append(SafeGet(e) ? "true" : "false");
            sb.Append('}');
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private static bool SafeGet(TechEntry e) {
        try { return e.Get != null && e.Get(); } catch { return false; }
    }

    public static bool SafeSet(string id, bool on) {
        var e = Find(id);
        if (e == null || e.Set == null) return false;
        try { e.Set(on); return true; } catch { return false; }
    }

    private static string Json(string s) {
        var sb = new StringBuilder("\"");
        foreach (char c in s ?? "") {
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c == '\n') sb.Append("\\n");
            else if (c < 0x20) sb.Append(' ');
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }

    // id / display name / Chinese alias / category / section / board position /
    // settings getter / settings setter.
    private static TechEntry E(string id, string name, string alias, TechCategory cat,
                               string section, int x, int y, string key,
                               Func<bool> get, Action<bool> set) {
        return new TechEntry {
            Id = id, Name = name, Alias = alias, Category = cat,
            Section = section, X = x, Y = y, Key = key,
            Get = get, Set = set,
        };
    }

    // The order here is the order the picker draws them in, and the X/Y values
    // are the positions it places them at. Both come from the layout that was
    // dragged together by hand: three sections stacked top to bottom, each one
    // a few columns wide, and the column a technique sits in is its family.
    private static List<TechEntry> Build() {
        var s = Settings;
        var list = new List<TechEntry>();

        // ================= 冲刺技巧 =================
        list.Add(E("super", "Super", "", TechCategory.Dash, SecDash, 100, 50, "SuperEnabled",
            () => s.Tech.SuperEnabled, v => s.Tech.SuperEnabled = v));
        list.Add(E("midairsuper", "Mid-air Super", "空中Super", TechCategory.Dash, SecDash, 100, 150, "MidairSuperEnabled",
            () => s.Tech.MidairSuperEnabled, v => s.Tech.MidairSuperEnabled = v));
        list.Add(E("crouchdash", "Crouch Dash", "下蹲冲刺", TechCategory.Dash, SecDash, 100, 250, "CrouchDashEnabled",
            () => s.Tech.CrouchDashEnabled, v => s.Tech.CrouchDashEnabled = v));
        list.Add(E("crouchclimb", "Crouch Climb", "蹲姿爬墙", TechCategory.Dashless, SecDash, 100, 340, "CrouchClimbEnabled",
            () => s.Tech.CrouchClimbEnabled, v => s.Tech.CrouchClimbEnabled = v));
        list.Add(E("wavedash", "Wavedash", "凌波微步", TechCategory.Dash, SecDash, 390, 50, "HyperEnabled",
            () => s.Tech.HyperEnabled, v => s.Tech.HyperEnabled = v));
        list.Add(E("hyper", "Hyper", "", TechCategory.Dash, SecDash, 390, 150, "HyperEnabled",
            () => s.Tech.HyperEnabled, v => s.Tech.HyperEnabled = v));
        list.Add(E("demohyper", "Demohyper", "", TechCategory.Dash, SecDash, 390, 250, "HyperEnabled",
            () => s.Tech.HyperEnabled, v => s.Tech.HyperEnabled = v));
        list.Add(E("extended", "Extended Super / Hyper", "充能Super / Hyper", TechCategory.Dash, SecDash, 390, 340, "ExtendedEnabled",
            () => s.Tech.ExtendedEnabled, v => s.Tech.ExtendedEnabled = v));
        list.Add(E("archie", "Archie", "", TechCategory.Dash, SecDash, 730, 50, "ArchieEnabled",
            () => s.Tech.ArchieEnabled, v => s.Tech.ArchieEnabled = v));
        list.Add(E("transitiondash", "Transition Dash", "", TechCategory.Dash, SecDash, 730, 150, "TransitionDashEnabled",
            () => s.Tech.TransitionDashEnabled, v => s.Tech.TransitionDashEnabled = v));
        list.Add(E("fastbubble", "Fast Bubble", "泡泡快启", TechCategory.Entity, SecDash, 730, 250, "FastBubbleEnabled",
            () => s.Tech.FastBubbleEnabled, v => s.Tech.FastBubbleEnabled = v));
        list.Add(E("launchboost", "Explosion Boost", "超级弹", TechCategory.Entity, SecDash, 730, 340, "LaunchBoostEnabled",
            () => s.Tech.LaunchBoostEnabled, v => s.Tech.LaunchBoostEnabled = v));
        list.Add(E("ultra", "Ultra", "", TechCategory.Dash, SecDash, 1090, 50, "UltraEnabled",
            () => s.Tech.UltraEnabled, v => s.Tech.UltraEnabled = v));
        list.Add(E("groundedultra", "Grounded Ultra", "平地Ultra", TechCategory.Dash, SecDash, 1090, 150, "GroundedUltraEnabled",
            () => s.Tech.GroundedUltraEnabled, v => s.Tech.GroundedUltraEnabled = v));
        list.Add(E("dashbounce", "Dash Bounce", "", TechCategory.Dash, SecDash, 1090, 250, "DashBounceEnabled",
            () => s.Tech.DashBounceEnabled, v => s.Tech.DashBounceEnabled = v));
        list.Add(E("wallbounce", "Wallbounce", "蹭墙", TechCategory.Dash, SecDash, 1090, 340, "WallbounceEnabled",
            () => s.Tech.WallbounceEnabled, v => s.Tech.WallbounceEnabled = v));
        list.Add(E("chainedultra", "Chained Ultra", "连续Ultra", TechCategory.Dash, SecDash, 1400, 50, "ChainedUltraEnabled",
            () => s.Tech.ChainedUltraEnabled, v => s.Tech.ChainedUltraEnabled = v));
        list.Add(E("delayedultra", "Delayed Ultra", "", TechCategory.Obscure, SecDash, 1400, 150, "DelayedUltraEnabled",
            () => s.Tech.DelayedUltraEnabled, v => s.Tech.DelayedUltraEnabled = v));
        list.Add(E("delayedwallbounce", "Delayed Wallbounce", "延迟蹭墙", TechCategory.Dash, SecDash, 1400, 340, "WallbounceEnabled",
            () => s.Tech.WallbounceEnabled, v => s.Tech.WallbounceEnabled = v));

        // ================= 无冲技巧 =================
        list.Add(E("bunnyhop", "Bunnyhop", "兔子跳", TechCategory.Dashless, SecDashless, 100, 540, "BunnyhopEnabled",
            () => s.Tech.BunnyhopEnabled, v => s.Tech.BunnyhopEnabled = v));
        list.Add(E("chainedbunnyhop", "Chained Bunnyhop", "", TechCategory.Dashless, SecDashless, 100, 660, "ChainedBunnyhopEnabled",
            () => s.Tech.ChainedBunnyhopEnabled, v => s.Tech.ChainedBunnyhopEnabled = v));
        list.Add(E("neutraljump", "Neutral Jump", "中性跳", TechCategory.Dashless, SecDashless, 390, 540, "NeutralJumpEnabled",
            () => s.Tech.NeutralJumpEnabled, v => s.Tech.NeutralJumpEnabled = v));
        list.Add(E("wallboost", "Wallboost", "偷体力抓跳", TechCategory.Dashless, SecDashless, 390, 660, "WallboostEnabled",
            () => s.Tech.WallboostEnabled, v => s.Tech.WallboostEnabled = v));
        list.Add(E("overclock", "Overclock", "", TechCategory.Dashless, SecDashless, 390, 770, "OverclockEnabled",
            () => s.Tech.OverclockEnabled, v => s.Tech.OverclockEnabled = v));
        list.Add(E("cornerkick", "Corner Kick", "咖啡跳", TechCategory.Dashless, SecDashless, 730, 540, "CornerKickEnabled",
            () => s.Tech.CornerKickEnabled, v => s.Tech.CornerKickEnabled = v));
        list.Add(E("ceilingpop", "Ceiling Pop", "cp", TechCategory.Obscure, SecDashless, 730, 660, "CeilingPopEnabled",
            () => s.Tech.CeilingPopEnabled, v => s.Tech.CeilingPopEnabled = v));
        list.Add(E("cornerboost", "Cornerboost", "cb", TechCategory.Dashless, SecDashless, 1090, 540, "CornerboostEnabled",
            () => s.Tech.CornerboostEnabled, v => s.Tech.CornerboostEnabled = v));
        list.Add(E("reversecornerboost", "Reverse Cornerboost", "rcb", TechCategory.Dashless, SecDashless, 1090, 660, "ReverseCornerboostEnabled",
            () => s.Tech.ReverseCornerboostEnabled, v => s.Tech.ReverseCornerboostEnabled = v));
        list.Add(E("doubleblockboost", "Double Block Boost", "双重机关加速", TechCategory.Dashless, SecDashless, 1090, 770, "DoubleBlockBoostEnabled",
            () => s.Tech.DoubleBlockBoostEnabled, v => s.Tech.DoubleBlockBoostEnabled = v));
        list.Add(E("spiketech", "Spike Tech", "", TechCategory.Dashless, SecDashless, 1400, 540, "SpikeTechEnabled",
            () => s.Tech.SpikeTechEnabled, v => s.Tech.SpikeTechEnabled = v));
        list.Add(E("retentiontech", "Retention Tech", "", TechCategory.Dashless, SecDashless, 1400, 660, "RetentionTechEnabled",
            () => s.Tech.RetentionTechEnabled, v => s.Tech.RetentionTechEnabled = v));

        // ================= 实体技巧 =================
        list.Add(E("dreamjump", "Dream Jump", "果冻跳", TechCategory.Entity, SecEntity, 100, 950, "DreamJumpEnabled",
            () => s.Tech.DreamJumpEnabled, v => s.Tech.DreamJumpEnabled = v));
        list.Add(E("dreamgrab", "Dream Grab", "果冻反抓", TechCategory.Entity, SecEntity, 100, 1030, "DreamGrabEnabled",
            () => s.Tech.DreamGrabEnabled, v => s.Tech.DreamGrabEnabled = v));
        list.Add(E("dreamsuper", "Dream Super / Hyper", "果冻Super / Hyper", TechCategory.Entity, SecEntity, 100, 1110, "DreamTechEnabled",
            () => s.Tech.DreamTechEnabled, v => s.Tech.DreamTechEnabled = v));
        list.Add(E("dreamdoublejump", "Dream Double Jump", "果冻双跳", TechCategory.Entity, SecEntity, 100, 1190, "DreamDoubleJumpEnabled",
            () => s.Tech.DreamDoubleJumpEnabled, v => s.Tech.DreamDoubleJumpEnabled = v));
        list.Add(E("dreamsmuggle", "Dream Smuggle", "带抓取物进果冻", TechCategory.Entity, SecEntity, 100, 1270, "DreamSmuggleEnabled",
            () => s.Tech.DreamSmuggleEnabled, v => s.Tech.DreamSmuggleEnabled = v));
        list.Add(E("reformtech", "Reform Tech", "方块刷新技巧", TechCategory.Entity, SecEntity, 550, 950, "ReformTechEnabled",
            () => s.Tech.ReformTechEnabled, v => s.Tech.ReformTechEnabled = v));
        list.Add(E("cassetteboost", "Cassette Block Boost", "节奏块加速", TechCategory.Entity, SecEntity, 550, 1030, "CassetteBoostEnabled",
            () => s.Tech.CassetteBoostEnabled, v => s.Tech.CassetteBoostEnabled = v));
        list.Add(E("featherboost", "Featherboost", "", TechCategory.Entity, SecEntity, 550, 1110, "FeatherboostEnabled",
            () => s.Tech.FeatherboostEnabled, v => s.Tech.FeatherboostEnabled = v));
        list.Add(E("coretech", "Core Super / Hyper", "岩浆块Super / Hyper", TechCategory.Entity, SecEntity, 550, 1190, "CoreTechEnabled",
            () => s.Tech.CoreTechEnabled, v => s.Tech.CoreTechEnabled = v));
        list.Add(E("theoregrab", "Theo Regrab", "", TechCategory.Entity, SecEntity, 1020, 950, "TheoRegrabEnabled",
            () => s.Tech.TheoRegrabEnabled, v => s.Tech.TheoRegrabEnabled = v));
        list.Add(E("theovator", "Theovator", "", TechCategory.Entity, SecEntity, 1020, 1030, "TheovatorEnabled",
            () => s.Tech.TheovatorEnabled, v => s.Tech.TheovatorEnabled = v));
        list.Add(E("neutraldrop", "Neutral Drop", "中性下放", TechCategory.Entity, SecEntity, 1020, 1110, "NeutralDropEnabled",
            () => s.Tech.NeutralDropEnabled, v => s.Tech.NeutralDropEnabled = v));
        list.Add(E("waterboost", "Water Boost", "水面加速", TechCategory.Obscure, SecEntity, 1020, 1190, "WaterBoostEnabled",
            () => s.Tech.WaterBoostEnabled, v => s.Tech.WaterBoostEnabled = v));
        list.Add(E("jellyregrab", "Jelly Regrab", "", TechCategory.Entity, SecEntity, 1340, 950, "JellyRegrabEnabled",
            () => s.Tech.JellyRegrabEnabled, v => s.Tech.JellyRegrabEnabled = v));
        list.Add(E("jellyvator", "Jellyvator", "", TechCategory.Entity, SecEntity, 1340, 1030, "JellyvatorEnabled",
            () => s.Tech.JellyvatorEnabled, v => s.Tech.JellyvatorEnabled = v));
        list.Add(E("backboost", "Backboost", "反丢加速", TechCategory.Entity, SecEntity, 1340, 1110, "BackboostEnabled",
            () => s.Tech.BackboostEnabled, v => s.Tech.BackboostEnabled = v));

        return list;
    }
}
