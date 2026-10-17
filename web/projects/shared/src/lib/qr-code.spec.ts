import { TestBed } from '@angular/core/testing';
import { QrCode } from './qr-code';

describe('QrCode', () => {
  function render(text: string) {
    const fixture = TestBed.createComponent(QrCode);
    fixture.componentRef.setInput('text', text);
    fixture.componentRef.setInput('label', 'Scan me');
    fixture.detectChanges();
    return fixture.nativeElement.querySelector('svg') as SVGSVGElement;
  }

  it('draws the code as a labelled image with a quiet zone', () => {
    const svg = render('otpauth://totp/Monitor%20Agent:mona@example.test?secret=ABCDEFGH&issuer=Monitor%20Agent');
    expect(svg.getAttribute('role')).toBe('img');
    expect(svg.getAttribute('aria-label')).toBe('Scan me');
    const [x, , size] = svg.getAttribute('viewBox')!.split(' ').map(Number);
    expect(x).toBe(-4);
    expect((size - 8 - 17) % 4).toBe(0); // QR sizes are 21, 25, 29, ... modules
    expect(svg.querySelector('path')!.getAttribute('d')).toMatch(/^M\d+ \d+h1v1h-1z/);
  });

  it('changes when the text changes', () => {
    expect(render('a').querySelector('path')!.getAttribute('d')).not.toBe(render('b').querySelector('path')!.getAttribute('d'));
  });
});
