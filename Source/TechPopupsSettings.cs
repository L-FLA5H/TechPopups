namespace Celeste.Mod.TechPopups;

public enum PopupDisplayMode {
    Both,
    TextOnly,
    IconOnly,
}

public class TechPopupsSettings : EverestModuleSettings
{
    [SettingName("modoptions_techpopups_enabled")]
    public bool Enabled { get; set; } = true;

    [SettingName("modoptions_techpopups_submenu_techsettings")]
    public TechSettings Tech { get; set; } = new TechSettings();

    [SettingSubMenu]
    public class TechSettings
    {
        [SettingName("modoptions_techpopups_wallbounce_enabled")]
        public bool WallbounceEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_neutraljump_enabled")]
        public bool NeutralJumpEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_super_enabled")]
        public bool SuperEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_hyper_enabled")]
        public bool HyperEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_bunnyhop_enabled")]
        public bool BunnyhopEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_chainedbunnyhop_enabled")]
        public bool ChainedBunnyhopEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_chainedultra_enabled")]
        public bool ChainedUltraEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_ultra_enabled")]
        public bool UltraEnabled { get; set; } = true;

        // Off by default: this one can only be told apart from a normal Ultra by
        // how long you waited, so it is a knob, not a fact. Turn it on only if
        // you actually care about the distinction.
        [SettingName("modoptions_techpopups_delayedultra_enabled")]
        public bool DelayedUltraEnabled { get; set; } = false;

        [SettingName("modoptions_techpopups_dreamgrab_enabled")]
        public bool DreamGrabEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_dreamjump_enabled")]
        public bool DreamJumpEnabled { get; set; } = true;

        // Dream Double Jump used to share DreamJumpEnabled with Dream Jump, so
        // switching one off silenced both. Its own switch now.
        [SettingName("modoptions_techpopups_dreamdoublejump_enabled")]
        public bool DreamDoubleJumpEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_crouchdash_enabled")]
        public bool CrouchDashEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_archie_enabled")]
        public bool ArchieEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_transitiondash_enabled")]
        public bool TransitionDashEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_dreamsmuggle_enabled")]
        public bool DreamSmuggleEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_dreamtech_enabled")]
        public bool DreamTechEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_coretech_enabled")]
        public bool CoreTechEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_overclock_enabled")]
        public bool OverclockEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_cornerboost_enabled")]
        public bool CornerboostEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_reformtech_enabled")]
        public bool ReformTechEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_backboost_enabled")]
        public bool BackboostEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_spiketech_enabled")]
        public bool SpikeTechEnabled { get; set; } = true;

        // ---- batch 2 (2026-10-11) ------------------------------------
        // Mid-air / grounded / extended variants default OFF: they fire on top
        // of the plain super / hyper / ultra, so leaving them on doubles up.

        [SettingName("modoptions_techpopups_midairsuper_enabled")]
        public bool MidairSuperEnabled { get; set; } = false;

        [SettingName("modoptions_techpopups_extended_enabled")]
        public bool ExtendedEnabled { get; set; } = false;

        [SettingName("modoptions_techpopups_groundedultra_enabled")]
        public bool GroundedUltraEnabled { get; set; } = false;

        [SettingName("modoptions_techpopups_crouchclimb_enabled")]
        public bool CrouchClimbEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_neutraldrop_enabled")]
        public bool NeutralDropEnabled { get; set; } = true;

        // Keeps the id `launchboost` on purpose: the setting moved from "any
        // launch" to the Explosion Boost proper, but renaming the id would
        // throw away everyone's saved toggle.
        [SettingName("modoptions_techpopups_launchboost_enabled")]
        public bool LaunchBoostEnabled { get; set; } = true;

        // 双重机关加速: two lift-boosted jumps / dashes inside 9 frames.
        [SettingName("modoptions_techpopups_doubleblockboost_enabled")]
        public bool DoubleBlockBoostEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_wallboost_enabled")]
        public bool WallboostEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_retentiontech_enabled")]
        public bool RetentionTechEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_reversecornerboost_enabled")]
        public bool ReverseCornerboostEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_jellyregrab_enabled")]
        public bool JellyRegrabEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_theoregrab_enabled")]
        public bool TheoRegrabEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_jellyvator_enabled")]
        public bool JellyvatorEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_theovator_enabled")]
        public bool TheovatorEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_fastbubble_enabled")]
        public bool FastBubbleEnabled { get; set; } = true;

