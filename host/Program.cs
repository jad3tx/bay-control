using System.Net;
using System.Text.Json;
using BayControl.Host.Services;
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
        var router = new CommandRouter(focus, profiles, input, state);
        var hub = new WebSocketHub(state, router);

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

        if (!settings.Current.FirstRunCompleted)
        {
            using var wizard = new FirstRunWizardForm(pairing, settings);
            wizard.ShowDialog();
        }

        var dashboard = new DashboardForm(pairing, state, settings, profiles, input, profilesDir, () =>
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
        cts.Cancel();
        try { webTask.GetAwaiter().GetResult(); }
        catch { /* shutting down */ }
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
