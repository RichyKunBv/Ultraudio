using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ultraudio.Services;

/// <summary>
/// Lightweight HTTP server for local remote control.
/// Runs on localhost only (not exposed to network).
/// Default port: 7654
/// 
/// Endpoints:
///   GET  /status          → current player state as JSON
///   POST /play            → play/resume
///   POST /pause           → pause
///   POST /toggle          → play/pause toggle
///   POST /next            → next track
///   POST /prev            → previous track
///   POST /stop            → stop
///   POST /volume/{0-100}  → set volume (0-100 integer)
///   GET  /playlist        → current playlist as JSON
/// </summary>
public class HttpRemoteService : IDisposable
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private bool _running = false;

    public int Port { get; private set; }

    // ── Player callbacks (wired by MainWindow) ────────────────────────────────
    public Action? OnPlay     { get; set; }
    public Action? OnPause    { get; set; }
    public Action? OnToggle   { get; set; }
    public Action? OnNext     { get; set; }
    public Action? OnPrev     { get; set; }
    public Action? OnStop     { get; set; }
    public Action<double>? OnVolume { get; set; } // 0.0-1.0

    // ── State providers (wired by MainWindow) ─────────────────────────────────
    public Func<object>? GetStatus   { get; set; }
    public Func<object>? GetPlaylist { get; set; }

    public bool IsRunning => _running;

    public void Start(int port = 7654)
    {
        if (_running) Stop();

        Port = port;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");

        try
        {
            _listener.Start();
            _running = true;
            _cts = new CancellationTokenSource();
            _ = ListenAsync(_cts.Token);
            Console.WriteLine($"[HTTP Remote] Listening on http://127.0.0.1:{port}/");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HTTP Remote] Failed to start: {ex.Message}");
            _running = false;
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _running = false;
        Console.WriteLine("[HTTP Remote] Stopped.");
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener!.IsListening)
        {
            HttpListenerContext? ctx = null;
            try
            {
                ctx = await _listener.GetContextAsync();
            }
            catch
            {
                break;
            }

            _ = Task.Run(() => HandleRequest(ctx), ct);
        }
    }

    private void HandleRequest(HttpListenerContext ctx)
    {
        try
        {
            var req = ctx.Request;
            var res = ctx.Response;
            res.Headers.Add("Access-Control-Allow-Origin", "*");
            res.ContentType = "application/json";

            string path = req.Url?.AbsolutePath.TrimEnd('/').ToLower() ?? "/";
            string method = req.HttpMethod.ToUpper();

            object? responseObj = null;

            if (method == "GET" && path == "/status")
            {
                responseObj = GetStatus?.Invoke() ?? new { status = "no_info" };
            }
            else if (method == "GET" && path == "/playlist")
            {
                responseObj = GetPlaylist?.Invoke() ?? new { playlist = Array.Empty<object>() };
            }
            else if (method == "POST" && path == "/play")
            {
                OnPlay?.Invoke();
                responseObj = new { ok = true, action = "play" };
            }
            else if (method == "POST" && path == "/pause")
            {
                OnPause?.Invoke();
                responseObj = new { ok = true, action = "pause" };
            }
            else if (method == "POST" && path == "/toggle")
            {
                OnToggle?.Invoke();
                responseObj = new { ok = true, action = "toggle" };
            }
            else if (method == "POST" && path == "/next")
            {
                OnNext?.Invoke();
                responseObj = new { ok = true, action = "next" };
            }
            else if (method == "POST" && path == "/prev")
            {
                OnPrev?.Invoke();
                responseObj = new { ok = true, action = "prev" };
            }
            else if (method == "POST" && path == "/stop")
            {
                OnStop?.Invoke();
                responseObj = new { ok = true, action = "stop" };
            }
            else if (method == "POST" && path.StartsWith("/volume/"))
            {
                string volStr = path.Substring("/volume/".Length);
                if (int.TryParse(volStr, out int volInt))
                {
                    double vol = Math.Clamp(volInt / 100.0, 0.0, 1.0);
                    OnVolume?.Invoke(vol);
                    responseObj = new { ok = true, volume = volInt };
                }
                else
                {
                    res.StatusCode = 400;
                    responseObj = new { ok = false, error = "Invalid volume value" };
                }
            }
            else
            {
                res.StatusCode = 404;
                responseObj = new { ok = false, error = "Unknown endpoint", path };
            }

            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(responseObj ?? new { });
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes, 0, bytes.Length);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HTTP Remote] Request error: {ex.Message}");
        }
        finally
        {
            ctx.Response.Close();
        }
    }

    public void Dispose() => Stop();
}
