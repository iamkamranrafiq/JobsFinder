/** Mirrors JobsFinder.Application.Common.PagedResult. */
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}
