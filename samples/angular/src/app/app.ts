import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterOutlet } from '@angular/router';

import { PositronService } from './positron.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  private readonly positron = inject(PositronService);

  protected readonly name = signal('world');
  protected readonly result = signal('Result will appear here.');

  /** Calls the C# handler via the Positron service and shows the reply. */
  protected async greet(): Promise<void> {
    try {
      const res = await this.positron.greet(this.name());
      this.result.set(res.message);
    } catch (e) {
      this.result.set('error: ' + (e as Error).message);
    }
  }
}
