using System.Net;
using System.Text.Json;
using BayControl.Host.Services;
using BayControl.Host.Services.Clips;
using BayControl.Host.Services.Launch;
using BayControl.Host.UI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BayControl.Host;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        var baseDir = AppContext.BaseDirectory;
        var webRoot = ResolvePath(baseDir, "wwwroot", Path.Combine(baseDir, "..", "..", "..", "..", "web"));
        var profilesDir = ResolvePath(baseDir, "profiles", Path.Combine(baseDir, "..", "..", "..", "..", "profiles"));

        var settings = new SettingsService();
        var port = GetPort(args) ?? settings.Current.Port;
        if (port <= 0)
        {
            port = 8787;
        }

        var pairing = new PairingService(port, settings.Current.Pin, settings.Current.Token);
        settings.Current.Port = pairing.Port;
        settings.Current.Pin = pairing.Pin;
        settings.Current.Token = pairing.Token;
        settings.Save();

        var profiles = new ProfileService();
        profiles.LoadFromDirectory(profilesDir);

        var state = new AppState();
        var focus = new FocusGuard(profiles);
        var input = new InputInjector();

        var shots = new ShotIngestService();
        shots.SetHistoryLimit(settings.Current.ShotHistoryLimit);
        var nova = new NovaWebSocketClient(shots, settings);
        var launch = new LaunchMonitorCoordinator(nova, settings, shots);

        var clipStore = new ClipStore();
        var cameras = new CameraService(settings, state);
        var clipPipeline = new ClipPipeline(cameras, clipStore, settings, state);

        var router = new CommandRouter(focus, profiles, input, state, shots, launch, settings);
        var hub = new WebSocketHub(state, router);

        shots.ShotReceived += shot =>
        {
            _ = hub.BroadcastAsync(new
            {
                type = "shot",
                shot = ShotDto.ToClient(
                    shot,
                    settings.Current.DisplaySpeedUnit,
                    settings.Current.DisplayDistanceUnit)
            });
            state.LogAction($"shot:{shot.Source}:{shot.SourceShotNumber?.ToString() ?? shot.Id[..8]}");
            clipPipeline.OnShotReceived(shot);
        };

        clipPipeline.ClipEvent += payload => { _ = hub.BroadcastAsync(payload); };

        launch.StatusChanged += () =>
        {
            _ = hub.BroadcastAsync(new
            {
                type = "launch_status",
                nova = new
                {
                    state = launch.NovaState,
                    detail = nova.Status.Detail,
                    firmware = nova.Status.Firmware,
                    shotCount = nova.Status.ShotCount
                },
                tee = new { state = launch.TeeState }
            });
        };

        if (settings.Current.AutostartEnabled && !AutostartService.IsEnabled())
        {
            AutostartService.SetEnabled(true);
        }
        else if (!settings.Current.AutostartEnabled && AutostartService.IsEnabled())
        {
            AutostartService.SetEnabled(false);
        }

        using var cts = new CancellationTokenSource();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = baseDir,
            WebRootPath = webRoot
        });

        builder.Logging.ClearProviders();
        builder.Logging.AddDebug();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Any, pairing.Port);
        });

        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton(pairing);
        builder.Services.AddSingleton(profiles);
        builder.Services.AddSingleton(state);
        builder.Services.AddSingleton(focus);
        builder.Services.AddSingleton(input);
        builder.Services.AddSingleton(shots);
        builder.Services.AddSingleton(nova);
        builder.Services.AddSingleton(launch);
        builder.Services.AddSingleton(clipStore);
        builder.Services.AddSingleton(cameras);
        builder.Services.AddSingleton(clipPipeline);
        builder.Services.AddSingleton(router);
        builder.Services.AddSingleton(hub);

        var app = builder.Build();
        app.UseWebSockets();
        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(webRoot)
        });

        app.MapGet("/api/health", () => Results.Json(new
        {
            status = "ok",
            product = "Bay Control",
            hint = "If you can see this from your phone, the network path is good."
        }));
        app.MapGet("/api/pairing", (PairingService p) => Results.Json(new
        {
            url = p.GetControllerUrl(),
            port = p.Port,
            lanIp = p.GetLanIp(),
            bookmarkHint = p.GetBookmarkHint()
        }));
        app.MapGet("/api/qr.png", (PairingService p) => Results.File(p.CreateQrPng(), "image/png"));
        app.MapGet("/api/status", (CommandRouter r) => Results.Json(r.BuildStatusPayload()));
        app.MapGet("/api/profiles", (ProfileService p) => Results.Json(p.Profiles.Values.Select(x => new
        {
            x.Id,
            x.DisplayName,
            actions = x.Actions
        })));

        app.MapPost("/api/auth", async (HttpContext ctx, PairingService p) =>
        {
            using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
            var pin = doc.RootElement.TryGetProperty("pin", out var pinEl) ? pinEl.GetString() : null;
            var token = doc.RootElement.TryGetProperty("token", out var tokEl) ? tokEl.GetString() : null;

            var ok = p.ValidatePin(pin) || p.ValidateToken(token);
            return ok
                ? Results.Json(new { ok = true, token = p.Token })
                : Results.Json(new { ok = false, error = "invalid_pin_or_token" }, statusCode: StatusCodes.Status401Unauthorized);
        });

        app.MapGet("/api/shots/latest", (HttpContext ctx, PairingService p, ShotIngestService s, SettingsService st) =>
        {
            if (!IsAuthorized(ctx, p))
            {
                return Results.Unauthorized();
            }

            var latest = s.Latest;
            return latest is null
                ? Results.NoContent()
                : Results.Json(ShotDto.ToClient(latest, st.Current.DisplaySpeedUnit, st.Current.DisplayDistanceUnit));
        });

        app.MapGet("/api/shots", (HttpContext ctx, PairingService p, ShotIngestService s, SettingsService st) =>
        {
            if (!IsAuthorized(ctx, p))
            {
                return Results.Unauthorized();
            }

            var limit = 30;
            if (int.TryParse(ctx.Request.Query["limit"], out var q) && q > 0)
            {
                limit = q;
            }

            var list = s.GetRecent(limit)
                .Select(x => ShotDto.ToClient(x, st.Current.DisplaySpeedUnit, st.Current.DisplayDistanceUnit));
            return Results.Json(list);
        });

        app.MapGet("/api/launch/status", (HttpContext ctx, PairingService p, LaunchMonitorCoordinator l) =>
        {
            if (!IsAuthorized(ctx, p))
            {
                return Results.Unauthorized();
            }

            return Results.Json(l.BuildLaunchPayload());
        });

        app.MapPost("/api/shots/simulate", (HttpContext ctx, PairingService p, ShotIngestService s) =>
        {
            if (!IsAuthorized(ctx, p))
            {
                return Results.Unauthorized();
            }

            var shot = s.Ingest(ShotNormalizer.Simulate());
            return Results.Json(new { ok = true, id = shot.Id });
        });

        app.MapGet("/api/cameras", (HttpContext ctx, PairingService p, CameraService cam) =>
        {
            if (!IsAuthorized(ctx, p))
            {
                return Results.Unauthorized();
            }

            return Results.Json(new
            {
                devices = cam.EnumerateDevices(),
                status = cam.StatusSummary(),
                faceOn = settings.Current.CameraFaceOnDeviceId,
                dtl = settings.Current.CameraDtlDeviceId,
                clubFace = settings.Current.CameraClubFaceDeviceId,
                enabled = settings.Current.ClipCaptureEnabled
            });
        });

        app.MapGet("/api/clips", (HttpContext ctx, PairingService p, ClipStore store) =>
        {
            if (!IsAuthorized(ctx, p))
            {
                return Results.Unauthorized();
            }

            var limit = 20;
            if (int.TryParse(ctx.Request.Query["limit"], out var q) && q > 0)
            {
                limit = q;
            }

            return Results.Json(store.List(limit).Select(c => new
            {
                id = c.Id,
                shotId = c.ShotId,
                cameraRole = c.CameraRole,
                cameraLabel = CameraRoles.DisplayName(c.CameraRole),
                createdAt = c.CreatedAt,
                durationMs = c.DurationMs,
                sizeBytes = c.SizeBytes,
                impactOffsetMs = c.ImpactOffsetMs
            }));
        });

        app.MapGet("/api/clips/{id}", (string id, HttpContext ctx, PairingService p, ClipStore store) =>
        {
            if (!IsAuthorized(ctx, p))
            {
                return Results.Unauthorized();
            }

            var c = store.Get(id);
            return c is null
                ? Results.NotFound()
                : Results.Json(new
                {
                    id = c.Id,
                    shotId = c.ShotId,
                    cameraRole = c.CameraRole,
                    cameraLabel = CameraRoles.DisplayName(c.CameraRole),
                    createdAt = c.CreatedAt,
                    durationMs = c.DurationMs,
                    sizeBytes = c.SizeBytes,
                    impactOffsetMs = c.ImpactOffsetMs
                });
        });

        app.MapGet("/api/clips/{id}/video", async (string id, HttpContext ctx, PairingService p, ClipStore store) =>
        {
            if (!IsAuthorized(ctx, p))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            var path = store.GetVideoPath(id);
            if (path is null)
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var fileInfo = new FileInfo(path);
            var length = fileInfo.Length;
            ctx.Response.Headers.AcceptRanges = "bytes";
            ctx.Response.ContentType = "video/mp4";

            var rangeHeader = ctx.Request.Headers.Range.ToString();
            if (!string.IsNullOrEmpty(rangeHeader) && rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            {
                var spec = rangeHeader["bytes=".Length..];
                var parts = spec.Split('-');
                if (long.TryParse(parts[0], out var start))
                {
                    var end = parts.Length > 1 && long.TryParse(parts[1], out var e) ? e : length - 1;
                    end = Math.Min(end, length - 1);
                    if (start <= end)
                    {
                        var count = end - start + 1;
                        ctx.Response.StatusCode = StatusCodes.Status206PartialContent;
                        ctx.Response.Headers.ContentRange = $"bytes {start}-{end}/{length}";
                        ctx.Response.ContentLength = count;
                        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                        fs.Seek(start, SeekOrigin.Begin);
                        var buffer = new byte[64 * 1024];
                        var remaining = count;
                        while (remaining > 0)
                        {
                            var read = await fs.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)));
                            if (read <= 0)
                            {
                                break;
                            }

                            await ctx.Response.Body.WriteAsync(buffer.AsMemory(0, read));
                            remaining -= read;
                        }

                        return;
                    }
                }
            }

            ctx.Response.ContentLength = length;
            await using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await fs.CopyToAsync(ctx.Response.Body);
            }
        });

        app.Map("/ws", async (HttpContext ctx, PairingService p, WebSocketHub h) =>
        {
            var token = ctx.Request.Query["t"].ToString();
            var pin = ctx.Request.Query["pin"].ToString();
            if (!p.ValidateToken(token) && !p.ValidatePin(pin))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsync("Unauthorized");
                return;
            }

            await h.HandleAsync(ctx);
        });

        app.MapFallbackToFile("index.html", new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(webRoot)
        });

        var webTask = app.RunAsync(cts.Token);
        launch.Start();
        cameras.ApplySettingsAndRestart();

        if (!settings.Current.FirstRunCompleted)
        {
            using var wizard = new FirstRunWizardForm(pairing, settings);
            wizard.ShowDialog();
        }

        var dashboard = new DashboardForm(
            pairing, state, settings, profiles, input, profilesDir,
            shots, launch, cameras, clipPipeline, clipStore,
            () =>
            {
                cts.Cancel();
                Application.Exit();
            });

        var broadcastTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        broadcastTimer.Tick += async (_, _) =>
        {
            try { await hub.BroadcastStatusAsync(); }
            catch { /* ignore */ }
        };
        broadcastTimer.Start();

        dashboard.ShowDashboard();
        Application.Run();

        broadcastTimer.Stop();
        launch.Stop();
        cameras.Dispose();
        cts.Cancel();
        try { webTask.GetAwaiter().GetResult(); }
        catch { /* shutting down */ }
    }

    private static bool IsAuthorized(HttpContext ctx, PairingService pairing)
    {
        var q = ctx.Request.Query["t"].ToString();
        if (pairing.ValidateToken(q))
        {
            return true;
        }

        var auth = ctx.Request.Headers.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return pairing.ValidateToken(auth["Bearer ".Length..].Trim());
        }

        var pin = ctx.Request.Query["pin"].ToString();
        return pairing.ValidatePin(pin);
    }

    private static string ResolvePath(string baseDir, string preferredRelative, string fallback)
    {
        var preferred = Path.GetFullPath(Path.Combine(baseDir, preferredRelative));
        if (Directory.Exists(preferred) && Directory.EnumerateFileSystemEntries(preferred).Any())
        {
            return preferred;
        }

        var alt = Path.GetFullPath(fallback);
        if (Directory.Exists(alt))
        {
            return alt;
        }

        Directory.CreateDirectory(preferred);
        return preferred;
    }

    private static int? GetPort(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] is "--port" or "-p" && int.TryParse(args[i + 1], out var port))
            {
                return port;
            }
        }

        return null;
    }
}
