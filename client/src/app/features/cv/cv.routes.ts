import { Routes } from '@angular/router';

import { CvUpload } from './pages/cv-upload/cv-upload';
import { CvStore } from './state/cv.store';

export const CV_ROUTES: Routes = [
  {
    path: '',
    providers: [CvStore],
    children: [{ path: '', component: CvUpload, title: 'Your CV · JobsFinder' }],
  },
];
