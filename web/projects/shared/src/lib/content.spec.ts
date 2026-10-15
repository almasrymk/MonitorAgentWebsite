import { TestBed } from '@angular/core/testing';
import { parseRichText, RichText, safeHref } from './content';

describe('rich text', () => {
  it('reads headings, paragraphs and lists', () => {
    const blocks = parseRichText('## Who we are\nWe build\nMonitor Agent.\n\n- one\n- **two**\n\n1. first\n2. second');
    expect(blocks.map((b) => b.kind)).toEqual(['h2', 'p', 'ul', 'ol']);
    expect(blocks[1]).toEqual({ kind: 'p', inlines: [{ kind: 'text', text: 'We build Monitor Agent.' }] });
    expect(blocks[2]).toEqual({ kind: 'ul', items: [[{ kind: 'text', text: 'one' }], [{ kind: 'strong', text: 'two' }]] });
  });

  it('links only to https, this site and email', () => {
    expect(safeHref('https://example.com/x')).toBe('https://example.com/x');
    expect(safeHref('/ar/pricing')).toBe('/ar/pricing');
    expect(safeHref('mailto:help@example.com')).toBe('mailto:help@example.com');
    for (const bad of ['javascript:alert(1)', 'JaVaScRiPt:alert(1)', 'data:text/html,x', '//evil.test', 'http://plain.test', '/\\evil.test']) {
      expect(safeHref(bad)).toBeNull();
    }
    const [p] = parseRichText('[click](javascript:alert(1))');
    const inlines = p.kind === 'p' ? p.inlines : [];
    expect(inlines.every((i) => i.kind === 'text')).toBe(true);
    expect(inlines.map((i) => i.text).join('')).toBe('[click](javascript:alert(1))');
  });

  it('shows markup as text instead of running it', () => {
    const fixture = TestBed.createComponent(RichText);
    fixture.componentRef.setInput('text', '<img src=x onerror="window.hacked=1"><script>window.hacked=1</script>\n\n[x](https://ok.test)');
    fixture.detectChanges();
    const host: HTMLElement = fixture.nativeElement;
    expect(host.querySelector('img, script')).toBeNull();
    expect(host.textContent).toContain('<script>window.hacked=1</script>');
    expect(host.querySelector('a')?.getAttribute('href')).toBe('https://ok.test');
    expect(host.querySelector('a')?.getAttribute('rel')).toBe('noopener');
  });
});
