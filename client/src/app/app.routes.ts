import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'jobs' },
  { path: 'jobs', loadChildren: () => import('./features/jobs/jobs.routes').then((m) => m.JOBS_ROUTES) },
  { path: 'cv', loadChildren: () => import('./features/cv/cv.routes').then((m) => m.CV_ROUTES) },
  { path: '**', redirectTo: 'jobs' },
];
