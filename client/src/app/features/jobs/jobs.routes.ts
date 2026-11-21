import { Routes } from '@angular/router';

import { JobList } from './pages/job-list/job-list';
import { JobsStore } from './state/jobs.store';

export const JOBS_ROUTES: Routes = [
  {
    path: '',
    providers: [JobsStore],
    children: [{ path: '', component: JobList, title: 'Jobs · JobsFinder' }],
  },
];
