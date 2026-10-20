import { Component, computed, input, ChangeDetectionStrategy } from '@angular/core';

/** A circle as path data, so every icon is a list of paths. */
function circle(cx: number, cy: number, r: number): string {
  return `M${cx - r} ${cy}a${r} ${r} 0 1 0 ${2 * r} 0a${r} ${r} 0 1 0 ${-2 * r} 0`;
}

/** A rounded rectangle as path data. */
function rect(x: number, y: number, w: number, h: number, r = 0): string {
  return `M${x + r} ${y}h${w - 2 * r}a${r} ${r} 0 0 1 ${r} ${r}v${h - 2 * r}a${r} ${r} 0 0 1 ${-r} ${r}h${-(w - 2 * r)}a${r} ${r} 0 0 1 ${-r} ${-r}v${-(h - 2 * r)}a${r} ${r} 0 0 1 ${r} ${-r}z`;
}

const doc = 'M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z M14 2v6h6';

/** Line icons drawn on a 24px grid, in the style of the design (2px strokes, round joins). */
const ICONS = {
  arrow: ['M5 12h14', 'M13 6l6 6-6 6'],
  download: ['M12 3v12', 'M7 10l5 5 5-5', 'M5 21h14'],
  globe: [circle(12, 12, 10), 'M2 12h20', 'M12 2a15 15 0 0 1 4 10 15 15 0 0 1-4 10 15 15 0 0 1-4-10 15 15 0 0 1 4-10z'],
  pulse: ['M2 12h4l3-7 4 14 3-7h6'],
  clock: [circle(12, 12, 10), 'M12 6v6l4 2'],
  timer: [circle(12, 14, 8), 'M10 2h4', 'M12 14l3-3'],
  bell: ['M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9', 'M10.3 21a1.94 1.94 0 0 0 3.4 0'],
  file: [doc, 'M16 13H8', 'M16 17H8', 'M10 9H8'],
  fileCheck: [doc, 'M9 15l2 2 4-4'],
  dashboard: [rect(3, 3, 7, 9, 1), rect(14, 3, 7, 5, 1), rect(14, 12, 7, 9, 1), rect(3, 16, 7, 5, 1)],
  monitor: [rect(2, 3, 20, 14, 2), 'M8 21h8', 'M12 17v4'],
  cpu: [rect(4, 4, 16, 16, 2), rect(9, 9, 6, 6, 1), 'M9 1v3M15 1v3M9 20v3M15 20v3M20 9h3M20 14h3M1 9h3M1 14h3'],
  disk: ['M22 12H2', 'M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z', 'M6 16h.01M10 16h.01'],
  memory: [rect(2, 6, 20, 12, 2), 'M6 10v4M10 10v4M14 10v4M18 10v4'],
  server: [rect(2, 2, 20, 8, 2), rect(2, 14, 20, 8, 2), 'M6 6h.01M6 18h.01'],
  database: ['M3 5a9 3 0 0 0 18 0a9 3 0 0 0-18 0', 'M3 5v14a9 3 0 0 0 18 0V5', 'M3 12a9 3 0 0 0 18 0'],
  camera: ['M3 6.5 13 4l2 7-10 2.5z', 'M15 7.5l4-1 1 3.5-4 1', 'M9 12.5V17', 'M4 17h5', 'M4 15v4'],
  network: [rect(16, 16, 6, 6, 1), rect(2, 16, 6, 6, 1), rect(9, 2, 6, 6, 1), 'M5 16v-3a1 1 0 0 1 1-1h12a1 1 0 0 1 1 1v3', 'M12 12V8'],
  wifi: ['M5 12.55a11 11 0 0 1 14.08 0', 'M1.42 9a16 16 0 0 1 21.16 0', 'M8.53 16.11a6 6 0 0 1 6.95 0', 'M12 20h.01'],
  check: ['M20 6 9 17l-5-5'],
  x: ['M18 6 6 18', 'M6 6l12 12'],
  warning: ['M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z', 'M12 9v4', 'M12 17h.01'],
  trendDown: ['M22 17l-8.5-8.5-5 5L2 7', 'M16 17h6v-6'],
  shield: ['M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z', 'M9 12l2 2 4-4'],
  key: [circle(7.5, 15.5, 5.5), 'M21 2l-9.6 9.6', 'M15.5 7.5l3 3L22 7l-3-3'],
  settings: [
    circle(12, 12, 3),
    'M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z',
  ],
  target: [circle(12, 12, 10), circle(12, 12, 3), 'M22 12h-4M6 12H2M12 6V2M12 22v-4'],
  checkSquare: ['M9 11l3 3L22 4', 'M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11'],
  search: [circle(11, 11, 8), 'M21 21l-4.35-4.35', 'M11 7v4', 'M11 14h.01'],
  wrench: ['M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z'],
  pin: ['M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0 1 18 0z', circle(12, 10, 3)],
  link: ['M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71', 'M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71'],
  lock: [rect(3, 11, 18, 11, 2), 'M7 11V7a5 5 0 0 1 10 0v4'],
  userPlus: ['M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2', circle(9, 7, 4), 'M19 8v6M22 11h-6'],
  trash: ['M3 6h18', 'M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6', 'M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2'],
  signature: ['M3 17c3-6 5-6 6-3s2 3 4 0 3-2 4 0', 'M3 21h18'],
  info: [circle(12, 12, 10), 'M12 16v-4', 'M12 8h.01'],
  message: ['M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z', 'M7 8h10M7 12h6'],
  plus: ['M12 5v14', 'M5 12h14'],
  chart: ['M3 3v18h18', 'M7 14l4-4 4 4 5-5'],
  chevron: ['M9 18l6-6-6-6'],
  windowMin: ['M5 12h14'],
  windowMax: [rect(5, 5, 14, 14, 1)],
} as const;

export type IconName = keyof typeof ICONS;

/** A decorative line icon. The text next to it carries the meaning, so it is hidden from screen readers. */
@Component({
  selector: 'site-icon',
  changeDetection: ChangeDetectionStrategy.Eager,
  host: { class: 'icon', '[class.forward]': "name() === 'arrow' || name() === 'chevron'" },
  template: `<svg
    aria-hidden="true"
    focusable="false"
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    stroke-width="2"
    stroke-linecap="round"
    stroke-linejoin="round"
  >
    @for (d of paths(); track $index) {
      <path [attr.d]="d" />
    }
  </svg>`,
})
export class Icon {
  readonly name = input.required<IconName>();
  protected readonly paths = computed(() => ICONS[this.name()]);
}
