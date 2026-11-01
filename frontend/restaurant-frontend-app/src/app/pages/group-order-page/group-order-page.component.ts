import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { finalize, forkJoin, interval, map, Subscription, switchMap } from 'rxjs';
import { ParticipantCardComponent } from '@app/components/group-orders/participant-card/participant-card.component';
import { GroupStatusResponse } from '@app/models/group-orders/group-status.model';
import { Participant } from '@app/models/group-orders/participant.model';
import { GuestInvite } from '@app/models/reservations/guest-invite.model';
import { GroupOrdersService } from '@app/services/group-orders/group-orders.service';
import { ToastService } from '@app/services/toast/toast.service';
import { consolidatePreOrder } from '@app/shared/helpers/consolidate-pre-order';
import { ModalComponent } from '@app/shared/custom-components/modal/modal.component';

const REFRESH_INTERVAL_MS = 15_000;

@Component({
  selector: 'app-group-order-page',
  imports: [CurrencyPipe, DatePipe, ParticipantCardComponent, ModalComponent],
  templateUrl: './group-order-page.component.html',
  styleUrl: './group-order-page.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class GroupOrderPageComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly service = inject(GroupOrdersService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  private polling: Subscription | null = null;
  private refreshInFlight = false;

  readonly reservationId = this.route.snapshot.paramMap.get('reservationId') ?? '';
  readonly invite = signal<GuestInvite | null>(null);
  readonly participants = signal<Participant[]>([]);
  readonly ownerSessionId = signal('');
  readonly groupStatus = signal<GroupStatusResponse | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly refreshError = signal(false);
  readonly updatedAt = signal<Date | null>(null);
  readonly confirmationOpen = signal(false);
  readonly submitting = signal(false);

  readonly consolidated = computed(() => consolidatePreOrder(this.participants()));
  readonly pendingCount = computed(() => this.participants().filter((person) => !person.isSubmitted).length);
  readonly anyDishes = computed(() => this.participants().some((person) => person.items.length > 0));
  readonly isOpen = computed(() => this.groupStatus()?.status === 'Open');
  readonly isSent = computed(() => this.groupStatus()?.status === 'Submitted');

  ngOnInit(): void {
    this.load();
  }

  ngOnDestroy(): void {
    this.stopPolling();
  }

  load(): void {
    this.stopPolling();
    if (!this.reservationId) {
      this.loading.set(false);
      this.error.set('Group reservation not found');
      return;
    }

    this.loading.set(true);
    this.error.set(null);
    this.refreshError.set(false);

    this.service.getGroupInvite$(this.reservationId).pipe(
      switchMap((invite) => this.service.getOwnerSessionId$(this.reservationId).pipe(
        switchMap((ownerSessionId) => forkJoin({
          sessions: this.service.getSessions$(invite.GroupReservationId),
          status: this.service.getStatus$(invite.GroupReservationId),
        }).pipe(map((result) => ({ invite, ownerSessionId, ...result })))),
      )),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: ({ invite, sessions, ownerSessionId, status }) => {
        this.invite.set(invite);
        this.participants.set(sessions.participants);
        this.ownerSessionId.set(ownerSessionId);
        this.groupStatus.set(status);
        this.updatedAt.set(new Date());
        this.loading.set(false);
        if (status.status === 'Open') this.startPolling();
      },
      error: (error: HttpErrorResponse) => {
        this.loading.set(false);
        this.error.set(error.status === 404 ? 'Group reservation not found' : 'Could not load group pre-order');
      },
    });
  }

  refresh(): void {
    this.refreshData();
  }

  openConfirmation(): void {
    if (this.isOpen() && this.consolidated().items.length > 0 && !this.submitting()) {
      this.confirmationOpen.set(true);
    }
  }

  closeConfirmation(): void {
    if (!this.submitting()) this.confirmationOpen.set(false);
  }

  submit(): void {
    const groupId = this.invite()?.GroupReservationId;
    if (!groupId || !this.isOpen() || this.submitting() || this.consolidated().items.length === 0) return;

    this.submitting.set(true);
    this.service.submit$(groupId, this.reservationId).pipe(
      finalize(() => this.submitting.set(false)),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => {
        this.groupStatus.update((status) => status ? { ...status, status: 'Submitted' } : status);
        this.confirmationOpen.set(false);
        this.stopPolling();
        this.toast.success('Pre-order sent to the restaurant');
      },
      error: () => {
        // tokenInterceptor displays the API error. Keep the screen available for retry.
        this.confirmationOpen.set(false);
      },
    });
  }

  private startPolling(): void {
    if (this.polling) return;
    this.polling = interval(REFRESH_INTERVAL_MS).subscribe(() => this.refreshData());
  }

  private stopPolling(): void {
    this.polling?.unsubscribe();
    this.polling = null;
  }

  private refreshData(): void {
    const groupId = this.invite()?.GroupReservationId;
    if (!groupId || !this.isOpen() || this.refreshInFlight) return;

    this.refreshInFlight = true;
    forkJoin({
      sessions: this.service.getSessions$(groupId, { silent: true }),
      status: this.service.getStatus$(groupId, { silent: true }),
    }).pipe(
      finalize(() => { this.refreshInFlight = false; }),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: ({ sessions, status }) => {
        this.participants.set(sessions.participants);
        this.groupStatus.set(status);
        this.updatedAt.set(new Date());
        this.refreshError.set(false);
        if (status.status !== 'Open') this.stopPolling();
      },
      error: () => this.refreshError.set(true),
    });
  }
}