        // These three were listed in the panel as "还没写判定" for a long time.
        // They are real detections now - see TechTracker for what each one
        // reads, and the tech table for the wiki definition they follow.
        [SettingName("modoptions_techpopups_cornerkick_enabled")]
        public bool CornerKickEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_ceilingpop_enabled")]
        public bool CeilingPopEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_cassetteboost_enabled")]
        public bool CassetteBoostEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_waterboost_enabled")]
        public bool WaterBoostEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_featherboost_enabled")]
        public bool FeatherboostEnabled { get; set; } = true;

    }

    [SettingName("modoptions_techpopups_submenu_timingsettings")]
    public TimingSettings Timing { get; set; } = new TimingSettings();

    [SettingSubMenu]
    public class TimingSettings
    {

        // Bunnyhop

        [SettingName("modoptions_techpopups_bhop_maxhold")]
        [SettingRange(1, 30)]
        public int BhopMaxHoldCs { get; set; } = 10;

        [SettingName("modoptions_techpopups_bhop_airtime")]
        [SettingRange(5, 60)]
        public int BhopAirTimeCs { get; set; } = 35;

        [SettingName("modoptions_techpopups_bhop_landjump")]
        [SettingRange(1, 30)]
        public int BhopLandJumpCs { get; set; } = 10;

        public float BhopMaxHoldTime => BhopMaxHoldCs * 0.01f;
        public float BhopAirTime => BhopAirTimeCs * 0.01f;
        public float BhopMaxLandJumpTime => BhopLandJumpCs * 0.01f;

        // Backboost

        [SettingName("modoptions_techpopups_backboost_maxtime")]
        [SettingRange(1, 60)]
        public int BackboostMaxTimeCs { get; set; } = 17;

        public float BackboostMaxTime => BackboostMaxTimeCs * 0.01f;

        // Popups

        [SettingName("modoptions_techpopups_popup_fadeintime")]
        [SettingRange(1, 250, true)]
        public int PopupFadeInTimeCs { get; set; } = 22;
        public float PopupFadeInTime => PopupFadeInTimeCs * 0.01f;


        [SettingName("modoptions_techpopups_popup_holdtime")]
        [SettingRange(1, 500, true)]
        public int PopupHoldTimeCs { get; set; } = 140;
        public float PopupHoldTime => PopupHoldTimeCs * 0.01f;


        [SettingName("modoptions_techpopups_popup_fadeouttime")]
        [SettingRange(1, 250, true)]
        public int PopupFadeOutTimeCs { get; set; } = 45;
        public float PopupFadeOutTime => PopupFadeOutTimeCs * 0.01f;


        [SettingName("modoptions_techpopups_popup_baselinespeed")]
        [SettingRange(100, 2000, true)]
        public int PopupBaselineSpeedInt { get; set; } = 325;
        public float PopupBaselineSpeed => PopupBaselineSpeedInt;


        [SettingName("modoptions_techpopups_popup_speedscale")]
        [SettingRange(100, 1000, true)]
        public int PopupSpeedScaleCs { get; set; } = 200;
        public float PopupSpeedScale => PopupSpeedScaleCs * 0.01f;

        // Neutrals

        [SettingName("modoptions_techpopups_neutral_cooldown")]
        [SettingRange(0, 1000, true)]
        public int NeutralJumpCooldownCs { get; set; } = 100;
        public float NeutralJumpCooldown => NeutralJumpCooldownCs * 0.01f;

        // Chain detection: a bunnyhop / ultra that lands within this window of
        // the previous one of the same kind is announced as a chained variant.
        [SettingName("modoptions_techpopups_chain_window")]
        [SettingRange(1, 300, true)]
        public int ChainWindowCs { get; set; } = 85;
        public float ChainWindow => ChainWindowCs * 0.01f;

        // Ultra vs Delayed Ultra. There is no boolean in the vanilla game that
        // separates them - they differ only in how long the boost was *stored*
        // between the end of the dash and the moment the 1.2x finally lands, so
        // this is a knob rather than a fact, and the whole thing defaults off.
        [SettingName("modoptions_techpopups_delayedultra_window")]
        [SettingRange(1, 300, true)]
        // Counted from the frame the DASH ENDS, not from the frame it starts -
        // during the dash nothing is being stored, and a plain Ultra lands either
        // during the dash or in the handful of frames right after it (well under
        // 0.1 s of storing). Anything that keeps the boost for longer than this
        // was carried somewhere else first, which is the wiki's Delayed Ultra.
        // 50 cs = half a second. It was 35, and the player had dialled it down to 10,
        // which is short enough that an ordinary Ultra landing a couple of frames
        // after the dash ends reads as a Delayed one. A Delayed Ultra is supposed to
        // mean the 1.2x was carried somewhere else first (cornerboost, zip, stall) -
        // half a second is about where that becomes true rather than incidental.
        public int DelayedUltraWindowCs { get; set; } = 50;
        public float DelayedUltraWindow => DelayedUltraWindowCs * 0.01f;
    }

