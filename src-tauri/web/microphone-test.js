(() => {
  "use strict";
  if (window.top !== window || location.origin !== "https://web.oopz.cn" || !document.body) return;
  const panelId = "oopz-desktop-microphone-test";
  if (document.getElementById(panelId)) return;

  const host = document.createElement("div");
  host.id = panelId;
  const root = host.attachShadow({ mode: "open" });
  const dialog = document.createElement("dialog");
  const sheet = new CSSStyleSheet();
  sheet.replaceSync(`
    dialog { box-sizing:border-box; width:min(25rem, calc(100vw - 2rem)); margin:auto; padding:24px;
      border:1px solid #b6c1c6; border-radius:12px; color:#17282e; background:#fafcfb;
      font:15px/1.5 system-ui,sans-serif; box-shadow:0 12px 50px #061d2840; }
    dialog::backdrop { background:#06182280; }
    h2 { font-size:1.3rem; margin:0 0 6px; }
    p { margin:0 0 16px; }
    label { display:block; margin-top:12px; font-weight:600; }
    select, button { font:inherit; }
    select, meter { width:100%; }
    select, button { padding:6px 10px; border:1px solid #889a9d; border-radius:6px; }
    select { margin:4px 0 8px; }
    meter { height:1.1rem; }
    [role=status] { min-height:3em; margin:8px 0; }
    footer { display:flex; justify-content:flex-end; gap:8px; }
    button { cursor:pointer; background:white; color:#203033; }
    button:last-child { color:white; border-color:#16645d; background:#16645d; }
    :focus-visible { outline:3px solid #2274ac; outline-offset:2px; }
  `);
  root.adoptedStyleSheets = [sheet];
  root.append(dialog);
  function add(parent, tag, text) {
    const el = document.createElement(tag);
    if (text) el.textContent = text;
    parent.append(el);
    return el;
  }
  add(dialog, "h2", "麦克风自检").id = "heading";
  dialog.setAttribute("aria-labelledby", "heading");
  add(dialog, "p", "说几句话，观察输入电平。测试持续 10 秒，仅在本机分析声音。");
  const label = add(dialog, "label", "自检输入设备");
  const select = add(label, "select");
  select.add(new Option("系统默认麦克风", ""));
  const meterLabel = add(dialog, "label", "输入电平");
  const meter = add(meterLabel, "meter");
  meter.max = 1;
  const processing = add(dialog, "small");
  const status = add(dialog, "p", "点击开始测试，按系统提示允许使用麦克风。");
  status.setAttribute("role", "status");
  const footer = add(dialog, "footer");
  const close = add(footer, "button", "关闭");
  const action = add(footer, "button", "开始测试");

  const devices = navigator.mediaDevices;
  const listeners = new AbortController();
  let active;
  function stop(message) {
    const session = active;
    active = null;
    if (session) {
      clearTimeout(session.timeout);
      clearInterval(session.meterTimer);
      session.stream?.getTracks().forEach(track => track.stop());
      if (session.context) session.context.close().catch(() => {});
    }
    meter.value = 0;
    select.disabled = false;
    action.textContent = "开始测试";
    status.textContent = message;
  }

  async function listInputs() {
    if (!devices?.enumerateDevices) return;
    try {
      const inputs = (await devices.enumerateDevices()).filter(device =>
        device.kind === "audioinput" && device.deviceId && device.deviceId !== "default");
      const previous = select.value;
      select.length = 1;
      inputs.forEach((device, i) => select.add(new Option(device.label || "麦克风 " + (i + 1), device.deviceId)));
      if (inputs.some(device => device.deviceId === previous)) select.value = previous;
    } catch {
      // The default input can still be tested without device enumeration.
    }
  }

  async function start() {
    if (active) return stop("测试已停止。");
    const session = {};
    active = session;
    select.disabled = true;
    action.textContent = "停止测试";
    processing.textContent = "";
    status.textContent = "正在请求麦克风…";
    session.timeout = setTimeout(() => stop("等待麦克风超时，可以重新测试。"), 30000);
    try {
      const stream = await devices.getUserMedia({
        audio: select.value ? { deviceId: { exact: select.value } } : true,
        video: false,
      });
      if (active !== session) {
        stream.getTracks().forEach(track => track.stop());
        return;
      }
      session.stream = stream;
      const track = stream.getAudioTracks()[0];
      const settings = track.getSettings();
      const details = [];
      for (const [key, name] of [["noiseSuppression", "降噪"], ["echoCancellation", "回声消除"]]) {
        if (typeof settings[key] === "boolean") details.push(name + (settings[key] ? "已开启" : "已关闭"));
      }
      processing.textContent = details.length ? "本次测试：" + details.join("，") : "";
      track.addEventListener("ended", () => {
        if (active === session) stop("麦克风已断开，请重新选择设备。");
      }, { once: true });
      session.context = new AudioContext();
      const analyser = session.context.createAnalyser();
      analyser.fftSize = 1024;
      session.context.createMediaStreamSource(stream).connect(analyser);
      await session.context.resume();
      if (active !== session) return;

      void listInputs();
      const samples = new Float32Array(analyser.fftSize);
      let peak = 0;
      status.textContent = "正在测试，请对着麦克风说话…";
      session.meterTimer = setInterval(() => {
        analyser.getFloatTimeDomainData(samples);
        let sum = 0;
        for (const sample of samples) sum += sample * sample;
        const rms = Math.sqrt(sum / samples.length);
        peak = Math.max(peak, rms);
        const db = 20 * Math.log10(Math.max(rms, 0.001));
        meter.value = Math.min(1, Math.max(0, (db + 60) / 60));
      }, 100);
      clearTimeout(session.timeout);
      session.timeout = setTimeout(() => stop(peak > 0.003
        ? "检测到输入信号，测试已结束。"
        : "输入信号很弱，请检查静音开关、输入音量和所选设备。"), 10000);
    } catch (error) {
      if (active !== session) return;
      const problems = {
        NotAllowedError: "麦克风访问被拒绝，请检查网站授权与系统麦克风权限。",
        NotFoundError: "找不到麦克风，请连接设备后重试。",
        NotReadableError: "无法读取麦克风，请检查设备连接或是否被其他应用独占。",
        OverconstrainedError: "所选麦克风已不可用，请重新选择。",
      };
      stop(problems[error.name] || "无法启动测试，请检查输入设备后重试。");
      void listInputs();
    }
  }
  function dismiss() {
    listeners.abort();
    stop("");
    if (dialog.open) dialog.close();
    host.remove();
  }
  document.body.append(host);
  dialog.showModal();
  dialog.addEventListener("cancel", event => {
    event.preventDefault();
    dismiss();
  });
  dialog.addEventListener("close", dismiss, { once: true });
  close.onclick = dismiss;
  action.onclick = start;
  window.addEventListener("pagehide", dismiss, { signal: listeners.signal });
  document.addEventListener("visibilitychange", () => {
    if (document.hidden) dismiss();
  }, { signal: listeners.signal });
  if (devices?.getUserMedia && window.AudioContext) void listInputs();
  else {
    action.disabled = true;
    status.textContent = "当前 WebView 无法访问麦克风或分析输入电平。";
  }
})();
