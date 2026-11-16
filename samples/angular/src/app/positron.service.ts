import { Injectable } from '@angular/core';

/** Reply payload from the C# "greet" handler. */
export interface GreetResponse {
  message: string;
}

/**
 * Thin wrapper around the Positron IPC bridge (window.positron). Keeping IPC in a service means
 * components depend on Angular DI rather than the global bridge object directly.
 */
@Injectable({ providedIn: 'root' })
export class PositronService {
  /** Invokes the C# "greet" handler and resolves to its reply. */
  greet(name: string): Promise<GreetResponse> {
    return window.positron.invoke<GreetResponse>('greet', { name });
  }
}
