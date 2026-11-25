// Course Control–style: Play primary, More secondary, Trackpad separate.
const LAYOUT = {
  play: ["prev_club", "next_club", "putt_toggle"],
  reads: [
    "heat_map", "green_grid", "pin_indicator", "aim_point",
    "shot_cam", "flyover", "scorecard"
  ],
  practice: [
    "goto_ball", "prev_hole", "next_hole",
    "cam_go_to_ball", "cam_fly_to_ball", "range_finder"
  ],
  camera: [
    "free_look", "vertical_dots", "clear_tracers",
    "hud_toggle", "toggle_ui", "fullscreen"
  ],
  system: [
    "show_shortcuts", "map_zoom_in", "map_zoom_out",
    "line_of_sight", "toggle_grass", "lighting",
    "mute", "sound_on", "fast_forward", "fps",
    "console_short", "console_tall"
  ]
};

const ACCENT = new Set(["mulligan"]);
const DANGER = new Set([]);
const HOLD_CONFIRM_MS = 700;
const HOLD_REPEAT_DELAY_MS = 320;
const HOLD_REPEAT_INTERVAL_MS = 110;

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
  sendText: document.getElementById("sendText"),
  surfaceNav: document.getElementById("surfaceNav"),
  board: document.getElementById("board")
};

