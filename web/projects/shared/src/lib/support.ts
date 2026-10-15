import { Component, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { WhenPipe } from './format';
import { I18n, TranslatePipe } from './i18n';
import { Ticket, TicketCategory } from './models';
import { errorText } from './session';
import { StatusBadge } from './ui';

export const TICKET_CATEGORIES: TicketCategory[] = ['Technical', 'Licensing', 'Billing', 'Other'];

/** A support conversation and the reply box, shared by the portal and the admin app. Text is shown exactly as written. */
@Component({
  selector: 'ma-ticket-thread',
  imports: [FormsModule, TranslatePipe, WhenPipe, StatusBadge],
  template: `
    <div class="page-head">
      <div>
        <p class="eyebrow mono">{{ ticket().number }}</p>
        <h1>{{ ticket().subject }}</h1>
        <p class="muted">
          {{ 'support.category.' + ticket().category | t }} · {{ ticket().openedAt | when }}
          @if (ticket().companyName) {
            · {{ ticket().companyName }}
          }
        </p>
      </div>
      <ma-status [value]="ticket().status" prefix="support.status." />
    </div>
    <ol class="thread" [attr.aria-label]="'support.conversation' | t">
      @for (message of ticket().messages; track message.id) {
        <li class="card message" [class.staff]="message.fromStaff">
          <p class="meta"><strong>{{ message.authorName }}</strong> · <span class="muted">{{ message.postedAt | when: true }}</span></p>
          <p class="body">{{ message.body }}</p>
        </li>
      }
    </ol>
    @if (error()) {
      <p class="alert" role="alert">{{ error() }}</p>
    }
    <form class="card" (ngSubmit)="send()">
      <div class="field">
        <label for="reply">{{ (ticket().status === 'Closed' ? 'support.reopen' : 'support.reply') | t }}</label>
        <textarea id="reply" name="reply" rows="5" required maxlength="5000" [(ngModel)]="body"></textarea>
      </div>
      <div class="row">
        <button class="btn" type="submit" [disabled]="busy()">{{ 'support.send' | t }}</button>
        @if (ticket().status !== 'Closed') {
          <button class="btn secondary" type="button" (click)="close.emit()" [disabled]="busy()">{{ 'support.close' | t }}</button>
        }
      </div>
    </form>
  `,
})
export class TicketThread {
  private readonly i18n = inject(I18n);
  readonly ticket = input.required<Ticket>();
  /** Posts the reply and returns the updated ticket; the thread clears the box only when it succeeds. */
  readonly post = input.required<(body: string) => Promise<unknown>>();
  readonly close = output();
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected body = '';

  protected async send(): Promise<void> {
    if (!this.body.trim()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      await this.post()(this.body);
      this.body = '';
    } catch (error) {
      this.error.set(errorText(this.i18n, error));
    } finally {
      this.busy.set(false);
    }
  }
}

const en = {
  'support.title': 'Support',
  'support.new': 'New ticket',
  'support.none': 'No tickets yet. Open one when you need help.',
  'support.number': 'Ticket',
  'support.subject': 'Subject',
  'support.categoryLabel': 'Topic',
  'support.message': 'What happened?',
  'support.messageHelp': 'Include the program version and what you were doing. Never send passwords or product keys.',
  'support.statusLabel': 'Status',
  'support.updated': 'Last update',
  'support.open': 'Open',
  'support.submit': 'Send ticket',
  'support.conversation': 'Conversation',
  'support.reply': 'Your reply',
  'support.reopen': 'Write to reopen this ticket',
  'support.send': 'Send',
  'support.close': 'Close ticket',
  'support.closeConfirm': 'Close this ticket? You can reopen it by writing again.',
  'support.category.Technical': 'Technical',
  'support.category.Licensing': 'Licensing',
  'support.category.Billing': 'Billing',
  'support.category.Other': 'Other',
  'support.status.Open': 'Waiting on support',
  'support.status.Answered': 'Answered',
  'support.status.Closed': 'Closed',
  'suggestions.title': 'Suggestions',
  'suggestions.lead': 'Tell us what would make Monitor Agent better. Our team reads every suggestion.',
  'suggestions.new': 'Your idea',
  'suggestions.titleLabel': 'Title',
  'suggestions.body': 'Details',
  'suggestions.submit': 'Send suggestion',
  'suggestions.sent': 'Thank you. Your suggestion was sent.',
  'suggestions.none': 'No suggestions yet.',
  'suggestions.answer': 'Our answer',
  'suggestions.status.New': 'Received',
  'suggestions.status.Planned': 'Planned',
  'suggestions.status.Done': 'Done',
  'suggestions.status.Declined': 'Not planned',
  'error.TICKET_NOT_FOUND': 'This ticket does not exist or is not yours.',
  'error.TICKET_CLOSED': 'This ticket is already closed.',
  'error.TICKET_TOO_LONG': 'This ticket has too many messages. Please open a new one.',
  'error.TOO_MANY_OPEN_TICKETS': 'Your company has too many open tickets. Close the ones that are solved first.',
  'error.SUGGESTION_NOT_FOUND': 'This suggestion does not exist.',
} as const;

const ar: Record<keyof typeof en, string> = {
  'support.title': 'الدعم',
  'support.new': 'تذكرة جديدة',
  'support.none': 'مفيش تذاكر لسه. افتح واحدة لما تحتاج مساعدة.',
  'support.number': 'التذكرة',
  'support.subject': 'الموضوع',
  'support.categoryLabel': 'النوع',
  'support.message': 'إيه اللي حصل؟',
  'support.messageHelp': 'اكتب نسخة البرنامج وكنت بتعمل إيه. ماتبعتش أبدًا باسورد أو مفتاح منتج.',
  'support.statusLabel': 'الحالة',
  'support.updated': 'آخر تحديث',
  'support.open': 'افتح',
  'support.submit': 'ابعت التذكرة',
  'support.conversation': 'المحادثة',
  'support.reply': 'ردك',
  'support.reopen': 'اكتب عشان تفتح التذكرة تاني',
  'support.send': 'ابعت',
  'support.close': 'اقفل التذكرة',
  'support.closeConfirm': 'تقفل التذكرة دي؟ تقدر تفتحها تاني لو كتبت فيها.',
  'support.category.Technical': 'فني',
  'support.category.Licensing': 'التراخيص',
  'support.category.Billing': 'الفواتير والدفع',
  'support.category.Other': 'حاجة تانية',
  'support.status.Open': 'مستنية الدعم',
  'support.status.Answered': 'اتردّ عليها',
  'support.status.Closed': 'مقفولة',
  'suggestions.title': 'الاقتراحات',
  'suggestions.lead': 'قولنا إيه اللي يخلّي Monitor Agent أحسن. فريقنا بيقرا كل اقتراح.',
  'suggestions.new': 'فكرتك',
  'suggestions.titleLabel': 'العنوان',
  'suggestions.body': 'التفاصيل',
  'suggestions.submit': 'ابعت الاقتراح',
  'suggestions.sent': 'شكرًا، الاقتراح اتبعت.',
  'suggestions.none': 'مفيش اقتراحات لسه.',
  'suggestions.answer': 'ردّنا',
  'suggestions.status.New': 'وصل',
  'suggestions.status.Planned': 'في الخطة',
  'suggestions.status.Done': 'اتعمل',
  'suggestions.status.Declined': 'مش في الخطة',
  'error.TICKET_NOT_FOUND': 'التذكرة دي مش موجودة أو مش بتاعتك.',
  'error.TICKET_CLOSED': 'التذكرة دي مقفولة بالفعل.',
  'error.TICKET_TOO_LONG': 'التذكرة دي فيها رسايل كتير. افتح واحدة جديدة لو سمحت.',
  'error.TOO_MANY_OPEN_TICKETS': 'شركتك عندها تذاكر مفتوحة كتير. اقفل اللي اتحلت الأول.',
  'error.SUGGESTION_NOT_FOUND': 'الاقتراح ده مش موجود.',
};

export const SUPPORT_TEXTS = { en, ar };
