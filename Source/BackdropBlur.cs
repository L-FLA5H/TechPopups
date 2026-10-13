using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Celeste.Mod.TechPopups;

// ---- real backdrop blur --------------------------------------------------
//
// The pill fakes frosted glass by drawing a blurred copy of the level behind
// itself. Getting hold of that copy has cost four rounds, and every earlier
// attempt failed for a different reason. What they all had in common: they
// tried to read the level out of a render target and then blur it between
// render targets. Both halves of that turned out to be wrong here.
//
// Decompiling Level.Render is what settled it. A frame is built like this:
//
//   SetRenderTarget(GameplayBuffers.Gameplay);  ...   // 320x180, tiles+entities
//   SetRenderTarget(GameplayBuffers.Level);     ...   // 320x180, background+fx
//   SetRenderTarget(null);
//   Draw.SpriteBatch.Draw(GameplayBuffers.Level, ..., CreateScale(6) * ScreenMatrix);
//   ... Level.HudRenderer.Render(level)  and  Level.SubHudRenderer.Render(level)
//
// So there are exactly two moments where a clean level image exists, and only
// one of them is reachable from hook code:
//
//   * Between the blit and the HUD render, the BACKBUFFER holds the finished
//     level at full resolution with nothing drawn on top. Level.Render reaches
//     that point by calling HudRenderer.RenderContent, which is what draws the
//     Tags.HUD entities - i.e. us. Prefixing that call puts us one step before
//     a single label exists on screen.
//
//   * GameplayBuffers.Level *looks* like the obvious source, and that is what
//     the previous versions used. It reads back black. The log proved readback
//     itself was fine (a 4x4 scratch target round-tripped as readback=200,100,50
//     while every real buffer reported src[sum=0 lit=0]), so the buffer really
//     was empty by the time our hook ran. Render targets in FNA carry a
//     RenderTargetUsage: PreserveContents targets are handed back to the device
//     on unbind, and DiscardContents ones are simply invalidated - either way
//     the pixels are not ours to read after Level.Render has moved on.
//
// Blurring *between* render targets was the other half of the problem. Every
// SetRenderTarget unbinds whatever was bound, and if that target is a
// PreserveContents one the device has to stash its contents somewhere - which
// is visible to the user when the stash is the backbuffer. Drawing a blurred
// plate that way is precisely what turned the whole screen black on the frame
// a label appeared: our intermediate buffer came back as the backbuffer.
//
// So this implementation does neither. It reads pixels with
// GraphicsDevice.GetBackBufferData (a plain CPU read: nothing is bound, nothing
// is cleared, no SpriteBatch is touched) and it produces a plain Texture2D, not
// a RenderTarget2D. Nothing in this file can hand anything back to the device,
// which means nothing in this file can disturb the frame.
//
// Pipeline, all on the CPU:
//
//   backbuffer 1920x1080  --GetBackBufferData-->  Color[]
//        --6x6 box average-->             320x180  (the level buffer's own size)
//        --three separable box passes-->  320x180  (radius 1..5, strength-scaled)
//        --SetData-->                     Texture2D
//
// The downsample is not a blur in any useful sense - a 6x6 box average can only
// spread an edge across one output texel, i.e. 6 screen pixels. (An earlier
// comment here claimed a radius of 18 screen pixels; that misreading is part of
// why the first attempts looked under-blurred.) All of the real work is in the
// box passes. Three per axis approximate a Gaussian closely enough that the
// frost shows no boxy banding, and the radius scales with the slider.
//
// Replaying this exact pipeline on a synthetic step edge (deliberately not
// aligned to the 6-pixel grid) measures a 10-90% transition of 24 px at
// strength 0, 54 px at the default 45 and 90 px at 100 - roughly 0.3 to 1.1
// times the height of a finished label, which is the range in which frost still
// shows shapes instead of collapsing into a flat wash.
//
// The radius stops at 5 texels on purpose. Almost all the detail is gone by
// then - the 6x resample eats anything finer than 6 screen pixels and the box
// passes take the rest - so more radius buys very little smoothness and instead
// flattens the plate towards a single average colour. Measured on a pill, 3
// texels and 8 texels came out within 5% of each other on high-frequency
// energy, while the eye reads the flatter one as LESS frosted, not more. Letting
// the slider run that far is what made 100 look weaker than 0.
//
// The readback is the expensive part - it is a synchronous pull of pixels out
// of the GPU, and on a 1080p backbuffer a full-screen one moves 8.3 MB - so it
// runs at most a few times a second, only while a label is on screen,
// and only over the part of the backbuffer the pills actually sample. Rationing
// the full-screen version was not enough: one stall every eight frames still
// read as a stutter. See WantedRect.
//
// That rationing has a consequence the first version of this file missed: the
// texture is up to MaxGrabSpacing out of date, and a label is anchored to
// a WORLD position, so it slides across the screen as the camera pans. Sampling
// the texture under the label therefore showed the level as it stood at grab
// time, i.e. shifted sideways by however far the camera had travelled since -
// 48 screen pixels on the report that exposed this, on a demodash (which keeps
// its horizontal speed, so the camera was panning at about 1 world pixel per
// frame). SourceShift() hands the caller the HUD-space distance to sample
// further along so the pill sits on the world it is anchored to; the grab rate
// then only affects how fresh the CONTENT is, not where it lines up.
//
// That compensation was then silently capped at +/-96 HUD pixels, i.e. 16 world
// pixels, on the theory that sampling further out would ask for pixels the
// texture does not have. It cannot: the readback rectangle is placed at the
// same offset the sample uses, so wherever the sample lands the texture has
// just been refreshed there. All the cap did was truncate the compensation on
// any pan faster than 16 world pixels per grab interval - two frames of camera
// travel at dash speed - which is exactly the "it lines up, but during hypers
// the backdrop sits about half a tile to one side" report. Removed; the only
// bound left is the per-column clamp to the texture, which cannot move the
// image, only repeat its rim.
public static class BackdropBlur
{
    // Same resolution as GameplayBuffers.Level, so on a 1920x1080 backbuffer
    // the downsample is a plain 6x6 box filter with no awkward remainders.
    private const int W = 320;
    private const int H = 180;

    // The virtual space the world blit and the HUD are both drawn in. Labels
    // map their own coordinates onto the 320x180 texture with this.
    public const float HudW = 1920f;
    public const float HudH = 1080f;

