// Positron preload bridge — defines window.positron in the web content.
//
// Injected at document-start on every backend. It is backend-agnostic: the native host wires
// two seams before/around this script:
//   * window.__positronPostToNative(jsonString)  — set by the backend; sends JS → C#.
//   * window.__positronReceive(jsonString)        — defined here; the backend calls it for C# → JS.
//
// The public surface mirrors Electron's contextBridge model: a small, typed, validated API and
// never a raw native handle.
(function () {
  "use strict";

  if (window.positron) {
    return; // Idempotent: never double-install.
  }

  var seq = 0;
  var pending = new Map(); // seq -> { resolve, reject }
  var listeners = new Map(); // channel -> Set<fn>

  function postToNative(message) {
    var json = JSON.stringify(message);
    if (typeof window.__positronPostToNative !== "function") {
      throw new Error("Positron native bridge is not available.");
    }
    window.__positronPostToNative(json);
  }

  // Called by the native host with a raw JSON string (a Result or an Event envelope).
  window.__positronReceive = function (json) {
    var message;
    try {
      message = JSON.parse(json);
    } catch (e) {
      return;
    }

    if (message.kind === "result") {
      var entry = pending.get(message.seq);
      if (!entry) {
        return;
      }
      pending.delete(message.seq);
      if (message.ok) {
        entry.resolve(message.payload);
      } else {
        entry.reject(new Error(message.error || "Positron invoke failed."));
      }
      return;
    }

    if (message.kind === "event") {
      var set = listeners.get(message.channel);
      if (!set) {
        return;
      }
      set.forEach(function (fn) {
        try {
          fn(message.payload);
        } catch (e) {
          // A faulty listener must not break delivery to the others.
        }
      });
    }
  };

  var api = {
    /**
     * Invokes a C# channel handler and resolves with its result.
     * @param {string} channel
     * @param {*} [payload]
     * @returns {Promise<*>}
     */
    invoke: function (channel, payload) {
      if (typeof channel !== "string" || channel.length === 0) {
        return Promise.reject(new Error("channel must be a non-empty string."));
      }
      var id = ++seq;
      return new Promise(function (resolve, reject) {
        pending.set(id, { resolve: resolve, reject: reject });
        try {
          postToNative({ kind: "invoke", seq: id, channel: channel, payload: payload });
        } catch (e) {
          pending.delete(id);
          reject(e);
        }
      });
    },

    /**
     * Subscribes to events pushed from C# on a channel.
     * @param {string} channel
     * @param {(payload:*)=>void} fn
     * @returns {() => void} unsubscribe
     */
    on: function (channel, fn) {
      if (typeof channel !== "string" || channel.length === 0) {
        throw new Error("channel must be a non-empty string.");
      }
      if (typeof fn !== "function") {
        throw new Error("listener must be a function.");
      }
      var set = listeners.get(channel);
      if (!set) {
        set = new Set();
        listeners.set(channel, set);
      }
      set.add(fn);
      return function () {
        var s = listeners.get(channel);
        if (s) {
          s.delete(fn);
          if (s.size === 0) {
            listeners.delete(channel);
          }
        }
      };
    },
  };

  // Built-in convenience namespaces over reserved "positron.*" channels the host registers.
  // These are thin wrappers around invoke so apps get file dialogs and file I/O without wiring
  // their own handlers — see the file-system tutorial.
  api.dialog = {
    /** Shows a native open-file dialog; resolves to the chosen path or null. */
    open: function (options) {
      return api.invoke("positron.dialog.open", options || {});
    },
    /** Shows a native save-file dialog; resolves to the chosen path or null. */
    save: function (options) {
      return api.invoke("positron.dialog.save", options || {});
    },
  };

  api.fs = {
    /** Reads a UTF-8 text file; resolves to its contents. */
    readText: function (path) {
      return api.invoke("positron.fs.readText", { path: path });
    },
    /** Writes a UTF-8 text file. */
    writeText: function (path, contents) {
      return api.invoke("positron.fs.writeText", { path: path, contents: contents });
    },
  };

  Object.freeze(api.dialog);
  Object.freeze(api.fs);
  Object.freeze(api);
  Object.defineProperty(window, "positron", { value: api, writable: false, configurable: false });
})();
