import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { App } from './app';

@Component({ template: '' })
class Blank {}

describe('App', () => {
  it('renders the brand link', async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([])],
    }).compileComponents();
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('.topbar a')?.textContent,
    ).toContain('JobsFinder');
  });

  it('D4-AC3 renders a Material toolbar with the product name and CV/Jobs navigation', async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([])],
    }).compileComponents();
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    const toolbar = el.querySelector('mat-toolbar');
    expect(toolbar).not.toBeNull();
    expect(toolbar?.querySelector('.brand')?.textContent?.trim()).toBe('JobsFinder');
    const links = [...el.querySelectorAll<HTMLAnchorElement>('nav[aria-label="Main"] a')];
    expect(links.map((a) => [a.textContent?.trim(), a.getAttribute('href')])).toEqual([
      ['CV', '/cv'],
      ['Jobs', '/jobs'],
    ]);
    expect(el.querySelector('main.container router-outlet')).not.toBeNull();
  });

  it('D4-AC3 highlights the active route', async () => {
    TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter([
          { path: 'cv', component: Blank },
          { path: 'jobs', component: Blank },
        ]),
      ],
    });
    const fixture = TestBed.createComponent(App);
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/jobs');
    fixture.detectChanges();
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const jobs = el.querySelector<HTMLAnchorElement>('nav a[href="/jobs"]')!;
    const cv = el.querySelector<HTMLAnchorElement>('nav a[href="/cv"]')!;

    expect(jobs.classList).toContain('active');
    expect(jobs.getAttribute('aria-current')).toBe('page');
    expect(cv.classList).not.toContain('active');
    expect(cv.getAttribute('aria-current')).toBeNull();
  });
});
