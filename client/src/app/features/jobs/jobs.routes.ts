import { Routes } from '@angular/router';

import { SourcingStore } from '../sourcing/state/sourcing.store';
import { JobList } from './pages/job-list/job-list';
import { JobsStore } from './state/jobs.store';

export const JOBS_ROUTES: Routes = [
  {
    path: '',
    providers: [JobsStore, SourcingStore],
    children: [{ path: '', component: JobList, title: 'Jobs · JobsFinder' }],
  },
];
