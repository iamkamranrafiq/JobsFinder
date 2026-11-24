import { ChangeDetectionStrategy, Component } from '@angular/core';

/** Tells the user that the last job fetch failed (F0.2-AC4). The page decides when to show it. */
@Component({
  selector: 'app-source-status-banner',
  templateUrl: './source-status-banner.html',
  styleUrl: './source-status-banner.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SourceStatusBanner {}
