(() => {
  'use strict';

  if (window.top !== window || location.origin !== 'https://web.oopz.cn' || window.__oopzDesktopVoice) return;

  const key = 'oopz-desktop.voice-cleanup.v1';
  let enabled = false;
  try {
    enabled = localStorage.getItem(key) === 'on';
  } catch {
    // The setting still works for this document when storage is unavailable.
  }

  const devices = navigator.mediaDevices;
  function withCleanup(constraints, supported) {
    if (constraints === null || typeof constraints !== 'object') return constraints;
    const { audio } = constraints;
    if (audio !== true && (audio === null || typeof audio !== 'object')) return constraints;
    // Leave ordered constraint sets and legacy Chromium audio sources entirely to the site.
    if (audio !== true && ('advanced' in audio || 'mandatory' in audio || 'optional' in audio)) return constraints;

    const preferences = {};
    for (const name of ['echoCancellation', 'noiseSuppression']) {
      if (supported[name] && (audio === true || !(name in audio))) {
        preferences[name] = { value: { ideal: true }, enumerable: true };
      }
    }
    if (Object.keys(preferences).length === 0) return constraints;

    // Web IDL dictionaries read inherited members too. Keep the original device, video,
    // and non-enumerable constraints without modifying the dictionaries owned by the page.
    const input = Object.create(audio === true ? null : audio, preferences);
    return Object.create(constraints, { audio: { value: input, enumerable: true } });
  }

  let installed = false;
  if (typeof devices?.getUserMedia === 'function' && typeof devices.getSupportedConstraints === 'function') {
    try {
      const supported = devices.getSupportedConstraints();
      // Install at document start so references cached by the site follow later menu choices.
      const capture = new Proxy(devices.getUserMedia, {
        apply(original, receiver, args) {
          if (enabled && receiver === devices && args.length) {
            try {
              args[0] = withCleanup(args[0], supported);
            } catch {
              // Let the original API handle unusual input and report its own errors.
            }
          }
          return Reflect.apply(original, receiver, args);
        },
      });
      Object.defineProperty(devices, 'getUserMedia', { value: capture, configurable: true, writable: true });
      installed = Boolean(supported.noiseSuppression || supported.echoCancellation);
    } catch {
      // Some WebViews protect the capture method.
    }
  }

  let notice;
  let dismiss;
  function announce(message) {
    if (!document.body) return;
    clearTimeout(dismiss);
    if (!notice?.isConnected) {
      notice = document.createElement('div');
      notice.setAttribute('role', 'status');
      notice.style.cssText = 'position:fixed;bottom:1.5rem;left:50%;transform:translateX(-50%);z-index:2147483647;padding:.7rem 1rem;border-radius:.5rem;background:#182127;color:#fff;font:14px/1.5 system-ui,sans-serif;pointer-events:none';
      document.body.append(notice);
    }
    notice.textContent = message;
    dismiss = setTimeout(() => notice.remove(), 4200);
  }

  window.__oopzDesktopVoice = Object.freeze({
    toggle() {
      if (!installed) {
        announce('当前 WebView 无法设置麦克风降噪。');
        return;
      }
      enabled = !enabled;
      try {
        if (enabled) localStorage.setItem(key, 'on');
        else localStorage.removeItem(key);
      } catch {
        // Retain the in-memory choice until the document is closed.
      }
      announce(enabled ? '已开启语音降噪偏好，下次打开麦克风时生效。' : '已恢复官网语音设置，下次打开麦克风时生效。');
    },
  });
})();