const state = {
  token: localStorage.getItem("baycontrol_token") || "",
  pin: sessionStorage.getItem("baycontrol_pin") || "",
  ws: null,
  profiles: {},
  activeProfileId: null,
  reconnectTimer: null,
  lastPointer: null,
  surface: "play",
  clips: [],
  clipRoleFilter: "all",
  selectedClipId: null,
  userPinnedClip: false
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
      if (msg.type === "shot") renderLastShot(msg.shot);
      if (msg.type === "launch_status") applyLaunchStatus(msg);
      if (msg.type === "clip.ready") onClipReady(msg);
      if (msg.type === "clip.failed") showToast(msg.error === "no_camera" ? "No swing camera armed" : (msg.error || "Clip failed"));
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

  const lastShot = pick(msg, "lastShot", "LastShot");
  if (lastShot) renderLastShot(lastShot);

  const launch = pick(msg, "launch", "Launch");
  if (launch) applyLaunchStatus({ nova: { state: pick(launch, "nova", "Nova") }, tee: { state: pick(launch, "tee", "Tee") } });

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

function makeActionButton(profileId, actionId) {
  const meta = actionMeta(profileId, actionId);
  const btn = document.createElement("button");
  btn.type = "button";
  btn.dataset.action = actionId;
  btn.textContent = (meta && (meta.label || meta.Label)) || actionId.replaceAll("_", " ");
  if (!meta) {
    btn.disabled = true;
    btn.title = "Not available in this sim profile";
  }
  if (ACCENT.has(actionId)) btn.classList.add("accent");
  if (DANGER.has(actionId)) btn.classList.add("danger");
  return btn;
}

function renderAllPads(profileId) {
  state.activeProfileId = profileId;

  for (const [group, actions] of Object.entries(LAYOUT)) {
    const host = document.getElementById(`zone-${group}-buttons`);
    if (!host) continue;
    host.innerHTML = "";
    for (const actionId of actions) {
      host.appendChild(makeActionButton(profileId, actionId));
    }
  }

  // Aim / tee / mulligan live in static markup — refresh enabled state + labels.
  document.querySelectorAll(".aim-block [data-action]").forEach((btn) => {
    const id = btn.dataset.action;
    const meta = actionMeta(profileId, id);
    btn.disabled = !meta;
    btn.title = meta ? "" : "Not available in this sim profile";
    if (meta && (meta.label || meta.Label) && !["aim_left", "aim_right", "aim_up", "aim_down"].includes(id)) {
      if (id === "reset_aim") btn.textContent = "Reset";
      else if (id === "mulligan") btn.textContent = meta.label || meta.Label || "Mulligan";
      else if (id === "tee_left") btn.textContent = "Tee ←";
      else if (id === "tee_right") btn.textContent = "Tee →";
    }
  });
}

function fmt(n, digits = 1) {
  if (n === undefined || n === null || Number.isNaN(Number(n))) return "—";
  return Number(n).toFixed(digits);
}

function renderLastShot(shot) {
  if (!shot) return;
  const empty = document.getElementById("shotEmpty");
  const metrics = document.getElementById("shotMetrics");
  const chip = document.getElementById("shotSource");
  if (!empty || !metrics || !chip) return;

  empty.classList.add("hidden");
  metrics.classList.remove("hidden");

  const ball = pick(shot, "ball", "Ball") || {};
  const speed = pick(ball, "speed", "Speed");
  const speedUnit = pick(ball, "speedUnit", "SpeedUnit") || "mph";
  const vla = pick(ball, "vlaDeg", "VlaDeg");
  const hla = pick(ball, "hlaDeg", "HlaDeg");
  const spin = pick(ball, "totalSpinRpm", "TotalSpinRpm");
  const axis = pick(ball, "spinAxisDeg", "SpinAxisDeg");
  const carry = pick(shot, "carry", "Carry");
  const carryUnit = pick(shot, "carryUnit", "CarryUnit") || "";
  const source = pick(shot, "source", "Source") || "—";

  document.getElementById("mSpeed").textContent = `${fmt(speed)} ${speedUnit}`;
  document.getElementById("mVla").textContent = `${fmt(vla)}°`;
  document.getElementById("mHla").textContent = `${fmt(hla)}°`;
  document.getElementById("mSpin").textContent = fmt(spin, 0);
  document.getElementById("mAxis").textContent = `${fmt(axis)}°`;
  document.getElementById("mCarry").textContent = carry == null ? "—" : `${fmt(carry, 0)} ${carryUnit}`.trim();
  chip.textContent = source;
  chip.dataset.source = source;
}

function applyLaunchStatus(msg) {
  const nova = pick(msg, "nova", "Nova");
  const state = typeof nova === "string" ? nova : pick(nova, "state", "State");
  const empty = document.getElementById("shotEmpty");
  const metrics = document.getElementById("shotMetrics");
  if (!empty || (metrics && !metrics.classList.contains("hidden"))) return;

  if (state === "connected" || state === "connecting") {
    empty.textContent = "Waiting for first shot…";
  } else if (state === "searching") {
    empty.textContent = "Nova not found — check Wi‑Fi / power";
  } else if (state === "disabled") {
    empty.textContent = "Nova disabled on the sim PC";
  } else if (state === "error" || state === "disconnected") {
    empty.textContent = "Nova disconnected — keys still work";
  }
}

function setSurface(name) {
  state.surface = name;
  els.surfaceNav.querySelectorAll(".surface-tab").forEach((tab) => {
    const on = tab.dataset.surface === name;
    tab.classList.toggle("active", on);
    tab.setAttribute("aria-pressed", on ? "true" : "false");
  });
  els.board.querySelectorAll(".surface").forEach((panel) => {
    const on = panel.id === `surface-${name}`;
    panel.classList.toggle("active", on);
    panel.hidden = !on;
  });
  if (name === "swings") refreshClips();
}

function authQuery() {
  return state.token
    ? `t=${encodeURIComponent(state.token)}`
    : `pin=${encodeURIComponent(state.pin)}`;
}

async function refreshClips() {
  try {
    const res = await fetch(`/api/clips?limit=40&${authQuery()}`);
    if (!res.ok) return;
    state.clips = await res.json();
    renderSwingList();
    if (!state.selectedClipId && state.clips.length) {
      selectClip(state.clips[0].id, false);
    }
  } catch {
    // ignore
  }
}

function onClipReady(msg) {
  const clipId = pick(msg, "clipId", "ClipId");
  if (!clipId) return;
  refreshClips().then(() => {
    if (!state.userPinnedClip) selectClip(clipId, false);
  });
  showToast("Swing clip ready");
}

function renderSwingList() {
  const host = document.getElementById("swingList");
  if (!host) return;
  const filter = state.clipRoleFilter;
  const rows = state.clips.filter((c) => filter === "all" || c.cameraRole === filter);
  host.innerHTML = "";
  for (const c of rows) {
    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = "swing-row" + (c.id === state.selectedClipId ? " active" : "");
    const when = c.createdAt ? new Date(c.createdAt).toLocaleTimeString() : "";
    btn.innerHTML = `<span>${c.cameraLabel || c.cameraRole}<br><small>${when}</small></span><small>${Math.round((c.durationMs || 0) / 100) / 10}s</small>`;
    btn.addEventListener("click", () => selectClip(c.id, true));
    host.appendChild(btn);
  }
  if (!rows.length) {
    host.innerHTML = `<p class="zone-sub">No clips yet — hit a shot with cameras armed on the sim PC.</p>`;
  }
}

function selectClip(id, pinned) {
  state.selectedClipId = id;
  state.userPinnedClip = !!pinned;
  const clip = state.clips.find((c) => c.id === id);
  const player = document.getElementById("swingPlayer");
  const meta = document.getElementById("swingMeta");
  if (player) {
    player.src = `/api/clips/${encodeURIComponent(id)}/video?${authQuery()}`;
    player.play?.().catch(() => {});
  }
  if (meta && clip) {
    meta.textContent = `${clip.cameraLabel || clip.cameraRole} · impact @ ${Math.round((clip.impactOffsetMs || 3000) / 100) / 10}s`;
  }
  renderSwingList();
}

function fireAction(btn) {
  if (!btn || btn.disabled) return;
  btn.classList.add("pressed");
  setTimeout(() => btn.classList.remove("pressed"), 120);
  btn.blur();
  send({ type: "action", action: btn.dataset.action });
}

function bindHoldRepeat(btn) {
  let delayTimer = null;
  let intervalTimer = null;
  let armed = false;

  const clear = () => {
    armed = false;
    clearTimeout(delayTimer);
    clearInterval(intervalTimer);
    delayTimer = null;
    intervalTimer = null;
  };

  const start = (e) => {
    if (btn.disabled) return;
    e.preventDefault();
    armed = true;
    fireAction(btn);
    delayTimer = setTimeout(() => {
      if (!armed) return;
      intervalTimer = setInterval(() => {
        if (!armed) return;
        fireAction(btn);
      }, HOLD_REPEAT_INTERVAL_MS);
    }, HOLD_REPEAT_DELAY_MS);
  };

  btn.addEventListener("pointerdown", start);
  btn.addEventListener("pointerup", clear);
  btn.addEventListener("pointercancel", clear);
  btn.addEventListener("pointerleave", clear);
  btn.addEventListener("lostpointercapture", clear);
}

function bindHoldConfirm(btn) {
  let raf = null;
  let started = false;

  const clear = () => {
    if (raf !== null) cancelAnimationFrame(raf);
    raf = null;
    started = false;
    btn.classList.remove("holding");
    btn.style.removeProperty("--hold");
  };

  btn.addEventListener("pointerdown", (e) => {
    if (btn.disabled) return;
    e.preventDefault();
    clear();
    started = true;
    btn.classList.add("holding");
    const t0 = performance.now();
    const tick = (now) => {
      if (!started) return;
      const p = Math.min(1, (now - t0) / HOLD_CONFIRM_MS);
      btn.style.setProperty("--hold", `${p * 100}%`);
      if (p < 1) {
        raf = requestAnimationFrame(tick);
      } else {
        clear();
        fireAction(btn);
      }
    };
    raf = requestAnimationFrame(tick);
  });

  btn.addEventListener("pointerup", clear);
  btn.addEventListener("pointercancel", clear);
  btn.addEventListener("pointerleave", clear);
}

function bindUi() {
  els.surfaceNav.addEventListener("click", (ev) => {
    const tab = ev.target.closest(".surface-tab");
    if (!tab) return;
    setSurface(tab.dataset.surface);
  });

  const roleTabs = document.getElementById("swingRoleTabs");
  if (roleTabs) {
    roleTabs.addEventListener("click", (ev) => {
      const tab = ev.target.closest(".role-tab");
      if (!tab) return;
      state.clipRoleFilter = tab.dataset.role;
      roleTabs.querySelectorAll(".role-tab").forEach((t) => t.classList.toggle("active", t === tab));
      renderSwingList();
    });
  }

  // Regular taps (exclude hold-repeat / hold-confirm — those manage their own fire).
  els.board.addEventListener("click", (ev) => {
    const btn = ev.target.closest("[data-action]");
    if (!btn || btn.disabled) return;
    if (btn.classList.contains("hold-repeat") || btn.classList.contains("hold-confirm")) {
      ev.preventDefault();
      return;
    }
    ev.preventDefault();
    fireAction(btn);
  });

  document.querySelectorAll(".hold-repeat").forEach(bindHoldRepeat);
  document.querySelectorAll(".hold-confirm").forEach(bindHoldConfirm);

  document.addEventListener("keydown", (ev) => {
    if ((ev.key === " " || ev.key === "Enter") && ev.target instanceof HTMLButtonElement && ev.target.dataset.action) {
      ev.preventDefault();
    }
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
  setSurface("play");
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
