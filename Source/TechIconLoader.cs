using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;
using Monocle;

namespace Celeste.Mod.TechPopups;

public static class TechIconLoader {
    // Icons live in Graphics/Atlases/Gui/techpopups/icons/, so every atlas key is
    // lowercase and keeps whatever spaces the file name had ("core hyper"). The
    // popup names, meanwhile, are proper case and sometimes carry a modifier
    // prefix ("Reverse Hyper", "Extended Hyper", "Chained Bunnyhop").
    //
    // The old lookup only tried the raw name and a space->underscore version,
    // which missed nearly everything: "Core Hyper" was looked up as
    // "techpopups/icons/Core Hyper" (wrong case - the atlas stores lowercase)
    // and as "techpopups/icons/core_hyper" (wrong separator - the atlas stores a
    // space). Only the single-word icons ever resolved, so a pile of techniques
    // that DO ship artwork fell back to the letter badge.
    //
    // Order of business now:
    //   1. the name as-is and lowercased, with spaces kept and with spaces
    //      turned into underscores, so any of those key shapes can hit
    //   2. the same again with the modifier prefix stripped, so "Extended
    //      Hyper" / "Reverse Wavedash" / "Chained Ultra" reuse the base art
    private static readonly string[] Prefixes = {
        "extended ", "reverse ", "mid-air ", "chained ", "delayed ", "grounded ",
    };

    public static MTexture TryGet(string techName)
    {
        if (string.IsNullOrEmpty(techName)) return null;

        foreach (string key in CandidateKeys(techName))
        {
            if (GFX.Gui.Has(key)) return GFX.Gui[key];
        }
        return null;
    }

    private static IEnumerable<string> CandidateKeys(string techName)
    {
        var seen = new HashSet<string>();
        string name = techName;

        for (int depth = 0; depth < 3; depth++)
        {
            foreach (string key in Variants(name))
            {
                if (seen.Add(key)) yield return key;
            }

            string stripped = StripPrefix(name);
            if (stripped == null || stripped == name) yield break;
            name = stripped;
        }
    }

    private static IEnumerable<string> Variants(string name)
    {
        // A slash cannot exist in a file name but does show up in popup text
        // ("Pufferfish / Bumper Boost"), and it would also split the atlas key
        // into a bogus sub-directory - flatten it into a single space.
        string flat = Collapse(name.Replace('/', ' ')).Trim();
        string lower = flat.ToLowerInvariant();

        // Both prefixes are tried: the art sits under .../techpopups/icons/ here,
        // but older releases shipped it directly under .../techpopups/, and the
        // atlas key shape has flipped between versions before.
        foreach (string dir in new[] { "techpopups/icons/", "techpopups/" })
        {
            yield return dir + flat;
            yield return dir + lower;
            yield return dir + flat.Replace(" ", "_");
            yield return dir + lower.Replace(" ", "_");
        }
    }

    private static string Collapse(string s)
    {
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s;
    }

    private static string StripPrefix(string name)
    {
        string lower = name.ToLowerInvariant();
        foreach (string p in Prefixes)
        {
            if (lower.StartsWith(p, StringComparison.Ordinal))
            {
                return name.Substring(p.Length).Trim();
            }
        }
        return null;
    }
}