    // Two 320x180 plain textures, filled alternately, plus the clock reading
    // and camera each was built with. Swapping in every grab whole read as a
    // 10 Hz flip-book next to a 144 Hz screen, so a label draws BOTH: the older
    // one fading out while the newer fades in over one grab interval. Every
    // layer carries its own camera position, so each is shifted onto TODAY's
    // world independently - what mixes is content of two different ages, never
    // two different alignments.
    private static Texture2D texA, texB;
    private static Color[] shot;       // readback of WantedRect, and only that
    private static int shotW, shotH;   // backbuffer size the readback was sized for
    // The game renders the level into GameplayBuffers.Level - a plain 320x180
    // target - and then blits that onto the 1080p backbuffer. Reading THAT is
    // a tenth of the pixels and needs no downsample pass, which is the whole
    // reason the frost can afford to refresh every frame instead of ten times
    // a second. It needs the target to keep its contents (see
    // BeforeLevelRender); if it ever stops doing so this falls back.
    private static Color[] levelShot;          // 320x180 straight off that target
    // DISABLED: the level target reads back blank in every arrangement tried -
    // at the HUD hook, right after Level.Render returns, and after forcing a
    // PreserveContents rebuild. The target is the right size (320x180, confirmed
    // in GameplayBuffers.Create's IL) and the flag is set, so something else is
    // wiping or detaching it before the read. Until that is understood this path
    // costs a readback's worth of nothing, so it stays off.
    private static bool    levelPathOk = false;
    private static int     levelBlank;         // consecutive all-black reads
    private static bool    lastFromLevel;      // which source the last grab used
    private static bool    levelReloaded;      // this wrapper has been rebuilt once

    private static Color[] work;       // 320x180, downsampled / blurred
    private static Color[] tmp;        // 320x180, blur scratch
    private static int[] psR, psG, psB;// prefix sums, one line of the box pass
    private static int[] recip;        // 65536/n, so the box pass can skip dividing

    // Frame counter of the last frame a popup drew itself. Engine.FrameCounter
    // is a ulong and wraps, hence the separate flag rather than a -1 sentinel.
    private static ulong lastWantedFrame;
    private static bool  everWanted;
    private static bool  ready;
    private static bool  failed;
    private static bool  reported;
    private static int   alignLogs;    // throttles the align line below
    private static int   costLogs;     // throttles the cost line below

    // Where the camera stood on the frame each texture was grabbed from, so a
    // label can work out how far the world under it has travelled since. See
    // GetLayers.
    private static Scene   shotScene;
    private static Vector2 camA, camB;
    private static float   zoomA = 1f, zoomB = 1f;
    private static double  timeA, timeB;   // clock seconds when each grab landed
    private static bool    hasA, hasB;

    // ---- how much of the screen the frost actually needs ----------------
    //
    // Only the pixels under the pills are ever sampled, so only those are worth
    // reading back. TechPopupEntity hands the pill rectangle over in HUD space
    // through Want(); CaptureBackdrop then takes the union from the PREVIOUS
    // frame (it runs before anything has drawn this frame) and grows it by
    // WantPad. Everything outside that union keeps whatever the previous grab
    // left in it, and is never sampled.
    private static ulong   wantFrame;
    private static bool    wantAny;
    private static float   wantL, wantT, wantR, wantB;
    private static Vector2 wantCam;
    private static float   wantZoom = 1f;

    // Slack, in HUD pixels, around the wanted rectangle.
    //
    // The box blur reaches 3 passes x 5 texels = 15 texels = 90 HUD px in from
    // the rim of whatever it is handed, so the readback has to extend at least
    // that far past the pill for the pill's OWN blur input to be real pixels
    // rather than stale ones left by an earlier grab. 108 leaves 3 texels of
    // margin on top of that.
    //
    // It does NOT have to absorb the camera's travel between the grab and the
    // frames that sample it: SourceShift reads the texture at exactly the place
    // the pill occupied on the frame of the grab, so that travel cancels - see
    // WantedRect. The old 150 was slack nobody used, and this rectangle is what
    // decides how many pixels have to cross the bus on every grab.
    private const float WantPad = 108f;

    // Rationing used to be counted in FRAMES, which stopped meaning anything
    // the moment Motion Smoothing entered the picture: that mod runs the render
    // loop several times per logical tick (the log showed ~144 draws a second,
    // against the game's 60), so "every 16 frames" quietly became every 0.10 s
    // instead of 0.27 s and the grabs went up about four-fold. What has to be
    // budgeted is a cost per SECOND, so it is measured in seconds now.
    //
    // The camera's travel is the other half of the bargain, and it is why the
    // grabs cannot simply be spread thinner. The pills are anchored to the
    // foreground layer and SourceShift lines that layer up exactly, but the
    // parallax layers behind it move at a fraction of the camera's speed and
    // are therefore left behind by `travel * (1 - parallax)` - roughly 3 *
    // travel in screen pixels. No offset can repair that: one texture cannot
    // hold two depths at once. Grabbing more often is the only lever, and the
    // two constants below are where that lever is traded against the stutter.
    private static readonly Stopwatch clock = Stopwatch.StartNew();
    private static double lastShotTime;

    // Diagnostic counters behind the `backdrop perf` line. The number that
    // matters is how often the render path runs per second: this file used to
    // ration grabs by frame count, which scaled with that number, and a frame
    // rate low enough to be worth complaining about cannot be told apart from
    // the blur's own cost without measuring it. Five seconds averages a hitch
    // out; grabs are counted over the same window so the two lines up.
    private static int    perfCalls;
    private static int    perfGrabs;
    // Bytes pulled out of the GPU since the last perf line. This is the number
    // that matters for memory: the backbuffer readback is the only place this
    // mod asks the device for anything, and a readback asks the driver to build
    // a staging surface the size of the rectangle. Reporting it next to the
    // process footprint is what tells a leak here apart from a leak anywhere
    // else - see PerfTick.
    private static long   perfReadBytes;
    private static double perfSince;
    private const double PerfWindow = 5.0;

    // Longest a texture may go unreplaced. This is about content freshness
    // only - alignment is the offset's job - so a third of a second is dark.
    private const double MaxGrabSpacing = 0.10;

    // Shortest gap between two grabs: this is the "how much stutter can I
    // afford" knob. One grab costs 2-3.5 ms of GPU sync, so 0.10 s caps that
    // at ~35 ms per second. The frame-counted version was spending ~54 ms per
    // second on this machine once Motion Smoothing had doubled the draw rate.
    private const double MinGrabSpacing = 0.05;

    // The spacing the level-target path can afford: one grab per frame at 60,
    // which is as current as the frost can be without the blit itself becoming
    // the cost. Roughly 1 ms a grab here against 3-5 ms over the backbuffer.
    private const double LevelRefreshSpacing = 1.0 / 60.0;

    // Camera travel, in world pixels, that forces a regrab whatever the clock
    // says. The slip left under the glass is about 3 * travel in screen pixels
    // and the frost's own transition band is 24-90 px wide, so six world
    // pixels is the point where the leftover is still inside the blur.
    private const float PanRefresh = 6f;

    // How often the split-timing line lands in log.txt. Grabs are ~16 frames
    // apart, so 60 of them is roughly one line every sixteen seconds: enough to
    // see a trend, not enough to bury the rest of the log.
    private const int CostLogEvery = 60;