    [SettingName("modoptions_techpopups_submenu_popupsettings")]
    public PopupSettings Popup { get; set; } = new PopupSettings();

    [SettingSubMenu]
    public class PopupSettings
    {
        [SettingName("modoptions_techpopups_popup_scale")]
        [SettingRange(25, 200, true)]
        public int ScaleInt { get; set; } = 100;
        public float Scale => ScaleInt * 0.01f;

        [SettingName("modoptions_techpopups_popup_displaymode")]
        public PopupDisplayMode DisplayMode { get; set; } = PopupDisplayMode.Both;

        // Height above Madeline's head. Raise it if the popup sits on top of her
        // hair, drop it if it collides with something above.
        [SettingName("modoptions_techpopups_popup_labeloffset")]
        [SettingRange(0, 120, true)]
        public int LabelOffsetY { get; set; } = 32;

        // Turn off to make the whole label appear at once instead of letter by
        // letter. Notably faster to read when you are chaining techs quickly.
        [SettingName("modoptions_techpopups_popup_perchar")]
        public bool PerCharEnabled { get; set; } = true;

        // Keep the whole pill on screen. Popups are world-anchored, so a trick
        // done right against a room border used to hang off the viewport and get
        // clipped - a Wallbounce against the left wall lost its icon and half its
        // text. This is the gap kept between the pill and the screen edge, in HUD
        // pixels (1920x1080 space, so 12 is about half a character).
        [SettingName("modoptions_techpopups_popup_edgemargin")]
        [SettingRange(0, 300, true)]
        public int EdgeMargin { get; set; } = 12;

        // Frosted glass instead of flat black: a translucent cool body that lets
        // the level show through, a bright top edge and a thin light rim. Grab a
        // screenshot of a dark level if you want to see why this exists.
        [SettingName("modoptions_techpopups_popup_glass")]
        public bool GlassBackdrop { get; set; } = true;

        // How see-through the pill's glass is. 0 = smoked: the dark rim, the
        // black wash over the body, the grain and the top light are all at full
        // strength. 100 = clear glass: only the bare frost is left. The frost
        // itself is deliberately NOT scaled by this - see DrawPillBackdrop - so
        // the slider no longer decides how much unfrosted level shows through.
        // Every layer is scaled, not just the body colour; scaling only the body
        // used to leave the shadow + grain behind, so even at 100 it still looked
        // like a solid plate.
        [SettingName("modoptions_techpopups_popup_glasstrans")]
        [SettingRange(0, 100, true)]
        public int GlassTransparencyPct { get; set; } = 30;

        // 1 = full-strength glass, 0 = nothing drawn at all. Applied to every
        // layer of DrawPillBackdrop, so the panel's slider really does go all
        // the way to "invisible".
        public float GlassBodyAlpha
        {
            get
            {
                int pct = GlassTransparencyPct;
                if (pct < 0) pct = 0;
                if (pct > 100) pct = 100;
                return 1f - pct * 0.01f;
            }
        }

        // ---- pill outline and drop shadow ----------------------------------
        //
        // Panel-only knobs: they deliberately carry no [SettingName], so they do
        // not appear in Everest's in-game menu and need no dialog entry. The
        // panel's 边框 / 阴影 tab drives them.
        //
        // The two layers these replace used to be hard-coded (a shadow one pixel
        // right and three down at 32%, and a rim one pixel out at 55%). Both are
        // still the defaults, so nothing changes until someone moves a slider.

        public bool PillOutline { get; set; } = true;
        public int  PillOutlineWidth { get; set; } = 2;
        public int  PillOutlineAlphaPct { get; set; } = 55;

        public bool PillShadow { get; set; } = true;
        public int  PillShadowX { get; set; } = 1;
        public int  PillShadowY { get; set; } = 3;
        public int  PillShadowAlphaPct { get; set; } = 32;

