import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: [],
  template: `
    <main class="min-h-screen bg-gray-50">
      <router-outlet />
    </main>
  `,
})
export class App {}