    // How often the camera/offset line lands. SourceShift runs once per label
    // per frame; 24 samples what a pan actually looks like - the misalignment
    // only exists while the camera is moving, so the sparser setting kept
    // landing on the quiet frames - and is still only a few lines a second.
    private const int AlignLogEvery = 24;

    // Set by TechPopupEntity while it builds a pill: "this much of the screen
    // wants frost this frame". Keeps the cost at zero when idle, and tells the
    // next grab which rectangle is worth reading back.
    //
    // x/y/w/h is the pill's rectangle in HUD space as drawn - before
    // SourceShift moves the sample window. The camera is noted alongside it so
    // the next frame can work out where the pill has travelled since.
    public static void Want(Scene scene, float x, float y, float w, float h)
    {
        if (Engine.Scene != scene) return;

        if (wantFrame != Engine.FrameCounter)
        {
            wantFrame = Engine.FrameCounter;
            wantAny = false;
            wantL = wantT = float.MaxValue;
            wantR = wantB = float.MinValue;
            if (scene is Level level && level.Camera != null)
            {
                wantCam = CamFloor(level.Camera.Position);
                wantZoom = level.Camera.Zoom;
            }
        }

        if (!wantAny)
        {
            wantL = x; wantT = y; wantR = x + w; wantB = y + h;
            wantAny = true;
        }
        else
        {
            if (x < wantL) wantL = x;
            if (y < wantT) wantT = y;
            if (x + w > wantR) wantR = x + w;
            if (y + h > wantB) wantB = y + h;
        }

        lastWantedFrame = Engine.FrameCounter;
        everWanted = true;
    }

    // One layer of frost for a label to draw this frame.
    public struct Layer
    {
        public Texture2D Texture;
        public Vector2   Shift;   // sample offset that puts it on TODAY's world
        public float     Alpha;   // share of the pill's opacity
    }

    // Fills `into` (length >= 2) with the frost layers a label should draw and
    // returns how many are live. Zero means "nothing yet - fall back".
    //
    // Two layers are live once two grabs have landed: the older drawn at
    // (1 - mix), the newer at mix, with mix ramping 0 -> 1 across one grab
    // interval and then sitting at 1 until the next grab arrives. The picture
    // therefore changes a little every frame instead of snapping ten times a
    // second. That is the whole point: the grab rate is pinned by what the
    // readback costs, but the APPEARANCE of the frost does not have to follow
    // it.
    //
    // Each layer is shifted by its OWN camera position, so both land on the
    // world as it stands now - what mixes is content of two different ages,
    // never two different alignments.
    public static int GetLayers(Level level, Layer[] into)
    {
        into[0].Texture = null; into[0].Shift = Vector2.Zero; into[0].Alpha = 0f;
        into[1].Texture = null; into[1].Shift = Vector2.Zero; into[1].Alpha = 0f;

        if (failed || !ready || level == null || level.Camera == null) return 0;
        if (!hasA && !hasB) return 0;

        Vector2 camNow = CamFloor(level.Camera.Position);

        // Before a second grab has landed there is nothing to mix with.
        if (!hasA || !hasB)
        {
            bool a = hasA;
            into[0].Texture = a ? texA : texB;
            into[0].Shift   = ShiftTo(camNow, a ? camA : camB, a ? zoomA : zoomB);
            into[0].Alpha   = 1f;
            ReportShift(into[0].Shift);
            return 1;
        }

        bool newerIsA = timeA >= timeB;
        double tn = newerIsA ? timeA : timeB;
        double to = newerIsA ? timeB : timeA;
        double span = tn - to;
        if (span < 0.05) span = 0.05;              // a burst of regrabs must not divide by ~0

        double t = (clock.Elapsed.TotalSeconds - tn) / span;
        if (t < 0.0) t = 0.0;
        if (t > 1.0) t = 1.0;
        float mix = (float)t;

        into[0].Texture = newerIsA ? texB : texA;
        into[0].Shift   = ShiftTo(camNow, newerIsA ? camB : camA, newerIsA ? zoomB : zoomA);
        // NB the older layer keeps its FULL alpha rather than (1 - mix). With
        // premultiplied "over" blending the total opacity of two stacked layers
        // is a1 + a0*(1 - a1), so fading both would leave the frost at 0.75 of
        // its alpha halfway through - the see-through flicker that made the
        // first version of this look wrong. Holding layer 0 at 1 and fading
        // layer 1 in from 0 gives mix*new + (1-mix)*old at a steady opacity.
        into[0].Alpha   = 1f;

        into[1].Texture = newerIsA ? texA : texB;
        into[1].Shift   = ShiftTo(camNow, newerIsA ? camA : camB, newerIsA ? zoomA : zoomB);
        into[1].Alpha   = mix;

        ReportShift(into[1].Shift);
        return 2;
    }

    // How far along its texture a layer has to sample so that the pill sits on
    // the world it is anchored to.
    //
    // The label is pinned to a world position, so when the camera pans the label
    // pans with it - but a texture does not. A column drawn at HUD x would
    // otherwise show whatever level content happened to be at x when that grab
    // was taken, i.e. the level as it stood then, offset by the camera's travel
    // since. Sampling at x + shift instead picks the content under x RIGHT NOW.
    //
    // One world pixel is 6 HUD pixels, so the travel in HUD space is
    // 6 * zoom * (camera now - camera at grab). The zoom is the one that grab
    // was built at; a room that changes zoom with a label on screen is rare
    // enough that the residue is not worth carrying a second zoom here.
    private static Vector2 ShiftTo(Vector2 camNow, Vector2 camAt, float zoomAt)
    {
        Vector2 moved = (camNow - camAt) * (6f * zoomAt);

        // Mirror mode turns the world around on the X axis, so the same camera
        // travel walks the texture the other way.
        if (SaveData.Instance != null && SaveData.Instance.Assists.MirrorMode)
            moved.X = -moved.X;

        // Deliberately NOT clamped. See the file header: an earlier version cut
        // this off at +/-96 HUD pixels, and that cut-off was the entire "the
        // backdrop sits to one side while the camera is panning" bug. The
        // readback rectangle is placed at this same offset, so there is always
        // fresh pixels where the sample lands; the texture's own edge is again
        // handled per column in DrawBlurredCapsule.
        return moved;
    }
    // The camera is floored to whole world pixels before the view matrix is
    // built - that is what Monocle's Camera.UpdateMatrices does - so a texture
    // which is a picture of that matrix is only ever aligned to a floored
    // camera. Rounding the same way here keeps every offset in this file in the
    // units the picture was drawn in; skipping it is worth up to one world
    // pixel, i.e. six screen pixels, of permanent slip.
    //
    // Monocle.Calc.Floor rounds too, but what reached the log through it was
    // not rounded (camFloor came out identical to camRaw), so the rounding is
    // spelled out here rather than inherited.
    private static Vector2 CamFloor(Vector2 p)
    {
        return new Vector2(MathF.Floor(p.X), MathF.Floor(p.Y));
    }

