import { Injectable } from '@angular/core';
import { AbstractControl } from '@angular/forms';
import { ErrorStateMatcher } from '@angular/material/core';

/**
 * Shows a Material form field in its error state as soon as an invalid control is dirty or touched,
 * matching the pre-Material forms (the default matcher waits for blur or submit).
 */
@Injectable()
export class DirtyOrTouchedErrorStateMatcher implements ErrorStateMatcher {
  isErrorState(control: AbstractControl | null): boolean {
    return !!control && control.invalid && (control.dirty || control.touched);
  }
}