        // Soft edge for the shadow, in HUD pixels. 0 = the hard shape the
        // game always drew; the panel's slider smears it with a few passes.
        public int  PillShadowBlur { get; set; } = 0;

        // The pill body: an arbitrary colour at an arbitrary alpha. This IS the
        // glass - a translucent tinted plate - and it has nothing to do with
        // whether the backdrop behind it is blurred; that is the experimental
        // switch further down. Defaults match the old hard-coded plate (26,29,38).
        public int PillBgR { get; set; } = 26;
        public int PillBgG { get; set; } = 29;
        public int PillBgB { get; set; } = 38;

        // Outline / shadow colour, 0..255 per channel. Black by default - the
        // layers used to be hard-coded Color.Black, so the default must match.
        public int PillOutlineR { get; set; } = 0;
        public int PillOutlineG { get; set; } = 0;
        public int PillOutlineB { get; set; } = 0;

        public int PillShadowR { get; set; } = 0;
        public int PillShadowG { get; set; } = 0;
        public int PillShadowB { get; set; } = 0;

        // Real backdrop blur: the pill is filled with an actual blurred copy of
        // the level instead of a flat translucent colour. Costs two tiny
        // 320x180 blur passes plus one 1920x1080 upscale, and only on frames
        // where a label is on screen. Switch it off if your machine dislikes it;
        // the pill falls straight back to the flat glass body.
        [SettingName("modoptions_techpopups_popup_glassblur")]
        public bool GlassBlur { get; set; } = true;

        // How far the frost reaches. The level is blurred at its native 320x180,
        // and the radius grows continuously from 1 to 5 texels (1 texel = 6
        // screen pixels) with three box passes per axis. Measured on a step edge
        // the 10-90% transition is 24 px at strength 0, 54 px at the default 45
        // and 90 px at 100 - about 0.3 to 1.1 times the height of a finished
        // label. Past that the plate only flattens out, which reads as less
        // frosted rather than more, so the slider stops there on purpose.
        [SettingName("modoptions_techpopups_popup_glassblurstrength")]
        [SettingRange(0, 100, true)]
        public int GlassBlurStrengthPct { get; set; } = 45;
    }

    [SettingName("modoptions_techpopups_submenu_miscsettings")]
    public MiscSettings Misc { get; set; } = new MiscSettings();

    [SettingSubMenu]
    public class MiscSettings
    {
        [SettingName("modoptions_techpopups_delayed_wallbounce_conversion_enabled")]
        public bool DelayedWallbounceConversionEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_wavedash_conversion_enabled")]
        public bool WavedashConversionEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_demohyper_conversion_enabled")]
        public bool DemohyperConversionEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_reverse_prefix_enabled")]
        public bool ReversePrefixEnabled { get; set; } = true;

        [SettingName("modoptions_techpopups_dreamdoublejump_conversion_enabled")]
        public bool DreamDoubleJumpConversionEnabled { get; set; } = true;

        // Well-known aliases. "Demodash" is what most people call the crouch
        // dash; "Wavedash" is already the common name so it stays as is.
        [SettingName("modoptions_techpopups_demodash_naming")]
        public bool DemodashNaming { get; set; } = false;
    }

    [SettingName("modoptions_techpopups_submenu_panelsettings")]
    public PanelSettings Panel { get; set; } = new PanelSettings();

    [SettingSubMenu]
    public class PanelSettings
    {
        // The out-of-game tech picker. It is a plain local HTTP server on
        // localhost only - no external access, no remote binding.
        [SettingName("modoptions_techpopups_panel_enabled")]
        public bool Enabled { get; set; } = true;

        [SettingName("modoptions_techpopups_panel_port")]
        [SettingRange(30000, 40000)]
        public int Port { get; set; } = 32274;

        // Fake checkbox: the getter always reads false so the box never stays
        // ticked, and every click fires the setter with true - which opens the
        // panel in your browser. This is the standard Everest trick for a button
        // inside a settings menu.
        [SettingName("modoptions_techpopups_panel_open")]
        public bool OpenNow {
            get { return false; }
            set {
                if (!value) return;
                try {
                    if (string.IsNullOrEmpty(TechPanelServer.Url)) {
                        TechPanelServer.Start();
                    }
                    string url = string.IsNullOrEmpty(TechPanelServer.Url)
                        ? "http://localhost:" + Port + "/"
                        : TechPanelServer.Url;
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                        FileName = url,
                        UseShellExecute = true,
                    });
                } catch { }
            }
        }
    }
}