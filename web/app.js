const LAYOUT = {
  play: [
    "prev_club", "next_club", "putt_toggle"
  ],
  green: [
    "heat_map", "green_grid", "vertical_dots",
    "pin_indicator", "range_finder", "aim_point"
  ],
  cameras: [
    "shot_cam", "flyover", "free_look",
    "cam_go_to_ball", "cam_fly_to_ball", "goto_ball"
  ],
  round: [
    "scorecard", "prev_hole", "next_hole",
    "clear_tracers", "hud_toggle", "toggle_ui", "fullscreen"
  ],
  more: [
    "show_shortcuts", "map_zoom_in", "map_zoom_out",
    "line_of_sight", "toggle_grass", "lighting",
    "mute", "sound_on", "fast_forward", "fps",
    "console_short", "console_tall"
  ]
};

const ACCENT = new Set(["mulligan"]);
const DANGER = new Set([]);

const els = {
  authGate: document.getElementById("authGate"),
  controller: document.getElementById("controller"),
  pinForm: document.getElementById("pinForm"),
  pinInput: document.getElementById("pinInput"),
  authError: document.getElementById("authError"),
  statusChip: document.getElementById("statusChip"),
  profileSelect: document.getElementById("profileSelect"),
  toast: document.getElementById("toast"),
  helpBtn: document.getElementById("helpBtn"),
  helpDialog: document.getElementById("helpDialog"),
  closeHelp: document.getElementById("closeHelp"),
  trackpad: document.getElementById("trackpad"),
  typeInput: document.getElementById("typeInput"),
  sendText: document.getElementById("sendText")
};

const state = {
  token: localStorage.getItem("baycontrol_token") || "",
  pin: sessionStorage.getItem("baycontrol_pin") || "",
  ws: null,
  profiles: {},
  activeProfileId: null,
  reconnectTimer: null,
  lastPointer: null
};

function qsToken() {
  return new URLSearchParams(location.search).get("t") || "";
}

function showToast(msg) {
  els.toast.textContent = msg;
  els.toast.classList.remove("hidden");
  clearTimeout(showToast._t);
  showToast._t = setTimeout(() => els.toast.classList.add("hidden"), 1800);
}

function setStatus(text, kind = "ok") {
  els.statusChip.textContent = text;
  els.statusChip.classList.remove("warn", "err");
  if (kind !== "ok") els.statusChip.classList.add(kind);
}

async function authenticate() {
  const token = qsToken() || state.token;
  const pin = state.pin;
  const res = await fetch("/api/auth", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ token, pin })
  });
  const data = await res.json();
  if (!res.ok || !data.ok) {
    throw new Error("Invalid PIN or token");
  }
  state.token = data.token;
  localStorage.setItem("baycontrol_token", state.token);
  return state.token;
}

function connectWs() {
  const proto = location.protocol === "https:" ? "wss" : "ws";
  const q = state.token
    ? `t=${encodeURIComponent(state.token)}`
    : `pin=${encodeURIComponent(state.pin)}`;
  const ws = new WebSocket(`${proto}://${location.host}/ws?${q}`);
  state.ws = ws;

  ws.addEventListener("open", () => setStatus("Reachable — waiting for sim focus…", "warn"));
  ws.addEventListener("close", () => {
    setStatus("Offline — wrong network or PC asleep?", "err");
    const tipEl = document.getElementById("healthTip");
    if (tipEl) {
      tipEl.textContent = "Use the same Wi‑Fi as the sim PC (not Guest). Check firewall on the PC.";
      tipEl.classList.remove("hidden");
    }
    scheduleReconnect();
  });
  ws.addEventListener("error", () => setStatus("Connection error — check Wi‑Fi / firewall", "err"));
  ws.addEventListener("message", (ev) => {
    try {
      const msg = JSON.parse(ev.data);
      if (msg.type === "status") applyStatus(msg);
      if (msg.type === "result" && !msg.ok) showToast(msg.message || "Failed");
    } catch {
      // ignore
    }
  });
}

function scheduleReconnect() {
  clearTimeout(state.reconnectTimer);
  state.reconnectTimer = setTimeout(async () => {
    try {
      await authenticate();
      connectWs();
    } catch {
      scheduleReconnect();
    }
  }, 1500);
}

