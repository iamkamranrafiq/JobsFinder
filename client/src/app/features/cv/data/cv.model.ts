// Mirrors JobsFinder.Application.Cv DTOs (CvDtos.cs). Enums are serialized as strings by the API.
export type CvFormat = 'Pdf' | 'Docx';
export type SkillProficiency = 'Beginner' | 'Intermediate' | 'Advanced' | 'Expert';
export type RemotePreference = 'Remote' | 'Hybrid' | 'OnSite' | 'Any';

export interface Cv {
  document: CvDocument;
  profile: CandidateProfile;
}

export interface CvDocument {
  id: string;
  fileName: string;
  contentType: string;
  format: CvFormat;
  sizeBytes: number;
  uploadedAt: string;
}

export interface CandidateProfile {
  id: string;
  cvDocumentId: string;
  version: number;
  createdAt: string;
  data: CandidateProfileData;
}

export interface CandidateProfileData {
  identity: { fullName: string | null; headline: string | null };
  contact: { email: string | null; phone: string | null; location: string | null };
  summary: string | null;
  skills: ProfileSkill[];
  experience: ProfileExperience[];
  education: ProfileEducation[];
  certifications: ProfileCertification[];
  links: ProfileLink[];
  workAuthorisation: string | null;
  currentEmployer: string | null;
  preferences: ProfilePreferences;
}

export interface ProfileSkill {
  name: string;
  proficiency: SkillProficiency | null;
  years: number | null;
}

export interface ProfileExperience {
  role: string;
  company: string;
  startDate: string | null;
  endDate: string | null;
  isCurrent: boolean;
  achievements: string[];
}

export interface ProfileEducation {
  institution: string;
  qualification: string | null;
  field: string | null;
  startDate: string | null;
  endDate: string | null;
}

export interface ProfileCertification {
  name: string;
  issuer: string | null;
  year: number | null;
}

export interface ProfileLink {
  label: string | null;
  url: string;
}

export interface ProfilePreferences {
  targetRoles: string[];
  locations: string[];
  remote: RemotePreference | null;
  salaryMin: number | null;
  salaryMax: number | null;
  salaryCurrency: string | null;
  noticePeriod: string | null;
}

/** Body of POST /api/cv/{id}/profile/versions (F0.1-AC4). `baseVersion` is the version that was edited. */
export interface SaveProfileVersionRequest {
  baseVersion: number;
  data: CandidateProfileData;
}

/** F0.1-BR2: mirrors CandidateProfileData length limits. The API is authoritative. */
export const PROFILE_SHORT_TEXT_MAX = 200;
export const PROFILE_LONG_TEXT_MAX = 4000;
export const PROFILE_URL_MAX = 2048;

/** F0.1-BR1: mirrors CvDocument.MaxSizeBytes. The API is authoritative; this only gives instant feedback. */
export const CV_MAX_SIZE_BYTES = 5 * 1024 * 1024;
export const CV_ACCEPTED_EXTENSIONS = ['.pdf', '.docx'] as const;
