import { describe, it, expect, vi } from "vitest";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";
import { JSDOM } from "jsdom";

// Load the REAL shipped bridge source so these tests exercise exactly what the library embeds.
const here = dirname(fileURLToPath(import.meta.url));
const bridgeSource = readFileSync(
  resolve(here, "../../src/Positron/Ipc/PositronBridge.js"),
  "utf8",
);

/**
 * Creates a fresh DOM window (so the frozen, non-configurable window.positron does not leak between
 * tests), installs the bridge with a mock native transport, and returns helpers.
 */
function installBridge() {
  const dom = new JSDOM("<!doctype html><html><body></body></html>", { runScripts: "outside-only" });
  const { window } = dom;
  const posted = [];
  window.__positronPostToNative = (json) => posted.push(JSON.parse(json));
  window.eval(bridgeSource);
  return {
    window,
    posted,
    receive: (obj) => window.__positronReceive(JSON.stringify(obj)),
    receiveRaw: (raw) => window.__positronReceive(raw),
  };
}

describe("window.positron.invoke", () => {
  it("posts an invoke frame and resolves on a matching result", async () => {
    const { window, posted, receive } = installBridge();

    const p = window.positron.invoke("greet", { name: "x" });

    expect(posted).toHaveLength(1);
    expect(posted[0]).toMatchObject({ kind: "invoke", channel: "greet", payload: { name: "x" } });
    const seq = posted[0].seq;
    expect(typeof seq).toBe("number");

    receive({ kind: "result", seq, ok: true, payload: { message: "hi" } });
    await expect(p).resolves.toEqual({ message: "hi" });
  });

  it("rejects when the result is not ok", async () => {
    const { window, posted, receive } = installBridge();
    const p = window.positron.invoke("boom", {});
    receive({ kind: "result", seq: posted[0].seq, ok: false, error: "nope" });
    await expect(p).rejects.toThrow("nope");
  });

  it("rejects an empty channel without posting", async () => {
    const { window, posted } = installBridge();
    await expect(window.positron.invoke("")).rejects.toThrow();
    expect(posted).toHaveLength(0);
  });

  it("assigns distinct sequence ids to concurrent invokes", () => {
    const { window, posted } = installBridge();
    window.positron.invoke("a", 1);
    window.positron.invoke("b", 2);
    expect(posted[0].seq).not.toBe(posted[1].seq);
  });

  it("ignores a result for an unknown seq", async () => {
    const { window, posted, receive } = installBridge();
    const p = window.positron.invoke("c", 1);
    receive({ kind: "result", seq: posted[0].seq + 999, ok: true, payload: 1 });
    // The real result still resolves the right promise.
    receive({ kind: "result", seq: posted[0].seq, ok: true, payload: 7 });
    await expect(p).resolves.toBe(7);
  });
});

describe("window.positron.on", () => {
  it("delivers events to multiple listeners", () => {
    const { window, receive } = installBridge();
    const a = vi.fn();
    const b = vi.fn();
    window.positron.on("tick", a);
    window.positron.on("tick", b);

    receive({ kind: "event", channel: "tick", payload: { t: 1 } });

    expect(a).toHaveBeenCalledWith({ t: 1 });
    expect(b).toHaveBeenCalledWith({ t: 1 });
  });

  it("unsubscribe stops further delivery", () => {
    const { window, receive } = installBridge();
    const cb = vi.fn();
    const off = window.positron.on("tick", cb);

    receive({ kind: "event", channel: "tick", payload: 1 });
    off();
    receive({ kind: "event", channel: "tick", payload: 2 });

    expect(cb).toHaveBeenCalledTimes(1);
  });

  it("a faulty listener does not block the others", () => {
    const { window, receive } = installBridge();
    const good = vi.fn();
    window.positron.on("tick", () => {
      throw new Error("boom");
    });
    window.positron.on("tick", good);

    receive({ kind: "event", channel: "tick", payload: 1 });

    expect(good).toHaveBeenCalledWith(1);
  });

  it("throws on an empty channel or non-function listener", () => {
    const { window } = installBridge();
    expect(() => window.positron.on("", () => {})).toThrow();
    expect(() => window.positron.on("tick", 123)).toThrow();
  });
});

describe("robustness", () => {
  it("is idempotent — a second install does not replace window.positron", () => {
    const { window, posted } = installBridge();
    const first = window.positron;
    window.eval(bridgeSource);
    expect(window.positron).toBe(first);
    expect(posted).toHaveLength(0);
  });

  it("__positronReceive swallows malformed JSON", () => {
    const { receiveRaw } = installBridge();
    expect(() => receiveRaw("{not json")).not.toThrow();
  });

  it("freezes the positron API surface", () => {
    const { window } = installBridge();
    expect(Object.isFrozen(window.positron)).toBe(true);
  });
});

describe("built-in dialog + fs namespaces", () => {
  it("dialog.open invokes the reserved channel and resolves the path", async () => {
    const { window, posted, receive } = installBridge();
    const p = window.positron.dialog.open({ allowedExtensions: ["txt"] });

    expect(posted[0]).toMatchObject({
      kind: "invoke",
      channel: "positron.dialog.open",
      payload: { allowedExtensions: ["txt"] },
    });
    receive({ kind: "result", seq: posted[0].seq, ok: true, payload: "/tmp/a.txt" });
    await expect(p).resolves.toBe("/tmp/a.txt");
  });

  it("dialog.save defaults options to an empty object", () => {
    const { window, posted } = installBridge();
    window.positron.dialog.save();
    expect(posted[0]).toMatchObject({ channel: "positron.dialog.save", payload: {} });
  });

  it("fs.readText invokes with the path", () => {
    const { window, posted } = installBridge();
    window.positron.fs.readText("/tmp/a.txt");
    expect(posted[0]).toMatchObject({ channel: "positron.fs.readText", payload: { path: "/tmp/a.txt" } });
  });

  it("fs.writeText invokes with path and contents", () => {
    const { window, posted } = installBridge();
    window.positron.fs.writeText("/tmp/a.txt", "hi");
    expect(posted[0]).toMatchObject({
      channel: "positron.fs.writeText",
      payload: { path: "/tmp/a.txt", contents: "hi" },
    });
  });

  it("the dialog and fs namespaces are frozen", () => {
    const { window } = installBridge();
    expect(Object.isFrozen(window.positron.dialog)).toBe(true);
    expect(Object.isFrozen(window.positron.fs)).toBe(true);
  });
});
