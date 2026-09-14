/**
 * The client side of the offset pagination contract (IA-REQ-038, IA-REQ-045). Every paged list route takes
 * `pageNumber` and `pageSize` and answers exactly the seven members below. These names and every developer message
 * in this module are invariant protocol data, so they never pass through `t()` (L5).
 */
import { ClientFailure } from './apiTransport';

/** The members every offset page declares, in the order the API documents them. */
export const paginationMembers = Object.freeze([
  'items',
  'pageNumber',
  'pageSize',
  'totalCount',
  'totalPages',
  'hasPreviousPage',
  'hasNextPage',
]);

const FIRST_PAGE_NUMBER = 1;
const MIN_PAGE_SIZE = 1;

export const DEFAULT_PAGE_SIZE = 25;
/** Mirrors PaginationQuery.MaxPageSize; the server clamps again. */
export const MAX_PAGE_SIZE = 100;
/** A whole-collection walk stops here: 100 pages at the largest page size is 10,000 rows (AD13). */
export const MAX_WALK_PAGES = 100;
export const pageSizeOptions = Object.freeze([10, 25, 50, 100]);
export const DEFAULT_PAGE = Object.freeze({ pageNumber: FIRST_PAGE_NUMBER, pageSize: DEFAULT_PAGE_SIZE });

const integerOr = (value, fallback) => (Number.isInteger(value) ? value : fallback);

/**
 * Mirrors PaginationQuery (AD12): a missing or non-integer member takes its default, and an integer is clamped, so
 * a page size of 0 or less means one row (PD-1), never the default. A `null` or `undefined` page is the default
 * page (D10).
 */
export function boundedPage(page) {
  const { pageNumber, pageSize } = page ?? {};
  return {
    pageNumber: Math.max(FIRST_PAGE_NUMBER, integerOr(pageNumber, DEFAULT_PAGE.pageNumber)),
    pageSize: Math.min(MAX_PAGE_SIZE, Math.max(MIN_PAGE_SIZE, integerOr(pageSize, DEFAULT_PAGE.pageSize))),
  };
}

/** Always sends both parameters, already bounded. */
export function paginationSearch(page) {
  const bounded = boundedPage(page);
  const search = new URLSearchParams();
  search.set('pageNumber', String(bounded.pageNumber));
  search.set('pageSize', String(bounded.pageSize));
  return search;
}

const isCount = (value) => Number.isInteger(value) && value >= 0;

/**
 * Checks the declared offset-page contract (E7). The transport has already refused undeclared and missing members;
 * this refuses metadata the contract cannot produce. The message is developer-facing
 * and stays invariant English (L5): callers turn a throw into `unreadable_response`, whose words come from
 * `errors.json`.
 */
export function readPage(body) {
  const valid = Array.isArray(body?.items)
    && Number.isInteger(body.pageNumber) && body.pageNumber >= FIRST_PAGE_NUMBER
    && Number.isInteger(body.pageSize) && body.pageSize >= MIN_PAGE_SIZE && body.pageSize <= MAX_PAGE_SIZE
    && isCount(body.totalCount) && isCount(body.totalPages)
    && typeof body.hasPreviousPage === 'boolean' && typeof body.hasNextPage === 'boolean';
  if (!valid) throw new Error('The page metadata does not match the offset pagination contract.');
  return body;
}

/**
 * Reads one offset page through the shared transport's `send` (AD11). What the transport already classified — an
 * `ApiProblem` or a `ClientFailure` — reaches the caller unchanged; a page whose metadata drifts from the contract
 * becomes `unreadable_response`, so drift is classified in one place (E7, error rule 16).
 */
export async function sendPage(send, path, page, { signal } = {}) {
  const body = await send(`${path}?${paginationSearch(page)}`, { expect: paginationMembers, signal });
  try {
    return readPage(body);
  } catch {
    throw new ClientFailure('unreadable_response');
  }
}

/**
 * Reads a whole collection, such as a role catalogue, by walking `pageNumber` at the largest page size until an
 * answer has no next page (PD-2, AD13). Rows are kept once per `keyOf`, because a write between two reads can shift
 * a row onto the next page. A failed read rejects the walk as it was classified, so no partial collection is ever
 * returned. The walk is bounded: a collection that still claims a next page after MAX_WALK_PAGES is drift, and
 * fails as `unreadable_response` rather than looping.
 */
export async function readEveryPage(readOne, keyOf, { signal } = {}) {
  const rows = new Map();
  for (let pageNumber = FIRST_PAGE_NUMBER; pageNumber <= MAX_WALK_PAGES; pageNumber += 1) {
    const page = await readOne({ pageNumber, pageSize: MAX_PAGE_SIZE }, { signal });
    page.items.forEach((item) => rows.set(keyOf(item), item));
    if (!page.hasNextPage) return [...rows.values()];
  }
  throw new ClientFailure('unreadable_response');
}
