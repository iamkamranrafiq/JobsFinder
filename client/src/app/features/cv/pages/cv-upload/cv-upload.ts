import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';

import { ProfileForm } from '../../components/profile-form/profile-form';
import { ProfileView } from '../../components/profile-view/profile-view';
import { CvStore } from '../../state/cv.store';

@Component({
  selector: 'app-cv-upload',
  imports: [ProfileView, ProfileForm],
  templateUrl: './cv-upload.html',
  styleUrl: './cv-upload.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CvUpload {
  protected readonly store = inject(CvStore);
  protected readonly selected = signal<File | null>(null);

  protected onFileChange(input: HTMLInputElement): void {
    this.selected.set(input.files?.item(0) ?? null);
  }

  protected onUpload(): void {
    const file = this.selected();
    if (file) {
      this.store.upload(file);
    }
  }
}