    // Camera position of the most recent grab, for the "camera has not moved,
    // so nothing behind the pills can have changed" early-out.
    private static Vector2 NewestCam()
    {
        if (!hasA) return camB;
        if (!hasB) return camA;
        return timeA >= timeB ? camA : camB;
    }

    private static float NewestZoom()
    {
        if (!hasA) return zoomB;
        if (!hasB) return zoomA;
        return timeA >= timeB ? zoomA : zoomB;
    }

    // Diagnostic line. One earlier round was spent guessing at a misalignment
    // because the log only carried the offset, not the camera numbers behind it;
    // the raw (unfloored) Position is printed alongside the floored one so a
    // fractional camera cannot hide again. Throttled to a few lines a second and
    // silent while the offset is zero.
    private static void ReportShift(Vector2 moved)
    {
        if (moved.X == 0f && moved.Y == 0f) return;
        if ((++alignLogs % AlignLogEvery) != 0) return;

        Level level = Engine.Scene as Level;
        if (level == null || level.Camera == null) return;

        Logger.Log(LogLevel.Info, "TechPopups",
                   "backdrop align: camRaw=" + level.Camera.Position.ToString()
                   + " camFloor=" + CamFloor(level.Camera.Position).ToString()
                   + " camShot=" + NewestCam().ToString()
                   + " zoom=" + NewestZoom().ToString()
                   + " gap=" + ((int)Math.Round((clock.Elapsed.TotalSeconds - lastShotTime) * 1000.0)) + "ms"
                   + " -> shift=" + moved.ToString());
    }

    public static void Unload()
    {
        failed = false;
        ready = false;
        reported = false;
        alignLogs = 0;
        everWanted = false;
        lastWantedFrame = 0;
        lastShotTime = 0;
        perfCalls = 0;
        perfGrabs = 0;
        perfReadBytes = 0;
        perfSince = 0.0;
        wantFrame = 0;
        wantAny = false;
        wantCam = Vector2.Zero;
        wantZoom = 1f;
        shotScene = null;
        shot = null;
        shotW = shotH = 0;
        costLogs = 0;
        work = null;
        tmp = null;
        psR = psG = psB = null;
        recip = null;
        texA?.Dispose();
        texB?.Dispose();
        texA = texB = null;
        hasA = hasB = false;
        timeA = timeB = 0.0;
        camA = camB = Vector2.Zero;
        zoomA = zoomB = 1f;
    }

    // Called from the HudRenderer.RenderContent prefix: the level has just been
    // blitted to the backbuffer and no HUD entity has drawn yet. This is the
    // only moment in the frame where the backbuffer holds a clean level image,
    // and it is also the last moment before our own labels draw.
    //
    // Everything here is read-only with respect to the device, so a failure can
    // cost the blur and nothing else.
    // One `backdrop perf` line every PerfWindow seconds: how often the render
    // path is entered, and how many of those entries actually took a grab.
    // The first is the real frame rate as the game's own loop sees it; the
    // second is what the blur is spending on. If the first is low while the
    // second is near zero, the stutter is not this feature.
    private static void PerfTick()
    {
        perfCalls++;
        double t = clock.Elapsed.TotalSeconds;
        if (perfSince == 0.0) { perfSince = t; return; }
        double span = t - perfSince;
        if (span < PerfWindow) return;

        // managed = the .NET heap, working = what the process actually holds
        // (managed heap plus textures, driver allocations, everything native).
        // A leak that moves `working` while `managed` stays put is not a
        // garbage-collection problem and no amount of array reuse will fix it;
        // a leak in both points here. readback is how many bytes the grabs in
        // this window pulled off the GPU, which is what the driver has to have
        // a surface for.
        Logger.Log(LogLevel.Info, "TechPopups",
                   "backdrop perf: render=" + Math.Round(perfCalls / span, 1)
                   + " calls/s, grabs=" + Math.Round(perfGrabs / span, 2)
                   + "/s over " + Math.Round(span, 2) + "s"
                   + " | memory: managed=" + (GC.GetTotalMemory(false) >> 20)
                   + "MB working=" + (Environment.WorkingSet >> 20)
                   + "MB readback=" + (perfReadBytes >> 10) + "KB");
        perfCalls = 0;
        perfGrabs = 0;
        perfReadBytes = 0;
        perfSince = t;
    }

    // The level-target grab, run right after Level.Render returns.
    //
    // Reading that target from the HUD hook was too early - it came back black
    // every time, which is what the first version of this path did. Level.Render
    // is where the target gets filled and then unbound, so the instant it
    // returns is both "complete" and "not bound": the moment GetData can see it.
    //
    // The backbuffer path keeps the HUD hook, because a backbuffer read must
    // happen BEFORE the HUD draws over the level image. Exactly one of the two
    // runs - see CaptureBackdrop.
    public static void AfterLevelRender(Level level)
    {
        if (failed || !levelPathOk) return;
        if (!everWanted || Engine.FrameCounter - lastWantedFrame > 2) return;
        if (level == null || level.Camera == null) return;

        // A camera that has not moved cannot have changed what sits behind the
        // pills.
        if (ready && CamFloor(level.Camera.Position) == NewestCam()
                  && level.Camera.Zoom == NewestZoom())
            return;

        Vector2 camNow = CamFloor(level.Camera.Position);
        double now = clock.Elapsed.TotalSeconds;
        double gap = now - lastShotTime;
        if (gap < LevelRefreshSpacing) return;
        lastShotTime = now;

        try
        {
            var device = Engine.Instance.GraphicsDevice;
            var pp = device.PresentationParameters;
            int sw = pp.BackBufferWidth;
            int sh = pp.BackBufferHeight;
            if (sw <= 0 || sh <= 0) return;

            EnsureOutput(device);

            // A stale texture would be drawn whole, so the grab only counts if
            // the pixels actually arrived.
            if (!TryReadLevel()) return;

            Rectangle core;
            WantedRect(level, sw, sh, out core);

            long t1 = Stopwatch.GetTimestamp();
            Blur(sw, sh, core);
            long t2 = Stopwatch.GetTimestamp();

            bool intoA = !hasA || (hasB && timeA <= timeB);
            (intoA ? texA : texB).SetData(work);
            long t3 = Stopwatch.GetTimestamp();

            Vector2 c = camNow;
            float   z = level.Camera.Zoom;
            if (intoA) { camA = c; zoomA = z; timeA = now; hasA = true; }
            else       { camB = c; zoomB = z; timeB = now; hasB = true; }

            ready = true;
            perfGrabs++;
            ReportLevel(t1, t2, t3);
        }
        catch (Exception e)
        {
            levelPathOk = false;
            Logger.Log(LogLevel.Warn, "TechPopups",
                       "backdrop blur: level path failed (" + e.Message
                       + ") - back to the backbuffer path");
        }
    }

