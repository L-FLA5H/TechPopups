using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.TechPopups;

public class TechPopupsModule : EverestModule
{
    public static TechPopupsModule Instance { get; private set; }

    public override Type SettingsType => typeof(TechPopupsSettings);
    public static TechPopupsSettings Settings => (TechPopupsSettings)Instance._Settings;

    private static readonly List<Rectangle> frameOccupiedBounds = new();
    private static int lastFrameCount = -1;

    public TechPopupsModule()
    {
        Instance = this;
    }

    public override void Load()
    {
        TechTracker.Load();
        if (Settings.Panel.Enabled)
        {
            TechPanelServer.Start();
        }
    }

    public override void Unload()
    {
        TechPanelServer.Stop();
        TechTracker.Unload();
    }
    public static void ShowPopup(string techName, Vector2 worldPosition, float delay = 0f)
    {
        if (!Settings.Enabled) return;

        var level = Engine.Scene as Level;
        if (level == null) return;

        var player = level.Tracker.GetEntity<Player>();
        float speed = player?.Speed.Length() ?? 0f;

        // No early-out for icon-only mode any more. Techniques without a PNG in
        // the atlas now get a generated letter badge, so there is always
        // something to draw.

        float slotHeight = 26f * Settings.Popup.Scale;

        int frame = (int)Engine.FrameCounter;
        if (frame != lastFrameCount)
        {
            frameOccupiedBounds.Clear();
            lastFrameCount = frame;
        }

        // Anchored at the true world position on purpose: clamping to
        // level.Bounds would snap it to the current room edge, which both
        // displaces the label away from the trick and makes it stop tracking
        // the background while the camera keeps moving.
        var entity = new TechPopupEntity(worldPosition, techName, delay, speed);

        foreach (var existing in level.Entities.FindAll<TechPopupEntity>())
        {
            frameOccupiedBounds.Add(existing.GetScreenBounds(level));
        }

        int chosenSlot = 0;
        Rectangle chosenBounds = Rectangle.Empty;
        bool found = false;

        for (int magnitude = 0; magnitude <= 64 && !found; magnitude++)
        {
            int[] candidates = magnitude == 0
                ? new[] { 0 }
                : new[] { -magnitude, magnitude };

            foreach (int slot in candidates)
            {
                entity.StackOffsetY = slot * slotHeight;
                var candidateBounds = entity.GetScreenBounds(level);

                bool overlaps = false;
                for (int i = 0; i < frameOccupiedBounds.Count; i++)
                {
                    if (frameOccupiedBounds[i].Intersects(candidateBounds))
                    {
                        overlaps = true;
                        break;
                    }
                }

                if (overlaps) continue;

                chosenSlot = slot;
                chosenBounds = candidateBounds;
                found = true;
                break;
            }
        }

        entity.StackOffsetY = chosenSlot * slotHeight;
        level.Add(entity);
        frameOccupiedBounds.Add(chosenBounds == Rectangle.Empty ? entity.GetScreenBounds(level) : chosenBounds);
    }
}