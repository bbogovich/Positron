// A tiny wrapper around the Positron IPC bridge. Keeping IPC calls in one module keeps
// components unaware of the global window.positron object and gives them typed results.

export interface GreetResponse {
  message: string
}

export const positronService = {
  /** Invokes the C# "greet" handler and returns its reply. */
  greet(name: string): Promise<GreetResponse> {
    return window.positron.invoke<GreetResponse>('greet', { name })
  },
}
