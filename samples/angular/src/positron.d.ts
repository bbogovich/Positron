// Ambient typing for the window.positron bridge injected by the Positron host.
// Only the members this sample uses are declared.

export {};

declare global {
  interface Positron {
    invoke<TResult = unknown>(channel: string, payload?: unknown): Promise<TResult>;
    on(channel: string, cb: (payload: unknown) => void): () => void;
  }

  interface Window {
    positron: Positron;
  }
}
