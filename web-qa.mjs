// GPU-free smoke test of the WebGL build: headless Chromium with SwiftShader (CPU WebGL),
// driven over the DevTools protocol with Node's built-in WebSocket. No npm packages needed.
// Usage: node web-qa.mjs <http-url> <screenshot-dir>
import { spawn } from "node:child_process";
import { mkdtempSync, writeFileSync, mkdirSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

const [url, outDir] = process.argv.slice(2);
if (!url || !outDir) { console.error("usage: node web-qa.mjs <url> <dir>"); process.exit(1); }
mkdirSync(outDir, { recursive: true });
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const port = 9300 + Math.floor(Math.random() * 500);
const profile = mkdtempSync(join(tmpdir(), "orbital-chrome-"));

const env = { ...process.env };
delete env.WAYLAND_DISPLAY; delete env.DISPLAY;
// keep every graphics library away from the NVIDIA driver
env.__EGL_VENDOR_LIBRARY_FILENAMES = "/usr/share/glvnd/egl_vendor.d/50_mesa.json";
env.__GLX_VENDOR_LIBRARY_NAME = "mesa";
if (process.env.LVP_ICD) env.VK_DRIVER_FILES = env.VK_ICD_FILENAMES = process.env.LVP_ICD;

const chrome = spawn(process.env.CHROME || "chromium", [
  "--headless=new", `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`,
  "--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--ignore-gpu-blocklist",
  "--disable-vulkan", "--autoplay-policy=no-user-gesture-required",
  "--window-size=1280,720", "--no-first-run", "--no-default-browser-check", "about:blank",
], { env, stdio: ["ignore", "ignore", "pipe"] });
let chromeErr = "";
chrome.stderr.on("data", (d) => { chromeErr += d; });

let ws, nextId = 1;
const pending = new Map();
const logs = [];
function send(method, params = {}, sessionId) {
  const id = nextId++;
  ws.send(JSON.stringify({ id, method, params, sessionId }));
  return new Promise((res, rej) => pending.set(id, { res, rej }));
}

async function main() {
  let ver;
  for (let i = 0; i < 50 && !ver; i++) {
    try { ver = await (await fetch(`http://127.0.0.1:${port}/json/version`)).json(); } catch { await sleep(200); }
  }
  if (!ver) throw new Error("chromium did not start: " + chromeErr.slice(0, 400));
  ws = new WebSocket(ver.webSocketDebuggerUrl);
  await new Promise((r) => ws.addEventListener("open", r));
  ws.addEventListener("message", (ev) => {
    const m = JSON.parse(ev.data);
    if (m.id && pending.has(m.id)) {
      const p = pending.get(m.id); pending.delete(m.id);
      m.error ? p.rej(new Error(m.error.message)) : p.res(m.result);
    } else if (m.method === "Runtime.consoleAPICalled") {
      logs.push(`[${m.params.type}] ` + m.params.args.map((a) => a.value ?? a.description ?? "").join(" "));
    } else if (m.method === "Runtime.exceptionThrown") {
      logs.push("[exception] " + (m.params.exceptionDetails.exception?.description || m.params.exceptionDetails.text));
    }
  });
  const { targetId } = await send("Target.createTarget", { url: "about:blank" });
  const { sessionId } = await send("Target.attachToTarget", { targetId, flatten: true });
  const s = (m, p) => send(m, p, sessionId);
  await s("Runtime.enable"); await s("Page.enable");
  await s("Emulation.setDeviceMetricsOverride", { width: 1280, height: 720, deviceScaleFactor: 1, mobile: false });
  const evalJs = async (expr) => (await s("Runtime.evaluate", { expression: expr, returnByValue: true, awaitPromise: true })).result.value;
  const shot = async (name) => {
    const { data } = await s("Page.captureScreenshot", { format: "png" });
    writeFileSync(join(outDir, name + ".png"), Buffer.from(data, "base64"));
    console.log("shot", name);
  };
  const key = async (type, code, keyName, vk, text) =>
    s("Input.dispatchKeyEvent", { type, code, key: keyName, windowsVirtualKeyCode: vk, nativeVirtualKeyCode: vk, text });

  const t0 = Date.now();
  await s("Page.navigate", { url });
  await sleep(1500);
  await shot("00_loading");
  let ready = false;
  for (let i = 0; i < 240 && !ready; i++) { ready = await evalJs("!!window.unityInstance"); if (!ready) await sleep(1000); }
  console.log("unity ready:", ready, "after", Math.round((Date.now() - t0) / 1000), "s");
  if (!ready) { await shot("99_stuck"); throw new Error("Unity never started"); }
  await sleep(6000);
  await shot("01_title");
  const { x, y } = { x: 640, y: 360 };
  await s("Input.dispatchMouseEvent", { type: "mousePressed", x, y, button: "left", clickCount: 1 });
  await s("Input.dispatchMouseEvent", { type: "mouseReleased", x, y, button: "left", clickCount: 1 });
  await key("keyDown", "Enter", "Enter", 13, "\r"); await sleep(80); await key("keyUp", "Enter", "Enter", 13);
  await sleep(5000);
  await shot("02_wave1");
  for (let i = 0; i < 30; i++) { await key("keyDown", "Space", " ", 32, " "); await sleep(100); }
  await shot("03_firing");
  await key("keyUp", "Space", " ", 32);
  await key("keyDown", "ShiftLeft", "Shift", 16); await sleep(80); await key("keyUp", "ShiftLeft", "Shift", 16);
  await sleep(400);
  await shot("04_panic");
  const fps = await evalJs(`new Promise(r => { let n = 0; const t = performance.now(); (function f() { n++; if (performance.now() - t < 3000) requestAnimationFrame(f); else r(Math.round(n / 3)); })(); })`).catch(() => null);
  console.log("page rAF fps (software rendering):", fps);
}

main()
  .then(() => { console.log("RESULT ok"); })
  .catch((e) => { console.log("RESULT fail:", e.message); process.exitCode = 1; })
  .finally(() => {
    const bad = logs.filter((l) => /exception|error/i.test(l));
    console.log(`console lines: ${logs.length}, errors: ${bad.length}`);
    bad.slice(0, 15).forEach((l) => console.log("  ", l.slice(0, 300)));
    writeFileSync(join(outDir, "console.log"), logs.join("\n"));
    chrome.kill("SIGTERM");
  });