function send(payload) {
  if (!state.ws || state.ws.readyState !== WebSocket.OPEN) {
    showToast("Not connected");
    return;
  }
  state.ws.send(JSON.stringify(payload));
}

function healthKind(code) {
  if (code === "controlling") return "ok";
  if (code === "sim_not_focused" || code === "remote_paused") return "warn";
  return "err";
}

function pick(obj, ...keys) {
  if (!obj) return undefined;
  for (const key of keys) {
    if (obj[key] !== undefined && obj[key] !== null) return obj[key];
  }
  return undefined;
}

function applyStatus(msg) {
  const healthCode = pick(msg, "healthCode", "HealthCode");
  const statusText = pick(msg, "statusText", "StatusText") || "Ready";
  const tip = pick(msg, "tip", "Tip");
  const bookmarkHint = pick(msg, "bookmarkHint", "BookmarkHint");
  const remotePaused = pick(msg, "remotePaused", "RemotePaused");
  const simFocused = pick(msg, "simFocused", "SimFocused");
  const profiles = pick(msg, "profiles", "Profiles");
  const activeProfileId = pick(msg, "activeProfileId", "ActiveProfileId");
  const manualOverrideId = pick(msg, "manualOverrideId", "ManualOverrideId");

  const kind = healthKind(healthCode) || (remotePaused || !simFocused ? "warn" : "ok");
  setStatus(statusText, kind);
  if (tip) {
    els.statusChip.title = tip;
  }

  const tipEl = document.getElementById("healthTip");
  if (tipEl) {
    tipEl.textContent = tip || "";
    tipEl.classList.toggle("hidden", !tip);
  }

  const bookmarkEl = document.getElementById("bookmarkHint");
  if (bookmarkEl && bookmarkHint && !bookmarkEl.dataset.shown) {
    bookmarkEl.textContent = bookmarkHint;
    bookmarkEl.classList.remove("hidden");
    bookmarkEl.dataset.shown = "1";
  }

  if (Array.isArray(profiles)) {
    state.profiles = Object.fromEntries(profiles.map((p) => {
      const id = pick(p, "id", "Id");
      return [id, {
        id,
        displayName: pick(p, "displayName", "DisplayName"),
        actions: pick(p, "actions", "Actions") || {}
      }];
    }).filter(([id]) => !!id));
    const current = els.profileSelect.value;
    els.profileSelect.innerHTML = `<option value="auto">Auto-detect</option>` +
      profiles.map((p) => {
        const id = pick(p, "id", "Id");
        const name = pick(p, "displayName", "DisplayName");
        return `<option value="${id}">${name}</option>`;
      }).join("");
    els.profileSelect.value = manualOverrideId || current || "auto";
    const firstId = pick(profiles[0], "id", "Id");
    renderAllPads(activeProfileId || firstId);
  } else if (activeProfileId) {
    renderAllPads(activeProfileId);
  }
}

function actionMeta(profileId, actionId) {
  const profile = state.profiles[profileId];
  const actions = pick(profile, "actions", "Actions") || {};
  return actions[actionId] || null;
}

function renderAllPads(profileId) {
  state.activeProfileId = profileId;
  for (const [group, actions] of Object.entries(LAYOUT)) {
    const panel = document.getElementById(`panel-${group}`);
    if (!panel) continue;
    const pad = document.createElement("div");
    pad.className = "pad";

    for (const actionId of actions) {
      const meta = actionMeta(profileId, actionId);
      const btn = document.createElement("button");
      btn.type = "button";
      btn.dataset.action = actionId;
      btn.textContent = meta?.label || actionId.replaceAll("_", " ");
      if (!meta) {
        btn.disabled = true;
        btn.title = "Not available in this sim profile";
      }
      if (ACCENT.has(actionId)) btn.classList.add("accent");
      if (DANGER.has(actionId)) btn.classList.add("danger");
      if (actionId === "mulligan") btn.classList.add("wide");
      pad.appendChild(btn);
    }

    panel.innerHTML = "";
    panel.appendChild(pad);

    if (group === "play") {
      const aim = document.createElement("div");
      aim.className = "aim-block";
      aim.innerHTML = `
        <div class="aim-pad">
          <button type="button" class="up" data-action="aim_up">Aim ↑</button>
          <button type="button" class="left" data-action="aim_left">←</button>
          <button type="button" class="mid accent" data-action="reset_aim">Reset</button>
          <button type="button" class="right" data-action="aim_right">→</button>
          <button type="button" class="down" data-action="aim_down">Aim ↓</button>
        </div>
        <div class="pad">
          <button type="button" data-action="tee_left">Tee ←</button>
          <button type="button" data-action="tee_right">Tee →</button>
          <button type="button" class="accent" data-action="mulligan">Mulligan</button>
        </div>`;
      // Disable missing aim actions
      aim.querySelectorAll("[data-action]").forEach((btn) => {
        const id = btn.dataset.action;
        if (!actionMeta(profileId, id)) {
          btn.disabled = true;
          btn.title = "Not available in this sim profile";
        }
      });
      panel.appendChild(aim);
    }
  }
}