    // The level path has no readback/downsample split to report, so it prints
    // its own, shorter line.
    private static void ReportLevel(long t1, long t2, long t3)
    {
        if ((++costLogs % CostLogEvery) != 0) return;

        double k = 1000.0 / Stopwatch.Frequency;
        Logger.Log(LogLevel.Info, "TechPopups",
                   "backdrop blur cost: src=level 320x180 read=0ms blur="
                   + ((t2 - t1) * k).ToString("F2")
                   + "ms upload=" + ((t3 - t2) * k).ToString("F2")
                   + "ms total=" + ((t3 - t1) * k).ToString("F2") + "ms");
    }

    // Asks GameplayBuffers.Level to hold on to its contents.
    //
    // It is built with DiscardContents, so its pixels are gone the moment the
    // engine unbinds it - that is why an earlier attempt read back nothing but
    // black. Run from Level.Render's prefix: the instant before the engine
    // binds the target and clears it with Level.BackgroundColor. That is the
    // one point in the frame where the target is definitely not bound, and the
    // engine paints over it completely a few instructions later, so flipping
    // the flag cannot change what the game draws.
    //
    // Re-checked every frame rather than once: GameplayBuffers.Create() can run
    // again at any time and would hand over a fresh wrapper whose flag is false.
    public static void BeforeLevelRender()
    {
        if (!levelPathOk || failed) return;

        try
        {
            VirtualRenderTarget vrt = GameplayBuffers.Level;
            if (vrt == null || vrt.IsDisposed) { levelPathOk = false; return; }

            // The flag and the actual target are two different things: the RT is
            // built FROM the flag, so a target that happens to already carry the
            // flag may still have been created without it (GameplayBuffers.Create
            // sets Preserve after building in some paths). Reload() once,
            // unconditionally, so the target on the device is certainly the
            // preserving kind - and once per wrapper, because Create() can hand
            // over a new one at any time.
            bool needReload = !vrt.Preserve || !levelReloaded;
            if (!needReload) return;

            var device = Engine.Instance.GraphicsDevice;
            var bound = device.GetRenderTargets();
            if (bound != null)
            {
                foreach (var b in bound)
                    if (ReferenceEquals(b.RenderTarget, vrt.Target)) return;
            }

            vrt.Preserve = true;
            vrt.Reload();
            levelReloaded = true;

            Logger.Log(LogLevel.Info, "TechPopups",
                       "backdrop blur: GameplayBuffers.Level rebuilt as PreserveContents");
        }
        catch (Exception e)
        {
            levelPathOk = false;
            Logger.Log(LogLevel.Warn, "TechPopups",
                       "backdrop blur: could not preserve GameplayBuffers.Level (" + e.Message + ")");
        }
    }

    // One 320x180 read straight off the level target, into work[].
    //
    // A target that was NOT preserved hands back nothing but zeroes, and that
    // is the one failure that would silently turn the glass black - so the
    // result is sampled before it is trusted, and three blanks in a row retire
    // this path for good. Everything else (a disposed target, a resize, a
    // thrown exception) retires it immediately.
    private static bool TryReadLevel()
    {
        if (!levelPathOk) return false;

        try
        {
            VirtualRenderTarget vrt = GameplayBuffers.Level;
            if (vrt == null || vrt.IsDisposed || !vrt.Preserve) return false;

            RenderTarget2D rt = vrt;                  // op_Implicit -> .Target
            if (rt == null || rt.IsDisposed) return false;
            if (rt.Width != W || rt.Height != H) return false;

            if (levelShot == null || levelShot.Length != W * H)
                levelShot = new Color[W * H];

            rt.GetData(levelShot);

            int sum = 0;
            for (int i = 0; i < levelShot.Length; i += 61)
                sum += levelShot[i].R + levelShot[i].G + levelShot[i].B;

            if (sum == 0)
            {
                if (++levelBlank > 3)
                {
                    levelPathOk = false;
                    Logger.Log(LogLevel.Warn, "TechPopups",
                               "backdrop blur: GameplayBuffers.Level reads back blank - "
                               + "back to the backbuffer path");
                }
                return false;
            }

            levelBlank = 0;
            Array.Copy(levelShot, work, W * H);
            return true;
        }
        catch (Exception e)
        {
            levelPathOk = false;
            Logger.Log(LogLevel.Warn, "TechPopups",
                       "backdrop blur: GameplayBuffers.Level unusable (" + e.Message
                       + ") - back to the backbuffer path");
            return false;
        }
    }

    // Thin wrapper whose only job is to make sure nothing this file does can
    // ever take the game with it.
    //
    // CaptureBackdrop runs from a HudRenderer render hook that half the mod list
    // also hooks, so an exception raised in it does not just lose a frame: it
    // unwinds through every hook above it, and Monocle has no way to carry on.
    // That is exactly what happened - a NullReferenceException from the camera
    // dereference below killed Celeste on three launches in a row, reported
    // through MotionSmoothing's HiresCameraSmoother hook.
    //
    // A failed readback should cost the frost, not the session, so anything that
    // escapes is caught here, logged once, and the feature switches itself off.
    public static void CaptureBackdrop(Scene scene)
    {
        try
        {
            CaptureBackdropCore(scene);
        }
        catch (Exception e)
        {
            failed = true;
            Logger.Log(LogLevel.Warn, "TechPopups",
                       "backdrop blur disabled: " + e.Message);
        }
    }

