using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using MonoMod.RuntimeDetour;
using Monocle;

namespace Celeste.Mod.TechPopups;

public static class TechTracker {
    // ---- Wallboost / Overclock --------------------------------------------
    // Both are the SAME event; they differ only in whether Madeline was on the
    // ground when the climbjump happened.
    //
    // Celeste wiki: "Pressing away from a wall 11 frames or less after neutral
    // climbjumping will refund the stamina cost from the climbjump as the jump
    // is converted into a wallkick 'after the fact.'"
    //
    // That is three instructions in the game's own code:
    //
    //   Player.ClimbJump:
    //       if (moveX == 0) { wallBoostDir = -Facing; wallBoostTimer = 0.2f; }
    //
    //   Player.Update (runs before StateMachine.Update, so it is inside orig):
    //       if (wallBoostTimer > 0f) {
    //           wallBoostTimer -= Engine.DeltaTime;
    //           if (moveX == wallBoostDir) {
    //               Speed.X = 130f * moveX;
    //               Stamina += 27.5f;        // <- the refund
    //               wallBoostTimer = 0f;
    //               StateMachine.State = StNormal;
    //           }
    //       }
    //
    // 0.2 s at 60 fps is the wiki's "11 frames or less", and the +27.5 is the
    // refund. Overclock is the grounded case: ClimbJump's very first
    // instruction skips the -27.5 cost while onGround, but the conversion still
    // pays the +27.5 back, so the stamina ends up at 137.5 - above the usual
    // 110 cap. That single bit is decided at ClimbJump time.
    //
    // This used to be detected off Player.wallBoosting, which is NOT this tech.
    // ClimbUpdate sets wallBoosting from WallBoosterCheck(), i.e. it means
    // "Madeline is hanging on one of the Core's conveyor walls". So the popup
    // fired while riding a conveyor wall and never for an actual wallboost.
    private static bool wallBoostArmed;
    private static bool wallBoostGrounded;

