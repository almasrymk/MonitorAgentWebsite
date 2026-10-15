import { formatBytes, formatDate, formatMoney } from './format';

describe('format', () => {
  it('shows money in the viewer language', () => {
    expect(formatMoney(1200, 'EGP', 'en')).toContain('1,200');
    expect(formatMoney(1200, 'EGP', 'ar')).toMatch(/١٬?٢٠٠|1,200/);
  });

  it('shows a dash for a missing date', () => {
    expect(formatDate(null, 'en')).toBe('—');
    expect(formatDate('2026-10-15T12:00:00Z', 'en')).toContain('2026');
  });

  it('shows sizes in readable units', () => {
    expect(formatBytes(1048576, 'en')).toBe('1 MB');
    expect(formatBytes(512, 'en')).toBe('512 B');
  });
});