    private static void CaptureBackdropCore(Scene scene)
    {
        PerfTick();
        if (failed) return;

        // When the level target is readable the grab belongs to
        // AfterLevelRender instead: a backbuffer read has to happen before the
        // HUD paints over the level, but the level target is only complete
        // once Level.Render has returned. Only one of the two ever runs.
        if (levelPathOk) return;

        // A different scene (a new level, or the room the player just walked
        // into) means the texture in hand belongs somewhere else entirely: drop
        // it and grab a fresh one on this very frame instead of waiting out
        // the next grab, and forget the framing it was built with.
        if (!ReferenceEquals(shotScene, scene))
        {
            shotScene = scene;
            lastShotTime = 0;
            ready = false;
            hasA = hasB = false;
            wantAny = false;
        }

        // No label has drawn recently -> there is nothing to blur, and the
        // readback is far too expensive to run for its own sake.
        if (!everWanted || Engine.FrameCounter - lastWantedFrame > 2) return;

        // Everything below this line reads the camera, and `scene as Level`
        // genuinely does come back null here - this hook is reached with a null
        // scene while a level loads, and with non-level scenes that still render
        // a HUD. There was no guard on the dereference further down, which is
        // the crash the wrapper above exists for.
        var level = scene as Level;
        if (level == null || level.Camera == null) return;

        // A camera that has not moved cannot have changed what sits behind the
        // pills, and the frost is coarse enough that animated room furniture
        // under it is not worth a readback either. Reusing the texture costs
        // nothing, and removes the stall outright whenever the player holds
        // still - which is most of the time between tricks.
        //
        // The grab clock is deliberately NOT advanced here: the moment the
        // camera moves again the grab may happen at once, rather than waiting
        // out a gap it never needed.
        if (ready
            && CamFloor(level.Camera.Position) == NewestCam()
            && level.Camera.Zoom == NewestZoom())
            return;

        // Grab when the clock says the texture is going stale, or when the
        // camera has travelled far enough that the layers under the pills have
        // come apart. The travel test is what keeps a dash aligned, and it is
        // the only one a frame rate cannot distort - which is exactly why the
        // budget below is a duration rather than a frame count.
        Vector2 camNow = CamFloor(level.Camera.Position);
        double now = clock.Elapsed.TotalSeconds;
        double gap = now - lastShotTime;

        // With the level target readable a grab is cheap enough to run every
        // frame, which is what lets the frost keep up with the picture instead
        // of lagging a tenth of a second behind it. The backbuffer path keeps
        // the old, much tighter budget.
        double minGap = levelPathOk ? LevelRefreshSpacing : MinGrabSpacing;
        double maxGap = levelPathOk ? LevelRefreshSpacing : MaxGrabSpacing;

        bool due = gap >= maxGap;
        bool panning = gap >= minGap
                       && Vector2.Distance(camNow, NewestCam()) >= PanRefresh;
        if (!due && !panning) return;
        lastShotTime = now;

        try
        {
            var device = Engine.Instance.GraphicsDevice;
            var pp = device.PresentationParameters;
            int sw = pp.BackBufferWidth;
            int sh = pp.BackBufferHeight;
            if (sw <= 0 || sh <= 0) return;

            EnsureOutput(device);

            // `core` is the slice of the texture a pill actually samples, and
            // both pixel sources feed the same Blur() with it. Only where the
            // pixels come from differs: the level target (one 320x180 read, no
            // downsample, cheap enough for every frame) or a rectangle of the
            // 1080p backbuffer (most of a millisecond per row of padding, so
            // rationed to a few grabs a second).
            // Nothing has asked for pixels since the last scene change, so
            // there is no rectangle to fetch: WantedRect would hand back the
            // whole screen, i.e. an 8 MB readback and an 8 MB array for pixels
            // no pill is going to sample. It is only ever false on the frame
            // right after entering a room (Want runs during the draw, this
            // runs before it), so skipping costs one frame of frost.
            if (!wantAny) return;

            Rectangle core;
            Rectangle rect = WantedRect(level, sw, sh, out core);

            long t0 = Stopwatch.GetTimestamp();
            bool fromLevel = TryReadLevel();
            long t1, t2;

            if (fromLevel)
            {
                t1 = t2 = Stopwatch.GetTimestamp();
            }
            else
            {
                int n = rect.Width * rect.Height;
                if (n <= 0) return;

                // Grow only, and with slack - never size the array to the
                // rectangle of the moment.
                //
                // The wanted rectangle is not a fixed box: it follows the label
                // and the camera, is grown by WantPad and clamped to the screen,
                // so its pixel count differs from one grab to the next. Sizing
                // the array to exactly that allocated a brand new one nearly
                // every grab - 600 KB for a pill-sized rectangle, 8 MB whenever
                // it degenerates to the whole screen. Both are far past the
                // 85 KB mark where the garbage collector puts an object on the
                // large object heap, which it does not compact, and the
                // backbuffer path takes fifteen to twenty grabs a second. That
                // is tens of megabytes of LOH garbage per second: the process
                // climbs towards the machine's total RAM and the collector only
                // wins once it is already there. (Reported as 37-54 GB of a 64
                // GB machine, with 37 GB on a fresh launch.)
                //
                // Nothing reads this array's length - Downsample walks exactly
                // rect.Width * rect.Height entries from index 0, which is what
                // GetBackBufferData fills - so an oversized one is harmless.
                if (shot == null || shotW != sw || shotH != sh || shot.Length < n)
                {
                    shot = new Color[n + (n >> 1) + (1 << 16)];
                    shotW = sw;
                    shotH = sh;
                }

                device.GetBackBufferData(rect, shot, 0, n);
                perfReadBytes += (long)n * 4;
                t1 = Stopwatch.GetTimestamp();

                Downsample(sw, sh, rect);
                t2 = Stopwatch.GetTimestamp();
            }

            lastFromLevel = fromLevel;
            Blur(sw, sh, core);
            long t3 = Stopwatch.GetTimestamp();

            // Into whichever slot is older, then let GetLayers fade it in. The
            // label draws both slots, so the swap itself is not a visible event.
            bool intoA = !hasA || (hasB && timeA <= timeB);
            (intoA ? texA : texB).SetData(work);
            long t4 = Stopwatch.GetTimestamp();

            // Remember the framing this grab was taken with, so labels can line
            // the texture up with a camera that has moved since.
            {
                Vector2 c = CamFloor(level.Camera.Position);
                float   z = level.Camera.Zoom;
                if (intoA) { camA = c; zoomA = z; timeA = now; hasA = true; }
                else       { camB = c; zoomB = z; timeB = now; hasB = true; }
            }

            ready = true;
            perfGrabs++;
            Report(rect);
            ReportCost(t0, t1, t2, t3, t4, rect);
        }
        catch (Exception e)
        {
            failed = true;
            Logger.Log(LogLevel.Warn, "TechPopups",
                       "backdrop blur disabled: " + e.Message);
        }
    }

    private static void EnsureOutput(GraphicsDevice device)
    {
        if (texA != null && !texA.IsDisposed) return;

        texA?.Dispose();
        texB?.Dispose();
        texA = new Texture2D(device, W, H, false, SurfaceFormat.Color);
        texB = new Texture2D(device, W, H, false, SurfaceFormat.Color);
        work = new Color[W * H];
        tmp = new Color[W * H];

        // Longest line + 1, so the same arrays serve both the horizontal
        // (length W) and the vertical (length H) pass.
        int n = Math.Max(W, H) + 1;
        psR = new int[n];
        psG = new int[n];
        psB = new int[n];

        // 65536/n for every window width the box pass can ask for. Radius tops
        // out at MaxRadius, so the widest window is 2 * MaxRadius + 1 texels.
        recip = new int[2 * MaxRadius + 2];
        for (int i = 1; i < recip.Length; i++)
            recip[i] = (int)(65536.0 / i + 0.5);

        hasA = hasB = false;
        ready = false;
    }

