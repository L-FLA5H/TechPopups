using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using Celeste.Mod;
using Monocle;

namespace Celeste.Mod.TechPopups;

// Out-of-game tech picker.
//
// A tiny HttpListener bound to localhost only - no 0.0.0.0, no external
// access. The serving thread NEVER touches game state: reads come from a JSON
// snapshot built on the main thread, and writes are queued and applied by the
// main thread. Reading Everest settings straight from a background thread is
// exactly the kind of thing that crashes randomly.
public static class TechPanelServer
{
    private const string PrefixHost = "localhost";
    private const string HtmlResourceSuffix = ".TechPanel.html";

    private static HttpListener listener;
    private static Thread thread;
    private static volatile bool running;
    private static volatile string cachedJson = "{\"techs\":[]}";

    private static readonly object queueLock = new object();
    private static readonly List<KeyValuePair<string, bool>> pendingWrites = new();
    private static readonly List<KeyValuePair<string, int>> pendingVisualWrites = new();
    private static volatile bool saveRequested;
    private static volatile string cachedVisual = "{\"visual\":[]}";

    public static string Url { get; private set; } = "";

    public static void Start() {
        Stop();
        try {
            int port = TechPopupsModule.Settings.Panel.Port;
            Url = "http://" + PrefixHost + ":" + port + "/";
            listener = new HttpListener();
            listener.Prefixes.Add(Url);
            listener.Start();
            running = true;
            thread = new Thread(Loop) { IsBackground = true, Name = "TechPopupsPanel" };
            thread.Start();
            Logger.Info("TechPopups", "Tech panel listening on " + Url);
        } catch (Exception e) {
            running = false;
            listener = null;
            Logger.Warn("TechPopups", "Tech panel failed to start: " + e.Message);
        }
    }

    public static void Stop() {
        running = false;
        try { listener?.Stop(); } catch { }
        try { listener?.Close(); } catch { }
        listener = null;
        thread = null;
        Url = "";
    }

    // Called from the main thread (Level.Update). Rebuilds the snapshot and
    // drains any writes the panel asked for.
    public static void OnMainThread() {
        cachedJson   = TechRegistry.ToJson();
        cachedVisual = TechVisual.ToJson();

        bool dirty = false;
        lock (queueLock) {
            if (pendingWrites.Count > 0) {
                foreach (var kv in pendingWrites) {
                    if (TechRegistry.SafeSet(kv.Key, kv.Value)) dirty = true;
                }
                pendingWrites.Clear();
            }
            if (pendingVisualWrites.Count > 0) {
                foreach (var kv in pendingVisualWrites) {
                    if (TechVisual.SafeSet(kv.Key, kv.Value)) dirty = true;
                }
                pendingVisualWrites.Clear();
            }
        }
        if (dirty) {
            try { TechPopupsModule.Instance?.SaveSettings(); } catch { }
        }
        if (saveRequested) {
            saveRequested = false;
            try { TechPopupsModule.Instance?.SaveSettings(); } catch { }
        }
    }

    private static void Loop() {
        while (running) {
            HttpListenerContext ctx;
            try {
                ctx = listener.GetContext();
            } catch {
                if (!running) return;
                continue;
            }

            try {
                Handle(ctx);
            } catch (Exception e) {
                Logger.Warn("TechPopups", "Tech panel request failed: " + e.Message);
                try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
            }
        }
    }

    private static void Handle(HttpListenerContext ctx) {
        var req = ctx.Request;
        string path = req.Url.AbsolutePath.Trim('/').ToLowerInvariant();

        if (req.HttpMethod == "POST" && path == "set") {
            string id = req.QueryString["id"] ?? "";
            string on = req.QueryString["on"] ?? "";
            bool value = on == "1" || on == "true";
            bool ok = TechRegistry.Find(id) != null;
            if (ok) {
                lock (queueLock) { pendingWrites.Add(new KeyValuePair<string, bool>(id, value)); }
            }
            Respond(ctx, ok ? "{\"ok\":true}" : "{\"ok\":false}", "application/json");
            return;
        }

        if (req.HttpMethod == "POST" && path == "setvisual") {
            string key = req.QueryString["key"] ?? "";
            string val = req.QueryString["value"] ?? "";
            int v;
            bool parsed = int.TryParse(val, out v);
            // Validate the key WITHOUT touching settings - this thread must not
            // read or write Everest settings. The write is queued for Level.Update.
            bool ok = parsed && TechVisual.HasKey(key);
            if (ok) {
                lock (queueLock) { pendingVisualWrites.Add(new KeyValuePair<string, int>(key, v)); }
            }
            Respond(ctx, ok ? "{\"ok\":true}" : "{\"ok\":false}", "application/json");
            return;
        }

        if (req.HttpMethod == "POST" && path == "save") {
            saveRequested = true;
            Respond(ctx, "{\"ok\":true}", "application/json");
            return;
        }

        if (path == "techs" || path == "api/techs") {
            Respond(ctx, cachedJson, "application/json");
            return;
        }

        if (path == "visual" || path == "api/visual") {
            Respond(ctx, cachedVisual, "application/json");
            return;
        }

        Respond(ctx, LoadHtml(), "text/html; charset=utf-8");
    }

    private static void Respond(HttpListenerContext ctx, string body, string type) {
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        ctx.Response.StatusCode = 200;
        // charset matters: every label in TechVisual is Chinese, and without it
        // the browser is free to guess a non-UTF-8 default for text/html.
        ctx.Response.ContentType = type.Contains("charset") ? type : type + "; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.AddHeader("Cache-Control", "no-store");
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.OutputStream.Close();
    }

    private static string LoadHtml() {
        Assembly asm = typeof(TechPanelServer).Assembly;
        foreach (string name in asm.GetManifestResourceNames()) {
            if (name.EndsWith(HtmlResourceSuffix, StringComparison.OrdinalIgnoreCase)) {
                using (Stream s = asm.GetManifestResourceStream(name))
                using (var reader = new StreamReader(s)) {
                    return reader.ReadToEnd();
                }
            }
        }
        return "<!doctype html><meta charset=\"utf-8\">"
             + "<p>TechPanel.html was not embedded in the DLL. Rebuild the mod.</p>";
    }
}
