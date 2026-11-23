using System.Text.Json;
using BayControl.Host.Models;

namespace BayControl.Host.Services;

public sealed class CommandRouter(
    FocusGuard focus,
    ProfileService profiles,
    InputInjector input,
    AppState state)
{
    private long _lastToggleTicks;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public CommandResult Handle(string json)
    {
        ClientMessage? msg;
        try
        {
            msg = JsonSerializer.Deserialize<ClientMessage>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            return CommandResult.Fail($"bad_json: {ex.Message}");
        }

        if (msg is null || string.IsNullOrWhiteSpace(msg.Type))
        {
            return CommandResult.Fail("missing_type");
        }

        if (state.RemotePaused)
        {
            return CommandResult.Fail("remote_paused");
        }

        return msg.Type.ToLowerInvariant() switch
        {
            "ping" => CommandResult.Ok("pong"),
            "action" => HandleAction(msg.Action),
            "mouse_move" => HandleMouseMove(msg.Dx ?? 0, msg.Dy ?? 0),
            "mouse_click" => HandleMouseClick(msg.Button),
            "mouse_scroll" => HandleMouseScroll(msg.Delta ?? 0),
            "type" => HandleType(msg.Text),
            "set_profile" => HandleSetProfile(msg.ProfileId),
            _ => CommandResult.Fail($"unknown_type:{msg.Type}")
        };
    }

    public object BuildStatusPayload()
    {
        var focusState = focus.GetState();
        var (healthCode, statusText, tip) = ResolveHealth(focusState);
        return new
        {
            type = "status",
            connectedClients = state.ConnectedClients,
            remotePaused = state.RemotePaused,
            simFocused = focusState.IsSimFocused,
            healthCode,
            statusText,
            tip,
            processName = focusState.ProcessName,
            windowTitle = focusState.WindowTitle,
            activeProfileId = focusState.Profile?.Id,
            activeProfileName = focusState.Profile?.DisplayName,
            manualOverrideId = profiles.ManualOverrideId,
            bookmarkHint = "Connected. Use Share → Add to Home Screen to keep this pairing on your home screen.",
            profiles = profiles.Profiles.Values.Select(p => new
            {
                p.Id,
                p.DisplayName,
                actions = p.Actions.ToDictionary(
                    kv => kv.Key,
                    kv => new { kv.Value.Label, kv.Value.Group, keys = kv.Value.Keys })
            })
        };
    }

    private (string healthCode, string statusText, string tip) ResolveHealth(FocusState focusState)
    {
        if (state.RemotePaused)
        {
            return ("remote_paused", "Remote paused from tray",
                "Uncheck Pause remote input on the PC dashboard.");
        }

        if (!focusState.IsSimFocused)
        {
            return ("sim_not_focused", "Paused — sim not focused",
                "Click the GSPro or Pureplay window so it is in front, then try again.");
        }

        return ("controlling", focusState.StatusText,
            "Reachable and armed. Taps go to the focused sim.");
    }

    private CommandResult HandleAction(string? actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return CommandResult.Fail("missing_action");
        }

        if (!focus.AllowInput(out var profile, out var reason) || profile is null)
        {
            state.LogAction($"blocked {actionId}: {reason}");
            return CommandResult.Fail(reason);
        }

        if (!profiles.TryGetAction(profile, actionId, out var mapping))
        {
            return CommandResult.Fail($"unknown_action:{actionId}");
        }

        if (IsToggleLike(actionId) && IsDebounced())
        {
            return CommandResult.Fail("debounced");
        }

        try
        {
            input.SendAction(mapping);
            state.LogAction($"{profile.Id}:{actionId}");
            return CommandResult.Ok(reason);
        }
        catch (Exception ex)
        {
            return CommandResult.Fail(ex.Message);
        }
    }

    private CommandResult HandleMouseMove(int dx, int dy)
    {
        if (!focus.AllowInput(out _, out var reason))
        {
            return CommandResult.Fail(reason);
        }

        input.MouseMove(dx, dy);
        return CommandResult.Ok("moved");
    }

    private CommandResult HandleMouseClick(string? button)
    {
        if (!focus.AllowInput(out _, out var reason))
        {
            return CommandResult.Fail(reason);
        }

        input.MouseClick(right: string.Equals(button, "right", StringComparison.OrdinalIgnoreCase));
        state.LogAction($"click:{button ?? "left"}");
        return CommandResult.Ok("clicked");
    }

    private CommandResult HandleMouseScroll(int delta)
    {
        if (!focus.AllowInput(out _, out var reason))
        {
            return CommandResult.Fail(reason);
        }

        input.MouseScroll(delta);
        return CommandResult.Ok("scrolled");
    }

    private CommandResult HandleType(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return CommandResult.Fail("missing_text");
        }

        if (!focus.AllowInput(out _, out var reason))
        {
            return CommandResult.Fail(reason);
        }

        input.TypeText(text);
        state.LogAction($"type:{text.Length} chars");
        return CommandResult.Ok("typed");
    }

    private CommandResult HandleSetProfile(string? profileId)
    {
        try
        {
            profiles.SetManualOverride(profileId);
            state.LogAction($"profile:{profileId ?? "auto"}");
            return CommandResult.Ok("profile_set");
        }
        catch (Exception ex)
        {
            return CommandResult.Fail(ex.Message);
        }
    }

    private bool IsDebounced()
    {
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastToggleTicks) < 120)
        {
            return true;
        }

        Interlocked.Exchange(ref _lastToggleTicks, now);
        return false;
    }

    private static bool IsToggleLike(string actionId) =>
        actionId.Contains("toggle", StringComparison.OrdinalIgnoreCase) ||
        actionId is "mulligan" or "heat_map" or "green_grid" or "scorecard" or "shot_cam" or "flyover";

    private sealed class ClientMessage
    {
        public string? Type { get; set; }
        public string? Action { get; set; }
        public int? Dx { get; set; }
        public int? Dy { get; set; }
        public string? Button { get; set; }
        public int? Delta { get; set; }
        public string? Text { get; set; }
        public string? ProfileId { get; set; }
    }
}

public sealed record CommandResult(bool Success, string Message)
{
    public static CommandResult Ok(string message) => new(true, message);
    public static CommandResult Fail(string message) => new(false, message);
}
