import { TestBed } from '@angular/core/testing';
import { Ticket } from './models';
import { TicketThread } from './support';
import { apiError, setUp, settle } from './testing';

const ticket: Ticket = {
  id: 't1', number: 'T-1', subject: 'Agent stops', category: 'Technical', status: 'Answered', openedAt: '2026-10-14T10:00:00Z', companyName: 'Acme',
  messages: [
    { id: 'm1', authorName: 'Mona', fromStaff: false, body: '<b>not bold</b>', postedAt: '2026-10-14T10:00:00Z' },
    { id: 'm2', authorName: 'Sara (Support)', fromStaff: true, body: 'Send the log.', postedAt: '2026-10-14T12:00:00Z' },
  ],
};

describe('TicketThread', () => {
  beforeEach(() => setUp());

  function create(post: (body: string) => Promise<unknown>, value: Ticket = ticket) {
    const fixture = TestBed.createComponent(TicketThread);
    fixture.componentRef.setInput('ticket', value);
    fixture.componentRef.setInput('post', post);
    fixture.detectChanges();
    return fixture;
  }

  it('shows the conversation as text, staff replies marked', () => {
    const fixture = create(() => Promise.resolve());
    const host: HTMLElement = fixture.nativeElement;
    expect(host.querySelector('b')).toBeNull();
    expect(host.textContent).toContain('<b>not bold</b>');
    expect(host.querySelectorAll('.message.staff').length).toBe(1);
    expect(host.textContent).toContain('Acme');
  });

  it('sends a reply and clears the box only when it worked', async () => {
    const post = vi.fn().mockRejectedValueOnce(apiError(409, 'TICKET_CLOSED')).mockResolvedValueOnce(undefined);
    const fixture = create(post);
    const thread = fixture.componentInstance as unknown as { body: string; send(): Promise<void> };
    thread.body = '   ';
    await thread.send();
    expect(post).not.toHaveBeenCalled();

    thread.body = 'Here it is';
    await thread.send();
    await settle(fixture);
    expect(fixture.nativeElement.querySelector('[role=alert]').textContent).toContain('already closed');
    expect(thread.body).toBe('Here it is');

    await thread.send();
    await settle(fixture);
    expect(post).toHaveBeenLastCalledWith('Here it is');
    expect(thread.body).toBe('');
    expect(fixture.nativeElement.querySelector('[role=alert]')).toBeNull();
  });

  it('offers closing only while the ticket is open', () => {
    let closed = 0;
    const fixture = create(() => Promise.resolve());
    fixture.componentInstance.close.subscribe(() => closed++);
    const closeButton = [...fixture.nativeElement.querySelectorAll('button')].find((b: HTMLElement) => b.textContent?.includes('Close ticket')) as HTMLElement;
    closeButton.click();
    expect(closed).toBe(1);

    const done = create(() => Promise.resolve(), { ...ticket, status: 'Closed' });
    expect(done.nativeElement.textContent).not.toContain('Close ticket');
    expect(done.nativeElement.textContent).toContain('Write to reopen this ticket');
  });
});
