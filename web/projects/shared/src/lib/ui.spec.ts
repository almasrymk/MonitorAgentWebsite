import { TestBed } from '@angular/core/testing';
import { LoadError, Skeleton, StatusBadge } from './ui';
import { apiError, setUp } from './testing';

describe('shared UI', () => {
  beforeEach(() => setUp());

  it('shows why a section failed and offers a retry', () => {
    const fixture = TestBed.createComponent(LoadError);
    fixture.componentRef.setInput('error', apiError(400, 'WRONG_PASSWORD'));
    let retried = 0;
    fixture.componentInstance.retry.subscribe(() => retried++);
    fixture.detectChanges();
    const host: HTMLElement = fixture.nativeElement;
    expect(host.querySelector('[role=alert]')?.textContent).toContain('Your current password is not right.');
    host.querySelector('button')!.click();
    expect(retried).toBe(1);
  });

  it('announces loading', () => {
    const fixture = TestBed.createComponent(Skeleton);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role=status]')).not.toBeNull();
    expect(fixture.nativeElement.querySelectorAll('.skeleton').length).toBe(4);
  });

  it('colors a status by meaning and always shows its text', () => {
    const fixture = TestBed.createComponent(StatusBadge);
    const tone = (value: string) => {
      fixture.componentRef.setInput('value', value);
      fixture.detectChanges();
      return (fixture.nativeElement.querySelector('.badge') as HTMLElement).className;
    };
    expect(tone('Active')).toContain('ok');
    expect(tone('Pending')).toContain('warn');
    expect(tone('Revoked')).toContain('bad');
    expect(tone('Whatever')).toBe('badge');
    expect(fixture.nativeElement.textContent).toBe('status.Whatever');
  });
});