function bindUi() {
  document.querySelectorAll(".tab").forEach((tab) => {
    tab.addEventListener("click", () => {
      document.querySelectorAll(".tab").forEach((t) => t.classList.toggle("active", t === tab));
      document.querySelectorAll(".panel").forEach((p) => {
        p.classList.toggle("active", p.id === `panel-${tab.dataset.tab}`);
      });
    });
  });

  document.querySelector(".panels").addEventListener("click", (ev) => {
    const btn = ev.target.closest("[data-action]");
    if (!btn || btn.disabled) return;
    btn.classList.add("pressed");
    setTimeout(() => btn.classList.remove("pressed"), 120);
    send({ type: "action", action: btn.dataset.action });
  });

  document.querySelector(".track-actions").addEventListener("click", (ev) => {
    const btn = ev.target.closest("button");
    if (!btn) return;
    if (btn.dataset.mouse) send({ type: "mouse_click", button: btn.dataset.mouse });
    if (btn.dataset.scroll) send({ type: "mouse_scroll", delta: Number(btn.dataset.scroll) });
  });

  els.profileSelect.addEventListener("change", () => {
    send({ type: "set_profile", profileId: els.profileSelect.value });
  });

  els.helpBtn.addEventListener("click", () => els.helpDialog.showModal());
  els.closeHelp.addEventListener("click", () => els.helpDialog.close());

  els.sendText.addEventListener("click", () => {
    const text = els.typeInput.value;
    if (!text) return;
    send({ type: "type", text });
    els.typeInput.value = "";
  });

  bindTrackpad();
}

function bindTrackpad() {
  const el = els.trackpad;
  let last = null;
  let moved = false;

  const onDown = (x, y) => {
    last = { x, y };
    moved = false;
  };
  const onMove = (x, y) => {
    if (!last) return;
    const dx = Math.round(x - last.x);
    const dy = Math.round(y - last.y);
    if (Math.abs(dx) > 0 || Math.abs(dy) > 0) {
      moved = true;
      send({ type: "mouse_move", dx, dy });
      last = { x, y };
    }
  };
  const onUp = () => {
    if (last && !moved) send({ type: "mouse_click", button: "left" });
    last = null;
  };

  el.addEventListener("pointerdown", (e) => {
    el.setPointerCapture(e.pointerId);
    onDown(e.clientX, e.clientY);
  });
  el.addEventListener("pointermove", (e) => onMove(e.clientX, e.clientY));
  el.addEventListener("pointerup", onUp);
  el.addEventListener("pointercancel", () => { last = null; });
}

async function boot() {
  bindUi();

  // QR deep-link token tries first.
  if (qsToken()) {
    state.token = qsToken();
  }

  try {
    await authenticate();
    els.authGate.classList.add("hidden");
    els.controller.classList.remove("hidden");
    connectWs();
  } catch {
    els.authGate.classList.remove("hidden");
    els.controller.classList.add("hidden");
    setStatus("PIN required", "warn");
  }

  els.pinForm.addEventListener("submit", async (e) => {
    e.preventDefault();
    state.pin = els.pinInput.value.trim();
    sessionStorage.setItem("baycontrol_pin", state.pin);
    try {
      await authenticate();
      els.authError.textContent = "";
      els.authGate.classList.add("hidden");
      els.controller.classList.remove("hidden");
      connectWs();
    } catch (err) {
      els.authError.textContent = err.message || "Auth failed";
    }
  });
}

boot();