    // backbuffer -> 320x180 with a 6x6 box average.
    //
    // This is a resample, not a blur: it can only soften an edge across one
    // output texel, i.e. 6 screen pixels. Everything that makes the pill look
    // frosted happens later, in Blur().
    //
    // Each output pixel samples a 3x3 grid inside its 6x6 block rather than all
    // 36 texels: the block is only six screen pixels across, so the samples are
    // neighbours and the average is the same to within a rounding step, at a
    // quarter of the cost. The result is then blurred anyway.
    //
    // Only the texels the readback rectangle covers are recomputed. The rest of
    // the 320x180 image keeps whatever the previous grabs left there. That is
    // deliberate: those texels are stale, but they are also further from any
    // pill than WantPad, and the blur reaches at most 15 texels, so stale
    // pixels can never be blended into a pill's own frost.
    private static void Downsample(int sw, int sh, Rectangle rect)
    {
        int rx = rect.X, ry = rect.Y, rw = rect.Width, rh = rect.Height;

        // The texel range the readback touches.
        int tx0 = rx * W / sw;
        int tx1 = Math.Min(W - 1, (rx + rw - 1) * W / sw);
        int ty0 = ry * H / sh;
        int ty1 = Math.Min(H - 1, (ry + rh - 1) * H / sh);

        for (int y = ty0; y <= ty1; y++)
        {
            int sy0 = y * sh / H;
            int sy1 = Math.Max(sy0 + 1, (y + 1) * sh / H);
            if (sy1 > sh) sy1 = sh;
            // Clip to the readback: a texel straddling its rim averages only
            // the lines that were actually fetched.
            if (sy0 < ry) sy0 = ry;
            if (sy1 > ry + rh) sy1 = ry + rh;
            if (sy0 >= sy1) continue;

            for (int x = tx0; x <= tx1; x++)
            {
                int sx0 = x * sw / W;
                int sx1 = Math.Max(sx0 + 1, (x + 1) * sw / W);
                if (sx1 > sw) sx1 = sw;
                if (sx0 < rx) sx0 = rx;
                if (sx1 > rx + rw) sx1 = rx + rw;
                if (sx0 >= sx1) continue;

                int r = 0, g = 0, b = 0, n = 0;
                for (int yy = sy0; yy < sy1; yy += 2)
                {
                    int row = (yy - ry) * rw;
                    for (int xx = sx0; xx < sx1; xx += 2)
                    {
                        var c = shot[row + (xx - rx)];
                        r += c.R;
                        g += c.G;
                        b += c.B;
                        n++;
                    }
                }

                if (n == 0) continue;
                work[y * W + x] = new Color(r / n, g / n, b / n, 255);
            }
        }
    }

    // The part of the backbuffer worth reading back: the union of the pill
    // rectangles the previous frame wanted, moved to wherever the camera has
    // taken them since, grown by WantPad and clamped to the screen.
    //
    // That "moved" step is what keeps the pad small. A pill is anchored to a
    // WORLD position, and SourceShift samples the texture at the point the pill
    // occupied on the frame of the grab - exactly the pill's current screen
    // position minus the camera travel. So the texture only ever has to cover
    // the pill as it stands on the frame of the grab, and the pad never has to
    // absorb a whole grab interval of panning however fast the camera moves.
    //
    // Mirror mode flips the world on X, which would send the arithmetic below
    // at the wrong half of the screen, so it simply keeps the old full-screen
    // readback. Mirror mode is an assist; nobody races with it on.
    //
    // `core` comes back as the part of the screen the pills will actually
    // sample - their own rectangles on the frame of the grab - which is what
    // Blur needs to know. It always sits inside the returned rectangle, by
    // WantPad on every side.
    private static Rectangle WantedRect(Level level, int sw, int sh, out Rectangle core)
    {
        Rectangle full = new Rectangle(0, 0, sw, sh);

        if (level == null || level.Camera == null) { core = full; return full; }
        if (SaveData.Instance != null && SaveData.Instance.Assists.MirrorMode)
        {
            core = full;
            return full;
        }
        if (!wantAny || wantR <= wantL || wantB <= wantT) { core = full; return full; }

        Vector2 moved = (CamFloor(level.Camera.Position) - wantCam) * (6f * wantZoom);

        float kx = sw / HudW;
        float ky = sh / HudH;

        int cx0 = (int)MathF.Floor((wantL - moved.X) * kx);
        int cy0 = (int)MathF.Floor((wantT - moved.Y) * ky);
        int cx1 = (int)MathF.Ceiling((wantR - moved.X) * kx);
        int cy1 = (int)MathF.Ceiling((wantB - moved.Y) * ky);

        int x0 = (int)MathF.Floor((wantL - moved.X - WantPad) * kx);
        int y0 = (int)MathF.Floor((wantT - moved.Y - WantPad) * ky);
        int x1 = (int)MathF.Ceiling((wantR - moved.X + WantPad) * kx);
        int y1 = (int)MathF.Ceiling((wantB - moved.Y + WantPad) * ky);

        if (x0 < 0) x0 = 0;
        if (y0 < 0) y0 = 0;
        if (x1 > sw) x1 = sw;
        if (y1 > sh) y1 = sh;

        // A rectangle that came out degenerate, or one that is most of the
        // screen anyway, is not worth the bookkeeping: fall back to the lot.
        // The core follows: blurring everything is slower but always correct.
        if (x1 - x0 < 16 || y1 - y0 < 16) { core = full; return full; }
        if (x1 - x0 > sw - 32 || y1 - y0 > sh - 32) { core = full; return full; }

        core = new Rectangle(cx0, cy0, cx1 - cx0, cy1 - cy0);
        return new Rectangle(x0, y0, x1 - x0, y1 - y0);
    }

    // Three separable box passes per axis over the 320x180 image.
    //
    // Radius is 1..5 texels - never 0, so the slider cannot land on "the raw 6x
    // resample", which is a mosaic of hard 6-pixel blocks rather than a weak
    // blur. One texel is 6 screen pixels. Three passes per axis straighten the
    // box kernel out into something close to a Gaussian, so even the largest
    // radius leaves no boxy banding.
    //
    // Measured on a synthetic step edge by replaying this exact pipeline: the
    // 10-90% transition is 24 px at strength 0, 54 px at the default 45 and
    // 90 px at 100.
    private static void Blur(int sw, int sh, Rectangle core)
    {
        int radius = RadiusFor(TechPopupsModule.Settings.Popup.GlassBlurStrengthPct);

        // Only the texels a pill actually samples, plus the blur's own reach, are
        // worth touching. Everything else keeps whatever the previous grab left
        // there and is never read: the sample window lands inside `core`, and the
        // readback rectangle that feeds Downsample is `core` grown by WantPad -
        // 25 texels, against a reach of at most 15 - so the rectangle below is
        // fully covered by pixels that were refreshed this very frame.
        //
        // Before this the passes ran over the whole 320x180 every grab, i.e.
        // 57,600 texels per pass whether or not a label is anywhere near them.
        // The timing line said blur = 5.5-6.2 ms, and it did not move with the
        // strength slider (the prefix-sum passes cost the same at radius 1 and
        // radius 5), which is what gave the game its once-every-eight-frames
        // hitch. A pill-sized rectangle is around a tenth of that.
        int reach = 3 * radius;                     // three passes per axis
        int tx0 = Math.Max(0, core.X * W / sw - reach);
        int tx1 = Math.Min(W - 1, (core.X + core.Width - 1) * W / sw + reach);
        int ty0 = Math.Max(0, core.Y * H / sh - reach);
        int ty1 = Math.Min(H - 1, (core.Y + core.Height - 1) * H / sh + reach);

        for (int i = 0; i < 3; i++)
        {
            BoxPass(radius, true,  tx0, tx1, ty0, ty1);
            BoxPass(radius, false, ty0, ty1, tx0, tx1);
        }
    }

