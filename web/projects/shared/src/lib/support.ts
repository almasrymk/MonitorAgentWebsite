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
  'support.none': 'لا توجد تذاكر بعد. افتح تذكرة عندما تحتاج إلى مساعدة.',
  'support.number': 'التذكرة',
  'support.subject': 'الموضوع',
  'support.categoryLabel': 'النوع',
  'support.message': 'ماذا حدث؟',
  'support.messageHelp': 'اذكر إصدار البرنامج وما كنت تفعله. لا ترسل أبداً كلمة مرور أو مفتاح منتج.',
  'support.statusLabel': 'الحالة',
  'support.updated': 'آخر تحديث',
  'support.open': 'فتح',
  'support.submit': 'أرسل التذكرة',
  'support.conversation': 'المحادثة',
  'support.reply': 'ردّك',
  'support.reopen': 'اكتب لإعادة فتح التذكرة',
  'support.send': 'أرسل',
  'support.close': 'أغلق التذكرة',
  'support.closeConfirm': 'هل تريد إغلاق هذه التذكرة؟ يمكنك إعادة فتحها بالكتابة فيها.',
  'support.category.Technical': 'فني',
  'support.category.Licensing': 'التراخيص',
  'support.category.Billing': 'الفواتير والدفع',
  'support.category.Other': 'أخرى',
  'support.status.Open': 'بانتظار الدعم',
  'support.status.Answered': 'تم الرد',
  'support.status.Closed': 'مغلقة',
  'suggestions.title': 'الاقتراحات',
  'suggestions.lead': 'أخبرنا بما يجعل Monitor Agent أفضل. يقرأ فريقنا كل اقتراح.',
  'suggestions.new': 'فكرتك',
  'suggestions.titleLabel': 'العنوان',
  'suggestions.body': 'التفاصيل',
  'suggestions.submit': 'أرسل الاقتراح',
  'suggestions.sent': 'شكراً لك، تم إرسال الاقتراح.',
  'suggestions.none': 'لا توجد اقتراحات بعد.',
  'suggestions.answer': 'ردّنا',
  'suggestions.status.New': 'جديد',
  'suggestions.status.Planned': 'ضمن الخطة',
  'suggestions.status.Done': 'تم التنفيذ',
  'suggestions.status.Declined': 'ليس ضمن الخطة',
  'error.TICKET_NOT_FOUND': 'هذه التذكرة غير موجودة أو لا تخصك.',
  'error.TICKET_CLOSED': 'هذه التذكرة مغلقة بالفعل.',
  'error.TICKET_TOO_LONG': 'تحتوي هذه التذكرة على رسائل كثيرة. يرجى فتح تذكرة جديدة.',
  'error.TOO_MANY_OPEN_TICKETS': 'لدى شركتك تذاكر مفتوحة كثيرة. أغلق التذاكر التي حُلّت أولاً.',
  'error.SUGGESTION_NOT_FOUND': 'هذا الاقتراح غير موجود.',
};

export const SUPPORT_TEXTS = { en, ar };
