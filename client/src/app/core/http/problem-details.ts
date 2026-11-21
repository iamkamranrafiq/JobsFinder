import { HttpErrorResponse } from '@angular/common/http';

/** RFC 9457 body returned by the API for every error (see Api/ErrorHandling). */
export interface ProblemDetails {
  title?: string;
  status?: number;
  code?: string;
  errors?: Record<string, string[]>;
}

export function toErrorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const problem = error.error as ProblemDetails | null;
    if (problem?.title) {
      return problem.title;
    }
    if (error.status === 0) {
      return 'Cannot reach the server.';
    }
  }
  return 'Something went wrong.';
}
