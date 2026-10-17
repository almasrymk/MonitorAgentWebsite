import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import qrcode from 'qrcode-generator';

/**
 * A QR code drawn as one SVG path, made in the browser so the text (an authenticator key) never leaves the page.
 * Dark modules on a white square with a quiet zone, so phone cameras read it in dark mode too.
 */
@Component({
  selector: 'ma-qr-code',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg class="qr" [attr.viewBox]="viewBox()" role="img" [attr.aria-label]="label()" shape-rendering="crispEdges">
      <rect [attr.x]="-quiet" [attr.y]="-quiet" width="100%" height="100%" fill="#fff" />
      <path [attr.d]="path()" fill="#000" />
    </svg>
  `,
  styles: `
    .qr {
      display: block;
      width: 200px;
      max-width: 100%;
      height: auto;
      border-radius: 8px;
    }
  `,
})
export class QrCode {
  readonly text = input.required<string>();
  readonly label = input.required<string>();
  protected readonly quiet = 4;
  private readonly code = computed(() => {
    const qr = qrcode(0, 'M');
    qr.addData(this.text());
    qr.make();
    return qr;
  });
  protected readonly viewBox = computed(() => {
    const size = this.code().getModuleCount() + 2 * this.quiet;
    return `${-this.quiet} ${-this.quiet} ${size} ${size}`;
  });
  protected readonly path = computed(() => {
    const qr = this.code();
    const count = qr.getModuleCount();
    let d = '';
    for (let row = 0; row < count; row++)
      for (let col = 0; col < count; col++) if (qr.isDark(row, col)) d += `M${col} ${row}h1v1h-1z`;
    return d;
  });
}
