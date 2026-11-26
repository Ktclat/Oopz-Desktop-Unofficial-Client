import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { runInNewContext } from 'node:vm';

const script = readFileSync(new URL('../src-tauri/web/voice.js', import.meta.url), 'utf8');

function load({ origin = 'https://web.oopz.cn', framed = false, supported = {}, storage = new Map(), result = Promise.resolve() } = {}) {
  const window = {};
  window.top = framed ? {} : window;
  const calls = [];
  const devices = {
    getSupportedConstraints: () => supported,
    getUserMedia(...args) {
      calls.push({ receiver: this, args });
      return result;
    },
  };
  const original = devices.getUserMedia;
  runInNewContext(script, {
    window, location: { origin }, navigator: { mediaDevices: devices }, document: { body: null },
    localStorage: {
      getItem: key => storage.get(key),
      setItem: (key, value) => storage.set(key, value),
      removeItem: key => storage.delete(key),
    },
  });
  return { calls, devices, original, result, window };
}

test('installs only on the official top-level origin', () => {
  for (const settings of [{ framed: true }, { origin: 'https://example.com' }, { origin: 'http://web.oopz.cn' }]) {
    const { devices, original, window } = load(settings);
    assert.equal(devices.getUserMedia, original);
    assert.equal(window.__oopzDesktopVoice, undefined);
  }
});

test('passes through by default and retains cached references across toggles', () => {
  const { devices, result, window, calls } = load({ supported: { noiseSuppression: true } });
  const capture = devices.getUserMedia.bind(devices);
  const request = { audio: true, video: false };
  assert.equal(capture(request), result);
  assert.equal(calls[0].args[0], request);
  window.__oopzDesktopVoice.toggle();
  assert.equal(capture(request), result);
  assert.equal(calls[1].args[0].audio.noiseSuppression.ideal, true);
  assert.equal(calls[1].args[0].audio.echoCancellation, undefined);
  assert.equal(calls[1].receiver, devices);
  window.__oopzDesktopVoice.toggle();
  capture(request);
  assert.equal(calls[2].args[0], request);
});

test('keeps device, video, and explicit site preferences, including inherited values', () => {
  const storage = new Map();
  const settings = { supported: { echoCancellation: true, noiseSuppression: true }, storage };
  load(settings).window.__oopzDesktopVoice.toggle();
  const { calls, devices } = load(settings);
  const audio = Object.freeze(Object.assign(Object.create({ echoCancellation: false }), { deviceId: { exact: 'chosen' } }));
  const request = Object.freeze({ audio, video: { width: 720 } });
  devices.getUserMedia(request);
  const [actual] = calls[0].args;
  assert.equal(actual.video, request.video);
  assert.equal(actual.audio.deviceId, audio.deviceId);
  assert.equal(actual.audio.echoCancellation, false);
  assert.equal(actual.audio.noiseSuppression.ideal, true);
  assert.equal(audio.noiseSuppression, undefined);
});

test('leaves advanced constraints, legacy constraints, video capture and call receivers to the browser', () => {
  const { calls, window, devices } = load({ supported: { echoCancellation: true } });
  window.__oopzDesktopVoice.toggle();
  for (const audio of [false, { advanced: [{ echoCancellation: false }] }, { mandatory: {} }, { optional: [] }]) {
    const request = { audio, video: true };
    devices.getUserMedia(request);
    assert.equal(calls.at(-1).args[0], request);
  }
  const request = { audio: true };
  const other = {};
  devices.getUserMedia.call(other, request);
  assert.equal(calls.at(-1).receiver, other);
  assert.equal(calls.at(-1).args[0], request);
});

test('propagates microphone permission denial without retrying', async () => {
  const denial = Object.assign(new Error('Blocked by the user'), { name: 'NotAllowedError' });
  const result = Promise.reject(denial);
  const { calls, devices, window } = load({ supported: { noiseSuppression: true }, result });
  window.__oopzDesktopVoice.toggle();
  const capture = devices.getUserMedia({ audio: true });
  assert.equal(capture, result);
  await assert.rejects(capture, error => error === denial);
  assert.equal(calls.length, 1);
});