    private static readonly FieldInfo FI_wallBoostTimer =
        typeof(Player).GetField(
            "wallBoostTimer",
            BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly FieldInfo FI_wallBoostDir =
        typeof(Player).GetField(
            "wallBoostDir",
            BindingFlags.NonPublic | BindingFlags.Instance);

    // State
    private static bool  dashedOnGround;
    private static bool  collidingV;
    private static bool  ultraDucked;
    // Snapshot, taken the instant the dash starts: was Madeline on the floor?
    // That single bit is the whole difference between a Grounded Ultra and a
    // normal one (see the wiki: "dashing down-diagonally while on or very close
    // to the ground"). It has to be snapshotted - by the time the jump fires she
    // has landed and is standing on the ground in *both* cases, which is why
    // testing self.onGround at jump time labelled every ultra a Grounded Ultra.
    private static bool  ultraDashOnGround;
    // Whether THIS ground contact has already stamped ultraLandTime. Kept apart
    // from ultraDucked, which means "there is an ultra waiting to be cashed in".
    // They used to be the same flag, and that is what broke Delayed Ultra
    // across a room border: a down-diagonal dash usually lands at once, so the
    // stamp was already taken on the previous screen, and the landing on the
    // next one was refused. ultraLandTime stayed behind on the old screen, the
    // measured delay came out near zero, and the DU was reported as a plain
    // Ultra. Cleared on leaving the ground, so every landing stamps once.
    private static bool  landStamped;
    // chainClock reading for the current ultra: the moment the crouch (the
    // "ultra" itself) actually landed.
    // Vanilla applies the 1.2x speed multiplier inside Player.OnCollideV:
    //     if (DashDir.X != 0 && DashDir.Y > 0 && Speed.Y > 0) {
    //         DashDir.X = Sign(DashDir.X); DashDir.Y = 0; Speed.Y = 0;
    //         Speed.X *= 1.2f; Ducking = true;
    //     }
    // so "how long the boost sat stored before the crouch arrived" is the wiki's
    // own definition of a Delayed Ultra ("dashing diagonally without landing,
    // stalling in the air, and getting the multiplier much later").
    private static float ultraLandTime = -999f;
    // Speed.Length() at that same instant, i.e. after the 1.2x has been applied
    // (OnCollideV multiplies Speed.X before it sets Ducking). A grounded ultra
    // lands at 288; a plain dash tops out at 240. Kept as a snapshot because
    // ground friction eats into it while she keeps running, so measuring it at
    // grab time reported "not > 250" for every grab that was not the very next
    // frame.
    private static float ultraLandSpeed;
    // How long a landed ultra's speed counts as still "in store" for a cancel.
    // dashAttackTimer is 0.3 s and the grab has to happen inside the dash or
    // the dash attack (see Player_Pickup), so this is that window plus a hair.
    private const float GroundedUltraWindow = 0.35f;

    // Second arm for the same trick, needed for the half of it that never
    // touches the floor: dashing down-diagonally onto a puffer's top keeps the
    // speed but the landing hook (PlayerSetDucking) never runs, so the only
    // evidence is the dash itself. A grounded down-diagonal dash IS the setup.
    private static float ultraCarryTime = -999f;
    private const float GroundedUltraCarryWindow = 0.6f;

    // ---- Grounded Ultra, the "interrupted dash" arm ------------------------
    // The player's own description of the trick (2026-10-14), and it is better
    // than anything derived from speeds:
    //
    //   "GU doesn't have to come from one ultra plus a down-diagonal dash - it
    //    can also be several ultras stacked. The key is NOT the 380-400 speed,
    //    because speed stacks without limit. The key is whether, at high speed,
    //    you dashed diagonally down toward the ground and that dash was then
    //    interrupted by a bounce or a throwable, so the speed was kept."
    //
    // So the signature is three facts, none of them a magic number:
    //   * a down-diagonal dash happened            -> lastDownDiagDashTime
    //   * it was still live when the entity hit    -> self.DashAttacking
    //   * the speed is above the ultra bar (240)   -> Speed.LengthSquared
    // A bouncing-off-a-snowball check that reads 380-400 would only ever catch
    // the first ultra of a chain; this catches the tenth one too.
    private static float lastDownDiagDashTime = -999f;
    // The dash attack itself only lives ~0.3 s, so this is slack on top of it,
    // not the real gate - it just keeps a stale stamp from ever matching.
    private const float GroundedUltraInterruptWindow = 0.5f;

    private static bool DashInterruptedByEntity(Player self, bool dashWasLive, float speedSq) {
        return dashWasLive
            && chainClock - lastDownDiagDashTime <= GroundedUltraInterruptWindow
            && speedSq > 57600f;          // 240, the game's own ultra bar
    }

    // ---- Double Block Boost (双重机关加速) --------------------------------
    // A moving block ("Kevin") hands its velocity to Madeline: Player.Jump is
    // literally `Speed += LiftBoost`, and LiftBoost is Actor.LiftSpeed clamped.
    // The grace period Actor keeps on that speed after you step off is what the
    // tech rides on - jump or dash again while the block's speed is still live
    // and you get a second helping. Two of those inside the window is the
    // Double Block Boost.
    private static float liftBoostTime = -999f;
    private static float lastBlockBoostTime = -999f;
    private const float LiftBoostMinSpeed = 100f;    // the game's own "launched" bar
    // 9 frames per the wiki, rounded up by one so that accumulating 1/60 s per
    // frame can never miss the boundary on a float comparison.
    private const float BlockBoostWindow = 10f / 60f;

    // ---- Explosion Boost (超级弹) -----------------------------------------
    // The 1.2x that Player.ExplodeLaunch hands out only when the stick points
    // the way the blast is throwing you. Two grant sites, so two pieces of
    // state: the immediate one is taken care of inside the hook, this is the
    // one Player.Update pays out a frame later (and forfeits if you never
    // point the stick that way).
    private static int explodeBoostSign;
    private static float explodeBoostWatch;
    // When the dash state ended. A "stored" ultra only starts counting once the
    // dash is over - while the dash is still running you are not storing
    // anything, you are just dashing. The gym map's own wording is "when your
    // last dash is down diagonal and you haven't touched the floor yet, we say
    // the ultra boost is stored. When you store an ultra boost for a
    // significant amount of time, we call that a delayed ultra."
    private static float ultraDashEndTime = -999f;
    private static bool  wasDashing;
    private static bool  inDashCoroutine;

    // Demohyper flag
    private static bool  demoDash;

    // Reverse tech: dash direction vs facing direction at jump time
    // If non-zero and opposite to Facing at SuperJump time -> reverse variant.
    private static int   lastDashDirX;

    // Extended super/hyper.
    // A dash lasts 15 frames and the refill cooldown is 10. Jumping after the
    // cooldown but before the dash ends gives the speed AND the dash back.
    // So: snapshot Player.Dashes the moment the dash starts; if it has gone
    // back up by the time the super/hyper jump fires, the dash was extended.
    // Comparing against a snapshot (instead of "Dashes >= 1") is what keeps a
    // two-dash player from being flagged on every single dash.
    private static int   dashesAtDashStart;

    // Crouch Climb: one popup per stay on the wall.
    //
    // The wiki definition is not "you grabbed a wall while crouched" - it is
    // "you are *climbing up* a wall while still crouched", which is the whole
    // point (the reduced hitbox). The old test fired on the state transition
    // into StClimb whenever Ducking happened to be set, so grabbing a wall
    // while ducking and uncrouching one frame later already popped it.
    private static bool  crouchClimbLatched;

    // Dream state 
    private static bool    dreamGrace;
    private static bool    dreamJumpedOnce;
    private static Vector2 dreamGracePos;
    // Dream Jump is delayed: if a second jump fires within this window we cancel
    // the first popup and show only Dream Double Jump instead.
    private static float   dreamJumpPendingTimer;
    private const  float   DreamJumpDelay = 0.07f;

    // Edge detection -
    private static int prevState;

    // Bunnyhop state machine 
    // Phase 1: after any Jump(), track how long jump is held.
    // If released within BhopMaxHoldTime -> "small jump" -> enter Phase 2.
    // Phase 2: small jump in the air. Two ways to confirm bhop:
    // a) Player lands AND jumps within BhopMaxLandJumpTime (normal landing).
    // b) Jump() fires before landing (buffered jump) - instant confirm.
    // In both cases a popup fires and Phase 2 immediately restarts for chaining.
    // If BhopAirTime expires without landing, or a dash occurs -> reset.
    private static bool  bhopTrackingHold;   // currently measuring hold time
    private static float bhopHoldTimer;      // how long jump has been held this jump
    private static bool  bhopPending;        // small jump done, watching for next jump
    private static float bhopAirTimer;       // countdown: must land within this time
    private static bool  bhopLanded;         // touched ground since small jump
    private static float bhopLandJumpTimer;  // countdown: must jump within this after landing

    private static TechPopupsSettings Settings => TechPopupsModule.Settings;

    private static Hook duckHook;

    // Reform tech: platforms whose static movers (spikes etc.) are disabled
    // A platform is added when DisableStaticMovers fires and removed when
    // EnableStaticMovers fires. Any player contact with such a platform during
    // that window counts as reform tech.
    private static readonly HashSet<Platform> reformingPlatforms = new();

    // Cornerboost (verbatim port of TechAnnouncer logic)
    // ClimbJump: wall is 3px away + |Speed.X| > 20 -> arm timer (0.08s).
    // OnCollideH: if wallSpeedRetentionTimer == 0 when we hit the wall -> bad CB, cancel.
    // Update: retention timer still alive + player cleared the wall in the retained direction -> good CB.
    private static float potentialCornerBoostTimer;
    // Our own copy of the speed direction to boost
    private static float cbRetainedSpeed;
    // Which side the wall is on, snapshotted when the arm happens.
    //
    // This is what makes Reverse Cornerboost work at all. The confirm geometry
    // below (wall gone at her height, wall still there a few pixels lower) is
    // only meaningful around the wall she is actually leaving, and for an rcb
    // that wall is on the Facing side while her momentum points the other way -
    // TASVideos: "If you are going right and getting close to a wall on your
    // left, you can press left+jump+grab ... you will get the speed boost but
    // toward the wall". Deriving the direction from cbRetainedSpeed instead
    // (which is what this used to do) pointed the whole test at the *empty* side
    // of an rcb, so the geometry never matched and the rcb stayed silent.
    // Facing is the right source: a climbjump only ever happens on the wall she
    // is facing, and for a forward cornerboost Facing and the momentum agree.
    private static int cbWallDir;
    // Position where cornerboost was armed; used to verify the player actually
    // climbed past a corner rather than simply detaching from a straight wall.
    private static Vector2 cbArmPos;
    private static readonly FieldInfo FI_wallSpeedRetentionTimer =
        typeof(Player).GetField("wallSpeedRetentionTimer", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo FI_wallSpeedRetained =
        typeof(Player).GetField("wallSpeedRetained",      BindingFlags.NonPublic | BindingFlags.Instance);

    // Dash Bounce
    // Wiki: "a wallbounce hands out up to 18 frames of constant vertical speed,
    // but a dash only lasts 15. If a wallbounce is interrupted by another dash
    // within its first 2 frames, the vertical speed survives past the end of
    // that dash" - a wallbounce cancel. Interrupting it with a DOWN-DIAGONAL
    // dash also keeps the horizontal speed, and that one is called a Dash
    // Bounce. Only that case is reported; every other wallbounce cancel is left
    // alone on purpose.
    private static float wallbounceTime = -999f;
    // The gym's own number is 2 frames, but this is measured at StartDash (the
    // frame the second dash is initiated) rather than at the frame it starts
    // moving, and dash presses go through the input buffer - so anything from
    // the same frame up to three frames later is a real bounce. 4 frames is
    // deliberately one frame of slack on top of that; a wallbounce followed by a
    // down-diagonal dash within 4 frames is the tech or it is nothing.
    private const float DashBounceWindow = 0.07f;
    // Armed by Player_StartDash (that is when the dash is initiated, so that is
    // the frame the 2-frame window has to be measured on) and cashed in by
    // Player_Update once the game has actually published DashDir.
    //
    // It has to be split like that: StartDash is not where the dash direction
    // exists yet. Decompiled: DashBegin sets `DashDir = Vector2.Zero`, and the
    // real value only appears one frame later in the dash coroutine's body
    // (`DashDir = lastAim` put through CorrectDashPrecision). DashUpdate keeps
    // it up to date only for the SuperDashing curve assist. So a StartDash-time
    // test on DashDir was reading the previous dash's leftover direction
    // (usually zero by then) and the bounce could never fire for anybody.
    private static bool dashBouncePending;
    // ...and the same "wait for the direction" latch, shared by everything that
    // needs to know which way the dash that just started is going.
    private static bool dashDirPending;

    // Transition Dash
    // Set when the player enters a transition airborne with fewer dashes than their
    // max. Confirmed if they use a dash before the next RefillDash. Cancelled if
    // another refill fires first (dash crystal, dream block, etc.).
    private static bool transitionDashPending;
    // ...and when it was armed. The flag on its own was far too loose: it stayed
    // valid for as long as she stayed airborne and un-refilled, so a dash many
    // seconds after the wipe - carried across the room by a dream block, a wind
    // current, a grab, or simply a long fall - still came out as a Transition
    // Dash. A real one spends the dash the wipe just handed back, i.e. within a
    // few frames of the border, so it gets a short window like every other
    // "armed, then cashed in" technique in this file.
    private static float transitionDashTime = -999f;
    private const float TransitionDashWindow = 0.5f;

    // Core block coyote grace
    // Armed when a BounceBlock's ShakeOffPlayer grants coyote frames via
    // StartJumpGraceTime(). A super/hyper during this window is a core tech.
    private static bool    coreGrace;
    private static Vector2 coreGracePos;

    // Backboost
    // Armed when a throwable is thrown horizontally. Confirmed when moveX flips to
    // the opposite direction within BackboostMaxTimeCs centiseconds.
    private static bool    backboostPending;
    private static int     backboostThrowDir;  // direction thrown (Facing at throw time)
    private static Vector2 backboostPos;
    private static float   backboostTimer;

    // Neutral Jump
    private static float neutralJumpPopupCooldown;
    private static float NeutralJumpPopupDelay => Settings.Timing.NeutralJumpCooldown;

    // Chain detection: a monotonic clock plus the timestamp of the last
    // bunnyhop / ultra. Two of the same tech inside ChainWindow count as a
    // "chained" variant. Purely derived from detections that already fire, so
    // it adds no new failure mode.
    private static float chainClock;
    private static float lastBhopTime = -999f;
    private static float lastUltraTime = -999f;
    // Walking-pace Grounded Ultra. Reported when the 1.2x actually lands: see
    // the check in Player_Update. There is deliberately no speed band any more -
    // speed stacks without limit, so "385 / 390" was never the signature.
    private static float lastGuSpeedTime = -999f;
    private const float GroundUltraSpeedCooldown = 0.6f;
    // How long after the band announced a walking Grounded Ultra a jump still
    // counts as the cash-in of that same move rather than a new one. The
    // landing-to-jump gap in the walking chain is a handful of frames, so this
    // only has to cover that handoff - deliberately much shorter than the band's
    // own cooldown, so a genuine chained ultra half a second later still gets
    // reported instead of being swallowed.
    private const float GroundedUltraHandoffWindow = 0.30f;

    // ---- Crouch Climb --------------------------------------------------------
    // Two shapes of the same trick, so one shared "when was she last crouched"
    // stamp serves both:
    //
    //   A. ducked for the whole grip - StClimb with Ducking true every frame.
    //   B. ducked at the moment of the climbjump - the wiki's wording is
    //      "Madeline can also climbjump while keeping crouch state", and
    //      ClimbUpdate is the only code that reads Ducking (IL_0032, where it
    //      refuses the jump only if there is no room to stand up). ClimbJump
    //      itself never writes Ducking, so the crouch survives it.
    //
    // Shape B needs a timestamp rather than a per-frame test: a crouch jump
    // into a wall is what the player does, and by the time the climbjump fires
    // the airborne branch of Player.Update has already stood her up
    // (IL_0D44: !onGround && jumpGraceTimer <= 0 -> Ducking = false).
    //
    // The window is short on purpose. NormalUpdate forces Ducking = false the
    // moment she grabs a wall (decompiled: ClimbCheck succeeds -> set_Ducking(0)),
    // so a real crouch-jump-to-climbjump always happens within a handful of
    // frames of the crouch. 0.30 s was long enough to swallow unrelated techs:
    // landing an ultra leaves her crouched (OnCollideV ducks her together with
    // the 1.2x), so a cornerboost or reverse cornerboost performed from that
    // crouched landing came out as "Crouch Climb" instead of itself.
    private static float lastDuckTime = -999f;
    private const float CrouchChainWindow = 0.15f;

    public static void Load() {
        On.Celeste.Player.SuperJump            += Player_SuperJump;
        On.Celeste.Player.ClimbJump            += Player_ClimbJump;
        On.Celeste.Player.SuperWallJump        += Player_SuperWallJump;
        On.Celeste.Player.CallDashEvents       += Player_CallDashEvents;
        On.Celeste.Player.OnCollideV           += Player_OnCollideV;
        On.Celeste.Player.OnCollideH           += Player_OnCollideH;
        On.Celeste.Player.Jump                 += Player_Jump;
        On.Celeste.Player.DashCoroutine        += Player_DashCoroutine;
        On.Celeste.Player.CorrectDashPrecision += Player_CorrectDashPrecision;
        On.Celeste.Player.WallJump             += Player_WallJump;
        On.Celeste.Player.Update               += Player_Update;
        On.Celeste.Player.DreamDashEnd         += Player_DreamDashEnd;
        On.Celeste.Player.OnTransition          += Player_OnTransition;
        On.Celeste.Player.StartDash             += Player_StartDash;
        On.Celeste.Player.RefillDash            += Player_RefillDash;
        On.Celeste.Player.UseRefill             += Player_UseRefill;
        On.Celeste.Platform.DisableStaticMovers += Platform_DisableStaticMovers;
        On.Celeste.Platform.EnableStaticMovers  += Platform_EnableStaticMovers;
        On.Celeste.Player.Throw                 += Player_Throw;
        On.Celeste.Player.StartJumpGraceTime     += Player_StartJumpGraceTime;
        On.Celeste.Player.Pickup                 += Player_Pickup;
        On.Celeste.Player.Bounce                 += Player_Bounce;
        On.Celeste.Level.Update += Level_Update;
        On.Celeste.Level.Render += Level_Render;
        // Backdrop blur: Level.Render blits the finished level onto the
        // backbuffer and then calls Level.HudRenderer.Render, which is what
        // draws the Tags.HUD entities - our popups. Prefixing RenderContent is
        // therefore the one moment where the backbuffer holds a clean level
        // image and no label has been drawn yet. See BackdropBlur.
        On.Celeste.HudRenderer.RenderContent += HudRenderer_RenderContent;
        // Single exit point for every "launched by an entity" case: bumper,
        // puffer, temple eyeball, seeker. Returns Vector2 - must be passed on.
        On.Celeste.Player.ExplodeLaunch_Vector2_bool_bool += Player_ExplodeLaunch;

        // Cassette blocks flip their own Collidable back on inside their Update
        // once every block in the group is clear (see CassetteBlock.Update:
        // `if (groupLeader && Activated && !Collidable) { ...Collidable = true... }`).
        // A false -> true transition is therefore exactly "this block just
        // reformed", which is the frame the wiki's Cassette Boost is about.
        // (Snowball / Seeker / Oshiro were hooked here before they were cut, and
        // the feather used to be too - it moved into Player_Update, see below.)
        On.Celeste.CassetteBlock.Update      += CassetteBlock_Update;

        // Hook the Ducking setter
        duckHook = new Hook(
            typeof(Player).GetProperty("Ducking").GetSetMethod(),
            new Action<Action<Player, bool>, Player, bool>(PlayerSetDucking));
    }

    public static void Unload() {
        On.Celeste.Player.SuperJump            -= Player_SuperJump;
        On.Celeste.Player.ClimbJump            -= Player_ClimbJump;
        On.Celeste.Player.SuperWallJump        -= Player_SuperWallJump;
        On.Celeste.Player.CallDashEvents       -= Player_CallDashEvents;
        On.Celeste.Player.OnCollideV           -= Player_OnCollideV;
        On.Celeste.Player.OnCollideH           -= Player_OnCollideH;
        On.Celeste.Player.Jump                 -= Player_Jump;
        On.Celeste.Player.DashCoroutine        -= Player_DashCoroutine;
        On.Celeste.Player.CorrectDashPrecision -= Player_CorrectDashPrecision;
        On.Celeste.Player.WallJump             -= Player_WallJump;
        On.Celeste.Player.Update               -= Player_Update;
        On.Celeste.Player.DreamDashEnd         -= Player_DreamDashEnd;
        On.Celeste.Player.OnTransition          -= Player_OnTransition;
        On.Celeste.Player.StartDash             -= Player_StartDash;
        On.Celeste.Player.RefillDash            -= Player_RefillDash;
        On.Celeste.Player.UseRefill             -= Player_UseRefill;
        On.Celeste.Platform.DisableStaticMovers -= Platform_DisableStaticMovers;
        On.Celeste.Platform.EnableStaticMovers  -= Platform_EnableStaticMovers;
        On.Celeste.Player.Throw                 -= Player_Throw;
        On.Celeste.Player.StartJumpGraceTime     -= Player_StartJumpGraceTime;
        On.Celeste.Player.Pickup                 -= Player_Pickup;
        On.Celeste.Player.Bounce                 -= Player_Bounce;
        On.Celeste.Level.Update -= Level_Update;
        On.Celeste.Level.Render -= Level_Render;
        On.Celeste.HudRenderer.RenderContent -= HudRenderer_RenderContent;
        On.Celeste.Player.ExplodeLaunch_Vector2_bool_bool -= Player_ExplodeLaunch;
        On.Celeste.CassetteBlock.Update      -= CassetteBlock_Update;

        reformingPlatforms.Clear();

        duckHook?.Dispose();
        duckHook = null;

        BackdropBlur.Unload();
    }

    // When Ducking is set to true while we are inside OnCollideV, it means the
    // diagonal-down dash just landed
    private static void PlayerSetDucking(Action<Player, bool> orig, Player self, bool ducking) {
        // Landing frame of a diagonal-down dash. Ducking is re-assigned every
        // frame while Madeline is on the floor, and OnCollideV runs on every one
        // of those frames too, so this used to fire continuously: ultraLandTime
        // was dragged forward to "now" for as long as she stayed grounded. The
        // dash -> landing gap then grew with every idle frame and every plain
        // Ultra looked delayed. Stamp the FIRST landing only.
        if (collidingV && ducking && !landStamped) {
            landStamped    = true;
            ultraDucked    = true;
            ultraLandTime  = chainClock;
            ultraCarryTime = chainClock;
            // Read here, not at grab time: this is the one instant the 1.2x is
            // certainly still in Speed (OnCollideV multiplies before it ducks).
            ultraLandSpeed = self.Speed.Length();
        }
        orig(self, ducking);
    }

    // True while the 1.2x speed of a grounded ultra is still being carried.
    // Shared by the two things that can "cash it in": grabbing a throwable and
    // bouncing off something that keeps the speed (a puffer).
    //
    // The ultraLandSpeed snapshot is itself the proof that a grounded ultra
    // happened - OnCollideV only takes that branch for a diagonal-down dash -
    // so there is deliberately no separate dash-direction test here. The second
    // arm covers the version that never lands on the floor (see ultraCarryTime).
    //
    // requireGround exists because the two callers see different situations:
    // a grab happens with her feet down, but the puffer she *steps on* has been
    // left behind by the time the bounce is reported, so demanding onGround
    // there would reject exactly the case that was missing.
    private static bool GroundedUltraArmed(Player self, bool requireGround)
    {
        bool landed = ultraLandSpeed > 250f
                      && chainClock - ultraLandTime <= GroundedUltraWindow;
        bool dashed = chainClock - ultraCarryTime <= GroundedUltraCarryWindow;
        if (!landed && !dashed) return false;
        return !requireGround || self.onGround;
    }

    // Entering a transition while airborne + below max dashes arms the pending flag.
    // The actual popup fires only if the player uses that refilled dash before the
    // next RefillDash cancels it (dash crystal, dream block, etc.).
    private static void Player_OnTransition(
            On.Celeste.Player.orig_OnTransition orig, Player self) {
        bool airborne  = !self.onGround && self.jumpGraceTimer <= 0f;
        bool belowMax  = self.Dashes < self.MaxDashes;
        transitionDashPending = false;

        // Popups used to be wiped here, which made them vanish mid-transition.
        // They are world-anchored now, so they just ride the camera out and
        // expire on their own timer. That also keeps a label made near a room
        // border visible after crossing into the next room.

        orig(self);
        if (airborne && belowMax) {
            transitionDashPending = true;
            transitionDashTime = chainClock;
        }
    }

    private static int Player_StartDash(
            On.Celeste.Player.orig_StartDash orig, Player self) {
        int before = self.Dashes;
        int result = orig(self);

        // Double Block Boost: a dash taken while the block's speed is live
        // counts as one of the two touches.
        BlockBoostTouch(self);

        // Capture fresh dash context immediately so an instant hyper does not
        // reuse stale state from the previous dash.
        dashedOnGround    = self.onGround || self.jumpGraceTimer > 0f;
        ultraDashOnGround = dashedOnGround;
        ultraDashEndTime  = -999f;
        // NOT lastDashDirX = Sign(self.DashDir.X) here any more. DashDir does not
        // hold this dash's direction yet - DashBegin sets it to Vector2.Zero and
        // the real value only arrives one frame later, from the dash coroutine's
        // body. Reading it in StartDash meant reading the previous dash's
        // leftovers. Every direction-dependent decision now waits for the value
        // to actually exist; this is the latch for that.
        dashDirPending    = true;

        // ---- Dash Bounce -----------------------------------------------------
        // A wallbounce interrupted by another dash inside its first 2 frames.
        // The TIMING is decided here - this is the frame the dash is initiated,
        // so this is the frame the 2-frame window has to be measured on. The
        // direction is checked a frame later, in Player_Update, once DashDir
        // exists (see dashDirPending). The wallbounce itself was announced a
        // frame or two earlier by Player_SuperWallJump.
        dashBouncePending = wallbounceTime > 0f
                            && chainClock - wallbounceTime <= DashBounceWindow;
        // One wallbounce can only ever be consumed by one dash.
        wallbounceTime = -999f;

        // A new dash invalidates a stored ultra from an earlier one - this is the
        // staleness lock, and it must NOT be based on DashDir: the game decays
        // DashDir towards zero every frame after a dash ends, so by the time the
        // jump comes around it has usually reached (0,0) and any check on it
        // kills the Ultra popup entirely.
        ultraDucked = false;

        // StartDash() spends one dash, so this is the count *during* the dash.
        // A later refill pushes it back up -> extended.
        dashesAtDashStart = self.Dashes < before ? self.Dashes : before - 1;

        if (transitionDashPending) {
            transitionDashPending = false;
            if (Settings.Tech.TransitionDashEnabled
                    && chainClock - transitionDashTime <= TransitionDashWindow)
            {
                TechPopupsModule.ShowPopup("Transition Dash", self.Position);
            }
        }
        return result;
    }

    private static bool Player_RefillDash(
            On.Celeste.Player.orig_RefillDash orig, Player self)
    {
        transitionDashPending = false;
        return orig(self);
    }

    private static bool Player_UseRefill(
            On.Celeste.Player.orig_UseRefill orig, Player self, bool twoDashes) {
        bool result = orig(self, twoDashes);
        if (result) {
            transitionDashPending = false;
        }
        return result;
    }

    // Platform reform tracking 
    private static void Platform_DisableStaticMovers(
            On.Celeste.Platform.orig_DisableStaticMovers orig, Platform self) {
        orig(self);
        reformingPlatforms.Add(self);
    }

    private static void Platform_EnableStaticMovers(
            On.Celeste.Platform.orig_EnableStaticMovers orig, Platform self) {
        orig(self);
        reformingPlatforms.Remove(self);
    }

    // Helper: show a Reform Tech popup if the given entity is a reforming platform and remove it from the set.
    private static bool TryShowReformTech(Player player, Entity hit) {
        if (hit is Platform platform && reformingPlatforms.Remove(platform)) {
            if (Settings.Tech.ReformTechEnabled)
            {
                TechPopupsModule.ShowPopup("Reform Tech", player.Position);
            }
            return true;
        }
        return false;
    }

    // Detects demohyper: inside dash coroutine, already Ducking, and aim is not downward.
    private static Vector2 Player_CorrectDashPrecision(
            On.Celeste.Player.orig_CorrectDashPrecision orig, Player self, Vector2 dir) {
        Vector2 result = orig(self, dir);
        if (inDashCoroutine && self.Ducking && result.Y <= 0f) {
            demoDash = true;
        } else {
            demoDash = false;
        }
        return result;
    }

    // Tracks inDashCoroutine. TechAnnouncer sets it true before the loop and
    // false after each yield (so it is only true during the first segment).
    // Also cancels all bunnyhop state
    private static IEnumerator Player_DashCoroutine(
            On.Celeste.Player.orig_DashCoroutine orig, Player self) {
        // Cancel bhop on dash
        bhopPending      = false;
        bhopTrackingHold = false;
        bhopLanded       = false;
        bhopLandJumpTimer = 0f;
        bhopAirTimer     = 0f;
        demoDash         = false;
        lastDashDirX     = 0; // cleared until CallDashEvents fires for this dash

        IEnumerator e = orig(self);
        inDashCoroutine = true;
        while (e.MoveNext()) {
            inDashCoroutine = false;
            yield return e.Current;
            inDashCoroutine = true;
        }
        inDashCoroutine = false;
    }

    // Snapshot ground state, jumpGraceTimer (coyote frames count as ground).
    // Also capture dash direction for reverse-tech detection.
    private static void Player_CallDashEvents(
            On.Celeste.Player.orig_CallDashEvents orig, Player self) {
        if (!self.calledDashEvents) {
            dashedOnGround = self.onGround || self.jumpGraceTimer > 0f;
            lastDashDirX   = Math.Sign(self.DashDir.X);

        }
        orig(self);
    }

    // Sets collidingV so the Ducking setter hook knows we are in a landing.
    private static void Player_OnCollideV(
            On.Celeste.Player.orig_OnCollideV orig, Player self, CollisionData data) {
        collidingV = true;
        // hitting top/bottom of a reforming block
        if (data.Hit != null) TryShowReformTech(self, data.Hit);
        orig(self, data);
        collidingV = false;
    }

    // kicking the side of a reforming block 
    private static void Player_OnCollideH(
            On.Celeste.Player.orig_OnCollideH orig, Player self, CollisionData data) {
        if (data.Hit != null) TryShowReformTech(self, data.Hit);

        orig(self, data);
        // Cornerboost: if the wall collision consumed retained speed
        // (i.e. the retention timer was never set), there's no speed to boost with.
        var retentionTimer = (float)FI_wallSpeedRetentionTimer.GetValue(self);
        if (retentionTimer <= 0f) {
            potentialCornerBoostTimer = 0f;
        }
    }

    // ---- entity bounces ---------------------------------------------------
    // Snowball / Seeker / Oshiro Bounce used to be hooked here. They were cut:
    // "I bumped into an enemy" is not a movement technique anyone drills, and
    // the popups just added noise. Featherboost, below, is the one that earned
    // its keep.

    // ---- Cassette Block Boost ---------------------------------------------
    // Wiki: "If you are near the top of a cassette block when it reforms, your
    // position is snapped upwards, placing you on top of it. By jumping as this
    // happens, the sudden vertical movement is added in the form of liftboost,
    // granting a considerably higher jump. The jump input cannot be buffered."
    //
    // "It reforms" is a single unambiguous frame in the game's own code:
    // CassetteBlock.Update runs `if (groupLeader && Activated && !Collidable)`
    // and only then sets `Collidable = true` for the whole group. So the
    // false -> true edge of Collidable IS the reform frame. The old test looked
    // for `LiftBoost.Y < -30` while jumping, which never holds: the block
    // teleports into place rather than moving through you, so Madeline is never
    // registered as a rider and never accumulates any liftboost at all.
    private static float cassetteBoostWindow;

    private static void CassetteBlock_Update(
            On.Celeste.CassetteBlock.orig_Update orig, CassetteBlock self) {
        bool wasSolid = self.Collidable;
        orig(self);
        if (wasSolid || !self.Collidable) return;

        Player player = self.Scene?.Tracker?.GetEntity<Player>();
        if (player == null) return;

        // Player.Position is the point between Madeline's feet (her hitbox is
        // 8x11 anchored at the bottom), so "near the top of the block" is just
        // "her feet are level with the block's top edge".
        Rectangle box = self.Collider.Bounds;
        Vector2   pos = player.Position;
        if (pos.X >= box.Left - 6f && pos.X <= box.Right + 6f
         && pos.Y >= box.Top  - 10f && pos.Y <= box.Top + 8f) {
            cassetteBoostWindow = 0.18f;
        }
    }

    // ---- Featherboost ------------------------------------------------------
    // Wiki: "Holding a diagonal direction with the movement buttons on the first
    // frame of feather movement gives you an initial speed boost", and the CN
    // wiki spells out the same thing: 速通玩家会以斜向的方向启动羽毛.
    //
    // "The first frame of feather movement" is not the frame you touch it.
    // Player.StarFlyBegin parks the fly behind a transform animation and sets
    // starFlyTransforming = true; Player.StarFlyUpdate then just decelerates
    // (`Speed = Approach(Speed, Zero, 1000 * dt)`) for as long as that flag is
    // set, and only afterwards seeds the flight from Calc.SafeNormalize(Speed)
    // - which by then is zero, so it falls back to the stick, Input.Feather.
    // So the launch direction is the direction held when the transform ends;
    // neither the speed Madeline carried in (the old test) nor the buttons she
    // was holding while touching the feather have anything to do with it.
    private static readonly FieldInfo FI_starFlyTransforming =
        typeof(Player).GetField(
            "starFlyTransforming", BindingFlags.NonPublic | BindingFlags.Instance);
    private static bool featherInFly;
    private static bool featherLaunchRead;

    private static void FeatherCheck(Player self, int state) {
        bool inFly = state == Player.StStarFly;
        if (inFly && !featherInFly) featherLaunchRead = false;
        featherInFly = inFly;

        if (!inFly || featherLaunchRead) return;

        // If reflection is unavailable for any reason, treat the fly as already
        // launched - worst case the popup is a hair early.
        bool transforming = false;
        if (FI_starFlyTransforming != null) {
            try { transforming = (bool)FI_starFlyTransforming.GetValue(self); } catch { }
        }
        if (transforming) return;

        featherLaunchRead = true;
        if (!Settings.Tech.FeatherboostEnabled) return;

        Vector2 dir = Input.Feather.Value;
        if (dir == Vector2.Zero) dir = Input.Aim.Value;
        if (dir.X != 0f && dir.Y != 0f) {
            TechPopupsModule.ShowPopup("Featherboost", self.Position);
        }
    }


    // Arming side of both cornerboosts.
    //
    // The two variants need different evidence, which is the thing the previous
    // round got wrong:
    //
    //  * Forward Cornerboost. She is flying over the top edge of the wall she is
    //    moving into, so the confirm has to be geometric - the wall is gone at
    //    her height but still there a few pixels lower (see the confirm in
    //    Player_Update). That is the only way to tell a real cornerboost from a
    //    climbjump that merely happened next to a wall.
    //
    //  * Reverse Cornerboost. The wall is BEHIND her: she is moving away from it
    //    (TASVideos: "If you are going right and getting close to a wall on your
    //    left, you can press left+jump+grab"), and what makes it a cornerboost is
    //    that this climbjump cancels her dash. Both facts are readable right
    //    here, and the geometric test is STRUCTURALLY IMPOSSIBLE for it: at the
    //    moment of the climbjump she is flush against the wall, so
    //    "no wall at her height" is false by construction, and the frames she
    //    then spends moving away from it are exactly the frames the test needs
    //    the wall to still be there. That is why every rcb armed and none ever
    //    fired (confirmed in the log: eight `wall=-1 facing=-1 speedX=+3xx`
    //    arms, zero `FIRED`).
    private static void ArmCornerboost(Player self, float speed, string how) {
        int wallDir = (int)self.Facing;

        // ---- Reverse Cornerboost: decided here, not by geometry --------------
        // Momentum points away from the wall she is climbing AND a dash attack
        // is live, i.e. this climbjump is the thing cancelling the dash. Both
        // halves of the wiki's execution, no guesswork.
        if (Math.Abs(self.Speed.X) > 20f
            && Math.Sign(self.Speed.X) != wallDir
            && self.DashAttacking)
        {
            potentialCornerBoostTimer = 0f;   // the geometric path is not this tech
            cbWallDir = 0;

            // Reverse Cornerboost has its own switch; CornerboostEnabled is the
            // master switch for the pair, exactly as it is for the forward case.
            if (Settings.Tech.CornerboostEnabled && Settings.Tech.ReverseCornerboostEnabled) {
                TechPopupsModule.ShowPopup("Reverse Cornerboost", self.Position);
            }
            return;
        }

        potentialCornerBoostTimer = 0.08f;
        cbRetainedSpeed = speed;
        cbArmPos        = self.Position;
        cbWallDir       = wallDir;

    }

    // cornerboost arming
    private static void Player_ClimbJump(
            On.Celeste.Player.orig_ClimbJump orig, Player self) {

        // ---- Crouch Climb, shape B: crouched at the jump ---------------------
        // The wiki says "Madeline can also climbjump while keeping crouch
        // state", and ClimbUpdate backs it up: it is the ONLY place that reads
        // Ducking, and it refuses the climbjump only when there is no room to
        // stand up (IL_0032). ClimbJump itself never writes Ducking, so the
        // crouch survives the jump - which is the whole trick.
        //
        // Tested with a window and not just `self.Ducking` because the way it
        // is actually performed is a crouch jump into the wall first, and the
        // airborne branch of Player.Update has already stood her up by the
        // time the jump comes (IL_0D44: !onGround && jumpGraceTimer <= 0).
        if (Settings.Tech.CrouchClimbEnabled
            && (self.Ducking || chainClock - lastDuckTime <= CrouchChainWindow)) {
            crouchClimbLatched = true;   // shape A must not report this again
            TechPopupsModule.ShowPopup("Crouch Climb", self.Position);
        }

        var retentionTimer = (float)FI_wallSpeedRetentionTimer.GetValue(self);
        var retainedSpeed  = (float)FI_wallSpeedRetained.GetValue(self);

        if (Math.Abs(self.Speed.X) > 20f &&
            self.CollideCheck<Solid>(self.Position + Vector2.UnitX * Math.Sign(self.Speed.X) * 3f)) {
            // Good CB: the +40 is already inside the speed and the wall she is
            // about to leave is the one in front of her.
            ArmCornerboost(self, self.Speed.X, "good");
        } else if (retentionTimer > 0f && Math.Abs(retainedSpeed) > 20f) {
            // Bad CB: already hit the wall (Speed.X zeroed by OnCollideH) but
            // wallSpeedRetained is still live from that collision
            ArmCornerboost(self, retainedSpeed, "bad");
        } else if (retentionTimer <= 0f && Math.Abs(self.Speed.X) > 20f) {
            // NormalUpdate ClimbJump path: player is near the wall but never physically
            // hit it (e.g. buffered from a booster), so OnCollideH never set
            // wallSpeedRetained.
            //
            // This is also where a Reverse Cornerboost lands, and that is worth
            // knowing: an rcb is a climbjump on a wall BEHIND Madeline, so there
            // is never a wall on the momentum side and the first test above can
            // never match it. It was always reaching this branch.
            ArmCornerboost(self, self.Speed.X, "near");
        }

        // Arm the wallboost. ClimbJump only sets wallBoostTimer when the
        // climbjump is NEUTRAL (moveX == 0) - pushing a direction makes it an
        // ordinary climbjump with no refund to hand back. The grounded bit is
        // read here because ClimbJump's first instruction is the one that
        // decides whether the 27.5 stamina was actually spent.
        if (self.moveX == 0) {
            wallBoostArmed    = true;
            wallBoostGrounded = self.onGround;
        }

        // ---- Ceiling Pop (cpop) ------------------------------------------
        // Celeste wiki: "When Madeline grabs a wall with downward momentum, she
        // slides a tiny amount down. If she holds the bottom pixel of a wall
        // while she has a low enough subpixel, she will slide below the wall
        // while still being able to climbjump for one frame. Because there is
        // no wall in the way, this climbjump will give Madeline forward
        // momentum."
        //
        // So the signature is geometric and quite sharp: at the moment of the
        // climbjump there is no wall level with her body any more, but there
        // still is one just above her head. There is no flag for this in the
        // engine - Player.CeilingVarJumpGrace is a compile-time constant that
        // never even gets read - so geometry is the only honest option.
        if (Settings.Tech.CeilingPopEnabled) {
            bool wallAtBody = CollideWall(self, 0f);
            bool wallAbove  = CollideWall(self, -6f);
            if (!wallAtBody && wallAbove) {
                TechPopupsModule.ShowPopup("Ceiling Pop", self.Position);
            }
        }

        orig(self);
    }

    // Is there a solid on the side Madeline is facing, `dy` pixels away from
    // her centre? Player.Position is the centre of the 8x11 hitbox, so her head
    // is 6px above it and her feet 5px below.
    private static bool CollideWall(Player self, float dy) {
        return CollideWallSide(self, (int)self.Facing, dy);
    }

    private static bool CollideWallSide(Player self, int side, float dy) {
        return self.CollideCheck<Solid>(
            self.Position + Vector2.UnitX * side + Vector2.UnitY * dy);
    }
    private static void Player_Update(
            On.Celeste.Player.orig_Update orig, Player self) {

        int state = self.StateMachine.State;

        chainClock += Engine.DeltaTime;

        // A new ground contact gets to stamp ultraLandTime again. Airborne is the
        // reset: it covers a jump, a fall, and being carried across a room
        // border, so a landing on the far side is measured from its own frame.
        if (!self.onGround) landStamped = false;

        // Crouch Climb shape B - remember the last frame she was crouched.
        if (self.Ducking) lastDuckTime = chainClock;

        // Double Block Boost: remember how recently a moving block's speed was
        // actually on her. The 100 px/s bar is Player.LaunchedBoostCheck's own
        // threshold, so this uses the game's definition rather than a guess.
        if (self.LiftBoost.LengthSquared() >= LiftBoostMinSpeed * LiftBoostMinSpeed)
            liftBoostTime = chainClock;

        // Explosion Boost, deferred half. Player.ExplodeLaunch parked the 1.2x
        // in explodeLaunchBoostSpeed with a 0.01 s timer, which is exactly one
        // frame, and Player.Update only pays it out if the stick points along
        // the launch on that frame - otherwise the boost is silently forfeited.
        // So this is a one-shot decision made on the frame after the hit, and
        // it reads the input rather than Speed: the paying-out happens inside
        // orig_Update, i.e. after this hook, and by the time the speed could be
        // compared the same frame's friction has already eaten into it.
        if (explodeBoostWatch > 0f)
        {
            explodeBoostWatch = 0f;
            if (Input.MoveX.Value == explodeBoostSign)
                GrantExplosionBoost(self);
        }

        if (neutralJumpPopupCooldown > 0f)
            neutralJumpPopupCooldown -= Engine.DeltaTime;

        if (cassetteBoostWindow > 0f)
            cassetteBoostWindow -= Engine.DeltaTime;

        // Fast Bubble. Sitting inside a booster (state 4) and pressing dash is
        // what leaves it early. Decompiling Player.BoostUpdate shows the test is
        // the PRESS EDGE `Input.DashPressed || Input.CrouchDashPressed`, checked
        // every frame you are in there.
        //
        // Two earlier versions of this were both wrong:
        //   - "held dash on the entry frame": that edge is long gone by the time
        //     you are inside, so entering while holding dash does nothing at all;
        //   - a hand-rolled 0.35 s window that popped on a held dash: it fired on
        //     inputs the game itself would have ignored.
        //
        // The state read here is the one from the START of the frame, which is
        // deliberate: on the frame you first touch a booster it still reads as
        // the old state, and vanilla cannot launch you on that frame either.
        if (state == Player.StBoost
                && (Input.DashPressed || Input.CrouchDashPressed)) {
            if (Settings.Tech.FastBubbleEnabled) {
                TechPopupsModule.ShowPopup("Fast Bubble", self.Position);
            }
        }

        // Featherboost: evaluated on the first frame the feather actually moves
        // Madeline (the transform is over), not on the frame she touches it.
        FeatherCheck(self, state);

        if (state != prevState) {
            if (state == Player.StBoost && self.Ducking) {
                if (Settings.Tech.ArchieEnabled)
                {
                    TechPopupsModule.ShowPopup("Archie", self.Position);
                }
            }
            if (state == Player.StClimb && prevState == Player.StDreamDash) {
                if (Settings.Tech.DreamGrabEnabled)
                {
                    TechPopupsModule.ShowPopup("Dream Grab", self.Position);
                }
            }
            if (state == Player.StDreamDash && self.Holding != null) {
                if (Settings.Tech.DreamSmuggleEnabled)
                {
                    TechPopupsModule.ShowPopup("Dream Smuggle", self.Position, 0.1f);
                }
            }
            // Crouch Climb is checked every frame further down - see the
            // crouchClimbLatched comment. It is not a state-transition event.
            // grabbing a reforming block
            if (state == Player.StClimb && prevState != Player.StDreamDash) {
                var wallSolid = self.CollideFirst<Solid>(self.Position + Vector2.UnitX * (float)self.Facing);
                if (wallSolid != null) TryShowReformTech(self, wallSolid);
            }
            // expire dream grace on grab
            if (state == Player.StClimb) {
                dreamGrace            = false;
                dreamJumpPendingTimer = 0f;

            }
        }

        if (dreamGrace && self.onGround) {
            dreamGrace            = false;
            dreamJumpPendingTimer = 0f;
        }

        // Dream grace expires when jumpGraceTimer runs out BUT only if the player hasn't jumped yet.
        // once dreamJumpedOnce is true the pending timer owns the lifetime instead.
        if (dreamGrace && !dreamJumpedOnce && self.jumpGraceTimer <= 0f) {
            dreamGrace            = false;
            dreamJumpPendingTimer = 0f;
        }

        // Core grace expires when the coyote window (jumpGraceTimer) runs out or
        // when the player lands on something else.
        if (coreGrace && (self.onGround || self.jumpGraceTimer <= 0f)) {
            coreGrace = false;
        }

        // fire the popup once the delay window expires without a second jump signaling a double dream jump.
        if (dreamJumpPendingTimer > 0f) {
            dreamJumpPendingTimer -= Engine.DeltaTime;
            if (dreamJumpPendingTimer <= 0f) {
                if (Settings.Tech.DreamJumpEnabled)
                {
                    TechPopupsModule.ShowPopup("Dream Jump", dreamGracePos);
                }

                dreamGrace = false;
                dreamJumpedOnce = false;
            }
        }

        // Landing cancels the transition dash window
        if (transitionDashPending && self.onGround) {
            transitionDashPending = false;
        }

        // Bunnyhop: hold tracking (check input before orig) -
        if (bhopTrackingHold) {
            if (Input.Jump.Check) {
                bhopHoldTimer += Engine.DeltaTime;
                if (bhopHoldTimer > Settings.Timing.BhopMaxHoldTime) {
                    // Jump too big
                    bhopTrackingHold = false;
                    bhopPending      = false;
                }
            } else {
                // Released
                bhopTrackingHold = false;
                if (bhopHoldTimer <= Settings.Timing.BhopMaxHoldTime) {
                    bhopPending      = true;
                    bhopAirTimer     = Settings.Timing.BhopAirTime;
                    bhopLanded       = false;
                    bhopLandJumpTimer = 0f;
                }
            }
        }

        if (bhopPending) {
            if (self.onGround) {
                if (!bhopLanded) {
                    // Just touched down
                    bhopLanded        = true;
                    bhopLandJumpTimer = Settings.Timing.BhopMaxLandJumpTime;
                } else {
                    // count down the post-land window
                    bhopLandJumpTimer -= Engine.DeltaTime;
                    if (bhopLandJumpTimer <= 0f) {
                        // Didn't jump in time
                        bhopPending = false;
                    }
                }
            } else {
                if (!bhopLanded) {
                    // Still in the air
                    bhopAirTimer -= Engine.DeltaTime;
                    if (bhopAirTimer <= 0f) {
                        bhopPending = false;
                    }
                }
            }
        }

        // Cornerboost confirm: Snapshot retention state before orig
        // orig (and ClimbJump inside it) may modify wallSpeedRetentionTimer, so we capture it now for the direction check.
        var retentionTimer = (float)FI_wallSpeedRetentionTimer.GetValue(self);
        var retainedSpeed  = (float)FI_wallSpeedRetained.GetValue(self);
        if (retentionTimer > 0f && Math.Sign(self.Speed.X) == -Math.Sign(retainedSpeed)) {
            // Moving opposite to the retained direction.
            //
            // This used to cancel the arm outright, which silently filtered out
            // every Reverse Cornerboost. Wiki: "A reverse cornerboost is
            // executed by climbjumping a corner that is *behind* Madeline ...
            // the +40 speed from jumping is applied in the opposite direction".
            // Moving against your retained speed IS the rcb setup, so cancelling
            // here is exactly the wrong answer. Arm it instead - with the
            // retained speed, not the current one, because the corner is the
            // wall she is no longer moving towards.
            //
            // Only re-arm when no arm is live: this runs every frame, and
            // resetting cbArmPos each time would mean she never looks like she
            // climbed up past it (climbedUpFromArmPos compares against it).
            if (Settings.Tech.ReverseCornerboostEnabled
                && Math.Abs(retainedSpeed) > 20f
                && potentialCornerBoostTimer <= 0f) {
                potentialCornerBoostTimer = 0.08f;
                cbRetainedSpeed = retainedSpeed;
                cbArmPos = self.Position;
                // She is moving against her retained speed, so the wall that
                // handed it to her is on the retained side - same side the
                // confirm geometry has to be measured around.
                cbWallDir = Math.Sign(retainedSpeed);
            } else if (!Settings.Tech.ReverseCornerboostEnabled) {
                potentialCornerBoostTimer = 0f;
            }
        }
        if (potentialCornerBoostTimer > 0f) {
            potentialCornerBoostTimer -= Engine.DeltaTime;
        }

        // ---- Crouch Climb, shape A: ducked for the whole grip ----------------
        // Celeste wiki (Tech page, section "Crouch Jumps"): "Madeline can also
        // climbjump while keeping crouch state, but she cannot grab walls
        // without moving downward and being uncrouched ... it is possible to be
        // crouched while holding a wall if the player lands a delayed ultra by
        // sliding to the bottom of a wall while still grabbing it, or grabbing a
        // dream block after demodashing through. They can then wallclimb upward
        // while keeping their crouched hitbox until they next move downward."
        //
        // Shape A is that second sentence: Ducking true while in StClimb. The
        // game will not let you START a wall grab while ducked - NormalUpdate
        // IL_006A bails out on Ducking before ClimbCheck is even tried - and
        // ClimbUpdate never writes Ducking, so the only ways in are the ultra
        // landing (OnCollideV ducks her together with the 1.2x) and grabbing a
        // dream block out of a demodash. ClimbUpdate DOES read Ducking
        // (IL_0032: ducked with no room to stand up -> the climbjump is
        // refused), which is the game confirming the state is reachable.
        //
        // No Speed.Y filter any more. The old one excluded slide-down, but it
        // also threw away frames in the middle of the climb - and the crouch is
        // granted on a landing frame, which is exactly where Speed.Y has just
        // been zeroed. Shape B is set in Player_ClimbJump and sets this same
        // latch, so one grip can never report twice.
        if (state != Player.StClimb) {
            crouchClimbLatched = false;
        } else if (!crouchClimbLatched && self.Ducking) {
            crouchClimbLatched = true;
            if (Settings.Tech.CrouchClimbEnabled) {
                TechPopupsModule.ShowPopup("Crouch Climb", self.Position);
            }
        }

        // ---- Wallboost / Overclock ------------------------------------------
        // Read BEFORE orig on purpose. The conversion itself happens at the top
        // of Player.Update - which orig runs - and it zeroes wallBoostTimer in
        // the same breath. Reading the timer afterwards therefore always saw 0,
        // which is why Overclock never fired either.
        if (wallBoostArmed) {
            float wbt = (float)FI_wallBoostTimer.GetValue(self);
            int   wbd = (int)FI_wallBoostDir.GetValue(self);
            if (wbt > 0f && (Input.MoveX.Value == wbd || self.moveX == wbd)) {
                wallBoostArmed = false;
                if (wallBoostGrounded) {
                    if (Settings.Tech.OverclockEnabled) {
                        TechPopupsModule.ShowPopup("Overclock", self.Position);
                    }
                } else if (Settings.Tech.WallboostEnabled) {
                    TechPopupsModule.ShowPopup("Wallboost", self.Position);
                }
            } else if (wbt <= 0f) {
                // The 0.2 s ran out without the player ever pressing away.
                wallBoostArmed = false;
            }
        }

        prevState = state;

        orig(self);

        // ---- which way does THIS dash go -------------------------------------
        // DashDir does not exist on the frame the dash starts: DashBegin zeroes
        // it, and the dash coroutine's body only writes the real value on the
        // next frame it runs (`DashDir = lastAim` through CorrectDashPrecision).
        // So every direction-dependent decision waits for the game to publish it.
        // Non-zero is the "published" test: lastAim is never zero (GetAimVector
        // falls back to (Facing, 0)), so a zero here means "not computed yet".
        if (dashDirPending && self.StateMachine.State == Player.StDash
            && self.DashDir != Vector2.Zero)
        {
            dashDirPending = false;
            bool downDiagonal = self.DashDir.Y > 0.1f && self.DashDir.X != 0f;

            // The value the old StartDash test was reaching for, now read at a
            // moment when it actually holds this dash's direction.
            lastDashDirX = Math.Sign(self.DashDir.X);

            // A grounded down-diagonal dash is the entire setup of a grounded
            // ultra. Stamped here as well as at the landing, because dashing
            // onto a puffer never touches the floor and the landing hook would
            // stay silent.
            if (dashedOnGround && downDiagonal) ultraCarryTime = chainClock;
            // ...and the same direction, stamped for ANY down-diagonal dash:
            // this is what "an entity interrupted that dash" is measured against
            // (see DashInterruptedByEntity - it must work for air dashes too,
            // since the whole point is that the speed was stacked first).
            if (downDiagonal) lastDownDiagDashTime = chainClock;

            // Dash Bounce: the dash that was initiated inside the wallbounce's
            // 2-frame window has now been shown to be the down-diagonal one.
            if (dashBouncePending) {
                dashBouncePending = false;
                if (downDiagonal && Settings.Tech.DashBounceEnabled) {
                    TechPopupsModule.ShowPopup("Dash Bounce", self.Position);
                }
            }
        }

        // After orig, ClimbJump may have fired (buffered-jump or NormalUpdate path),
        // arming potentialCornerBoostTimer and setting cbRetainedSpeed.
        if (potentialCornerBoostTimer > 0f && Math.Abs(cbRetainedSpeed) > 0f) {
            // Measured around the wall she is leaving - the Facing side, captured
            // at arm time - not around the side her momentum points at. Those two
            // only agree for a forward cornerboost; for a Reverse Cornerboost the
            // wall is behind her, so the old momentum-side test looked at empty
            // air, never matched, and the rcb stayed silent.
            int dir = cbWallDir != 0 ? cbWallDir : Math.Sign(cbRetainedSpeed);
            bool wallAtCurrentHeight = self.CollideCheck<Solid>(self.Position + Vector2.UnitX * dir);
            bool wallSlightlyBelow   = self.CollideCheck<Solid>(self.Position + Vector2.UnitX * dir + Vector2.UnitY * 4f);
            bool climbedUpFromArmPos = self.Position.Y < cbArmPos.Y - 1f;

            // confirm that the player has cleared a corner
            if (!wallAtCurrentHeight && wallSlightlyBelow && climbedUpFromArmPos) {
                potentialCornerBoostTimer = 0f;
                if (Settings.Tech.CornerboostEnabled)
                {
                    // Reverse cornerboost: momentum and wall are on opposite
                    // sides, so the jump's +40 goes into the wall and she loses
                    // 40 speed - but the dash is cancelled and the rest kept.
                    bool reversed = Settings.Tech.ReverseCornerboostEnabled
                        && Math.Sign(cbRetainedSpeed) != 0
                        && Math.Sign(cbRetainedSpeed) != dir;
                    TechPopupsModule.ShowPopup(
                        reversed ? "Reverse Cornerboost" : "Cornerboost", self.Position);
                }
            }
        }

        // Backboost confirmation 
        if (backboostPending) {
            backboostTimer += Engine.DeltaTime;
            if (backboostTimer > Settings.Timing.BackboostMaxTime) {
                backboostPending = false;
            } else if (self.moveX == -backboostThrowDir) {
                if (Settings.Tech.BackboostEnabled)
                {
                    TechPopupsModule.ShowPopup("Backboost", backboostPos);
                }
                backboostPending = false;
            }
        }

        // Where the dash state ends. Combined with ultraLandTime below this gives
        // "how long the ultra boost was stored after the dash finished", which is
        // the only honest way to tell a Delayed Ultra from a plain one.
        bool dashingNow = self.StateMachine.State == Player.StDash;
        if (wasDashing && !dashingNow) {
            ultraDashEndTime = chainClock;
            // A dash that got cut short before DashUpdate ever ran (an instant
            // climbjump out of the dash, for one) must not leave either latch
            // waiting for a direction that is never coming.
            dashDirPending    = false;
            dashBouncePending = false;
        }
        wasDashing = dashingNow;

        // Crouch Dash fallback: the dash is over and it did not turn into a
        // demohyper (SuperJump consumes demoDash and reports the demohyper
        // itself), so what happened was a plain crouch dash - say so now.
        // The old test also required jumpGraceTimer <= 0, which on flat ground
        // only becomes true once you jump, so the popup always arrived late
        // ("要一直等到跳起来之后技巧框才出来").
        if (demoDash && !dashingNow) {
            demoDash = false;
            if (Settings.Tech.CrouchDashEnabled)
            {
                // "Demodash" is what the community actually calls this; the alias
                // is opt-in so the popup never disagrees with how you learned it.
                TechPopupsModule.ShowPopup(
                    Settings.Misc.DemodashNaming ? "Demodash" : "Crouch Dash",
                    self.Position);
            }
        }


        // Overclock / Wallboost used to be polled here, after orig. Both now
        // run before orig (see the block above) because the game zeroes
        // wallBoostTimer as part of the very conversion we are looking for.
        // The old Wallboost test watched Player.wallBoosting, which ClimbUpdate
        // sets from WallBoosterCheck() - the Core conveyor wall, not this tech.

        // ---- Grounded Ultra, the walking case --------------------------------
        // It used to be a 380-400 speed band. The player retired that idea
        // (2026-10-14): "the key is NOT the 380-400 speed, speed stacks without
        // limit". He is right - a band only ever catches the first ultra of a
        // chain, and it also fires for anything else that happens to reach it.
        //
        // The honest signature is the one PlayerSetDucking already stamps:
        // vanilla applies Speed.X *= 1.2 exactly there, for a down-diagonal
        // dash, on the frame it ducks her. So "a fast down-diagonal dash just
        // got cashed in on the floor" is the whole test - no band, no jump.
        //
        // ultraDashOnGround is what keeps this off a plain air ultra, and it
        // took a round to get right: without it, every down-diagonal dash that
        // landed above 250 popped "Grounded Ultra" on its landing frame, and the
        // handoff below then suppressed the jump that cashes the ultra in - so a
        // plain Ultra came out reading as a GU.
        //
        // The separator is the wiki's own definition of the technique: "you can
        // perform a Wave immediately followed by a down-diagonal Dash".
        // ultraDashOnGround IS "that dash started on the floor, or inside the
        // coyote frames a Wave/hyper hands you" - exactly what the flat-ground
        // chain has and an air ultra does not.
        if (self.onGround && ultraDashOnGround && ultraLandSpeed > 250f
            && chainClock - ultraLandTime <= GroundedUltraWindow
            && chainClock - lastGuSpeedTime > GroundUltraSpeedCooldown)
        {
            lastGuSpeedTime = chainClock;
            if (Settings.Tech.GroundedUltraEnabled)
            {
                TechPopupsModule.ShowPopup("Grounded Ultra", self.Position);
            }
        }

        spikeAfterPlayerUpdate = self.CollideCheck<Spikes>();
        if (spikePopupArmed)
        {
            if (!spikeAfterPlayerUpdate && !self.Dead)
            {
                if (Settings.Tech.SpikeTechEnabled)
                {
                    TechPopupsModule.ShowPopup("Spike Tech", self.Position);
                }
            }
            spikePopupArmed = false;
        }
    }

    static bool spikeAfterPlayerUpdate;
    static bool spikeClipLocked;
    static bool spikePopupArmed = false;

    // Theo/Jelly Regrab and Jellyvator/Theovator.
    //
    // Player.Pickup is called from three places: NormalUpdate, LaunchUpdate and
    // - the one that matters here - DashUpdate. Grabbing a throwable while the
    // dash is still running is what cancels the dash and preserves its speed.
    //
    // The dash attack window counts too. dashAttackTimer is set to 0.3 s the
    // moment a dash begins (DashBegin) and counts down in Update; it survives
    // the dash state ending, so a grab made a few frames after the dash
    // finished still preserves the dash speed exactly the same way. Missing it
    // meant short dashes - which end well inside the 0.3 s window - only ever
    // popped when the grab happened to land before the state switched.
    //
    // DashDir is a plain field written once when the dash fires (by
    // <DashCoroutine>.MoveNext) and never cleared afterwards, so it is still
    // valid throughout the dash attack window and can classify the direction.
    //
    // Straight-up dash = vator (drop the throwable, dash up into it, grab).
    // Any other dash direction = regrab.
    private static bool Player_Pickup(
            On.Celeste.Player.orig_Pickup orig, Player self, Holdable holdable) {

        // Snapshot before orig: the pickup switches to StPickup and the dash
        // (and with it DashDir) is gone by the time orig returns - and the
        // pickup animation zeroes Speed, so the "how fast was she" reading has
        // to be taken now as well.
        bool    dashing = self.StateMachine.State == Player.StDash;
        bool    dashAttack = !dashing && self.dashAttackTimer > 0f;
        Vector2 dashDir = self.DashDir;
        float   speedSq = self.Speed.LengthSquared();

        bool result = orig(self, holdable);
        if (!result || (!dashing && !dashAttack) || holdable == null) return result;

        // Holdable is a Component, not the entity itself - the jellyfish and
        // the crystal are the Actors that own one. `holdable is Glider` would
        // always be false (CS0184); go through holdable.Entity instead.
        var owner   = holdable.Entity;
        bool isJelly = owner is Glider;
        bool isTheo  = owner is TheoCrystal;
        if (!isJelly && !isTheo) return result;

        bool upDash = dashDir.Y < -0.5f && Math.Abs(dashDir.X) < 0.25f;
        string who  = isJelly ? "Jelly" : "Theo";

        // ---- Grounded Ultra, the cash-in half --------------------------------
        // Grabbing does not cancel anything, it KEEPS the boosted speed. One
        // technique, three ways to fire it - the labelled Ultra in Player_Jump,
        // this grab (feet still down), and the puffer bounce in Player_Bounce
        // (the floor is never touched at all) - so all three read the single
        // GroundedUltraEnabled switch. See GroundedUltraArmed for the two arms.
        //
        // Two things were wrong with the first version, which is why this never
        // fired:
        //   * it tested `dashDir.Y > 0` at grab time, but OnCollideV sets
        //     DashDir.Y = 0 on the very frame it applies the boost - so by the
        //     time a grab could happen the direction was already gone.
        //   * it tested Speed.Length() > 250 at grab time, but ground friction
        //     has been eating the speed since the landing.
        // Both facts are now snapshotted at the landing instead (see
        // ultraLandSpeed), and the window is the dash attack plus a hair.
        // requireGround = true keeps this from stealing the Jelly / Theo Regrab
        // label for a mid-air grab that merely happens to follow an ultra.
        //
        // The second arm is the player's own definition and needs no ground:
        // a down-diagonal dash at high speed that this grab interrupted, which
        // keeps the speed the same way the puffer bounce does. It is narrow on
        // purpose - the dash has to have been down-diagonal and still live - so
        // an ordinary Regrab (a horizontal or upward dash) keeps its own label.
        bool groundedUltra = GroundedUltraArmed(self, true)
            || DashInterruptedByEntity(self, dashing || dashAttack, speedSq);

        if (groundedUltra && Settings.Tech.GroundedUltraEnabled) {
            TechPopupsModule.ShowPopup("Grounded Ultra", self.Position);
        } else if (upDash) {
            if ((isJelly ? Settings.Tech.JellyvatorEnabled : Settings.Tech.TheovatorEnabled))
            {
                TechPopupsModule.ShowPopup(who + "vator", self.Position);
            }
        } else {
            if ((isJelly ? Settings.Tech.JellyRegrabEnabled : Settings.Tech.TheoRegrabEnabled))
            {
                TechPopupsModule.ShowPopup(who + " Regrab", self.Position);
            }
        }
        return result;
    }

    // ---- Grounded Ultra, the bounce half ------------------------------------
    // The grab is not the only way to cash a grounded ultra in: bouncing off
    // something that keeps the speed does it too, and a puffer is the one you
    // meet in normal play - Puffer.OnPlayer calls player.Bounce(Top) whenever
    // Madeline's bottom edge is above the puffer, which is precisely "stepped
    // on it"; hitting it anywhere else explodes it instead.
    //
    // Player.Bounce is the single funnel for every speed-preserving bounce, so
    // this covers "a puffer, and anything else that behaves like one" without
    // naming any of them. Verified callers: Puffer.OnPlayer, Snowball
    // .OnPlayerBounce, AngryOshiro.OnPlayerBounce, Seeker.OnBouncePlayer and
    // FireBall.OnBounce (that last one covers both the fire and the ICE ball -
    // it is the same entity with an iceMode flag). So the snowball / Oshiro /
    // seeker / ice ball bounces were always reaching this hook; what they were
    // failing was the arm test below.
    //
    // requireGround is false here because the step-on bounce has already lifted
    // her off the floor - and on a puffer the floor landing never happens, so
    // this arm leans on the dash stamp instead.
    private static void Player_Bounce(
            On.Celeste.Player.orig_Bounce orig, Player self, float fromY) {
        // Both read BEFORE orig: Bounce writes dashAttackTimer itself, and it
        // also hands out vertical speed, so afterwards there is no way to tell
        // whether a dash was live (nor how fast she was) when she landed on it.
        bool  dashLive = self.DashAttacking;
        float speedSq  = self.Speed.LengthSquared();
        orig(self, fromY);
        if (!Settings.Tech.GroundedUltraEnabled) return;

        bool armed       = GroundedUltraArmed(self, false);
        bool interrupted = DashInterruptedByEntity(self, dashLive, speedSq);

        if (armed || interrupted) {
            TechPopupsModule.ShowPopup("Grounded Ultra", self.Position);
        }
    }

    // Fast Bubble is detected in Player_Update straight off the booster state -
    // there is no hook on Player.Boost any more. See the comment there for why
    // both of the older approaches (entry-frame dash, and a 0.35 s window that
    // accepted a HELD dash) were wrong.

    // Every "launched by something" case funnels through here (bumper, puffer,
    // temple eyeball, seeker) - it refills dashes and sets StLaunch.
    //
    // Firing the popup on the launch itself was wrong: that is just "something
    // pushed you", so it popped for every puffer, seeker and bumper. The tech
    // is the extra 1.2x, and ExplodeLaunch only ever computes it when the stick
    // points the way the blast is throwing her:
    //
    //   Input.MoveX == Sign(Speed.X)  ->  Speed.X *= 1.2 right now
    //   otherwise                     ->  explodeLaunchBoostSpeed = Speed.X * 1.2
    //                                     and a 0.01 s timer; Player.Update pays
    //                                     it out only if the stick points that
    //                                     way next frame, else it is forfeited
    //
    // So a plain bump gives nothing and must stay silent, which is the whole
    // point of replacing the old detection.
    private static Vector2 Player_ExplodeLaunch(
            On.Celeste.Player.orig_ExplodeLaunch_Vector2_bool_bool orig,
            Player self, Vector2 direction, bool bounce, bool alt)
    {
        Vector2 result = orig(self, direction, bounce, alt);

        explodeBoostSign = 0;
        explodeBoostWatch = 0f;

        if (self.Speed.X != 0f)
        {
            int launchSign = Math.Sign(self.Speed.X);
            if (Input.MoveX.Value == launchSign)
            {
                // Already multiplied inside orig(); nothing left to watch.
                GrantExplosionBoost(self);
            }
            else
            {
                explodeBoostSign = launchSign;
                explodeBoostWatch = 0.05f;   // a couple of frames, the game's own is 0.01
            }
        }
        return result;
    }

    private static void GrantExplosionBoost(Player self)
    {
        if (Settings.Tech.LaunchBoostEnabled)
            TechPopupsModule.ShowPopup("Explosion Boost", self.Position);
    }

    // Shared by Player_Jump and Player_StartDash. One boosted jump is just the
    // block boost; two inside the 9-frame window is the Double Block Boost.
    // After reporting, the counter is reset so the popup cannot chain itself on
    // every single hop of a bunnyhop chain.
    private static void BlockBoostTouch(Player self)
    {
        if (chainClock - liftBoostTime > BlockBoostWindow) return;

        if (chainClock - lastBlockBoostTime <= BlockBoostWindow)
        {
            lastBlockBoostTime = -999f;
            if (Settings.Tech.DoubleBlockBoostEnabled)
                TechPopupsModule.ShowPopup("Double Block Boost", self.Position);
            return;
        }
        lastBlockBoostTime = chainClock;
    }

    // The backdrop blur runs here, before the HUD is drawn rather than after.
    //
    // Level.Render blits the finished level from GameplayBuffers.Level onto the
    // backbuffer and only then calls Level.HudRenderer.Render - and HudRenderer
    // is what renders the Tags.HUD entities, our popups among them. So the
    // prefix of RenderContent is simultaneously "the level is complete in the
    // backbuffer" and "not one label has been drawn yet", which is the only
    // moment in the frame where a clean level image is reachable at all.
    //
    // Everything the blur does with that image is CPU-side (GetBackBufferData
    // plus a downsample), so this hook cannot disturb the frame either. See
    // BackdropBlur for the whole story.
    // The level target keeps its pixels only if it is asked to BEFORE the
    // engine binds and clears it, which is exactly here - see
    // BackdropBlur.BeforeLevelRender for what that flag buys.
    private static void Level_Render(On.Celeste.Level.orig_Render orig, Level self)
    {
        BackdropBlur.BeforeLevelRender();
        orig(self);
        BackdropBlur.AfterLevelRender(self);
    }

    private static void HudRenderer_RenderContent(
            On.Celeste.HudRenderer.orig_RenderContent orig,
            HudRenderer self, Scene scene)
    {
        BackdropBlur.CaptureBackdrop(scene);
        orig(self, scene);
    }

    private static void Level_Update(On.Celeste.Level.orig_Update orig, Level self)
    {
        orig(self);

        // Main thread: rebuild the panel snapshot and apply any toggles it
        // queued up. Never touch game state from the HTTP thread.
        TechPanelServer.OnMainThread();

        var player = self.Tracker.GetEntity<Player>();
        if (player != null)
        {
            bool spikeAfterLevelUpdate = player.CollideCheck<Spikes>();
            if (!spikeAfterPlayerUpdate && spikeAfterLevelUpdate && !player.Dead)
            {
                if (!spikeClipLocked)
                {
                    spikePopupArmed = true;
                }
                spikeClipLocked = true;
            }
            else
            {
                spikeClipLocked = false;
                spikePopupArmed = false;
            }
        }
    }

    // Core block coyote 
    // BounceBlock.ShakeOffPlayer is the only caller of StartJumpGraceTime in the
    // entire Celeste codebase. When it fires, a core block just launched and
    // shook the player off, granting coyote frames and horizontal liftboost.
    // The next SuperJump during those coyote frames is a Core Super or a Core Hyper.
    private static void Player_StartJumpGraceTime(
            On.Celeste.Player.orig_StartJumpGraceTime orig, Player self) {
        orig(self);
        coreGrace    = true;
        coreGracePos = self.Position;
    }

    // Ducking + dashedOnGround -> Hyper
    // Ducking + !dashedOnGround -> Wavedash
    // !Ducking -> Super
    // demoDash -> Demohyper
    // Reverse: if the dash had a horizontal component opposite to current Facing -> prefix "Reverse ".
    // Core / Dream variants layered on top.
    private static void Player_SuperJump(
            On.Celeste.Player.orig_SuperJump orig, Player self) {
        ultraDucked = false;

        string baseTech;
        if (self.Ducking) {
            if (demoDash) {
                baseTech = Settings.Misc.DemohyperConversionEnabled ? "Demohyper" : "Hyper";
                demoDash = false; // consumed crouch dash
            } else if (dashedOnGround) {
                baseTech = "Hyper";
            } else
            {
                baseTech = Settings.Misc.WavedashConversionEnabled ? "Wavedash" : "Hyper";
            }
        } else {
            baseTech = "Super";
        }

        // Reverse: dashed horizontally in the opposite direction to current Facing.
        // lastDashDirX == 0 means a pure-down dash, which has no reverse variant.
        bool isReverse  = lastDashDirX != 0 && lastDashDirX != (int)self.Facing;

        // Extended: the dash refill cooldown elapsed while the dash was still
        // running, so the dash came back before this jump happened.
        bool isExtended = self.Dashes > dashesAtDashStart;

        string prefix = "";
        if (isExtended && Settings.Tech.ExtendedEnabled) prefix += "Extended ";
        if (isReverse && Settings.Misc.ReversePrefixEnabled) prefix += "Reverse ";

        bool isDownward = baseTech == "Hyper" || baseTech == "Demohyper" || baseTech == "Wavedash";
        bool hasDream   = dreamGrace;
        bool hasCore    = coreGrace;

        if (!hasDream && !hasCore) {
            if (isDownward)
            {
                if (Settings.Tech.HyperEnabled)
                {
                    TechPopupsModule.ShowPopup(prefix + baseTech, self.Position);
                }
            }
            else
            {
                // Mid-air variant: the dash that produced this super did not
                // start on the ground. Off by default - it fires instead of
                // the plain super, so leaving it on hides the normal name.
                if (Settings.Tech.MidairSuperEnabled && !dashedOnGround)
                {
                    TechPopupsModule.ShowPopup(prefix + "Mid-air Super", self.Position);
                }
                else if (Settings.Tech.SuperEnabled)
                {
                    TechPopupsModule.ShowPopup(prefix + baseTech, self.Position);
                }
            }
        }

        if (hasCore) {
            if (Settings.Tech.CoreTechEnabled)
            {
                TechPopupsModule.ShowPopup(prefix + (isDownward ? "Core Hyper" : "Core Super"), coreGracePos);
            }
            coreGrace = false;
        }
        else if (hasDream) {
            if (Settings.Tech.DreamTechEnabled)
            {
                TechPopupsModule.ShowPopup(prefix + (isDownward ? "Dream Hyper" : "Dream Super"), dreamGracePos, 0.05f);
            }
            dreamGrace = false;
        }


        orig(self);

        // Arm bunnyhop hold tracking for the jump that SuperJump just performed (since Jump() doesn't run in this case)
        bhopTrackingHold = true;
        bhopHoldTimer    = 0f;
    }

    // SuperWallJump -> Wallbounce 
    private static void Player_SuperWallJump(
            On.Celeste.Player.orig_SuperWallJump orig, Player self, int dir) {
        var delayed = self.dashAttackTimer > 0f && self.StateMachine.State != Player.StDash;
        orig(self, dir);

        if (dreamGrace && !delayed) {
            if (Settings.Tech.DreamTechEnabled)
            {
                TechPopupsModule.ShowPopup("Dream Hyper", dreamGracePos, 0.05f);
            }
            dreamGrace = false;
        }
        else
        {
            var text = (delayed && Settings.Misc.DelayedWallbounceConversionEnabled) ? "Delayed Wallbounce" : "Wallbounce";
            wallbounceTime = chainClock;
            if (Settings.Tech.WallbounceEnabled)
            {
                TechPopupsModule.ShowPopup(text, self.Position);
            }
        }
    }

    // Ultra: ultraDucked still set when Jump fires = diagonal-down air dash landed, player did not super-jump. Speed.LengthSquared > 57600 (= 240²) -> Ultra.
    // Dream Jump / Dream Double Jump.
    // Bunnyhop: confirm if bhopPending, then restart hold tracking.
    private static void Player_Jump(
            On.Celeste.Player.orig_Jump orig, Player self, bool particles, bool playSfx) {
        // Double Block Boost: taken before orig so LiftBoost is still the live
        // value from the frame's update - this is the jump that gets the block's
        // speed added to it (orig does `Speed += LiftBoost`).
        BlockBoostTouch(self);

        // Ultra (verbatim from TechAnnouncer, threshold 240 px/s)
        if (ultraDucked && self.StateMachine.State == 0) {
            // NOTE: no DashDir check here. DashDir is decayed towards zero every
            // frame once the dash ends, so by the time the jump happens it is
            // usually (0,0) - checking it made every Ultra silently disappear.
            // Staleness is handled by StartDash clearing ultraDucked instead.
            if (self.Speed.LengthSquared() > 57600f) {
                // The walking (flat-ground) Grounded Ultra is announced by the
                // speed band the moment the 380-400 arrives on the floor, and
                // that arrival is the thing a player watches while chaining. A
                // jump a few frames later is the SAME move, so it must not
                // announce it a second time - and it must not call it a plain
                // "Ultra" either, which is what reading ultraDashOnGround here
                // did (the walking chain's down-diagonal dash starts in the air,
                // so that snapshot says false even though the tech is grounded).
                bool walkingAlreadyAnnounced = Settings.Tech.GroundedUltraEnabled
                    && chainClock - lastGuSpeedTime <= GroundedUltraHandoffWindow;
                if (Settings.Tech.UltraEnabled && !walkingAlreadyAnnounced)
                {
                    // Grounded vs normal comes from the dash-start snapshot, NOT
                    // from self.onGround here: an ultra always ends with Madeline
                    // standing on the floor, so testing it at jump time made every
                    // single ultra come out as "Grounded Ultra".
                    bool grounded = Settings.Tech.GroundedUltraEnabled && ultraDashOnGround;
                    bool chained  = !grounded
                        && Settings.Tech.ChainedUltraEnabled
                        && (chainClock - lastUltraTime) <= Settings.Timing.ChainWindow;
                    // "Stored for a significant amount of time" - and the clock only
                    // starts once the dash is over, because nothing is being stored
                    // while you are still dashing. A plain Ultra lands during the
                    // dash or in the few frames right after it; a Delayed Ultra is
                    // one you deliberately carried somewhere else first (cornerboost,
                    // zip block, stall) before cashing the 1.2x in. Measuring from
                    // the dash *start* counted the 0.2 s dash itself as "delay",
                    // which is exactly why every plain Ultra came out as a DU.
                    bool delayed  = !grounded && Settings.Tech.DelayedUltraEnabled
                        && ultraLandTime > ultraDashEndTime
                        && (ultraLandTime - ultraDashEndTime) > Settings.Timing.DelayedUltraWindow;
                    lastUltraTime = chainClock;
                    // Also stamps the walking-band clock: an ultra is a jump, so
                    // the speed left behind by this very move must not be read as
                    // a fresh arrival in the Grounded Ultra band a frame later.
                    // That is what produced "plain Ultra AND Grounded Ultra" for
                    // one move.
                    lastGuSpeedTime = chainClock;
                    // Delayed wins over Chained when both fit: a Du done as part of
                    // a chain is still a Du, and the delay is the more specific
                    // fact about it (a chained Ultra is the common case, so it must
                    // not swallow the delayed one).
                    TechPopupsModule.ShowPopup(
                        grounded ? "Grounded Ultra"
                            : delayed  ? "Delayed Ultra"
                            : chained  ? "Chained Ultra" : "Ultra",
                        self.Position);
                }
            }
            ultraDucked = false;
        }

        // Reform tech: jumping off the top of a reforming block
        if (self.onGround || self.jumpGraceTimer > 0f) {
            var blockBelow = self.CollideFirst<Solid>(self.Position + Vector2.UnitY);
            if (blockBelow != null) TryShowReformTech(self, blockBelow);
        }

        // Cassette Block Boost: armed by CassetteBlock_Update on the exact frame a
        // block reforms near your feet, and confirmed by the jump that follows.
        // The old test (`LiftBoost.Y < -30` while a cassette sits under you) was
        // dead code twice over: a block that teleports into place never registers
        // Madeline as a rider, so she accumulates no liftboost from it at all, and
        // the LiftBoost getter clamps every positive Y to zero, so the check could
        // only ever have meant "riding something that is rising".
        if (cassetteBoostWindow > 0f) {
            cassetteBoostWindow = 0f;
            if (Settings.Tech.CassetteBoostEnabled) {
                TechPopupsModule.ShowPopup("Cassette Block Boost", self.Position);
            }
        }

        // Water Boost: jumping out of the water surface.
        // The game's own SwimJumpCheck() is exactly `CollideCheck<Water>(Position + UnitY * -14f)`,
        // so reusing that test means this fires on exactly the same frames the game
        // would let you jump out of the water - no guessing involved.
        if (self.CollideCheck<Water>(self.Position + Vector2.UnitY * -14f)) {
            if (Settings.Tech.WaterBoostEnabled)
            {
                TechPopupsModule.ShowPopup("Water Boost", self.Position);
            }
        }

        // Dream Jump / Dream Double Jump.
        // Normal path: dreamGrace was set by DreamDashEnd before this Jump() call.
        // Short path: DreamDashUpdate sets dreamJump=true and calls Jump()
        // directly while still in StDreamDash, so DreamDashEnd hasn't fired yet.
        if (!dreamGrace && self.dreamJump && self.StateMachine.State == Player.StDreamDash) {
            // Short exit: arm grace and start the Dream Jump delay.
            dreamGrace            = true;
            dreamJumpedOnce       = true;
            dreamGracePos         = self.Position;
            dreamJumpPendingTimer = DreamJumpDelay;
        } else if (dreamGrace) {
            if (!dreamJumpedOnce) {
                dreamJumpedOnce       = true;
                dreamJumpPendingTimer = DreamJumpDelay; // may be cancelled by Dream Double Jump
            } else {
                // Second jump: cancel the pending Dream Jump and show Dream Double Jump instead.
                dreamJumpPendingTimer = 0f;
                // Its own switch: this used to read DreamJumpEnabled, so turning off
                // either of the two dream jumps silenced the other one as well.
                var text = Settings.Misc.DreamDoubleJumpConversionEnabled ? "Dream Double Jump" : "Dream Jump";
                if (Settings.Tech.DreamDoubleJumpEnabled)
                {
                    TechPopupsModule.ShowPopup(text, dreamGracePos);
                }
                dreamGrace = false;
            }
        }

        // Bunnyhop confirm:
        // bhopLanded   - normal landing path: Update saw the player on the ground first,
        //                land-jump window was started, now Jump() fires -> confirmed.
        // self.onGround - buffered jump path: player pressed jump in the air, the game
        //                fires Jump() immediately when they land inside orig(self), so
        //                Update never had a chance to set bhopLanded - but onGround IS
        //                already true at the moment Jump() runs -> skip land window,
        //                confirm directly.
        if (bhopPending && (bhopLanded || self.onGround)) {
            if (Settings.Tech.BunnyhopEnabled)
            {
                bool chained = Settings.Tech.ChainedBunnyhopEnabled
                    && (chainClock - lastBhopTime) <= Settings.Timing.ChainWindow;
                lastBhopTime = chainClock;
                TechPopupsModule.ShowPopup(chained ? "Chained Bunnyhop" : "Bunnyhop", self.Position);
            }
            // Immediately re-arm for chaining
            bhopPending      = false;
            bhopLanded       = false;
            bhopLandJumpTimer = 0f;
        }

        // Start tracking hold time for this jump
        bhopTrackingHold = true;
        bhopHoldTimer    = 0f;

        orig(self, particles, playSfx);
    }

    // WallJump 
    private static void Player_WallJump(
            On.Celeste.Player.orig_WallJump orig, Player self, int dir) {
        if (dreamGrace) {
            if (Settings.Tech.DreamJumpEnabled)
            {
                TechPopupsModule.ShowPopup("Dream Jump", dreamGracePos);
            }
            dreamGrace = false;
        }

        // Wall kick off a reforming block, check both sides since dir tells us which wall
        var wallSolid = self.CollideFirst<Solid>(self.Position - Vector2.UnitX * dir);
        if (wallSolid != null) TryShowReformTech(self, wallSolid);

        // ---- Corner Kick ----------------------------------------------------
        // Celeste wiki: "Cornerkicks are wallkicks off corners. Performed when
        // Madeline passes right under them, they give a little bit of height.
        // To cornerkick, jump towards the corner, and press Jump again when
        // you're right next to the corner." The other revision spells it out as
        // "the use of bottom corners of terrain to kick off". Madeline goes
        // UNDER the wall's bottom corner, the game's corner correction lifts
        // her past it by a few pixels, and the wallkick she gets there is it.
        //
        // There is no flag for this - it IS an ordinary Player.WallJump - so it
        // has to be measured. Use the game's own wall probe rather than a
        // hand-rolled CollideCheck: Player.ClimbCheck(dir, y) is exactly "is
        // there a climbable wall beside me, y pixels from my centre", and it is
        // the test WallJumpCheck itself calls.
        //
        //   ClimbCheck(side, 0)  -> the wall is level with her body. This is
        //                           what made the wallkick legal in the first
        //                           place.
        //   ClimbCheck(side, 11) -> the wall runs on past her feet (her hitbox is
        //                           11 px tall, so this is level with its bottom
        //                           edge), and she is in the middle of a wall
        //                           rather than at the end of one.
        //
        // Corner kick = first true, second false: the wall's bottom corner sits
        // inside her own body height. `dir` is the way she kicks, so the wall is
        // at -dir.
        //
        // The previous version probed with the full 8x11 hitbox shifted by 1 px,
        // which shifts the box by about a tenth of its own width - so "wall
        // beside her" and "wall at her feet" were nearly the same question, and
        // firing needed the corner to land in a ~1 px window.
        if (Settings.Tech.CornerKickEnabled) {
            int side = -dir;
            // 11 rather than 13: the probe now reaches exactly the bottom of her
            // hitbox, so "there is no wall at her feet" means the corner really is
            // inside her own height. At 13 the probe sat below her, which let a
            // corner sitting further down - i.e. a plain wall-kick - through.
            if (self.ClimbCheck(side, 0) && !self.ClimbCheck(side, 11)) {
                TechPopupsModule.ShowPopup("Corner Kick", self.Position);
            }
        }

        bool neutralJump = Input.MoveX.Value == 0;

        // Retention Tech: wall-jumping while the game is still holding on to
        // the speed you had before touching the wall.
        var retentionTimer = (float)FI_wallSpeedRetentionTimer.GetValue(self);
        if (retentionTimer > 0f && Input.MoveX.Value != 0)
        {
            if (Settings.Tech.RetentionTechEnabled)
            {
                TechPopupsModule.ShowPopup("Retention Tech", self.Position);
            }
        }

        orig(self, dir);

        if (neutralJump && neutralJumpPopupCooldown <= 0f)
        {
            if (Settings.Tech.NeutralJumpEnabled)
            {
                TechPopupsModule.ShowPopup("Neutral Jump", self.Position);
            }
            neutralJumpPopupCooldown = NeutralJumpPopupDelay;
        }
    }

    // dreamJump = true only for horizontal exits (set by game in DreamDashBegin).
    // For short blocks, DreamDashUpdate calls Jump() before DreamDashEnd fires, so
    // Player_Jump already armed dreamGrace and fired the first popup - don't re-arm.
    private static void Player_DreamDashEnd(
            On.Celeste.Player.orig_DreamDashEnd orig, Player self) {
        orig(self);
        if (self.dreamJump && !dreamGrace) {
            dreamGrace      = true;
            dreamJumpedOnce = false;
            dreamGracePos   = self.Position;
        }
    }

    // A throw gives speed opposite to Facing. If the player is throwing behind
    // them (Facing is opposite to their current horizontal movement), arm the
    // backboost pending state so we can confirm when they turn back around.
    private static void Player_Throw(
            On.Celeste.Player.orig_Throw orig, Player self) {
        // Capture pre-throw state.
        bool hasHoldable = self.Holding != null;
        int  facing      = (int)self.Facing;

        orig(self);

        int moveDir = Math.Sign(self.Speed.X);

        // Neutral Drop: hold Down and tap Grab, and the thing in your
        // hands is set down on the spot instead of being thrown.
        //
        // Decompiling Player.Throw shows exactly two branches:
        //   if (Input.MoveY.Value == 1) Drop();          // set it down, v = 0
        //   else                        ... v = 80 * Facing   // throw it
        // and Player.Drop does `Holding.Release(Vector2.Zero); Holding = null`.
        // So the test is `MoveY == 1`, i.e. down IS held. The old check had it
        // as `!= 1`, exactly backwards - which is why ordinary left/right throws
        // were being reported as neutral drops.
        if (Settings.Tech.NeutralDropEnabled
                && hasHoldable && self.Holding == null
                && Input.MoveY.Value == 1)
        {
            TechPopupsModule.ShowPopup("Neutral Drop", self.Position);
        }

        // Arm on any horizontal throw (MoveY != 1).
        if (hasHoldable && self.Holding == null
                && Input.MoveY.Value != 1
                && moveDir != 0 &&
                moveDir == -facing) {
            backboostPending  = true;
            backboostThrowDir = facing;
            backboostPos      = self.Position;
            backboostTimer    = 0f;
        }
    }
}
