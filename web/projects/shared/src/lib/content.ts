import { Component, computed, input, ChangeDetectionStrategy } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';

/** A piece of a line: plain text, bold text, or a link. */
export type Inline =
  | { kind: 'text'; text: string }
  | { kind: 'strong'; text: string }
  | { kind: 'link'; text: string; href: string };

export type Block =
  { kind: 'h2' | 'h3' | 'p'; inlines: Inline[] } | { kind: 'ul' | 'ol'; items: Inline[][] };

/**
 * Only https addresses and paths on this site may be linked. Anything else (javascript:, data:, //other-host) is shown
 * as plain text.
 */
export function safeHref(href: string): string | null {
  const value = href.trim();
  if (/^https:\/\/[^\s/]+/i.test(value)) return value;
  if (/^\/(?!\/)[^\s\\]*$/.test(value)) return value;
  if (/^mailto:[^\s@]+@[^\s@]+$/i.test(value)) return value;
  return null;
}

const INLINE = /\*\*(.+?)\*\*|\[([^\]]+)\]\(([^)\s]+)\)/g;

export function parseInline(line: string): Inline[] {
  const out: Inline[] = [];
  let last = 0;
  for (const match of line.matchAll(INLINE)) {
    if (match.index > last) out.push({ kind: 'text', text: line.slice(last, match.index) });
    if (match[1] !== undefined) {
      out.push({ kind: 'strong', text: match[1] });
    } else {
      const href = safeHref(match[3]);
      out.push(href ? { kind: 'link', text: match[2], href } : { kind: 'text', text: match[0] });
    }
    last = match.index + match[0].length;
  }
  if (last < line.length) out.push({ kind: 'text', text: line.slice(last) });
  return out;
}

/**
 * The small markdown subset page bodies use: "## " and "### " headings, "- " and "1. " lists, **bold**, [links](https://...),
 * and blank lines between paragraphs. The result is data that templates render as text, never as HTML, so whatever an
 * editor types (tags, scripts) shows up literally.
 */
export function parseRichText(text: string): Block[] {
  const blocks: Block[] = [];
  let paragraph: string[] = [];
  let list: { kind: 'ul' | 'ol'; items: Inline[][] } | null = null;
  const flush = () => {
    if (paragraph.length) blocks.push({ kind: 'p', inlines: parseInline(paragraph.join(' ')) });
    paragraph = [];
    if (list) blocks.push(list);
    list = null;
  };
  for (const raw of text.replace(/\r\n?/g, '\n').split('\n')) {
    const line = raw.trim();
    const bullet = /^[-*]\s+(.*)$/.exec(line);
    const numbered = /^\d+[.)]\s+(.*)$/.exec(line);
    const heading = /^(#{2,3})\s+(.*)$/.exec(line);
    if (!line) {
      flush();
    } else if (heading) {
      flush();
      blocks.push({
        kind: heading[1].length === 2 ? 'h2' : 'h3',
        inlines: parseInline(heading[2]),
      });
    } else if (bullet || numbered) {
      const kind = bullet ? 'ul' : 'ol';
      if (paragraph.length || (list && list.kind !== kind)) flush();
      list ??= { kind, items: [] };
      list.items.push(parseInline((bullet ?? numbered)![1]));
    } else {
      if (list) flush();
      paragraph.push(line);
    }
  }
  flush();
  return blocks;
}

/** Renders a page body written in the markdown subset above. */
@Component({
  selector: 'ma-rich-text',
  template: `
    <ng-template #line let-inlines>
      @for (part of inlines; track $index) {
        @switch (part.kind) {
          @case ('strong') {
            <strong>{{ part.text }}</strong>
          }
          @case ('link') {
            <a [href]="part.href" [attr.rel]="part.href.startsWith('/') ? null : 'noopener'">{{
              part.text
            }}</a>
          }
          @default {
            {{ part.text }}
          }
        }
      }
    </ng-template>
    @for (block of blocks(); track $index) {
      @switch (block.kind) {
        @case ('h2') {
          <h2><ng-container *ngTemplateOutlet="line; context: { $implicit: block.inlines }" /></h2>
        }
        @case ('h3') {
          <h3><ng-container *ngTemplateOutlet="line; context: { $implicit: block.inlines }" /></h3>
        }
        @case ('p') {
          <p><ng-container *ngTemplateOutlet="line; context: { $implicit: block.inlines }" /></p>
        }
        @case ('ul') {
          <ul>
            @for (item of block.items; track $index) {
              <li><ng-container *ngTemplateOutlet="line; context: { $implicit: item }" /></li>
            }
          </ul>
        }
        @case ('ol') {
          <ol>
            @for (item of block.items; track $index) {
              <li><ng-container *ngTemplateOutlet="line; context: { $implicit: item }" /></li>
            }
          </ol>
        }
      }
    }
  `,
  imports: [NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.Eager,
  host: { class: 'rich-text' },
})
export class RichText {
  readonly text = input.required<string>();
  protected readonly blocks = computed(() => parseRichText(this.text()));
}