    // The single place the slider turns into a texel radius, so the log line and
    // the blur itself can never disagree. The floor is 1, not 0: at 0 the plate
    // would be filled with the raw 6x resample, which is a mosaic of hard
    // 6-pixel blocks and reads as "cut out of a checkerboard", not as weak
    // frosted glass. That mosaic was the other half of why a strength of 0 used
    // to look stronger than 100.
    private const int MaxRadius = 5;   // texels; the widest the slider can ask for

    private static int RadiusFor(int pct)
    {
        if (pct < 0) pct = 0;
        if (pct > 100) pct = 100;
        return 1 + (int)MathF.Round(pct * (MaxRadius - 1) / 100f);   // 1..MaxRadius
    }

    // One separable box pass over a rectangle of the image, done with a prefix
    // sum per line so the cost is proportional to the area rather than to the
    // radius - radius 5 costs the same as radius 1. Edges clamp: a pixel at the
    // rim averages a shorter window rather than pulling in colour from the far
    // side of the image.
    //
    // i0..i1 is the range along each line that gets written, l0..l1 the lines
    // that get touched; Blur decides both. Two details matter for speed and were
    // not there before: the prefix sum only spans the window the written range
    // reads, and the divide is a rounded multiply-shift against a small table of
    // reciprocals, because three integer divisions per texel per pass was most
    // of the blur's cost.
    private static void BoxPass(int radius, bool horizontal, int i0, int i1, int l0, int l1)
    {
        int len      = horizontal ? W : H;
        int iStep    = horizontal ? 1 : W;   // step along a line
        int lineStep = horizontal ? W : 1;   // step between lines

        int lo = i0 - radius; if (lo < 0) lo = 0;
        int hi = i1 + radius; if (hi > len - 1) hi = len - 1;
        int span = hi - lo + 1;

        for (int line = l0; line <= l1; line++)
        {
            int at = line * lineStep;

            psR[0] = psG[0] = psB[0] = 0;
            for (int i = 0; i < span; i++)
            {
                Color s = work[at + (lo + i) * iStep];
                psR[i + 1] = psR[i] + s.R;
                psG[i + 1] = psG[i] + s.G;
                psB[i + 1] = psB[i] + s.B;
            }

            for (int i = i0; i <= i1; i++)
            {
                int a = i - radius; if (a < lo) a = lo;
                int b = i + radius; if (b > hi) b = hi;
                int inv = recip[b - a + 1];

                // 65536/n, rounded, so `sum * inv >> 16` lands within half a
                // step of sum/n. Sums are 255 * 11 at the very most, so the
                // product stays well inside an int.
                byte r  = (byte)(((psR[b - lo + 1] - psR[a - lo]) * inv + 32768) >> 16);
                byte g  = (byte)(((psG[b - lo + 1] - psG[a - lo]) * inv + 32768) >> 16);
                byte bl = (byte)(((psB[b - lo + 1] - psB[a - lo]) * inv + 32768) >> 16);

                // (byte) casts, not the clamping int constructor: every value
                // here is already the average of bytes.
                tmp[at + i * iStep] = new Color(r, g, bl, (byte)255);
            }
        }

        // Each pass reads `work` and writes `tmp`, so the line sweeps above can
        // never see a half-updated image.
        Array.Copy(tmp, work, work.Length);
    }

    // One-off line in log.txt so a "the blur does nothing" report can be told
    // apart from "the blur never ran". `sampled` is the brightness of the
    // finished 320x180 image: sum is the total, lit counts texels that are not
    // black. Both being 0 means the readback itself came back empty.
    private static void Report(Rectangle rect)
    {
        if (reported) return;
        reported = true;

        long sum = 0;
        int lit = 0;
        if (work != null)
        {
            for (int i = 0; i < work.Length; i += 97)
            {
                int v = work[i].R + work[i].G + work[i].B;
                sum += v;
                if (v > 12) lit++;
            }
        }

        Logger.Log(LogLevel.Info, "TechPopups",
                   "backdrop blur on: source=" + shotW + "x" + shotH
                   + " rect=" + rect.Width + "x" + rect.Height
                   + " (" + Percent(rect) + " of the screen)"
                   + " -> " + W + "x" + H
                   + " sampled[sum=" + sum + " lit=" + lit + "]"
                   + " strength=" + TechPopupsModule.Settings.Popup.GlassBlurStrengthPct
                   + " radius=" + RadiusFor(TechPopupsModule.Settings.Popup.GlassBlurStrengthPct)
                   + " (texels, 1 texel = 6px)");
    }

    private static string Percent(Rectangle rect)
    {
        long all = (long)shotW * shotH;
        if (all <= 0) return "?%";
        return ((100.0 * rect.Width * rect.Height) / all).ToString("F1") + "%";
    }

    // Split timings for one grab, so "it stutters" can be pinned on a stage
    // instead of guessed at. Throttled: one line every CostLogEvery grabs.
    private static void ReportCost(long t0, long t1, long t2, long t3, long t4,
                                   Rectangle rect)
    {
        if ((++costLogs % CostLogEvery) != 0) return;

        double k = 1000.0 / Stopwatch.Frequency;
        Logger.Log(LogLevel.Info, "TechPopups",
                   "backdrop blur cost: readback=" + ((t1 - t0) * k).ToString("F2")
                   + "ms downsample=" + ((t2 - t1) * k).ToString("F2")
                   + "ms blur=" + ((t3 - t2) * k).ToString("F2")
                   + "ms upload=" + ((t4 - t3) * k).ToString("F2")
                   + "ms total=" + ((t4 - t0) * k).ToString("F2")
                   + "ms | " + (lastFromLevel
                        ? "src=level 320x180"
                        : "rect " + rect.Width + "x" + rect.Height + " (" + Percent(rect) + ")"));
    }
}
