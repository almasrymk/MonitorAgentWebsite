import {
  afterNextRender,
  Component,
  ChangeDetectionStrategy,
  DOCUMENT,
  inject,
} from '@angular/core';
import { RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: '<router-outlet />',
})
export class App {
  constructor() {
    // Web fonts arrive after first paint: the server-rendered text shows at once in the system font, then swaps.
    const document = inject(DOCUMENT);
    afterNextRender(() => {
      const fonts = document.createElement('link');
      fonts.rel = 'stylesheet';
      fonts.href = 'fonts.css';
      document.head.append(fonts);
    });
  }
}
